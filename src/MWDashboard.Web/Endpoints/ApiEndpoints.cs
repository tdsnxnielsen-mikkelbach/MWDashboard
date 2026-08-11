using MWDashboard.Shared.Services;
using MWDashboard.Web.Services;

namespace MWDashboard.Web.Endpoints;

/// <summary>
/// Programmatic JSON read API for the collected metrics — the read path counterpart to the
/// write-only collector. Exposes the same datasets as the CSV export (single source of truth:
/// the <c>Exports</c> registry in <see cref="ExportEndpoints"/>) as JSON. Authenticated with a
/// per-tenant read-API key (<c>X-API-Key</c> header); tenant scope is resolved server-side from
/// the key via <see cref="ReadApiKeyStore"/> and never from client input.
/// </summary>
public static class ApiEndpoints
{
    private const string HeaderName = "X-API-Key";
    private const string ScopeItemKey = "ReadApi:Scope";

    private sealed record ResolvedScope(IEnumerable<string>? TenantIds, string KeyName);

    public static void MapReadApiEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1")
            .AllowAnonymous()
            .AddEndpointFilter(async (ctx, next) =>
            {
                var store = ctx.HttpContext.RequestServices.GetRequiredService<ReadApiKeyStore>();
                if (!store.Enabled)
                    return Results.Problem("Read API is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

                var presented = ctx.HttpContext.Request.Headers[HeaderName].ToString();
                if (!store.TryResolve(presented, out var scope, out var name))
                    return Results.Problem("Invalid or missing API key.", statusCode: StatusCodes.Status401Unauthorized);

                ctx.HttpContext.Items[ScopeItemKey] = new ResolvedScope(scope, name);
                return await next(ctx);
            })
            .WithTags("Data API");

        // GET /api/v1/datasets — discover available dataset keys + columns
        group.MapGet("datasets", () =>
        {
            var datasets = ExportEndpoints.FeatureNames
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(f =>
                {
                    ExportEndpoints.TryGetColumns(f, out var columns);
                    return new { feature = f, columns };
                });
            return Results.Ok(new { datasets });
        })
        .WithName("ListDatasets")
        .WithSummary("List available datasets")
        .WithDescription(
            "Returns every dataset feature key and its column names. Use a feature key with " +
            "/api/v1/data/{feature} to query the rows. Requires a valid read-API key (X-API-Key header).")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        // GET /api/v1/data — every dataset in one payload (all tenants when using the admin key)
        group.MapGet("data", async (IMauDataService data, HttpContext ctx) =>
        {
            var resolved = (ResolvedScope)ctx.Items[ScopeItemKey]!;
            var datasets = new List<object>();
            foreach (var feature in ExportEndpoints.FeatureNames.OrderBy(f => f, StringComparer.Ordinal))
            {
                var table = await ExportEndpoints.BuildJsonTableAsync(feature, data, resolved.TenantIds);
                if (table is null)
                    continue;
                var (columns, rows) = table.Value;
                datasets.Add(new { feature, columns, rowCount = rows.Count, rows });
            }
            return Results.Ok(new { datasetCount = datasets.Count, datasets });
        })
        .WithName("GetAllData")
        .WithSummary("Query every dataset as JSON")
        .WithDescription(
            "Returns all datasets in a single response, each as JSON objects keyed by column name. " +
            "Tenant scope is derived from the read-API key: an unrestricted (home/admin) key returns " +
            "every tenant's data across all datasets; a tenant-bound key is limited to its own tenant. " +
            "Intended for internal system-to-system consumption — the payload can be large.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        // GET /api/v1/data/{feature} — query one dataset as JSON
        group.MapGet("data/{feature}", async (string feature, IMauDataService data, HttpContext ctx) =>
        {
            var resolved = (ResolvedScope)ctx.Items[ScopeItemKey]!;
            var table = await ExportEndpoints.BuildJsonTableAsync(feature, data, resolved.TenantIds);
            if (table is null)
                return Results.NotFound(new { error = $"Unknown dataset '{feature}'." });

            var (columns, rows) = table.Value;
            return Results.Ok(new { feature, columns, rowCount = rows.Count, rows });
        })
        .WithName("GetDataset")
        .WithSummary("Query a dataset as JSON")
        .WithDescription(
            "Returns the collected snapshot rows for the given dataset as JSON objects keyed by " +
            "column name. The `{feature}` route value is one of the keys returned by /api/v1/datasets " +
            "(e.g. `licenses`, `mau`, `consumption`, `secure-scores`). All values are strings. Tenant " +
            "scope is derived from the read-API key: a tenant-bound key only ever returns its own " +
            "tenant's data; an unrestricted (home/admin) key returns all tenants. Returns 404 for an " +
            "unknown feature key.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound);
    }
}
