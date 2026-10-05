using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TaskSpace.Api.Data;
using TaskSpace.Api.Models;
using TaskStatus = TaskSpace.Api.Models.TaskStatus;

namespace TaskSpace.Api.Services;

public enum WorkflowOutcome { Created, NotFound, Forbidden, Invalid, Conflict }

public sealed record WorkflowResult<T>(WorkflowOutcome Outcome, T? Value = null) where T : class
{
    public static WorkflowResult<T> Fail(WorkflowOutcome outcome) => new(outcome);
}

// D-03/D-04: сдача и решение. Предварительное чтение выбирает код ответа (404/403/400/409),
// но решающая проверка — условный UPDATE внутри транзакции: роль, статус, expectedVersion
// и текущая сдача входят в WHERE, поэтому устаревшая или параллельная команда меняет 0 строк.
public sealed class WorkflowService(AppDbContext db, TeamAccessService access)
{
    public const int MaxTextLength = 10_000;

    public async Task<WorkflowResult<Submission>> SubmitAsync(int taskId, int userId, string? text,
        long expectedVersion, CancellationToken cancellation)
    {
        var task = await ReadAsync(taskId, userId, cancellation);
        if (task is null)
            return WorkflowResult<Submission>.Fail(WorkflowOutcome.NotFound);
        if (task.AssigneeId != userId)
            return WorkflowResult<Submission>.Fail(WorkflowOutcome.Forbidden);
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
            return WorkflowResult<Submission>.Fail(WorkflowOutcome.Invalid);
        var transition = TaskWorkflow.Find(task.Status, WorkflowAction.Submit);
        if (transition is null || task.Version != expectedVersion)
            return WorkflowResult<Submission>.Fail(WorkflowOutcome.Conflict);

        return await InTransactionAsync(async () =>
        {
            var updated = await db.Tasks
                .Where(t => t.Id == taskId && t.Version == expectedVersion && t.Status == transition.From
                    && t.AssigneeId == userId && t.Team.Members.Any(m => m.UserId == userId))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, transition.To)
                    .SetProperty(t => t.Version, t => t.Version + 1), cancellation);
            if (updated != 1)
                return null;
            var submission = new Submission { TaskId = taskId, SubmitterId = userId, Text = text };
            db.Submissions.Add(submission);
            await db.SaveChangesAsync(cancellation);
            await db.Tasks.Where(t => t.Id == taskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.CurrentSubmissionId, submission.Id), cancellation);
            return submission;
        }, cancellation);
    }

    public async Task<WorkflowResult<Decision>> DecideAsync(int taskId, int userId, int submissionId,
        string? kind, string? remark, long expectedVersion, CancellationToken cancellation)
    {
        var task = await ReadAsync(taskId, userId, cancellation);
        if (task is null)
            return WorkflowResult<Decision>.Fail(WorkflowOutcome.NotFound);
        if (task.AuthorId != userId)
            return WorkflowResult<Decision>.Fail(WorkflowOutcome.Forbidden);
        WorkflowAction? action = kind switch
        {
            nameof(DecisionKind.Accepted) => WorkflowAction.Accept,
            nameof(DecisionKind.Returned) => WorkflowAction.Return,
            _ => null
        };
        if (action is null || remark?.Length > MaxTextLength
            || (action == WorkflowAction.Return && string.IsNullOrWhiteSpace(remark)))
            return WorkflowResult<Decision>.Fail(WorkflowOutcome.Invalid);
        var transition = TaskWorkflow.Find(task.Status, action.Value);
        if (transition is null || task.Version != expectedVersion || task.CurrentSubmissionId != submissionId)
            return WorkflowResult<Decision>.Fail(WorkflowOutcome.Conflict);

        return await InTransactionAsync(async () =>
        {
            int? current = submissionId;
            var updated = await db.Tasks
                .Where(t => t.Id == taskId && t.Version == expectedVersion && t.Status == transition.From
                    && t.CurrentSubmissionId == current && t.AuthorId == userId
                    && t.Team.Members.Any(m => m.UserId == userId))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, transition.To)
                    .SetProperty(t => t.Version, t => t.Version + 1), cancellation);
            if (updated != 1)
                return null;
            var decision = new Decision
            {
                TaskId = taskId,
                SubmissionId = submissionId,
                AuthorId = userId,
                Kind = action == WorkflowAction.Accept ? DecisionKind.Accepted : DecisionKind.Returned,
                Remark = string.IsNullOrWhiteSpace(remark) ? null : remark
            };
            db.Decisions.Add(decision);
            await db.SaveChangesAsync(cancellation);
            return decision;
        }, cancellation);
    }

    private Task<TaskSnapshot?> ReadAsync(int taskId, int userId, CancellationToken cancellation) =>
        access.AccessibleTasks(userId).Where(t => t.Id == taskId)
            .Select(t => new TaskSnapshot(t.AuthorId, t.AssigneeId, t.Status, t.Version, t.CurrentSubmissionId))
            .SingleOrDefaultAsync(cancellation);

    // Транзакция начинается с записи, поэтому параллельный писатель ждёт блокировку, а не
    // упирается в повышение SHARED → RESERVED. Без commit не остаётся ни статуса, ни истории.
    private async Task<WorkflowResult<T>> InTransactionAsync<T>(Func<Task<T?>> write, CancellationToken cancellation)
        where T : class
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellation);
            var value = await write();
            if (value is null)
                return WorkflowResult<T>.Fail(WorkflowOutcome.Conflict);
            await transaction.CommitAsync(cancellation);
            return new WorkflowResult<T>(WorkflowOutcome.Created, value);
        }
        catch (Exception error) when (IsConflict(error))
        {
            db.ChangeTracker.Clear();
            return WorkflowResult<T>.Fail(WorkflowOutcome.Conflict);
        }
    }

    // Дубликат решения (UNIQUE) и занятость SQLite — допустимый отказ без предметных изменений.
    private static bool IsConflict(Exception error) =>
        (error as SqliteException ?? error.InnerException as SqliteException) is { } sqlite
        && (sqlite.SqliteErrorCode is 5 or 6 || sqlite.SqliteExtendedErrorCode == 2067);

    private sealed record TaskSnapshot(int AuthorId, int AssigneeId, TaskStatus Status, long Version, int? CurrentSubmissionId);
}
