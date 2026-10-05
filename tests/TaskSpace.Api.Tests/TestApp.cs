using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TaskSpace.Api.Data;
using TaskSpace.Api.Endpoints;

namespace TaskSpace.Api.Tests;

internal sealed class TestApp : IAsyncDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("taskspace-m1-tests-");
    private readonly string _environment;
    public TestApp(string environment = "Production")
    {
        _environment = environment;
        Factory = CreateFactory();
    }

    public string DatabasePath => Path.Combine(_directory.FullName, "test.db");
    public CaptureLogs Logs { get; } = new();
    public WebApplicationFactory<Program> Factory { get; private set; }
    public HttpClient Client() => Factory.CreateClient();

    public async Task<LoginResponse> LoginAsync(HttpClient client, string username = "alice")
    {
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password = DatabaseInitializer.DemoPassword });
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>() ?? throw new InvalidOperationException("Missing login response.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        return login;
    }

    public async Task<string> SnapshotAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return JsonSerializer.Serialize(new
        {
            Users = await db.Users.OrderBy(u => u.Id).Select(u => new { u.Id, u.Username, u.PasswordHash }).ToListAsync(),
            Teams = await db.Teams.OrderBy(t => t.Id).Select(t => new { t.Id, t.Name }).ToListAsync(),
            Members = await db.TeamMembers.OrderBy(m => m.TeamId).ThenBy(m => m.UserId).Select(m => new { m.TeamId, m.UserId }).ToListAsync(),
            Tasks = await db.Tasks.OrderBy(t => t.Id).Select(t => new
            {
                t.Id,
                t.TeamId,
                t.AuthorId,
                t.AssigneeId,
                t.Title,
                t.Description,
                t.Status,
                t.Version,
                t.CurrentSubmissionId
            }).ToListAsync(),
            Submissions = await db.Submissions.OrderBy(s => s.Id).Select(s => new { s.Id, s.TaskId, s.SubmitterId, s.Text, s.CreatedAt }).ToListAsync(),
            Decisions = await db.Decisions.OrderBy(d => d.Id).Select(d => new
            {
                d.Id,
                d.TaskId,
                d.SubmissionId,
                d.AuthorId,
                d.Kind,
                d.Remark,
                d.CreatedAt
            }).ToListAsync(),
            Sessions = await db.Sessions.OrderBy(s => s.Id).Select(s => new { s.Id, s.UserId, s.TokenHash }).ToListAsync()
        });
    }

    public async Task RestartAsync()
    {
        await Factory.DisposeAsync();
        Factory = CreateFactory();
    }

    private WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(_environment);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:TaskSpace"] = $"Data Source={DatabasePath};Foreign Keys=True;Default Timeout=5"
            }));
            builder.ConfigureLogging(logging => logging.ClearProviders().AddProvider(Logs));
        });

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _directory.Delete(recursive: true);
    }
}

internal sealed class CaptureLogs : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CaptureLogger(Messages);
    public void Dispose() { }

    private sealed class CaptureLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Enqueue(formatter(state, exception));
    }
}
