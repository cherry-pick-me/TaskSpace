using System.Net;
using System.Net.Http.Json;
using TaskSpace.Api.Endpoints;
using TaskSpace.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace TaskSpace.Api.Tests;

public sealed class WorkflowTests
{
    [Fact]
    public async Task Two_sessions_return_resubmit_reject_stale_decision_and_preserve_history_after_restart()
    {
        await using var app = new TestApp();
        using var author = app.Client();
        using var worker = app.Client();
        await app.LoginAsync(author);
        await app.LoginAsync(worker, "bob");
        const string text = "<b>text</b><img src=x onerror=alert(1)>";
        using var creation = await author.PostAsJsonAsync("/api/teams/1/tasks", new { title = text, description = text, assigneeId = 2 });
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        var path = creation.Headers.Location!.OriginalString;
        using var first = await worker.PostAsJsonAsync(path + "/submissions", new { text, expectedVersion = 1, userId = 4 });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var r1 = (await first.Content.ReadFromJsonAsync<SubmissionResponse>())!;
        Assert.Equal(2, r1.SubmitterId);
        using var returned = await author.PostAsJsonAsync(path + "/decisions", new { submissionId = r1.Id, kind = "Returned", remark = text, expectedVersion = 2, userId = 4 });
        Assert.Equal(HttpStatusCode.Created, returned.StatusCode);
        Assert.Equal(1, (await returned.Content.ReadFromJsonAsync<DecisionResponse>())!.AuthorId);
        using var second = await worker.PostAsJsonAsync(path + "/submissions", new { text = "R2", expectedVersion = 3 });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var r2 = (await second.Content.ReadFromJsonAsync<SubmissionResponse>())!;
        var before = await app.SnapshotAsync();
        foreach (var version in new[] { 2, 4 })
        {
            using var stale = await author.PostAsJsonAsync(path + "/decisions", new { submissionId = r1.Id, kind = "Accepted", expectedVersion = version });
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal(before, await app.SnapshotAsync());
        }
        using var accepted = await author.PostAsJsonAsync(path + "/decisions", new { submissionId = r2.Id, kind = "Accepted", expectedVersion = 4 });
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        var saved = await author.GetFromJsonAsync<TaskDetailResponse>(path);
        Assert.Equal("Accepted", saved!.Status);
        Assert.Equal(5, saved.Version);
        Assert.Equal(text, saved.Submissions[0].Text);
        Assert.Equal(text, saved.Decisions[0].Remark);
        Assert.Equal(2, saved.Submissions.Count);
        Assert.Equal(2, saved.Decisions.Count);
        var snapshot = await app.SnapshotAsync();
        await app.RestartAsync();
        using var reader = app.Client();
        var persisted = await reader.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, persisted.StatusCode);
        Assert.Equal(snapshot, await app.SnapshotAsync());
    }

    [Theory]
    [InlineData("alice", "submissions", 403)]
    [InlineData("bob", "decisions", 403)]
    [InlineData("carol", "submissions", 403)]
    [InlineData("carol", "decisions", 403)]
    [InlineData("dave", "submissions", 404)]
    [InlineData("dave", "decisions", 404)]
    public async Task Direct_http_cannot_spoof_roles_or_cross_team(string username, string action, int status)
    {
        await using var app = new TestApp();
        using var client = app.Client();
        await app.LoginAsync(client, username);
        var before = await app.SnapshotAsync();
        using var response = await client.PostAsJsonAsync($"/api/tasks/1/{action}", new
        { text = "private-probe", expectedVersion = 1, submissionId = 2, kind = "Accepted", userId = 2, authorId = 1 });
        Assert.Equal(status, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-probe", body);
        Assert.DoesNotContain("README", body);
        Assert.Equal(before, await app.SnapshotAsync());
    }

    [Theory]
    [InlineData("PUT", "/api/tasks/3")]
    [InlineData("PATCH", "/api/tasks/3/submissions/1")]
    [InlineData("DELETE", "/api/tasks/3/submissions/1")]
    [InlineData("PUT", "/api/tasks/3/decisions/1")]
    [InlineData("DELETE", "/api/tasks/3/decisions/1")]
    public async Task History_cannot_be_edited_or_deleted(string method, string path)
    {
        await using var app = new TestApp();
        using var client = app.Client();
        await app.LoginAsync(client, "dave");
        var before = await app.SnapshotAsync();
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { text = "rewritten", kind = "Accepted" }) };
        using var response = await client.SendAsync(request);
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Equal(before, await app.SnapshotAsync());
    }

    [Fact]
    public async Task Concurrent_independent_sessions_create_only_one_submission()
    {
        await using var app = new TestApp();
        using var a = app.Client();
        using var b = app.Client();
        var first = await app.LoginAsync(a, "bob");
        var second = await app.LoginAsync(b, "bob");
        Assert.NotEqual(first.Token, second.Token);
        var replies = await Task.WhenAll(a.PostAsJsonAsync("/api/tasks/1/submissions", new { text = "A", expectedVersion = 1 }),
            b.PostAsJsonAsync("/api/tasks/1/submissions", new { text = "B", expectedVersion = 1 }));
        try
        {
            Assert.Equal(new[] { 201, 409 }, replies.Select(r => (int)r.StatusCode).Order());
            var task = await a.GetFromJsonAsync<TaskDetailResponse>("/api/tasks/1");
            Assert.Single(task!.Submissions);
            Assert.Equal(2, task.Version);
        }
        finally { foreach (var reply in replies) reply.Dispose(); }
    }
    [Fact]
    public async Task Failure_after_submission_insert_rolls_back_history_and_state()
    {
        await using var app = new TestApp();
        using var client = app.Client();
        await app.LoginAsync(client, "bob");
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_transition BEFORE UPDATE ON Tasks BEGIN SELECT RAISE(ABORT, 'private-database-probe'); END;");
        }
        var before = await app.SnapshotAsync();
        using var response = await client.PostAsJsonAsync("/api/tasks/1/submissions", new { text = "must roll back", expectedVersion = 1 });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Не удалось обработать запрос.", (await response.Content.ReadFromJsonAsync<ApiError>())!.Error);
        Assert.Equal(before, await app.SnapshotAsync());
    }

    [Theory]
    [InlineData("", 1, 400)]
    [InlineData("   ", 1, 400)]
    [InlineData("result", 0, 409)]
    public async Task Invalid_submission_does_not_change_state(string text, long version, int status)
    {
        await using var app = new TestApp();
        using var client = app.Client();
        await app.LoginAsync(client, "bob");
        var before = await app.SnapshotAsync();
        using var response = await client.PostAsJsonAsync("/api/tasks/1/submissions", new { text, expectedVersion = version });
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(before, await app.SnapshotAsync());
    }

    [Theory]
    [InlineData("Returned", null, 4, 2, 400)]
    [InlineData("Returned", "   ", 4, 2, 400)]
    [InlineData("Deleted", "remark", 4, 2, 400)]
    [InlineData("Accepted", null, 3, 2, 409)]
    [InlineData("Accepted", null, 4, 1, 409)]
    public async Task Invalid_or_stale_decision_preserves_history(string kind, string? remark, long version, int submissionId, int status)
    {
        await using var app = new TestApp();
        using var client = app.Client();
        await app.LoginAsync(client, "dave");
        var before = await app.SnapshotAsync();
        using var response = await client.PostAsJsonAsync("/api/tasks/3/decisions", new { kind, remark, expectedVersion = version, submissionId });
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(before, await app.SnapshotAsync());
    }

    [Fact]
    public async Task Two_author_sessions_cannot_decide_the_same_submission_twice()
    {
        await using var app = new TestApp();
        using var a = app.Client();
        using var b = app.Client();
        await app.LoginAsync(a, "dave");
        await app.LoginAsync(b, "dave");
        var command = new { kind = "Accepted", submissionId = 2, expectedVersion = 4 };
        var replies = await Task.WhenAll(a.PostAsJsonAsync("/api/tasks/3/decisions", command), b.PostAsJsonAsync("/api/tasks/3/decisions", command));
        try
        {
            Assert.Equal(new[] { 201, 409 }, replies.Select(r => (int)r.StatusCode).Order());
            var task = await a.GetFromJsonAsync<TaskDetailResponse>("/api/tasks/3");
            Assert.Equal("Accepted", task!.Status);
            Assert.Equal(5, task.Version);
            Assert.Single(task.Decisions, d => d.SubmissionId == 2);
            using var worker = app.Client();
            await app.LoginAsync(worker, "erin");
            var before = await app.SnapshotAsync();
            using var resubmit = await worker.PostAsJsonAsync("/api/tasks/3/submissions", new { text = "after acceptance", expectedVersion = 5 });
            Assert.Equal(HttpStatusCode.Conflict, resubmit.StatusCode);
            Assert.Equal(before, await app.SnapshotAsync());
        }
        finally { foreach (var reply in replies) reply.Dispose(); }
    }

}
