internal class ServiceDetail
{
    public string Name { get; }
    public string BasePath { get; }
    public string OpenApiUrl { get; }
    public string SwaggerUi { get; }
    public string Status { get; }

    public ServiceDetail(string name, string basePath, string openApiUrl, string swaggerUi, string status)
    {
        Name = name;
        BasePath = basePath;
        OpenApiUrl = openApiUrl;
        SwaggerUi = swaggerUi;
        Status = status;
    }

    public override bool Equals(object? obj)
    {
        return obj is ServiceDetail other &&
               Name == other.Name &&
               BasePath == other.BasePath &&
               OpenApiUrl == other.OpenApiUrl &&
               SwaggerUi == other.SwaggerUi &&
               Status == other.Status;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, BasePath, OpenApiUrl, SwaggerUi, Status);
    }
}
