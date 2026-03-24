using Microsoft.OpenApi.Models;
using OpenApiMockServer.WebApi.Configure;
using OpenApiMockServer.WebApi.Middleware;
using OpenApiMockServer.WebApi.Models;
using OpenApiMockServer.WebApi.Services;
using System.Text.Json;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = null, Args = args });

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.WriteIndented = true;
        });

        builder.Services.AddSwaggerGen();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddHttpClient("BrowserLikeClient", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Clear();

            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json, application/yaml, */*");
            client.DefaultRequestHeaders.Add("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
            client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
            client.DefaultRequestHeaders.Add("Connection", "keep-alive");
            client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
            client.DefaultRequestHeaders.Add("Pragma", "no-cache");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip |
                                      System.Net.DecompressionMethods.Deflate |
                                      System.Net.DecompressionMethods.Brotli,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            UseCookies = true
        });

        builder.Services.AddSingleton<OpenApiParser>();
        builder.Services.AddSingleton<MockDataGenerator>();
        builder.Services.AddSingleton<RequestValidator>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<AuthAnalyzer>();
        builder.Services.AddSingleton<RelationshipResolver>();
        builder.Services.AddSingleton<MockEndpointBuilder>();
        builder.Services.AddSingleton<OAuthService>();
        builder.Services.AddSingleton<OAuthEndpointBuilder>();

        var app = builder.Build();

        await app.ConfigurateMock();

        app.Run();
    }


}
