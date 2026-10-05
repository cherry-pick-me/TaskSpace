using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TaskSpace.Api.Data;
using TaskEntity = TaskSpace.Api.Models.Task;

namespace TaskSpace.Api.Services;

public sealed class TeamAccessService(AppDbContext db)
{
    public Task<bool> CanAccessTeamAsync(int userId, int teamId, CancellationToken cancellation) =>
        db.TeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == userId, cancellation);

    public IQueryable<TaskEntity> AccessibleTasks(int userId) => db.Tasks.AsNoTracking()
        .Where(t => t.Team.Members.Any(m => m.UserId == userId));

    public static int CurrentUserId(ClaimsPrincipal user) => int.Parse(
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Missing authenticated user."),
        System.Globalization.CultureInfo.InvariantCulture);
}
