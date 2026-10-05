using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskSpace.Api.Data;
using TaskSpace.Api.Endpoints;
using TaskSpace.Api.Services;
using Decision = TaskSpace.Api.Models.Decision;
using DecisionKind = TaskSpace.Api.Models.DecisionKind;
using TaskStatus = TaskSpace.Api.Models.TaskStatus;

namespace TaskSpace.Api.Tests;

// M3: таблица переходов D-03, атомарность и версии D-04, контрпример R1/R2 (SR-05–SR-08, SR-10).
public sealed class WorkflowTransitionTests
{
    [Fact]
    public void Transition_table_allows_exactly_the_four_D03_transitions()
    {
        var expected = new (TaskStatus, WorkflowAction, TaskStatus?)[]
        {
            (TaskStatus.Assigned, WorkflowAction.Submit, TaskStatus.InReview),
            (TaskStatus.ChangesRequested, WorkflowAction.Submit, TaskStatus.InReview),
            (TaskStatus.InReview, WorkflowAction.Accept, TaskStatus.Accepted),
            (TaskStatus.InReview, WorkflowAction.Return, TaskStatus.ChangesRequested),
            (TaskStatus.InReview, WorkflowAction.Submit, null),
            (TaskStatus.Accepted, WorkflowAction.Submit, null),
            (TaskStatus.Assigned, WorkflowAction.Accept, null),
            (TaskStatus.Assigned, WorkflowAction.Return, null),
            (TaskStatus.ChangesRequested, WorkflowAction.Accept, null),
            (TaskStatus.ChangesRequested, WorkflowAction.Return, null),
            (TaskStatus.Accepted, WorkflowAction.Accept, null),
            (TaskStatus.Accepted, WorkflowAction.Return, null)
        };
        foreach (var (from, action, to) in expected)
            Assert.Equal(to, TaskWorkflow.Find(from, action)?.To);
        Assert.Equal(4, TaskWorkflow.Transitions.Count);
        Assert.All(TaskWorkflow.Transitions, t => Assert.Equal(TaskWorkflow.ActorFor(t.Action), t.Actor));
        Assert.Equal(WorkflowActor.Assignee, TaskWorkflow.ActorFor(WorkflowAction.Submit));
        Assert.Equal(WorkflowActor.Author, TaskWorkflow.ActorFor(WorkflowAction.Accept));
        Assert.Equal(WorkflowActor.Author, TaskWorkflow.ActorFor(WorkflowAction.Return));
    }

    [Fact]
    public async Task Stale_R1_decision_after_return_and_R2_is_rejected_and_history_survives_restart()
    {
        await using var app = new TestApp();
        using var author = app.Client();
        using var authorOldTab = app.Client();
        using var worker = app.Client();
        await app.LoginAsync(author);
        await app.LoginAsync(authorOldTab);
        await app.LoginAsync(worker, "bob");
        using var creation = await author.PostAsJsonAsync("/api/teams/1/tasks", new { title = "R1/R2", description = "Counterexample", assigneeId = 2 });
        var path = creation.Headers.Location!.OriginalString;

        var r1 = await SubmitAsync(worker, path, "R1", 1);
        var seenByOldTab = await authorOldTab.GetFromJsonAsync<TaskDetailResponse>(path);
        Assert.Equal((2L, r1.Id, "InReview"), (seenByOldTab!.Version, seenByOldTab.CurrentSubmissionId!.Value, seenByOldTab.Status));
        await DecideAsync(author, path, r1.Id, "Returned", "Исправить", 2, HttpStatusCode.Created);
        // Задержанная сдача из прошлого цикла: статус снова допускает сдачу, но версия уже другая.
        using (var delayed = await worker.PostAsJsonAsync(path + "/submissions", new { text = "R1 again", expectedVersion = 1 }))
            Assert.Equal(HttpStatusCode.Conflict, delayed.StatusCode);
        var r2 = await SubmitAsync(worker, path, "R2", 3);

        var before = await app.SnapshotAsync();
        await DecideAsync(authorOldTab, path, r1.Id, "Accepted", null, seenByOldTab.Version, HttpStatusCode.Conflict);
        await DecideAsync(authorOldTab, path, r1.Id, "Accepted", null, 4, HttpStatusCode.Conflict);
        await DecideAsync(authorOldTab, path, r2.Id, "Accepted", null, 2, HttpStatusCode.Conflict);
        await DecideAsync(authorOldTab, path, r1.Id, "Returned", "старое", 2, HttpStatusCode.Conflict);
        Assert.Equal(before, await app.SnapshotAsync());

        var current = await author.GetFromJsonAsync<TaskDetailResponse>(path);
        Assert.Equal(("InReview", 4L, r2.Id), (current!.Status, current.Version, current.CurrentSubmissionId!.Value));
        Assert.Equal(r1.Id, Assert.Single(current.Decisions).SubmissionId);

        await DecideAsync(author, path, r2.Id, "Accepted", null, 4, HttpStatusCode.Created);
        var history = await app.SnapshotAsync();
        await app.RestartAsync();
        Assert.Equal(history, await app.SnapshotAsync());
        using var reader = app.Client();
        await app.LoginAsync(reader, "carol");
        var saved = await reader.GetFromJsonAsync<TaskDetailResponse>(path);
        Assert.Equal(("Accepted", 5L), (saved!.Status, saved.Version));
        Assert.Equal(new[] { "R1", "R2" }, saved.Submissions.Select(s => s.Text));
        Assert.Equal(new[] { (r1.Id, "Returned", (string?)"Исправить"), (r2.Id, "Accepted", null) },
            saved.Decisions.Select(d => (d.SubmissionId, d.Kind, d.Remark)));
        Assert.All(saved.Submissions, s => Assert.Equal(2, s.SubmitterId));
        Assert.All(saved.Decisions, d => Assert.Equal(1, d.AuthorId));
    }

