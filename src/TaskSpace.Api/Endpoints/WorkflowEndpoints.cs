using System.Security.Claims;
using TaskSpace.Api.Services;

namespace TaskSpace.Api.Endpoints;

public static class WorkflowEndpoints
{
    public static async Task<IResult> SubmitAsync(int taskId, SubmitRequest request, ClaimsPrincipal user,
        WorkflowService workflow, CancellationToken cancellation)
    {
        var result = await workflow.SubmitAsync(taskId, TeamAccessService.CurrentUserId(user),
            request.Text, request.ExpectedVersion, cancellation);
        return result.Value is { } s
            ? Results.Created($"/api/tasks/{taskId}", new SubmissionResponse(s.Id, s.SubmitterId, s.Text, s.CreatedAt))
            : Failure(result.Outcome, "Результат обязателен; максимум 10000 символов.");
    }

    public static async Task<IResult> DecideAsync(int taskId, DecideRequest request, ClaimsPrincipal user,
        WorkflowService workflow, CancellationToken cancellation)
    {
        var result = await workflow.DecideAsync(taskId, TeamAccessService.CurrentUserId(user), request.SubmissionId,
            request.Kind, request.Remark, request.ExpectedVersion, cancellation);
        return result.Value is { } d
            ? Results.Created($"/api/tasks/{taskId}", new DecisionResponse(d.Id, d.SubmissionId, d.AuthorId,
                d.Kind.ToString(), d.Remark, d.CreatedAt))
            : Failure(result.Outcome, "Выберите Accepted или Returned; для возврата укажите замечание до 10000 символов.");
    }

    private static IResult Failure(WorkflowOutcome outcome, string invalid) => outcome switch
    {
        WorkflowOutcome.NotFound => ApiResponses.NotFound(),
        WorkflowOutcome.Forbidden => Results.Json(new ApiError("Действие недоступно."), statusCode: 403),
        WorkflowOutcome.Invalid => ApiResponses.BadRequest(invalid),
        _ => Results.Json(new ApiError("Задача изменилась. Перечитайте её и повторите действие."), statusCode: 409)
    };
}
