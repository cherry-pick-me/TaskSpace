using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskSpace.Api.Data;
using TaskSpace.Api.Endpoints;
using Xunit.Abstractions;

namespace TaskSpace.Api.Tests;

// S06: evidence for an existing property, not an intentionally introduced vulnerability.
public sealed class DataHandlingEvidenceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("{ not valid json", "Production")]
    [InlineData("{ not valid json", "Development")]
    [InlineData("{\"title\":\"s06-private-input\",\"description\":\"text\",\"assigneeId\":\"not-an-integer\"}", "Production")]
    [InlineData("{\"title\":\"s06-private-input\",\"description\":\"text\",\"assigneeId\":\"not-an-integer\"}", "Development")]
    public async Task Invalid_json_and_types_are_400_without_details_or_database_changes(string body, string environment)
    {
        await using var app = new TestApp(environment);
        using var client = app.Client();
        await app.LoginAsync(client);
        var before = await app.SnapshotAsync();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/teams/1/tasks", content);
        output.WriteLine($"Environment: {environment}; actor: alice; POST /api/teams/1/tasks; request: {body}");
        await RecordResponseAsync(response);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(new ApiError("Некорректный запрос."), await response.Content.ReadFromJsonAsync<ApiError>());
        await AssertNoDetailsAsync(response, app.DatabasePath);
        Assert.Equal(before, await app.SnapshotAsync());
        output.WriteLine("Snapshot of users, teams, members, tasks, submissions, decisions and sessions: unchanged.");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Database_failure_rolls_back_submission_and_is_a_sanitized_500(string environment)
    {
        await using var app = new TestApp(environment);
        using var client = app.Client();
        var login = await app.LoginAsync(client, "bob");
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Fail the history INSERT after WorkflowService has already updated status/version.
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER s06_reject_history BEFORE INSERT ON Submissions BEGIN SELECT RAISE(ABORT, 's06-internal-storage-probe'); END;");
        }
        var before = await app.SnapshotAsync();
        using var response = await client.PostAsJsonAsync("/api/tasks/1/submissions", new { text = "s06-private-input", expectedVersion = 1 });
        output.WriteLine($"Environment: {environment}; actor: bob; POST /api/tasks/1/submissions; text=s06-private-input; expectedVersion=1; SQLite history INSERT deliberately rejected.");
        await RecordResponseAsync(response);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(new ApiError("Не удалось обработать запрос."), await response.Content.ReadFromJsonAsync<ApiError>());
        await AssertNoDetailsAsync(response, app.DatabasePath);
        Assert.Equal(before, await app.SnapshotAsync());
        output.WriteLine("Snapshot after database failure: unchanged, including task status/version and history.");
        var middlewareLogs = app.Logs.Messages.Where(m => m.StartsWith("API error ", StringComparison.Ordinal)).ToArray();
        Assert.Single(middlewareLogs);
        foreach (var entry in middlewareLogs) output.WriteLine("Middleware log: " + entry);
        var logs = string.Join('\n', app.Logs.Messages);
        Assert.DoesNotContain(login.Token, logs);
        Assert.DoesNotContain("s06-private-input", logs);
        output.WriteLine("Captured formatted application logs contain neither the bearer token nor the submitted private text. No claim that all internal diagnostic details are absent from server logs.");
    }

    [Fact]
    public async Task Ordinary_and_sql_looking_text_create_tasks_without_changing_foreign_data()
    {
        await using var app = new TestApp();
        using var author = app.Client();
        using var foreign = app.Client();
        await app.LoginAsync(author);
        await app.LoginAsync(foreign, "dave");
        var foreignBefore = await foreign.GetStringAsync("/api/teams/2/tasks");
        var ordinary = new { title = "S06 normal task", description = "Normal result", assigneeId = 2 };
        var special = new { title = "O'Reilly'; DROP TABLE Tasks; --", description = "<b>text</b><img src=x onerror=alert(1)>", assigneeId = 2 };
        foreach (var input in new[] { ordinary, special })
        {
            using var response = await author.PostAsJsonAsync("/api/teams/1/tasks", input);
            output.WriteLine($"Actor: alice; POST /api/teams/1/tasks; title={input.title}; description={input.description}; assigneeId=2");
            await RecordResponseAsync(response);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var saved = await author.GetFromJsonAsync<TaskDetailResponse>(response.Headers.Location);
            Assert.NotNull(saved);
            Assert.Equal(input.title, saved.Title);
            Assert.Equal(input.description, saved.Description);
            Assert.Equal(1, saved.AuthorId);
            Assert.Equal(1, saved.TeamId);
            Assert.Equal(2, saved.AssigneeId);
            Assert.Equal("Assigned", saved.Status);
            Assert.Equal(1, saved.Version);
            Assert.Empty(saved.Submissions);
            Assert.Empty(saved.Decisions);
            var snapshot = await app.SnapshotAsync();
            using var denied = await foreign.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            Assert.Equal(new ApiError("Объект не найден."), await denied.Content.ReadFromJsonAsync<ApiError>());
            Assert.Equal(snapshot, await app.SnapshotAsync());
        }
        Assert.Equal(foreignBefore, await foreign.GetStringAsync("/api/teams/2/tasks"));
        var tasks = await author.GetFromJsonAsync<TaskResponse[]>("/api/teams/1/tasks");
        Assert.Equal(3, tasks!.Length);
        output.WriteLine("Both inputs persisted literally; tasks table remains readable; foreign team list unchanged; foreign reads return 404 without database changes. JSON round-trip is not a DOM/XSS test.");
    }

    private async Task RecordResponseAsync(HttpResponseMessage response)
    {
        output.WriteLine($"HTTP {(int)response.StatusCode}; Content-Type: {response.Content.Headers.ContentType}; Cache-Control: {response.Headers.CacheControl}; body: {await response.Content.ReadAsStringAsync()}");
    }

    private static async Task AssertNoDetailsAsync(HttpResponseMessage response, string databasePath)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var marker in new[] { "Exception", "System.Text.Json", "Sqlite", "Program.cs", "s06-private-input", "s06-internal-storage-probe", databasePath })
            Assert.DoesNotContain(marker, body, StringComparison.OrdinalIgnoreCase);
    }
}
