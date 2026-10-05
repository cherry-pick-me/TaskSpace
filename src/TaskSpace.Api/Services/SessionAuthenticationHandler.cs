using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskSpace.Api.Data;
using TaskSpace.Api.Endpoints;

namespace TaskSpace.Api.Services;

public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, AppDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headers = Request.Headers.Authorization;
        if (headers.Count == 0)
            return AuthenticateResult.NoResult();
        if (headers.Count != 1 || !AuthenticationHeaderValue.TryParse(headers[0], out var header)
            || !header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase)
            || header.Parameter is not { } token || !SessionTokens.IsWellFormed(token))
            return AuthenticateResult.Fail("Invalid session.");

        var hash = SessionTokens.Hash(token);
        var user = await db.Sessions.AsNoTracking().Where(s => s.TokenHash == hash)
            .Select(s => new { s.UserId, s.User.Username }).SingleOrDefaultAsync(Context.RequestAborted);
        if (user is null)
            return AuthenticateResult.Fail("Invalid session.");
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.Username)
        ], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        await Response.WriteAsJsonAsync(new ApiError("Требуется действующая сессия."), Context.RequestAborted);
    }
}