    [Theory]
    [InlineData("erin", "submissions", 3, 0, null, 4, 409)]       // сдача в «На проверке»
    [InlineData("dave", "submissions", 3, 0, null, 4, 403)]       // автор сдаёт
    [InlineData("frank", "submissions", 3, 0, null, 4, 403)]      // участник не назначен
    [InlineData("erin", "decisions", 3, 2, "Accepted", 4, 403)]   // исполнитель принимает
    [InlineData("frank", "decisions", 3, 2, "Returned", 4, 403)]  // неавтор возвращает
    [InlineData("alice", "decisions", 1, 0, "Accepted", 1, 409)]  // принятие без сдачи
    [InlineData("alice", "decisions", 1, 1, "Accepted", 1, 409)]  // сдача другой задачи
    [InlineData("dave", "decisions", 3, 1, "Accepted", 4, 409)]   // старая сдача R1
    [InlineData("dave", "decisions", 3, 2, "accept", 4, 400)]     // недопустимое действие
    [InlineData("dave", "decisions", 3, 2, "Returned", 4, 400)]   // возврат без замечания
    [InlineData("dave", "decisions", 3, 99, "Accepted", 4, 409)]  // несуществующая сдача
    public async Task Disallowed_roles_and_states_are_rejected_without_changes(string username, string action,
        int taskId, int submissionId, string? kind, long version, int status)
    {
        await using var app = new TestApp();
        using var client = app.Client();
        await app.LoginAsync(client, username);
        var before = await app.SnapshotAsync();
        using var response = await client.PostAsJsonAsync($"/api/tasks/{taskId}/{action}",
            new { text = "result", submissionId, kind, expectedVersion = version, status = "Accepted", authorId = 4, assigneeId = 5 });
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(before, await app.SnapshotAsync());
    }

    [Fact]
    public async Task Decision_on_changes_requested_and_accepted_tasks_is_conflict()
    {
        await using var app = new TestApp();
        using var author = app.Client();
        using var worker = app.Client();
        await app.LoginAsync(author, "dave");
        await app.LoginAsync(worker, "erin");
        await DecideAsync(author, "/api/tasks/3", 2, "Returned", "ещё раз", 4, HttpStatusCode.Created);
        var before = await app.SnapshotAsync();
        await DecideAsync(author, "/api/tasks/3", 2, "Accepted", null, 5, HttpStatusCode.Conflict);
        Assert.Equal(before, await app.SnapshotAsync());

        var r3 = await SubmitAsync(worker, "/api/tasks/3", "R3", 5);
        await DecideAsync(author, "/api/tasks/3", r3.Id, "Accepted", null, 6, HttpStatusCode.Created);
        before = await app.SnapshotAsync();
        await DecideAsync(author, "/api/tasks/3", r3.Id, "Returned", "после принятия", 7, HttpStatusCode.Conflict);
        using (var resubmit = await worker.PostAsJsonAsync("/api/tasks/3/submissions", new { text = "R4", expectedVersion = 7 }))
            Assert.Equal(HttpStatusCode.Conflict, resubmit.StatusCode);
        Assert.Equal(before, await app.SnapshotAsync());
    }

