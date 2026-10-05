using TaskSpace.Api.Services;

namespace TaskSpace.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (LoginRequest request, LoginService login, CancellationToken cancellation) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 128
                || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length > 1024)
                return ApiResponses.BadRequest("Укажите логин и пароль допустимой длины.");
            var result = await login.LoginAsync(request.Username.Trim(), request.Password, cancellation);
            return result is null
                ? Results.Json(new ApiError("Неверный логин или пароль."), statusCode: 401)
                : Results.Ok(result);
        }).AllowAnonymous();
    }
}
