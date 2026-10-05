using System.Text.Json.Serialization;

namespace TaskSpace.Api.Models;

public sealed class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string DisplayName { get; set; }
    [JsonIgnore]
    public required string PasswordHash { get; set; }
    public ICollection<TeamMember> Memberships { get; set; } = [];
}