    [Fact]
    public async Task Concurrent_accept_and_return_of_one_submission_commit_exactly_one_decision()
    {
        for (var round = 0; round < 3; round++)
        {
            await using var app = new TestApp();
            using var a = app.Client();
            using var b = app.Client();
            await app.LoginAsync(a, "dave");
            await app.LoginAsync(b, "dave");
            var replies = await Task.WhenAll(
                a.PostAsJsonAsync("/api/tasks/3/decisions", new { submissionId = 2, kind = "Accepted", expectedVersion = 4 }),
                b.PostAsJsonAsync("/api/tasks/3/decisions", new { submissionId = 2, kind = "Returned", remark = "нет", expectedVersion = 4 }));
            try
            {
                Assert.Equal(new[] { 201, 409 }, replies.Select(r => (int)r.StatusCode).Order());
                var winner = replies.Single(r => r.StatusCode == HttpStatusCode.Created);
                var decision = await winner.Content.ReadFromJsonAsync<DecisionResponse>();
                var task = await a.GetFromJsonAsync<TaskDetailResponse>("/api/tasks/3");
                Assert.Equal(5, task!.Version);
                Assert.Equal(decision!.Kind == "Accepted" ? "Accepted" : "ChangesRequested", task.Status);
                Assert.Single(task.Decisions, d => d.SubmissionId == 2);
            }
            finally { foreach (var reply in replies) reply.Dispose(); }
        }
    }

    [Theory]
    [InlineData("Submissions", "bob", "/api/tasks/1/submissions")]
    [InlineData("Decisions", "dave", "/api/tasks/3/decisions")]
    public async Task History_insert_failure_after_task_update_rolls_back_everything(string table, string username, string path)
    {
        await using var app = new TestApp();
        using var client = app.Client();
        await app.LoginAsync(client, username);
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Сбой возникает уже после условного UPDATE задачи, но до commit.
            var trigger = table == "Submissions"
                ? "CREATE TRIGGER fail_history BEFORE INSERT ON Submissions BEGIN SELECT RAISE(ABORT, 'private-database-probe'); END;"
                : "CREATE TRIGGER fail_history BEFORE INSERT ON Decisions BEGIN SELECT RAISE(ABORT, 'private-database-probe'); END;";
            await db.Database.ExecuteSqlRawAsync(trigger);
        }
        var before = await app.SnapshotAsync();
        using var response = await client.PostAsJsonAsync(path,
            new { text = "lost", submissionId = 2, kind = "Accepted", expectedVersion = table == "Submissions" ? 1 : 4 });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("private-database-probe", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await app.SnapshotAsync());
    }

    [Fact]
    public async Task Duplicate_decision_rejected_by_unique_index_is_conflict_without_state_change()
    {
        await using var app = new TestApp();
        using var client = app.Client();
        await app.LoginAsync(client, "dave");
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            // Решение по текущей сдаче записано в обход API: условный UPDATE пройдёт, вставка — нет.
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Decisions.Add(new Decision { TaskId = 3, SubmissionId = 2, AuthorId = 4, Kind = DecisionKind.Returned, Remark = "x" });
            await db.SaveChangesAsync();
        }
        var before = await app.SnapshotAsync();
        await DecideAsync(client, "/api/tasks/3", 2, "Accepted", null, 4, HttpStatusCode.Conflict);
        Assert.Equal(before, await app.SnapshotAsync());
    }

    private static async Task<SubmissionResponse> SubmitAsync(HttpClient client, string path, string text, long version)
    {
        using var response = await client.PostAsJsonAsync(path + "/submissions", new { text, expectedVersion = version });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SubmissionResponse>())!;
    }

    private static async Task DecideAsync(HttpClient client, string path, int submissionId, string? kind, string? remark,
        long version, HttpStatusCode expected)
    {
        using var response = await client.PostAsJsonAsync(path + "/decisions", new { submissionId, kind, remark, expectedVersion = version });
        Assert.Equal(expected, response.StatusCode);
    }
}
