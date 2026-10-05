namespace TaskSpace.Api.Models;

public sealed class Team
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public ICollection<TeamMember> Members { get; set; } = [];
}
