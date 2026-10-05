using System.Net;
using System.Net.Http.Json;
using System.Text;
using TaskSpace.Api.Endpoints;

namespace TaskSpace.Api.Tests;

public sealed class TaskAccessTests
{
    [Fact]
    public async Task Member_can_read_own_tasks_and_outsider_cannot_read_foreign_history()
    {
        await using var app = new TestApp();
        using var client = app.Client();
        var login = await app.LoginAsync(client);
        Assert.Equal(1, login.User.Id);
        Assert.Equal(1, Assert.Single(login.Teams).Id);
        var own = await client.GetFromJsonAsync<TaskDetailResponse>("/api/tasks/1");
        Assert.NotNull(own);
        Assert.Equal("Assigned", own.Status);
        Assert.Equal(1, own.Version);
        Assert.Empty(own.Submissions);
        Assert.Empty(own.Decisions);
        using var foreign = await client.GetAsync("/api/tasks/3");
        using var missing = await client.GetAsync("/api/tasks/99999");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(await missing.Content.ReadAsStringAsync(), await foreign.Content.ReadAsStringAsync());
        Assert.Equal("Объект не найден.", (await foreign.Content.ReadFromJsonAsync<ApiError>())?.Error);
        var tasks = await client.GetFromJsonAsync<TaskResponse[]>("/api/teams/1/tasks");
        Assert.Equal(1, Assert.Single(tasks!).TeamId);
    }

    [Fact]
    public async Task Foreign_and_missing_teams_have_identical_responses_and_no_writes()
    {
        await using var app = new TestApp();
        using var client = app.Client();
        await app.LoginAsync(client);
        var before = await app.SnapshotAsync();
        foreach (var method in new[] { "GET", "POST" })
        {
            var responses = new List<string>();
            foreach (var id in new[] { 2, 99999 })
            {
                using var request = new HttpRequestMessage(new HttpMethod(method), $"/api/teams/{id}/tasks");
                if (method == "POST")
                    request.Content = JsonContent.Create(new { title = "Task", description = "Text", assigneeId = 5, authorId = 4 });
                using var response = await client.SendAsync(request);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                responses.Add(await response.Content.ReadAsStringAsync());
            }
            Assert.Equal(responses[0], responses[1]);
        }
        Assert.Equal(before, await app.SnapshotAsync());
    }

    [Fact]
    public async Task User_in_two_teams_can_read_both_and_the_complete_history()
    {
        await using var app = new TestApp();
        using var client = app.Client();
        var login = await app.LoginAsync(client, "frank");
        Assert.Equal(new[] { 1, 2 }, login.Teams.Select(t => t.Id));
        foreach (var teamId in new[] { 1, 2 })
        {
            var tasks = await client.GetFromJsonAsync<TaskResponse[]>($"/api/teams/{teamId}/tasks");
            Assert.NotEmpty(tasks!);
            Assert.All(tasks!, task => Assert.Equal(teamId, task.TeamId));
        }
        var history = await client.GetFromJsonAsync<TaskDetailResponse>("/api/tasks/3");
        Assert.NotNull(history);
        Assert.Equal(2, history.Submissions.Count);
        Assert.Single(history.Decisions);
        Assert.Equal(2, history.CurrentSubmissionId);
        Assert.Equal(1, history.Decisions[0].SubmissionId);
        Assert.Equal(4, history.Version);
    }

