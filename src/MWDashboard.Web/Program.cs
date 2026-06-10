using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using MudBlazor.Services;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Services;
using MWDashboard.Web.Components;
using MWDashboard.Web.Services;
using StackExchange.Redis;
using Azure.Monitor.OpenTelemetry.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry with Azure Monitor (distributed tracing, metrics, logging)
var aiConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrEmpty(aiConnectionString))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(options =>
    {
        options.ConnectionString = aiConnectionString;
    });
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Azure AD authentication (restrict dashboard access to allowed tenants)
// Reuse Graph API client secret for user authentication
var homeTenantId = builder.Configuration["AzureAd:TenantId"] ?? "";
builder.Configuration["AzureAdAuth:ClientSecret"] = builder.Configuration["AzureAd:ClientSecret"];
builder.Services.AddMicrosoftIdentityWebAppAuthentication(builder.Configuration, "AzureAdAuth");
builder.Services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
{
    options.ResponseType = "code"; // Use authorization code flow (no implicit ID token required)
    options.Events.OnTokenValidated = async context =>
    {
        var tenantId = context.Principal?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
        if (string.IsNullOrEmpty(tenantId))
        {
            context.Fail("No tenant claim found.");
            return;
        }

        // Home tenant is always allowed
        if (tenantId.Equals(homeTenantId, StringComparison.OrdinalIgnoreCase))
            return;

        // Check if the tenant has been registered (consented) in the database
        var dbFactory = context.HttpContext.RequestServices.GetRequiredService<IDbContextFactory<MauDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var isRegistered = await db.Tenants.AnyAsync(t => t.TenantId == tenantId && t.IsActive);
        if (!isRegistered)
        {
            context.Fail($"Tenant {tenantId} is not registered. Please complete the consent flow first.");
        }
    };
    options.Events.OnRemoteFailure = context =>
    {
        // Show user-friendly message regardless of internal error details
        var userMessage = "Your organization is not authorized to access this application. Please contact your administrator to complete the consent process.";
        context.Response.Redirect("/access-denied?message=" + Uri.EscapeDataString(userMessage));
        context.HandleResponse();
        return Task.CompletedTask;
    };
});
builder.Services.AddControllersWithViews()
    .AddMicrosoftIdentityUI();
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// MudBlazor
builder.Services.AddMudServices();

// EF Core with SQL Server for Azure deployment
builder.Services.AddDbContextFactory<MauDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"), sqlOptions =>
    {
        sqlOptions.CommandTimeout(120);
        sqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: [-2, 4060]);
    }));

// Redis distributed cache + connection multiplexer for pub/sub
var redisConnection = builder.Configuration.GetConnectionString("Redis");
IConnectionMultiplexer? redisMultiplexer = null;
if (!string.IsNullOrEmpty(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = "MWDashboard:";
    });

    // Register IConnectionMultiplexer for pub/sub invalidation
    redisMultiplexer = ConnectionMultiplexer.Connect(redisConnection);
    builder.Services.AddSingleton<IConnectionMultiplexer>(redisMultiplexer);
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

// Redis pub/sub cache invalidation service
builder.Services.AddSingleton<RedisCacheInvalidationService>();

// Output caching for dashboard pages
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(policy => policy.Expire(TimeSpan.FromMinutes(5)));
    options.AddPolicy("Dashboard", policy => policy.Expire(TimeSpan.FromMinutes(15)));
});

// Application services
builder.Services.AddScoped<IGraphReportService, GraphReportService>();
builder.Services.AddScoped<MauDataService>();
builder.Services.AddScoped<IMauDataService>(sp =>
    new CachedMauDataService(
        sp.GetRequiredService<MauDataService>(),
        sp.GetRequiredService<IDistributedCache>(),
        sp.GetRequiredService<RedisCacheInvalidationService>()));
builder.Services.AddScoped<TenantFilterService>();

// On-demand collection: HTTP client to collector container (with local fallback)
var collectorBaseUrl = builder.Configuration["CollectorBaseUrl"];
if (!string.IsNullOrEmpty(collectorBaseUrl))
{
    builder.Services.AddHttpClient<IDataCollectionService, HttpCollectorClient>(client =>
    {
        client.BaseAddress = new Uri(collectorBaseUrl);
        client.Timeout = TimeSpan.FromMinutes(5);
    });
}
else
{
    // Local fallback when no collector URL is configured (dev/local)
    builder.Services.AddScoped<IDataCollectionService, MWDashboard.Shared.Services.OnDemandDataCollectionService>();
}

// Cache warm-up on startup
builder.Services.AddHostedService<CacheWarmupService>();

var app = builder.Build();

// Auto-migrate database on startup
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MauDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Trust forwarded headers from Container Apps ingress (TLS termination at proxy)
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost
};
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseOutputCache();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers(); // Microsoft Identity UI login/logout endpoints

// Access denied page must be accessible without auth (handles rejected tenant logins)
app.MapGet("/access-denied", (HttpContext ctx) =>
{
    var message = ctx.Request.Query["message"].FirstOrDefault() ?? "Your tenant is not authorized to access this application.";
    var encodedMessage = System.Net.WebUtility.HtmlEncode(message);
    return Results.Content(
        "<!DOCTYPE html><html><head><title>Access Denied</title>" +
        "<style>body{font-family:sans-serif;display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0;background:#1e1e2e;color:#fff;}" +
        ".box{text-align:center;padding:2rem;}.box h1{color:#f44336;}a{color:#90caf9;text-decoration:none;padding:0.7rem 1.5rem;border:1px solid #90caf9;border-radius:4px;display:inline-block;margin-top:1.5rem;}</style>" +
        "</head><body><div class=\"box\"><h1>Access Denied</h1><p>" + encodedMessage + "</p>" +
        "<a href=\"MicrosoftIdentity/Account/SignOut\">Sign Out &amp; Try Another Account</a></div></body></html>",
        "text/html");
}).AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization();

// Export endpoint for consumption data (CSV)
app.MapGet("/api/export/consumption", async (IMauDataService dataService, HttpContext ctx) =>
{
    var consumption = await dataService.GetConsumptionAsync(null, months: 12);
    ctx.Response.ContentType = "text/csv";
    ctx.Response.Headers.Append("Content-Disposition", "attachment; filename=consumption-report.csv");

    await using var writer = new StreamWriter(ctx.Response.Body);
    await writer.WriteLineAsync("TenantId,TenantName,ReportDate,ConsumptionScore,ActiveUsers,LicensedUsers,AdoptionPct,StorageUsedGB,AvgWorkloads,TotalActivity");
    foreach (var c in consumption)
    {
        var adoptionPct = c.LicensedUserCount > 0 ? (double)c.ActiveUserCount / c.LicensedUserCount * 100 : 0;
        await writer.WriteLineAsync($"{c.TenantId},{EscapeCsv(c.TenantName)},{c.ReportDate:yyyy-MM-dd},{c.ConsumptionScore:F1},{c.ActiveUserCount},{c.LicensedUserCount},{adoptionPct:F1},{c.StorageUsedBytes / 1073741824.0:F2},{c.AvgWorkloadsPerUser:F2},{c.TotalActivityCount}");
    }
}).RequireAuthorization();

static string EscapeCsv(string value) => value.Contains(',') ? $"\"{value}\"" : value;

app.Run();
