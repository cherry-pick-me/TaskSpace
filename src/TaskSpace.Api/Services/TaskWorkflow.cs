using TaskStatus = TaskSpace.Api.Models.TaskStatus;

namespace TaskSpace.Api.Services;

public enum WorkflowAction { Submit, Accept, Return }

public enum WorkflowActor { Assignee, Author }

public sealed record WorkflowTransition(TaskStatus From, WorkflowAction Action, WorkflowActor Actor, TaskStatus To);

// D-03: единственный источник допустимых переходов. Всё, чего нет в таблице, отклоняется.
public static class TaskWorkflow
{
    public static readonly IReadOnlyList<WorkflowTransition> Transitions =
    [
        new(TaskStatus.Assigned, WorkflowAction.Submit, WorkflowActor.Assignee, TaskStatus.InReview),
        new(TaskStatus.ChangesRequested, WorkflowAction.Submit, WorkflowActor.Assignee, TaskStatus.InReview),
        new(TaskStatus.InReview, WorkflowAction.Accept, WorkflowActor.Author, TaskStatus.Accepted),
        new(TaskStatus.InReview, WorkflowAction.Return, WorkflowActor.Author, TaskStatus.ChangesRequested)
    ];

    public static WorkflowActor ActorFor(WorkflowAction action) =>
        action == WorkflowAction.Submit ? WorkflowActor.Assignee : WorkflowActor.Author;

    public static WorkflowTransition? Find(TaskStatus from, WorkflowAction action) =>
        Transitions.SingleOrDefault(t => t.From == from && t.Action == action);
}
