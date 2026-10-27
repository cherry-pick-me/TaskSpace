using System.Net;
using System.Net.Http.Json;
using TaskSpace.Api.Endpoints;
using Xunit.Abstractions;

namespace TaskSpace.Api.Tests;

// S07: наблюдаемые сценарии T-02/SR-03/D-02 и T-04/T-05/SR-05/SR-06/D-03.
public sealed class AccessControlEvidenceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Existing_task_and_nested_history_are_visible_only_to_team_members()
    {
        await using var app = new TestApp();
        using var alice = app.Client();
        using var dave = app.Client();
        using var frank = app.Client();
        await app.LoginAsync(alice, "alice");
        await app.LoginAsync(dave, "dave");
        await app.LoginAsync(frank, "frank");

        using var allowed = await dave.GetAsync("/api/tasks/3");
        await RecordAsync("dave", "GET", "/api/tasks/3", allowed, HttpStatusCode.OK);
        var existing = await allowed.Content.ReadFromJsonAsync<TaskDetailResponse>();
        Assert.NotNull(existing);
        Assert.Equal(2, existing.TeamId);
        Assert.Equal(2, existing.Submissions.Count);
        Assert.Single(existing.Decisions);

        var before = await app.SnapshotAsync();
        using var denied = await alice.GetAsync("/api/tasks/3");
        await RecordAsync("alice", "GET", "/api/tasks/3", denied, HttpStatusCode.NotFound);
        using var missing = await alice.GetAsync("/api/tasks/99999");
        await RecordAsync("alice", "GET", "/api/tasks/99999", missing, HttpStatusCode.NotFound);
        var body = await denied.Content.ReadAsStringAsync();
        Assert.Equal(await missing.Content.ReadAsStringAsync(), body);
        foreach (var privateText in new[] { existing.Title, existing.Description }
                     .Concat(existing.Submissions.Select(s => s.Text))
                     .Concat(existing.Decisions.Select(d => d.Remark!)))
            Assert.DoesNotContain(privateText, body);

        using var foreignList = await alice.GetAsync("/api/teams/2/tasks");
        await RecordAsync("alice", "GET", "/api/teams/2/tasks", foreignList, HttpStatusCode.NotFound);
        Assert.True(before == await app.SnapshotAsync(), "Отклонённое чтение изменило базу.");

        using var own = await alice.GetAsync("/api/tasks/1");
        await RecordAsync("alice", "GET", "/api/tasks/1", own, HttpStatusCode.OK);
        using var both = await frank.GetAsync("/api/tasks/3");
        await RecordAsync("frank", "GET", "/api/tasks/3", both, HttpStatusCode.OK);
        output.WriteLine("PASS: существование подтверждено владельцем; чужая история не выдана; штатный доступ работает.");
    }

    [Fact]
    public async Task Role_and_team_denials_preserve_state_and_authorized_workflow_still_works()
    {
        await using var app = new TestApp();
        using var author = app.Client();
        using var worker = app.Client();
        using var member = app.Client();
        using var outsider = app.Client();
        await app.LoginAsync(author, "dave");
        await app.LoginAsync(worker, "erin");
        await app.LoginAsync(member, "frank");
        await app.LoginAsync(outsider, "alice");

        using var creation = await author.PostAsJsonAsync("/api/teams/2/tasks", new
        {
            title = "S07: проверка полномочий", description = "Учебный результат команды B", assigneeId = 5
        });
        await RecordAsync("dave", "POST", "/api/teams/2/tasks", creation, HttpStatusCode.Created);
        var path = creation.Headers.Location!.OriginalString;

        foreach (var (client, name, expected) in new[]
                 {
                     (author, "dave", HttpStatusCode.Forbidden),
                     (member, "frank", HttpStatusCode.Forbidden),
                     (outsider, "alice", HttpStatusCode.NotFound)
                 })
        {
            var before = await app.SnapshotAsync();
            using var denied = await client.PostAsJsonAsync(path + "/submissions", new
            {
                text = "Поддельная сдача", expectedVersion = 1, userId = 5
            });
            await RecordAsync(name, "POST", path + "/submissions", denied, expected);
            Assert.True(before == await app.SnapshotAsync(), "Отказ в сдаче изменил данные.");
        }

        using var submitted = await worker.PostAsJsonAsync(path + "/submissions",
            new { text = "R1", expectedVersion = 1 });
        await RecordAsync("erin", "POST", path + "/submissions", submitted, HttpStatusCode.Created);
        var first = await submitted.Content.ReadFromJsonAsync<SubmissionResponse>();
        Assert.NotNull(first);
        Assert.Equal(5, first.SubmitterId);

        foreach (var (client, name, expected) in new[]
                 {
                     (worker, "erin", HttpStatusCode.Forbidden),
                     (member, "frank", HttpStatusCode.Forbidden),
                     (outsider, "alice", HttpStatusCode.NotFound)
                 })
        {
            var before = await app.SnapshotAsync();
            using var denied = await client.PostAsJsonAsync(path + "/decisions", new
            {
                submissionId = first.Id, kind = "Accepted", expectedVersion = 2, authorId = 4
            });
            await RecordAsync(name, "POST", path + "/decisions", denied, expected);
            Assert.True(before == await app.SnapshotAsync(), "Отказ в решении изменил данные.");
        }

        using var returned = await author.PostAsJsonAsync(path + "/decisions", new
        {
            submissionId = first.Id, kind = "Returned", remark = "Нужна доработка", expectedVersion = 2
        });
        await RecordAsync("dave", "POST", path + "/decisions", returned, HttpStatusCode.Created);
        using var resubmitted = await worker.PostAsJsonAsync(path + "/submissions",
            new { text = "R2", expectedVersion = 3 });
        await RecordAsync("erin", "POST", path + "/submissions", resubmitted, HttpStatusCode.Created);
        var second = await resubmitted.Content.ReadFromJsonAsync<SubmissionResponse>();
        Assert.NotNull(second);
        using var accepted = await author.PostAsJsonAsync(path + "/decisions", new
        {
            submissionId = second.Id, kind = "Accepted", expectedVersion = 4
        });
        await RecordAsync("dave", "POST", path + "/decisions", accepted, HttpStatusCode.Created);
        var saved = await author.GetFromJsonAsync<TaskDetailResponse>(path);
        Assert.NotNull(saved);
        Assert.Equal("Accepted", saved.Status);
        Assert.Equal(5, saved.Version);
        Assert.Equal(2, saved.Submissions.Count);
        Assert.Equal(2, saved.Decisions.Count);
        output.WriteLine("PASS: отказы не изменили снимок базы; разрешённый цикл завершён, Accepted/version 5, две сдачи и два решения.");
    }

    private async Task RecordAsync(string user, string method, string path,
        HttpResponseMessage response, HttpStatusCode expected)
    {
        output.WriteLine($"{user} {method} {path} -> {(int)response.StatusCode}; {await response.Content.ReadAsStringAsync()}");
        Assert.Equal(expected, response.StatusCode);
    }
}
