
namespace OpenApiMockServer.WebApi.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Необработанная ошибка");

            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";

            var errorResponse = new
            {
                error = "Внутренняя ошибка сервера",
                details = ex.Message,
                timestamp = DateTime.UtcNow
            };

            await context.Response.WriteAsJsonAsync(errorResponse);
        }
    }
}