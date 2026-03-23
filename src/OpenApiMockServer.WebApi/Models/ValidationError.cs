namespace OpenApiMockServer.WebApi.Models;

public class ValidationError
{
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ExpectedType { get; set; }
    public string? ReceivedValue { get; set; }
}
