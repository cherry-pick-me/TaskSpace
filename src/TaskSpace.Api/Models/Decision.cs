namespace TaskSpace.Api.Models;

public enum DecisionKind { Accepted, Returned }

public sealed class Decision
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public int SubmissionId { get; set; }
    public int AuthorId { get; set; }
    public DecisionKind Kind { get; set; }
    public string? Remark { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Task Task { get; set; } = null!;
    public Submission Submission { get; set; } = null!;
    public User Author { get; set; } = null!;
}
