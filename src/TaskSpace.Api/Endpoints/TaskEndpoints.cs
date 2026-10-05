using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TaskSpace.Api.Data;
using TaskSpace.Api.Services;
using TaskEntity = TaskSpace.Api.Models.Task;

namespace TaskSpace.Api.Endpoints;

public static class TaskEndpoints
{
    public static void MapTaskEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/teams/{teamId:int}/tasks", ListAsync);
        api.MapGet("/tasks/{taskId:int}", GetAsync);
        api.MapPost("/teams/{teamId:int}/tasks", CreateAsync);
        api.MapPost("/tasks/{taskId:int}/submissions", WorkflowEndpoints.SubmitAsync);
        api.MapPost("/tasks/{taskId:int}/decisions", WorkflowEndpoints.DecideAsync);
    }

    private static async Task<IResult> ListAsync(int teamId, ClaimsPrincipal user,
        TeamAccessService access, CancellationToken cancellation)
    {
        var userId = TeamAccessService.CurrentUserId(user);
        if (!await access.CanAccessTeamAsync(userId, teamId, cancellation))
            return ApiResponses.NotFound();
        var tasks = await access.AccessibleTasks(userId).Where(t => t.TeamId == teamId).OrderBy(t => t.Id)
            .Select(t => new TaskResponse(t.Id, t.TeamId, t.AuthorId, t.AssigneeId, t.Title, t.Description,
                t.Status.ToString(), t.Version, t.CurrentSubmissionId)).ToListAsync(cancellation);
        return Results.Ok(tasks);
    }

    private static async Task<IResult> GetAsync(int taskId, ClaimsPrincipal user, TeamAccessService access,
        CancellationToken cancellation)
    {
        var task = await access.AccessibleTasks(TeamAccessService.CurrentUserId(user))
            .Include(t => t.Submissions).Include(t => t.Decisions).AsSingleQuery()
            .SingleOrDefaultAsync(t => t.Id == taskId, cancellation);
        if (task is null)
            return ApiResponses.NotFound();
        var submissions = task.Submissions.OrderBy(s => s.Id)
            .Select(s => new SubmissionResponse(s.Id, s.SubmitterId, s.Text, s.CreatedAt)).ToArray();
        var decisions = task.Decisions.OrderBy(d => d.Id)
            .Select(d => new DecisionResponse(d.Id, d.SubmissionId, d.AuthorId, d.Kind.ToString(), d.Remark, d.CreatedAt)).ToArray();
        return Results.Ok(new TaskDetailResponse(task.Id, task.TeamId, task.AuthorId, task.AssigneeId,
            task.Title, task.Description, task.Status.ToString(), task.Version, task.CurrentSubmissionId, submissions, decisions));
    }

    private static async Task<IResult> CreateAsync(int teamId, CreateTaskRequest request, ClaimsPrincipal user,
        TeamAccessService access, AppDbContext db, CancellationToken cancellation)
    {
        var userId = TeamAccessService.CurrentUserId(user);
        if (!await access.CanAccessTeamAsync(userId, teamId, cancellation))
            return ApiResponses.NotFound();
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200
            || string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 10_000)
            return ApiResponses.BadRequest("Название и описание обязательны; максимум 200 и 10000 символов.");
        if (request.AssigneeId == userId
            || !await access.CanAccessTeamAsync(request.AssigneeId, teamId, cancellation))
            return ApiResponses.BadRequest("Выберите другого участника этой команды.");

        var task = new TaskEntity
        {
            TeamId = teamId,
            AuthorId = userId,
            AssigneeId = request.AssigneeId,
            Title = request.Title,
            Description = request.Description
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellation);
        return Results.Created($"/api/tasks/{task.Id}", new TaskResponse(task.Id, task.TeamId, task.AuthorId,
            task.AssigneeId, task.Title, task.Description, task.Status.ToString(), task.Version, task.CurrentSubmissionId));
    }
}
