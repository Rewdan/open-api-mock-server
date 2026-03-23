namespace OpenApiMockServer.WebApi.Models;

public class ErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string? Details { get; set; }
    public int StatusCode { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
