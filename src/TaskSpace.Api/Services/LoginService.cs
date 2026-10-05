using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TaskSpace.Api.Data;
using TaskSpace.Api.Endpoints;
using User = TaskSpace.Api.Models.User;
using Session = TaskSpace.Api.Models.Session;

namespace TaskSpace.Api.Services;

public sealed class LoginService(AppDbContext db, IPasswordHasher<User> hasher)
{
    private static readonly User DummyUser = CreateDummyUser();

    public async Task<LoginResponse?> LoginAsync(string username, string password, CancellationToken cancellation)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username, cancellation);
        var candidate = user ?? DummyUser;
        var result = hasher.VerifyHashedPassword(candidate, candidate.PasswordHash, password);
        if (user is null || result == PasswordVerificationResult.Failed)
            return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, password);

        var teams = await db.TeamMembers.Where(m => m.UserId == user.Id).OrderBy(m => m.TeamId)
            .Select(m => new TeamResponse(m.TeamId, m.Team.Name)).ToListAsync(cancellation);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var token = SessionTokens.Generate();
            var session = new Session { UserId = user.Id, TokenHash = SessionTokens.Hash(token) };
            db.Sessions.Add(session);
            try
            {
                await db.SaveChangesAsync(cancellation);
                return new LoginResponse(token, "Bearer",
                    new UserResponse(user.Id, user.Username, user.DisplayName), teams);
            }
            catch (DbUpdateException error) when (error.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
            {
                db.Entry(session).State = EntityState.Detached;
            }
        }
        throw new InvalidOperationException("Unable to allocate a unique session.");
    }

    private static User CreateDummyUser()
    {
        var user = new User { Username = "", DisplayName = "", PasswordHash = "" };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "unused-demo-password");
        return user;
    }
}
