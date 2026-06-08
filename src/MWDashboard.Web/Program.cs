using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using MudBlazor.Services;
using MWDashboard.Shared.Data;
using MWDashboard.Shared.Services;
using MWDashboard.Web.Components;
using MWDashboard.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MudBlazor
builder.Services.AddMudServices();

// EF Core with SQL Server for Azure deployment
builder.Services.AddDbContextFactory<MauDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"), sqlOptions =>
    {
        sqlOptions.CommandTimeout(60);
        sqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null);
    }));

// Redis distributed cache
var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrEmpty(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = "MWDashboard:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

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
        sp.GetRequiredService<IDistributedCache>()));
builder.Services.AddScoped<TenantFilterService>();
builder.Services.AddScoped<IDataCollectionService, OnDemandDataCollectionService>();

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

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseOutputCache();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

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
});

static string EscapeCsv(string value) => value.Contains(',') ? $"\"{value}\"" : value;

app.Run();
