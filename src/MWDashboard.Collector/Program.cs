using Microsoft.EntityFrameworkCore;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Services;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using MWDashboard.ApiDocs;

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
builder.Services.AddScoped<IDataCollectionService, OnDemandDataCollectionService>();

// Scalar OpenAPI reference (key-gated)
builder.Services.AddApiDocs("MW Dashboard Collector API", builder.Configuration["ApiDocs:PublicBasePath"]);

var app = builder.Build();

// Auto-migrate database on startup
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MauDbContext>>();
    await using var db = await dbFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

// Single endpoint: collect data for a tenant
app.MapPost("/collect/{tenantId}", async (string tenantId, HttpContext ctx, IServiceProvider sp) =>
{
    var tenantName = ctx.Request.Query["tenantName"].ToString();
    if (string.IsNullOrEmpty(tenantName))
        return Results.BadRequest("tenantName query parameter is required");

    using var scope = sp.CreateScope();
    var collectionService = scope.ServiceProvider.GetRequiredService<IDataCollectionService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    logger.LogInformation("Collection request received for tenant {TenantName} ({TenantId})", tenantName, tenantId);

    await collectionService.CollectForTenantAsync(tenantId, tenantName, ctx.RequestAborted);

    logger.LogInformation("Collection completed for tenant {TenantName} ({TenantId})", tenantName, tenantId);
    return Results.Ok(new { status = "completed", tenantId, tenantName });
})
.WithName("CollectTenant")
.WithTags("Collection")
.WithSummary("Collect all Microsoft 365 metrics for a single tenant")
.WithDescription(
    "Runs the full on-demand collection pipeline for the given tenant: pulls licenses, usage, " +
    "adoption, security posture, identity and governance metrics from the Microsoft Graph and " +
    "Reports APIs, then upserts the snapshots into the database. Collection is phased and runs " +
    "the independent metric steps concurrently with an adaptive, throttle-aware concurrency gate. " +
    "The `tenantId` is the Entra directory (tenant) GUID; `tenantName` is passed as a query " +
    "parameter and used for logging and display. Returns once collection has completed.")
.Produces(StatusCodes.Status200OK)
.Produces(StatusCodes.Status400BadRequest);

// Health check endpoint
app.MapGet("/health", () => Results.Ok("healthy"))
    .WithName("Health")
    .WithTags("Diagnostics")
    .WithSummary("Liveness/health probe")
    .WithDescription("Returns HTTP 200 with the literal text \"healthy\" when the service is running. Used by Container Apps health probes.");

// OpenAPI document + Scalar UI (key-gated)
app.MapApiDocs("MW Dashboard Collector API");

app.Run();
