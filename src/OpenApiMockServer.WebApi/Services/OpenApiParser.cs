using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using OpenApiMockServer.WebApi.Models;

namespace OpenApiMockServer.WebApi.Services;

public class OpenApiParser
{
    private readonly ILogger<OpenApiParser> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public OpenApiParser(ILogger<OpenApiParser> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<OpenApiDocument?> ParseOpenApiUrlAsync(string url)
    {
        try
        {
            _logger.LogInformation($"Загрузка OpenAPI спецификации из {url}");

            // Проверяем, является ли URL файловым путем
            if (File.Exists(url))
            {
                _logger.LogInformation($"Обнаружен локальный файл: {url}");
                return await ParseOpenApiFileAsync(url);
            }

            // Иначе загружаем по HTTP
            return await ParseOpenApiHttpAsync(url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Ошибка загрузки OpenAPI из {url}");
            throw;
        }
    }
    private string GetBaseUrlFromContext(HttpContext context)
    {
        return $"{context.Request.Scheme}://{context.Request.Host}";
    }
    private async Task<OpenApiDocument?> ParseOpenApiHttpAsync(string url)
    {
        var client = _httpClientFactory.CreateClient("BrowserLikeClient");

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var ur = new Uri(url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        request.Headers.Add("accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
        request.Headers.Add("accept-language", "en-GB,en;q=0.9,ru-RU;q=0.8,ru;q=0.7,en-US;q=0.6");
        request.Headers.Add("cache-control", "no-cache");
        request.Headers.Add("pragma", "no-cache");
        request.Headers.Add("priority", "u=0, i");
        request.Headers.Add("sec-ch-ua", "\"Chromium\";v=\"146\", \"Not-A.Brand\";v=\"24\", \"Google Chrome\";v=\"146\"");
        request.Headers.Add("sec-ch-ua-mobile", "?0");
     request.Headers.Add("sec-ch-ua-platform", "\"Windows\"");
        request.Headers.Add("sec-fetch-dest", "document");
        request.Headers.Add("sec-fetch-mode", "navigate");
        request.Headers.Add("sec-fetch-site", "none");
        request.Headers.Add("sec-fetch-user", "?1");
        request.Headers.Add("upgrade-insecure-requests", "1");
        request.Headers.Add("Referer",$"{ur.Scheme}://{ur.Authority}/");
        request.Headers.Add("Origin", $"{ur.Scheme}://{ur.Authority}");
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var format = DetectFormat(url, response, content);

        return ParseContent(content, format);
    }

    public async Task<OpenApiDocument?> ParseOpenApiFileAsync(string filePath)
    {
        try
        {
            _logger.LogInformation($"Чтение OpenAPI спецификации из файла {filePath}");

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Файл не найден: {filePath}");
            }

            var content = await File.ReadAllTextAsync(filePath);
            var format = DetectFormat(filePath, null, content);

            return ParseContent(content, format);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Ошибка чтения OpenAPI из файла {filePath}");
            throw;
        }
    }

    private OpenApiDocument? ParseContent(string content, string format)
    {
        var reader = new OpenApiStringReader();
        var document = reader.Read(content, out var diagnostic);

        if (diagnostic.Errors.Any())
        {
            foreach (var error in diagnostic.Errors)
            {
                _logger.LogWarning($"Ошибка парсинга OpenAPI: {error}");
            }
        }

        return document;
    }

    private string DetectFormat(string urlOrPath, HttpResponseMessage? response, string content)
    {
        if (response?.Content.Headers.ContentType != null)
        {
            var contentType = response.Content.Headers.ContentType.MediaType;
            if (contentType.Contains("yaml") || contentType.Contains("x-yaml"))
                return "yaml";
            if (contentType.Contains("json"))
                return "json";
        }

        if (urlOrPath.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) ||
            urlOrPath.EndsWith(".yml", StringComparison.OrdinalIgnoreCase))
            return "yaml";

        if (urlOrPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return "json";

        var trimmed = content.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            return "json";

        if (trimmed.StartsWith("openapi:") || trimmed.StartsWith("swagger:"))
            return "yaml";

        return "yaml";
    }
}