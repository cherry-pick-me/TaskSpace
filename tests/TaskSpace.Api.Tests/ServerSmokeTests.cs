using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using TaskSpace.Api.Data;
using TaskSpace.Api.Endpoints;

namespace TaskSpace.Api.Tests;

public sealed class ServerSmokeTests
{
    [Fact]
    public async Task Real_server_starts_on_clean_database_and_preserves_state_after_process_restart()
    {
        var directory = Directory.CreateTempSubdirectory("taskspace-m1-server-");
        var database = Path.Combine(directory.FullName, "server.db");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(5) };
        Process? server = null;
        try
        {
            server = StartServer(database, port);
            await WaitForHealthAsync(client, server);
            using var loginReply = await client.PostAsJsonAsync("/api/auth/login", new { username = "alice", password = DatabaseInitializer.DemoPassword });
            Assert.Equal(HttpStatusCode.OK, loginReply.StatusCode);
            var login = await loginReply.Content.ReadFromJsonAsync<LoginResponse>();
            Assert.NotNull(login);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
            var own = await client.GetFromJsonAsync<TaskDetailResponse>("/api/tasks/1");
            Assert.Equal("Подготовить README", own?.Title);
            using var foreign = await client.GetAsync("/api/tasks/3");
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
            using var created = await client.PostAsJsonAsync("/api/teams/1/tasks", new
            {
                title = "Real HTTP task",
                description = "Preserved across process restart",
                assigneeId = 2,
                authorId = 4
            });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            using var tooLarge = await client.PostAsJsonAsync("/api/teams/1/tasks", new
            {
                title = "Too large",
                description = new string('x', 90_000),
                assigneeId = 2
            });
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
            using var worker = new HttpClient { BaseAddress = client.BaseAddress };
            using var workerLogin = await worker.PostAsJsonAsync("/api/auth/login", new { username = "bob", password = DatabaseInitializer.DemoPassword });
            var workerSession = await workerLogin.Content.ReadFromJsonAsync<LoginResponse>();
            worker.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", workerSession!.Token);
            var taskPath = created.Headers.Location!.OriginalString;
            using var submitted = await worker.PostAsJsonAsync(taskPath + "/submissions", new { text = "Saved submission", expectedVersion = 1 });
            Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
            var submission = await submitted.Content.ReadFromJsonAsync<SubmissionResponse>();
            using var decision = await client.PostAsJsonAsync(taskPath + "/decisions", new { submissionId = submission!.Id, kind = "Accepted", expectedVersion = 2 });
            Assert.Equal(HttpStatusCode.Created, decision.StatusCode);
            await StopServerAsync(server);
            server.Dispose();
            server = StartServer(database, port);
            await WaitForHealthAsync(client, server);
            var saved = await client.GetFromJsonAsync<TaskDetailResponse>(created.Headers.Location);
            Assert.NotNull(saved);
            Assert.Equal("Real HTTP task", saved.Title);
            Assert.Equal(1, saved.AuthorId);
            Assert.Equal(3, saved.Version);
            Assert.Equal("Accepted", saved.Status);
            Assert.Equal("Saved submission", Assert.Single(saved.Submissions).Text);
            Assert.Equal("Accepted", Assert.Single(saved.Decisions).Kind);
            var tasks = await client.GetFromJsonAsync<TaskResponse[]>("/api/teams/1/tasks");
            Assert.Equal(2, tasks?.Length);
        }
        finally
        {
            if (server is not null)
            {
                await StopServerAsync(server);
                server.Dispose();
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            // На Windows дескриптор файла убитого процесса освобождается с задержкой.
            for (var attempt = 0; ; attempt++)
            {
                try { directory.Delete(recursive: true); break; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException && attempt < 50)
                {
                    await Task.Delay(100);
                }
            }
        }
    }

    private static Process StartServer(string database, int port)
    {
        var projectDirectory = Path.GetFullPath("../../../../../src/TaskSpace.Api", AppContext.BaseDirectory);
        var framework = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        var configuration = Path.GetFileName(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)));
        var dll = Path.Combine(projectDirectory, "bin", configuration!, framework, "TaskSpace.Api.dll");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = projectDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(dll);
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.ArgumentList.Add("--ConnectionStrings:TaskSpace");
        start.ArgumentList.Add($"Data Source={database};Foreign Keys=True;Default Timeout=5");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        var process = Process.Start(start) ?? throw new InvalidOperationException("Server process did not start.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task WaitForHealthAsync(HttpClient client, Process process)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!timeout.IsCancellationRequested)
        {
            Assert.False(process.HasExited, "Server exited before becoming healthy.");
            try
            {
                using var health = await client.GetAsync("/health", timeout.Token);
                if (health.IsSuccessStatusCode)
                    return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(100, timeout.Token);
        }
        throw new TimeoutException("Server did not become healthy.");
    }

    private static async Task StopServerAsync(Process process)
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }
}
