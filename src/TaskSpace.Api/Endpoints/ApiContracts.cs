namespace TaskSpace.Api.Endpoints;

public sealed record ApiError(string Error);
public sealed record LoginRequest(string? Username, string? Password);
public sealed record UserResponse(int Id, string Username, string DisplayName);
public sealed record TeamResponse(int Id, string Name);
public sealed record LoginResponse(string Token, string TokenType, UserResponse User, IReadOnlyList<TeamResponse> Teams);
public sealed record CreateTaskRequest(string? Title, string? Description, int AssigneeId);
public sealed record TaskResponse(int Id, int TeamId, int AuthorId, int AssigneeId,
    string Title, string Description, string Status, long Version, int? CurrentSubmissionId);
public sealed record SubmissionResponse(int Id, int SubmitterId, string Text, DateTimeOffset CreatedAt);
public sealed record DecisionResponse(int Id, int SubmissionId, int AuthorId, string Kind,
    string? Remark, DateTimeOffset CreatedAt);
public sealed record TaskDetailResponse(int Id, int TeamId, int AuthorId, int AssigneeId,
    string Title, string Description, string Status, long Version, int? CurrentSubmissionId,
    IReadOnlyList<SubmissionResponse> Submissions, IReadOnlyList<DecisionResponse> Decisions);

public static class ApiResponses
{
    public static IResult NotFound() => Results.Json(new ApiError("Объект не найден."), statusCode: 404);
    public static IResult BadRequest(string error) => Results.Json(new ApiError(error), statusCode: 400);
}
