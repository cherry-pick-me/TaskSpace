using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskSpace.Api.Data;
using TaskSpace.Api.Endpoints;
using TaskSpace.Api.Services;
using User = TaskSpace.Api.Models.User;

namespace TaskSpace.Api.Tests;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task Health_is_public_and_returns_no_domain_data()
    {
        await using var app = new TestApp();
        using var client = app.Client();
        using var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"ok\"}", await response.Content.ReadAsStringAsync());
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("GET", "/api/teams/1/tasks", "missing")]
    [InlineData("GET", "/api/tasks/1", "missing")]
    [InlineData("POST", "/api/teams/1/tasks", "missing")]
    [InlineData("GET", "/api/teams/1/tasks", "unknown")]
    [InlineData("GET", "/api/tasks/1", "unknown")]
    [InlineData("POST", "/api/teams/1/tasks", "unknown")]
    [InlineData("GET", "/api/teams/1/tasks", "tampered")]
    [InlineData("GET", "/api/tasks/1", "tampered")]
    [InlineData("POST", "/api/teams/1/tasks", "tampered")]
    [InlineData("GET", "/api/tasks/1", "malformed")]
    public async Task Protected_endpoints_reject_invalid_sessions_without_changes(string method, string path, string kind)
    {
        await using var app = new TestApp();
        using var client = app.Client();
        if (kind == "tampered")
        {
            var login = await app.LoginAsync(client);
            var token = (login.Token[0] == 'A' ? "B" : "A") + login.Token[1..];
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        else if (kind != "missing")
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                kind == "unknown" ? SessionTokens.Generate() : "not-a-session");
        var before = await app.SnapshotAsync();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = JsonContent.Create(new { title = "Test", description = "Description", assigneeId = 2, userId = 1 });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.Single().Scheme);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("Требуется действующая сессия.", (await response.Content.ReadFromJsonAsync<ApiError>())?.Error);
        Assert.Equal(before, await app.SnapshotAsync());
    }

    [Fact]
    public async Task Tokens_in_query_or_cookie_and_body_userId_do_not_authenticate()
    {
        await using var app = new TestApp();
        using var client = app.Client();
        var login = await app.LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.Add("Cookie", $"session={login.Token}");
        using var response = await client.GetAsync($"/api/tasks/1?userId=1&token={Uri.EscapeDataString(login.Token)}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("alice", "wrong-demo-password")]
    [InlineData("unknown-user", "Study123!")]
    [InlineData("alice' OR 1=1 --", "Study123!")]
    public async Task Incorrect_credentials_do_not_create_sessions_or_disclose_user(string username, string password)
    {
        await using var app = new TestApp();
        using var client = app.Client();
        var before = await app.SnapshotAsync();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("Неверный логин или пароль.", (await response.Content.ReadFromJsonAsync<ApiError>())?.Error);
        Assert.Equal(before, await app.SnapshotAsync());
    }

    [Theory]
    [InlineData(null, "Study123!")]
    [InlineData("", "Study123!")]
    [InlineData("   ", "Study123!")]
    [InlineData("alice", null)]
    [InlineData("alice", "")]
    public async Task Missing_credentials_are_bad_requests(string? username, string? password)
    {
        await using var app = new TestApp();
        using var client = app.Client();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Passwords_are_salted_and_only_token_hashes_are_stored()
    {
        await using var app = new TestApp();
        using var client = app.Client();
        var first = await app.LoginAsync(client);
        var second = await app.LoginAsync(client);
        Assert.NotEqual(first.Token, second.Token);
        Assert.Equal(32, Convert.FromBase64String(first.Token).Length);
        Assert.Equal(32, Convert.FromBase64String(second.Token).Length);
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var users = await db.Users.ToListAsync();
        Assert.Equal(6, users.Select(u => u.PasswordHash).Distinct().Count());
        foreach (var user in users)
        {
            Assert.NotEqual(DatabaseInitializer.DemoPassword, user.PasswordHash);
            Assert.NotEqual(PasswordVerificationResult.Failed,
                hasher.VerifyHashedPassword(user, user.PasswordHash, DatabaseInitializer.DemoPassword));
        }
        var sessions = await db.Sessions.ToListAsync();
        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, s => s.TokenHash == SessionTokens.Hash(first.Token) && s.UserId == first.User.Id);
        Assert.Contains(sessions, s => s.TokenHash == SessionTokens.Hash(second.Token));
        var file = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(app.DatabasePath));
        Assert.DoesNotContain(DatabaseInitializer.DemoPassword, file);
        Assert.DoesNotContain(first.Token, file);
        Assert.DoesNotContain(second.Token, file);
    }

    [Fact]
    public async Task Responses_and_logs_do_not_expose_credentials_or_hashes()
    {
        await using var app = new TestApp();
        using var client = app.Client();
        var login = await app.LoginAsync(client);
        using var response = await client.GetAsync("/api/tasks/1");
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", body, StringComparison.OrdinalIgnoreCase);
        using var badLogin = await client.PostAsJsonAsync("/api/auth/login", new { username = "alice", password = "private-password-probe" });
        var logs = string.Join('\n', app.Logs.Messages);
        Assert.DoesNotContain(login.Token, logs);
        Assert.DoesNotContain(DatabaseInitializer.DemoPassword, logs);
        Assert.DoesNotContain("private-password-probe", logs);
    }

    [Fact]
    public async Task Wrong_auth_scheme_and_multiple_authorization_values_are_rejected()
    {
        await using var app = new TestApp();
        using var client = app.Client();
        var login = await app.LoginAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", login.Token);
        using var basic = await client.GetAsync("/api/tasks/1");
        Assert.Equal(HttpStatusCode.Unauthorized, basic.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", new[] { $"Bearer {login.Token}", $"Bearer {login.Token}" });
        using var multiple = await client.GetAsync("/api/tasks/1");
        Assert.Equal(HttpStatusCode.Unauthorized, multiple.StatusCode);
    }
}
