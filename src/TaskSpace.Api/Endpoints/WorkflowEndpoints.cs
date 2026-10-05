using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TaskSpace.Api.Data;
using TaskSpace.Api.Models;
using TaskSpace.Api.Services;
using TaskStatus = TaskSpace.Api.Models.TaskStatus;

namespace TaskSpace.Api.Endpoints;

public static class WorkflowEndpoints
{
    private static IResult Conflict() => Results.Json(new ApiError("Задача изменилась. Перечитайте её и повторите действие."), statusCode: 409);
    private static IResult Forbidden() => Results.Json(new ApiError("Действие недоступно."), statusCode: 403);

    public static async Task<IResult> SubmitAsync(int taskId, SubmitRequest request, ClaimsPrincipal user,
        TeamAccessService access, AppDbContext db, CancellationToken cancellation)
    {
        var userId = TeamAccessService.CurrentUserId(user);
        // Begin before reading: SQLite serializes writers; all history and state writes commit together.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellation);
        var task = await access.AccessibleTasks(userId).AsTracking().SingleOrDefaultAsync(t => t.Id == taskId, cancellation);
        if (task is null) return ApiResponses.NotFound();
        if (task.AssigneeId != userId) return Forbidden();
        if (task.Version != request.ExpectedVersion || task.Status is not (TaskStatus.Assigned or TaskStatus.ChangesRequested))
            return Conflict();
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 10_000)
            return ApiResponses.BadRequest("Результат обязателен; максимум 10000 символов.");
        var submission = new Submission { TaskId = taskId, SubmitterId = userId, Text = request.Text };
        db.Submissions.Add(submission);
        await db.SaveChangesAsync(cancellation);
        task.CurrentSubmissionId = submission.Id;
        task.Status = TaskStatus.InReview;
        task.Version++;
        await db.SaveChangesAsync(cancellation);
        await transaction.CommitAsync(cancellation);
        return Results.Created($"/api/tasks/{taskId}", new SubmissionResponse(submission.Id, userId, submission.Text, submission.CreatedAt));
    }

    public static async Task<IResult> DecideAsync(int taskId, DecideRequest request, ClaimsPrincipal user,
        TeamAccessService access, AppDbContext db, CancellationToken cancellation)
    {
        var userId = TeamAccessService.CurrentUserId(user);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellation);
        var task = await access.AccessibleTasks(userId).AsTracking().SingleOrDefaultAsync(t => t.Id == taskId, cancellation);
        if (task is null) return ApiResponses.NotFound();
        if (task.AuthorId != userId) return Forbidden();
        if (task.Version != request.ExpectedVersion || task.Status != TaskStatus.InReview
            || task.CurrentSubmissionId != request.SubmissionId) return Conflict();
        if (request.Kind is not ("Accepted" or "Returned") || request.Remark?.Length > 10_000
            || (request.Kind == "Returned" && string.IsNullOrWhiteSpace(request.Remark)))
            return ApiResponses.BadRequest("Выберите Accepted или Returned; для возврата укажите замечание до 10000 символов.");
        var decision = new Decision
        {
            TaskId = taskId, SubmissionId = request.SubmissionId, AuthorId = userId,
            Kind = request.Kind == "Accepted" ? DecisionKind.Accepted : DecisionKind.Returned, Remark = request.Remark
        };
        db.Decisions.Add(decision);
        task.Status = decision.Kind == DecisionKind.Accepted ? TaskStatus.Accepted : TaskStatus.ChangesRequested;
        task.Version++;
        await db.SaveChangesAsync(cancellation);
        await transaction.CommitAsync(cancellation);
        return Results.Created($"/api/tasks/{taskId}", new DecisionResponse(decision.Id, decision.SubmissionId,
            userId, decision.Kind.ToString(), decision.Remark, decision.CreatedAt));
    }
}
