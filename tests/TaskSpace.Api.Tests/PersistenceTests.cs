using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskSpace.Api.Data;
using TaskSpace.Api.Endpoints;
using Decision = TaskSpace.Api.Models.Decision;
using DecisionKind = TaskSpace.Api.Models.DecisionKind;
using Session = TaskSpace.Api.Models.Session;
using TaskEntity = TaskSpace.Api.Models.Task;

namespace TaskSpace.Api.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task Restart_preserves_seed_history_sessions_and_created_tasks()
    {
        await using var app = new TestApp();
        using var original = app.Client();
        var login = await app.LoginAsync(original);
        using var creation = await original.PostAsJsonAsync("/api/teams/1/tasks", new { title = "Survives restart", description = "Saved text", assigneeId = 2 });
        Assert.Equal(HttpStatusCode.Created, creation.StatusCode);
        var before = await app.SnapshotAsync();
        await app.RestartAsync();
        using var restarted = app.Client();
        restarted.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", login.Token);
        var task = await restarted.GetFromJsonAsync<TaskDetailResponse>(creation.Headers.Location);
        Assert.Equal("Survives restart", task?.Title);
        Assert.Equal(before, await app.SnapshotAsync());
    }

    [Theory]
    [InlineData("other-author")]
    [InlineData("other-assignee")]
    [InlineData("self")]
    [InlineData("cross-task-submission")]
    [InlineData("duplicate-decision")]
    [InlineData("duplicate-session")]
    public async Task Sqlite_enforces_relational_invariants(string violation)
    {
        await using var app = new TestApp();
        using var client = app.Client();
        var before = await app.SnapshotAsync();
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (violation == "duplicate-session")
        {
            db.Sessions.AddRange(new Session { UserId = 1, TokenHash = "same" }, new Session { UserId = 2, TokenHash = "same" });
        }
        else if (violation == "duplicate-decision")
            db.Decisions.Add(new Decision { TaskId = 3, SubmissionId = 1, AuthorId = 4, Kind = DecisionKind.Accepted });
        else
            db.Tasks.Add(new TaskEntity
            {
                TeamId = 1,
                AuthorId = violation == "other-author" ? 4 : 1,
                AssigneeId = violation == "other-assignee" ? 5 : violation == "self" ? 1 : 2,
                Title = "Invalid DB write",
                Description = "Must not persist",
                CurrentSubmissionId = violation == "cross-task-submission" ? 2 : null
            });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(before, await app.SnapshotAsync());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Unexpected_database_errors_are_sanitized_in_all_environments(string environment)
    {
        await using var app = new TestApp(environment);
        using var client = app.Client();
        var login = await app.LoginAsync(client);
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE Sessions");
        }
        using var response = await client.GetAsync("/api/tasks/1");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("Не удалось обработать запрос.", (await response.Content.ReadFromJsonAsync<ApiError>())?.Error);
        Assert.DoesNotContain(login.Token, string.Join('\n', app.Logs.Messages));
    }

    [Fact]
    public async Task Concurrent_logins_and_creations_produce_independent_valid_records()
    {
        await using var app = new TestApp();
        using var client = app.Client();
        var logins = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            using var independent = app.Client();
            return await app.LoginAsync(independent);
        }));
        Assert.Equal(4, logins.Select(l => l.Token).Distinct().Count());
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", logins[0].Token);
        var creations = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => client.PostAsJsonAsync("/api/teams/1/tasks",
            new { title = $"Parallel {i}", description = "Independent task", assigneeId = 2 })));
        var ids = new List<int>();
        foreach (var response in creations)
        {
            using (response)
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                var created = await response.Content.ReadFromJsonAsync<TaskResponse>();
                Assert.NotNull(created);
                Assert.Equal(1, created.AuthorId);
                ids.Add(created.Id);
            }
        }
        Assert.Equal(4, ids.Distinct().Count());
    }
}
