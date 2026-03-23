namespace OpenApiMockServer.WebApi.Models;

public class ValidationErrorResponse : ErrorResponse
{
    public List<ValidationError> ValidationErrors { get; set; } = new();
}