using System.Text.Json;
using TaskSpace.Api.Endpoints;

namespace TaskSpace.Api.Services;

public sealed class ApiErrorMiddleware(RequestDelegate next, ILogger<ApiErrorMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            context.Abort();
        }
        catch (Exception error) when (!context.Response.HasStarted)
        {
            var status = error switch
            {
                BadHttpRequestException bad => bad.StatusCode,
                JsonException => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status500InternalServerError
            };
            if (status >= 500)
                logger.LogError("API error {ErrorType}; request {RequestId}.", error.GetType().Name, context.TraceIdentifier);
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new ApiError(status >= 500
                ? "Не удалось обработать запрос." : "Некорректный запрос."), context.RequestAborted);
        }
    }
}
