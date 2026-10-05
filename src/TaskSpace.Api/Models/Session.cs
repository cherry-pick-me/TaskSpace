using System.Text.Json.Serialization;

namespace TaskSpace.Api.Models;

public sealed class Session
{
    public int Id { get; set; }
    public int UserId { get; set; }
    [JsonIgnore]
    public required string TokenHash { get; set; }
    public User User { get; set; } = null!;
}
