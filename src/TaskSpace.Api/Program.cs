using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TaskSpace.Api.Data;
using TaskSpace.Api.Endpoints;
using TaskSpace.Api.Services;
using User = TaskSpace.Api.Models.User;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 64 * 1024);
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddDbContext<AppDbContext>((services, options) =>
{
    var config = services.GetRequiredService<IConfiguration>();
    var environment = services.GetRequiredService<IHostEnvironment>();
    var connection = new SqliteConnectionStringBuilder(config.GetConnectionString("TaskSpace")
        ?? "Data Source=App_Data/taskspace.db");
    if (connection.DataSource != ":memory:")
    {
        connection.DataSource = Path.GetFullPath(connection.DataSource, environment.ContentRootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(connection.DataSource)!);
    }
    connection.ForeignKeys = true;
    options.UseSqlite(connection.ToString());
});
builder.Services.AddScoped<LoginService>();
builder.Services.AddScoped<TeamAccessService>();
builder.Services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration["Cors:AllowedOrigin"] ?? "http://localhost:5173")
    .WithMethods("GET", "POST").WithHeaders("Content-Type", "Authorization")));

var app = builder.Build();
app.UseMiddleware<ApiErrorMiddleware>();
app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;
    await response.WriteAsJsonAsync(new ApiError(response.StatusCode is 404 or 405
        ? "Объект не найден." : "Запрос отклонён."), context.HttpContext.RequestAborted);
});
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", async (AppDbContext db, CancellationToken cancellation) =>
    await db.Database.CanConnectAsync(cancellation)
        ? Results.Ok(new { status = "ok" })
        : Results.Json(new { status = "unavailable" }, statusCode: 503)).AllowAnonymous();
app.MapAuthEndpoints();
app.MapTaskEndpoints();
await DatabaseInitializer.InitializeAsync(app.Services);
await app.RunAsync();

public partial class Program;
