using Microsoft.EntityFrameworkCore;
using TaskSpace.Api.Models;
using TaskEntity = TaskSpace.Api.Models.Task;

namespace TaskSpace.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<TaskEntity> Tasks => Set<TaskEntity>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<Decision> Decisions => Set<Decision>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>().HasIndex(u => u.Username).IsUnique();
        model.Entity<TeamMember>().HasKey(m => new { m.TeamId, m.UserId });
        model.Entity<TeamMember>().HasOne(m => m.Team).WithMany(t => t.Members)
            .HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<TeamMember>().HasOne(m => m.User).WithMany(u => u.Memberships)
            .HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<Session>().HasIndex(s => s.TokenHash).IsUnique();
        model.Entity<Session>().Property(s => s.TokenHash).HasMaxLength(64);
        model.Entity<Session>().HasOne(s => s.User).WithMany()
            .HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);

        var task = model.Entity<TaskEntity>();
        // Id входит в FK текущей сдачи, поэтому генерацию нового ключа задаём явно.
        task.Property(t => t.Id).ValueGeneratedOnAdd();
        task.ToTable("Tasks", table =>
        {
            table.HasCheckConstraint("CK_Task_Participants", "AuthorId <> AssigneeId");
            table.HasCheckConstraint("CK_Task_Version", "Version >= 1");
            table.HasCheckConstraint("CK_Task_Status", "Status IN ('Assigned', 'InReview', 'ChangesRequested', 'Accepted')");
        });
        task.Property(t => t.Status).HasConversion<string>();
        task.Property(t => t.Version).IsConcurrencyToken();
        task.Property(t => t.Title).HasMaxLength(200);
        task.Property(t => t.Description).HasMaxLength(10_000);
        task.HasIndex(t => new { t.TeamId, t.Id });
        task.HasOne(t => t.Team).WithMany().HasForeignKey(t => t.TeamId).OnDelete(DeleteBehavior.Restrict);
        task.HasOne(t => t.Author).WithMany().HasForeignKey(t => t.AuthorId).OnDelete(DeleteBehavior.Restrict);
        task.HasOne(t => t.Assignee).WithMany().HasForeignKey(t => t.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        task.HasOne<TeamMember>().WithMany().HasForeignKey(t => new { t.TeamId, t.AuthorId })
            .OnDelete(DeleteBehavior.Restrict);
        task.HasOne<TeamMember>().WithMany().HasForeignKey(t => new { t.TeamId, t.AssigneeId })
            .OnDelete(DeleteBehavior.Restrict);

        var submission = model.Entity<Submission>();
        submission.HasAlternateKey(s => new { s.TaskId, s.Id });
        submission.HasOne(s => s.Task).WithMany(t => t.Submissions)
            .HasForeignKey(s => s.TaskId).OnDelete(DeleteBehavior.Restrict);
        submission.HasOne(s => s.Submitter).WithMany()
            .HasForeignKey(s => s.SubmitterId).OnDelete(DeleteBehavior.Restrict);
        task.HasOne(t => t.CurrentSubmission).WithMany()
            .HasForeignKey(t => new { t.Id, t.CurrentSubmissionId })
            .HasPrincipalKey(s => new { s.TaskId, s.Id }).OnDelete(DeleteBehavior.Restrict);

        var decision = model.Entity<Decision>();
        decision.Property(d => d.Kind).HasConversion<string>();
        decision.HasIndex(d => d.SubmissionId).IsUnique();
        decision.HasOne(d => d.Task).WithMany(t => t.Decisions)
            .HasForeignKey(d => d.TaskId).OnDelete(DeleteBehavior.Restrict);
        decision.HasOne(d => d.Submission).WithMany()
            .HasForeignKey(d => new { d.TaskId, d.SubmissionId })
            .HasPrincipalKey(s => new { s.TaskId, s.Id }).OnDelete(DeleteBehavior.Restrict);
        decision.HasOne(d => d.Author).WithMany()
            .HasForeignKey(d => d.AuthorId).OnDelete(DeleteBehavior.Restrict);
    }
}
