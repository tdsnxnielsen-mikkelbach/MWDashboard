using Microsoft.EntityFrameworkCore;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Services;

var builder = WebApplication.CreateBuilder(args);

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
});

// Health check endpoint
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();
