namespace TaskSpace.Api.Models;

public sealed class TeamMember
{
    public int TeamId { get; set; }
    public int UserId { get; set; }
    public Team Team { get; set; } = null!;
    public User User { get; set; } = null!;
}
