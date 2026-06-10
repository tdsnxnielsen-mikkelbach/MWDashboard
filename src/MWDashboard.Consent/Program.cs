using Microsoft.EntityFrameworkCore;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Services;
using MWDashboard.Shared.Models;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Graph;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// OpenTelemetry with Azure Monitor
var aiConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrEmpty(aiConnectionString))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor(options =>
    {
        options.ConnectionString = aiConnectionString;
    });
}

// EF Core with SQL Server
builder.Services.AddDbContextFactory<MauDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"), sqlOptions =>
    {
        sqlOptions.CommandTimeout(120);
        sqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: [-2, 4060]);
    }));

// Data collection service for initial collection trigger
builder.Services.AddScoped<IGraphReportService, GraphReportService>();
builder.Services.AddScoped<IMauDataService, MauDataService>();
builder.Services.AddScoped<IDataCollectionService, OnDemandDataCollectionService>();

// CORS for static web app
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Auto-migrate database on startup
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MauDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

app.UseCors();

// Consent callback endpoint
app.MapPost("/consent-callback", async (HttpContext ctx, IServiceProvider sp, IConfiguration config, ILogger<Program> logger) =>
{
    var tenantId = ctx.Request.Query["tenant"].ToString();
    var token = ctx.Request.Query["token"].ToString();

    if (string.IsNullOrEmpty(tenantId))
        return Results.BadRequest(new { error = "tenant query parameter is required" });

    // Validate HMAC token
    var secret = config["ConsentCallback:SharedSecret"];
    if (string.IsNullOrEmpty(secret))
        return Results.StatusCode(500);

    var expectedToken = ComputeHmac(secret, tenantId);
    if (!CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(token),
        Encoding.UTF8.GetBytes(expectedToken)))
    {
        logger.LogWarning("Invalid token for consent callback, tenant: {TenantId}", tenantId);
        return Results.Unauthorized();
    }

    logger.LogInformation("Consent callback received for tenant {TenantId}", tenantId);

    // Verify consent by calling Graph API for this tenant
    string tenantName;
    string displayName;
    try
    {
        var credential = new ClientSecretCredential(
            tenantId,
            config["AzureAd:ClientId"],
            config["AzureAd:ClientSecret"]);

        var graphClient = new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
        var org = await graphClient.Organization.GetAsync();
        var orgInfo = org?.Value?.FirstOrDefault();

        if (orgInfo == null)
            return Results.Problem("Could not retrieve organization info — consent may not have been granted.");

        // Get the *.onmicrosoft.com domain
        tenantName = orgInfo.VerifiedDomains?
            .FirstOrDefault(d => d.Name?.EndsWith(".onmicrosoft.com") == true && !d.Name.Contains(".mail."))?.Name
            ?? orgInfo.VerifiedDomains?.FirstOrDefault()?.Name
            ?? tenantId;

        // Display name = org display name, or derive from onmicrosoft.com domain
        displayName = orgInfo.DisplayName
            ?? tenantName.Replace(".onmicrosoft.com", "");
    }
    catch (ServiceException ex)
    {
        logger.LogError(ex, "Graph API call failed for tenant {TenantId} — consent may not be valid", tenantId);
        return Results.Problem($"Could not verify consent for tenant. Graph API returned: {ex.Message}");
    }

    // Upsert TenantInfo
    using var dbScope = sp.CreateScope();
    var dbFactory = dbScope.ServiceProvider.GetRequiredService<IDbContextFactory<MauDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();

    var existing = await db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId);
    if (existing != null)
    {
        existing.TenantName = tenantName;
        existing.DisplayName = displayName;
        existing.IsActive = true;
    }
    else
    {
        db.Tenants.Add(new TenantInfo
        {
            TenantId = tenantId,
            TenantName = tenantName,
            DisplayName = displayName,
            IsActive = true,
            OnboardedAt = DateTime.UtcNow
        });
    }
    await db.SaveChangesAsync();

    logger.LogInformation("Tenant {TenantId} ({DisplayName}) registered via consent callback", tenantId, displayName);

    // Trigger initial data collection
    try
    {
        var collectionService = dbScope.ServiceProvider.GetRequiredService<IDataCollectionService>();
        _ = Task.Run(async () =>
        {
            try
            {
                await collectionService.CollectForTenantAsync(tenantId, tenantName);
                logger.LogInformation("Initial data collection completed for tenant {TenantId}", tenantId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Initial data collection failed for tenant {TenantId}", tenantId);
            }
        });
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not trigger initial collection for tenant {TenantId}", tenantId);
    }

    return Results.Ok(new { status = "registered", tenantId, tenantName, displayName });
});

// Health check endpoint
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();

static string ComputeHmac(string secret, string tenantId)
{
    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(tenantId));
    return Convert.ToHexStringLower(hash);
}
