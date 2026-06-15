using Microsoft.EntityFrameworkCore;
using MWDashboard.CopilotAudit;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Services;
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

// EF Core with SQL Server
builder.Services.AddDbContextFactory<MauDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"), sqlOptions =>
    {
        sqlOptions.CommandTimeout(120);
        sqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: [-2, 4060]);
    }));

// Application services
builder.Services.AddScoped<IGraphReportService, GraphReportService>();
builder.Services.AddScoped<IMauDataService, MauDataService>();
builder.Services.AddHttpClient<IManagementActivityClient, ManagementActivityClient>();
builder.Services.AddScoped<ICopilotAuditCollectionService, CopilotAuditCollectionService>();
builder.Services.AddScoped<IExternalSharingCollectionService, ExternalSharingCollectionService>();
builder.Services.AddScoped<IMailRuleAuditCollectionService, MailRuleAuditCollectionService>();
builder.Services.AddScoped<IDlpAuditCollectionService, DlpAuditCollectionService>();

// Internal cron scheduler — advances the per-tenant cursor on a fixed interval.
builder.Services.AddHostedService<CopilotAuditScheduleService>();

var app = builder.Build();

// Auto-migrate database on startup
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MauDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

// On-demand collection for a single tenant (mirrors the Collector container's contract).
app.MapPost("/collect/{tenantId}", async (string tenantId, HttpContext ctx, IServiceProvider sp) =>
{
    var tenantName = ctx.Request.Query["tenantName"].ToString();
    if (string.IsNullOrEmpty(tenantName))
        return Results.BadRequest("tenantName query parameter is required");

    using var scope = sp.CreateScope();
    var collectionService = scope.ServiceProvider.GetRequiredService<ICopilotAuditCollectionService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    logger.LogInformation("Copilot Chat audit collection request received for tenant {TenantName} ({TenantId})", tenantName, tenantId);

    await collectionService.CollectForTenantAsync(tenantId, tenantName, ctx.RequestAborted);

    logger.LogInformation("Copilot Chat audit collection completed for tenant {TenantName} ({TenantId})", tenantName, tenantId);

    // External sharing audit shares the same Management Activity API plumbing — collect it too.
    var sharingService = scope.ServiceProvider.GetRequiredService<IExternalSharingCollectionService>();
    try
    {
        await sharingService.CollectForTenantAsync(tenantId, tenantName, ctx.RequestAborted);
    }
    catch (CopilotAuditConfigurationException ex)
    {
        logger.LogWarning("External sharing audit not yet available for tenant {TenantName} ({TenantId}): {Message}", tenantName, tenantId, ex.Message);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "External sharing audit collection failed for tenant {TenantName} ({TenantId})", tenantName, tenantId);
    }

    // Suspicious mail-rule / auto-forwarding audit (Audit.Exchange) — same plumbing.
    var mailRuleService = scope.ServiceProvider.GetRequiredService<IMailRuleAuditCollectionService>();
    try
    {
        await mailRuleService.CollectForTenantAsync(tenantId, tenantName, ctx.RequestAborted);
    }
    catch (CopilotAuditConfigurationException ex)
    {
        logger.LogWarning("Mail-rule audit not yet available for tenant {TenantName} ({TenantId}): {Message}", tenantName, tenantId, ex.Message);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Mail-rule audit collection failed for tenant {TenantName} ({TenantId})", tenantName, tenantId);
    }

    // DLP policy matches (DLP.All) — same plumbing. Tenants without DLP simply yield no data.
    var dlpService = scope.ServiceProvider.GetRequiredService<IDlpAuditCollectionService>();
    try
    {
        await dlpService.CollectForTenantAsync(tenantId, tenantName, ctx.RequestAborted);
    }
    catch (CopilotAuditConfigurationException ex)
    {
        logger.LogWarning("DLP audit not yet available for tenant {TenantName} ({TenantId}): {Message}", tenantName, tenantId, ex.Message);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "DLP audit collection failed for tenant {TenantName} ({TenantId})", tenantName, tenantId);
    }

    return Results.Ok(new { status = "completed", tenantId, tenantName });
});

// Health check endpoint
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();
