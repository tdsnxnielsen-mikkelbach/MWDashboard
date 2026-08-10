using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    /// <summary>Fallback key used when <c>ApiDocs:ApiKey</c> is not configured.</summary>
    public const string DefaultApiKey = "mwd-scalar-9F3b7Qk2xR8vTn6L";

    /// <summary>Cookie the Web "API" page sets after a successful unlock so same-origin
    /// Scalar/OpenAPI requests (iframe + doc fetch) pass the gate automatically.</summary>
    public const string CookieName = "mwd_apidocs";

    private const string HeaderName = "X-API-Key";
    private const string QueryName = "apiKey";

    public static string GetApiDocsKey(this IConfiguration config) =>
        config[ApiKeyConfigKey] is { Length: > 0 } key ? key : DefaultApiKey;

    public static IServiceCollection AddApiDocs(this IServiceCollection services, string title)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = title;
                document.Info.Version = "v1";
                document.Info.Description =
                    "Protected API reference. Supply the API key via the X-API-Key header, " +
                    "the ?apiKey= query string, or unlock it through the dashboard API page.";
                return Task.CompletedTask;
            });
        });
        return services;
    }

    /// <summary>
    /// Adds the API-key gate for <c>/openapi</c> and <c>/scalar</c>, maps the OpenAPI document,
    /// the Scalar UI, and the unlock endpoint that stores the key in a session cookie.
    /// </summary>
    public static void MapApiDocs(this WebApplication app, string title)
    {
        var key = app.Configuration.GetApiDocsKey();

        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path;
            if ((path.StartsWithSegments("/openapi") || path.StartsWithSegments("/scalar"))
                && !IsAuthorized(ctx, key))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("API key required.");
                return;
            }
            await next();
        });

        app.MapOpenApi();
        app.MapScalarApiReference(options => options.WithTitle(title));

        // Validates the key and stores it in a short-lived cookie so the Scalar UI and its
        // OpenAPI document fetch pass the gate on subsequent same-origin requests.
        app.MapPost("/api-docs/unlock", (HttpContext ctx) =>
        {
            var provided = ctx.Request.HasFormContentType
                ? ctx.Request.Form["key"].ToString()
                : ctx.Request.Query["key"].ToString();

            if (!string.Equals(provided, key, StringComparison.Ordinal))
                return Results.Unauthorized();

            ctx.Response.Cookies.Append(CookieName, key, new CookieOptions
            {
                HttpOnly = true,
                Secure = ctx.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                MaxAge = TimeSpan.FromHours(8)
            });
            return Results.Ok();
        }).DisableAntiforgery();
    }

    private static bool IsAuthorized(HttpContext ctx, string key)
    {
        if (string.Equals(ctx.Request.Headers[HeaderName], key, StringComparison.Ordinal))
            return true;
        if (string.Equals(ctx.Request.Query[QueryName], key, StringComparison.Ordinal))
            return true;
        return ctx.Request.Cookies.TryGetValue(CookieName, out var cookie)
            && string.Equals(cookie, key, StringComparison.Ordinal);
    }
}
