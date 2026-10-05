using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskSpace.Api.Models;
using TaskEntity = TaskSpace.Api.Models.Task;

namespace TaskSpace.Api.Data;

public static class DatabaseInitializer
{
    public const string DemoPassword = "Study123!";

    public static async System.Threading.Tasks.Task InitializeAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (await db.Users.AnyAsync())
        {
            await transaction.CommitAsync();
            return;
        }

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var users = new[]
        {
            DemoUser(1, "alice", "Алиса"), DemoUser(2, "bob", "Борис"),
            DemoUser(3, "carol", "Карина"), DemoUser(4, "dave", "Давид"),
            DemoUser(5, "erin", "Елена"), DemoUser(6, "frank", "Фёдор")
        };
        foreach (var user in users)
            user.PasswordHash = hasher.HashPassword(user, DemoPassword);
        db.Users.AddRange(users);
        db.Teams.AddRange(new Team { Id = 1, Name = "Команда A" }, new Team { Id = 2, Name = "Команда B" });
        db.TeamMembers.AddRange(
            Member(1, 1), Member(1, 2), Member(1, 3), Member(1, 6),
            Member(2, 4), Member(2, 5), Member(2, 6));
        db.Tasks.AddRange(
            new TaskEntity
            {
                Id = 1,
                TeamId = 1,
                AuthorId = 1,
                AssigneeId = 2,
                Title = "Подготовить README",
                Description = "Описать запуск учебного проекта."
            },
            new TaskEntity
            {
                Id = 2,
                TeamId = 2,
                AuthorId = 4,
                AssigneeId = 5,
                Title = "Задача команды B",
                Description = "Внутреннее описание команды B."
            },
            new TaskEntity
            {
                Id = 3,
                TeamId = 2,
                AuthorId = 4,
                AssigneeId = 5,
                Title = "Проверить результат команды B",
                Description = "Задача с историей сдач и решений.",
                Status = Models.TaskStatus.InReview,
                Version = 4
            });
        await db.SaveChangesAsync();
        db.Submissions.AddRange(
            new Submission { Id = 1, TaskId = 3, SubmitterId = 5, Text = "Первая сдача команды B." },
            new Submission { Id = 2, TaskId = 3, SubmitterId = 5, Text = "Доработанная сдача команды B." });
        await db.SaveChangesAsync();
        db.Decisions.Add(new Decision
        {
            Id = 1,
            TaskId = 3,
            SubmissionId = 1,
            AuthorId = 4,
            Kind = DecisionKind.Returned,
            Remark = "Замечание автора команды B."
        });
        (await db.Tasks.SingleAsync(t => t.Id == 3)).CurrentSubmissionId = 2;
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static User DemoUser(int id, string username, string name) =>
        new() { Id = id, Username = username, DisplayName = name, PasswordHash = "" };
    private static TeamMember Member(int team, int user) => new() { TeamId = team, UserId = user };
}
