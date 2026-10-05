namespace TaskSpace.Api.Models;

public sealed class Submission
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public int SubmitterId { get; set; }
    public required string Text { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Task Task { get; set; } = null!;
    public User Submitter { get; set; } = null!;
}