    [Fact]
    public async Task Spoofed_author_team_status_version_and_history_are_ignored()
    {
        await using var app = new TestApp();
        using var client = app.Client();
        await app.LoginAsync(client);
        using var response = await client.PostAsJsonAsync("/api/teams/1/tasks", new
        {
            title = "New task",
            description = "Original description",
            assigneeId = 2,
            authorId = 4,
            userId = 4,
            teamId = 2,
            status = "Accepted",
            version = 99,
            currentSubmissionId = 2,
            submissions = new[] { new { text = "fake" } },
            decisions = new[] { new { kind = "Accepted" } }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.NotNull(created);
        Assert.True(created.Id > 3);
        Assert.Equal(1, created.AuthorId);
        Assert.Equal(1, created.TeamId);
        Assert.Equal(2, created.AssigneeId);
        Assert.Equal("Assigned", created.Status);
        Assert.Equal(1, created.Version);
        Assert.Null(created.CurrentSubmissionId);
        Assert.Equal($"/api/tasks/{created.Id}", response.Headers.Location?.OriginalString);
        var read = await client.GetFromJsonAsync<TaskDetailResponse>(response.Headers.Location);
        Assert.NotNull(read);
        Assert.Empty(read.Submissions);
        Assert.Empty(read.Decisions);
        Assert.Equal(created.Title, read.Title);
    }

    [Fact]
    public async Task Ordinary_member_can_create_and_other_members_can_read_the_new_task()
    {
        await using var app = new TestApp();
        using var creator = app.Client();
        await app.LoginAsync(creator, "carol");
        using var response = await creator.PostAsJsonAsync("/api/teams/1/tasks", new { title = "Task", description = "Text", assigneeId = 2 });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<TaskResponse>();
        Assert.Equal(3, created?.AuthorId);
        using var reader = app.Client();
        await app.LoginAsync(reader, "bob");
        var task = await reader.GetFromJsonAsync<TaskDetailResponse>(response.Headers.Location);
        Assert.Equal(3, task?.AuthorId);
        Assert.Equal(2, task?.AssigneeId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(99999)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Invalid_assignees_are_rejected_without_changes(int assigneeId)
    {
        await using var app = new TestApp();
        using var client = app.Client();
        await app.LoginAsync(client);
        var before = await app.SnapshotAsync();
        using var response = await client.PostAsJsonAsync("/api/teams/1/tasks", new { title = "Task", description = "Text", assigneeId });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Выберите другого участника этой команды.", (await response.Content.ReadFromJsonAsync<ApiError>())?.Error);
        Assert.Equal(before, await app.SnapshotAsync());
    }

    [Theory]
    [InlineData(null, "Text")]
    [InlineData("", "Text")]
    [InlineData("  ", "Text")]
    [InlineData("Task", null)]
    [InlineData("Task", "")]
    [InlineData("Task", "  ")]
    public async Task Invalid_text_is_rejected_without_changes(string? title, string? description)
    {
        await using var app = new TestApp();
        using var client = app.Client();
        await app.LoginAsync(client);
        var before = await app.SnapshotAsync();
        using var response = await client.PostAsJsonAsync("/api/teams/1/tasks", new { title, description, assigneeId = 2 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await app.SnapshotAsync());
    }

    [Fact]
    public async Task Text_length_limits_and_plain_text_round_trip_are_preserved()
    {
        await using var app = new TestApp();
        using var client = app.Client();
        await app.LoginAsync(client);
        foreach (var pair in new[] { (new string('x', 201), "Text"), ("Task", new string('x', 10001)) })
        {
            using var invalid = await client.PostAsJsonAsync("/api/teams/1/tasks", new { title = pair.Item1, description = pair.Item2, assigneeId = 2 });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        const string text = "<img src=x onerror=alert(1)> ' OR 1=1 --";
        using var valid = await client.PostAsJsonAsync("/api/teams/1/tasks", new { title = text, description = new string('x', 10000), assigneeId = 2 });
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        var read = await client.GetFromJsonAsync<TaskDetailResponse>(valid.Headers.Location);
        Assert.Equal(text, read?.Title);
        Assert.Equal(10000, read?.Description.Length);
        Assert.Equal("application/json", valid.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"title\":\"Task\",\"description\":\"Text\",\"assigneeId\":\"private-input-probe\"}")]
    public async Task Invalid_json_returns_sanitized_400(string body)
    {
        await using var app = new TestApp();
        using var client = app.Client();
        await app.LoginAsync(client);
        var before = await app.SnapshotAsync();
        using var response = await client.PostAsync("/api/teams/1/tasks", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("private-input-probe", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await app.SnapshotAsync());
    }

    [Theory]
    [InlineData("http://localhost:5173", true)]
    [InlineData("https://other.example", false)]
    public async Task Cors_allows_only_the_configured_origin(string origin, bool allowed)
    {
        await using var app = new TestApp();
        using var client = app.Client();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/teams/1/tasks");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        using var response = await client.SendAsync(request);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed)
            Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }
}
