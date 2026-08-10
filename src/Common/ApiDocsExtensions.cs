using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace MWDashboard.ApiDocs;

/// <summary>
/// Shared wiring for the Scalar OpenAPI reference UI. Linked into every web-hosting project
/// so each service exposes its own <c>/openapi/v1.json</c> document and a Scalar UI at
/// <c>/scalar</c>. Both are gated behind a simple API key (config <c>ApiDocs:ApiKey</c>).
/// </summary>
public static class ApiDocsExtensions
{
    public const string ApiKeyConfigKey = "ApiDocs:ApiKey";

    /// <summary>When set (e.g. <c>/proxy/collector</c>), advertised as the OpenAPI server so a
    /// reverse proxy in front of this service builds correct "Send Request" URLs in Scalar.</summary>
    public const string PublicBasePathConfigKey = "ApiDocs:PublicBasePath";

    /// <summary>Fallback key used when <c>ApiDocs:ApiKey</c> is not configured.</summary>
    public const string DefaultApiKey = "mwd-scalar-9F3b7Qk2xR8vTn6L";

    private const string SchemeName = "ApiKey";
    private const string HeaderName = "X-API-Key";
    private const string QueryName = "apiKey";

    public static string GetApiDocsKey(this IConfiguration config) =>
        config[ApiKeyConfigKey] is { Length: > 0 } key ? key : DefaultApiKey;

    public static IServiceCollection AddApiDocs(this IServiceCollection services, string title, string? publicBasePath = null)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = title;
                document.Info.Version = "v1";
                document.Info.Description =
                    "Browse the reference freely. Calling a protected endpoint requires the API key — " +
                    "set it under Authentication (sent as the X-API-Key header).";
                if (!string.IsNullOrWhiteSpace(publicBasePath))
                    document.Servers = [new OpenApiServer { Url = publicBasePath }];

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = HeaderName,
                    Description = "API key sent as the X-API-Key header."
                };
                document.Security ??= [];
                document.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(SchemeName, document)] = new List<string>()
                });
                return Task.CompletedTask;
            });
        });
        return services;
    }

    /// <summary>
    /// Maps the OpenAPI document and Scalar UI (optionally aggregating extra proxied documents),
    /// both open to browse. Only actual proxied API <em>calls</em> (<c>/proxy/*</c>) require the
    /// API key, supplied as the X-API-Key header (Scalar's Authentication) or the ?apiKey= query.
    /// </summary>
    public static void MapApiDocs(this WebApplication app, string title,
        (string Name, string Title, string RoutePattern)[]? extraDocuments = null)
    {
        var key = app.Configuration.GetApiDocsKey();

        // Browsing the reference is open; the key is enforced only on real proxied calls, never on
        // the proxied OpenAPI document itself (so the reference can load without a key).
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path;
            if (path.StartsWithSegments("/proxy", out var remaining)
                && !remaining.Value!.Contains("/openapi", StringComparison.OrdinalIgnoreCase)
                && !IsAuthorized(ctx, key))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("API key required. Set X-API-Key under Authentication in the API reference.");
                return;
            }
            await next();
        });

        app.MapOpenApi();
        app.MapScalarApiReference(options =>
        {
            options.WithTitle(title);
            if (extraDocuments is { Length: > 0 })
            {
                options.AddDocument("v1", title);
                foreach (var (name, docTitle, route) in extraDocuments)
                    options.AddDocument(name, docTitle, route);
            }
        });
    }

    private static bool IsAuthorized(HttpContext ctx, string key) =>
        string.Equals(ctx.Request.Headers[HeaderName], key, StringComparison.Ordinal)
        || string.Equals(ctx.Request.Query[QueryName], key, StringComparison.Ordinal);
}
