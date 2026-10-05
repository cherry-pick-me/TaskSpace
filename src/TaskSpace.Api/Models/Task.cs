namespace TaskSpace.Api.Models;

public enum TaskStatus { Assigned, InReview, ChangesRequested, Accepted }

public sealed class Task
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public int AuthorId { get; set; }
    public int AssigneeId { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Assigned;
    public long Version { get; set; } = 1;
    public int? CurrentSubmissionId { get; set; }
    public Team Team { get; set; } = null!;
    public User Author { get; set; } = null!;
    public User Assignee { get; set; } = null!;
    public Submission? CurrentSubmission { get; set; }
    public ICollection<Submission> Submissions { get; set; } = [];
    public ICollection<Decision> Decisions { get; set; } = [];
}
