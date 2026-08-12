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

        // GET /api/v1/data/{feature} — query one dataset as JSON (optionally scoped to one tenant)
        group.MapGet("data/{feature}", async (string feature, string? tenantId, IMauDataService data, HttpContext ctx) =>
        {
            var resolved = (ResolvedScope)ctx.Items[ScopeItemKey]!;
            if (!TryScopeToTenant(resolved, tenantId, out var scope, out var error))
                return error!;

            var table = await ExportEndpoints.BuildJsonTableAsync(feature, data, scope);
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
            "(e.g. `licenses`, `mau`, `consumption`, `secure-scores`). All values are strings. Pass an " +
            "optional `?tenantId={guid}` to restrict the result to a single tenant — useful with an " +
            "unrestricted (home/admin) key that would otherwise return every tenant. Tenant scope is " +
            "still enforced from the read-API key: a tenant-bound key may only ever request its own " +
            "tenant (any other `tenantId` yields 403). Returns 404 for an unknown feature key, 400 for " +
            "a malformed `tenantId`.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // GET /api/v1/tenants — onboarding/status directory (no dataset pull)
        group.MapGet("tenants", async (IMauDataService data, HttpContext ctx) =>
        {
            var resolved = (ResolvedScope)ctx.Items[ScopeItemKey]!;
            var directory = await data.GetTenantDirectoryAsync(resolved.TenantIds);
            return Results.Ok(directory);
        })
        .WithName("ListTenants")
        .WithSummary("List onboarded tenants and their status")
        .WithDescription(
            "Returns the onboarding/status directory of registered tenants — `tenantId`, `tenantName`, " +
            "`isActive`, `onboardedAt`, `missingPermissions` (array) and `lastCollectedAt` (null when a " +
            "tenant is onboarded but no data has been collected yet) — without pulling any dataset. " +
            "Inactive/offboarded tenants are included so callers can distinguish them. Tenant scope is " +
            "derived from the read-API key: an unrestricted (home/admin) key lists all tenants; a " +
            "tenant-bound key lists only its own.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        // GET /api/v1/summary/{tenantId} — per-tenant headline (mau + licenses + copilot in one call)
        group.MapGet("summary/{tenantId}", async (string tenantId, IMauDataService data, HttpContext ctx) =>
        {
            var resolved = (ResolvedScope)ctx.Items[ScopeItemKey]!;
            if (!TryScopeToTenant(resolved, tenantId, out var scope, out var error))
                return error!;

            var mau = await data.GetLatestMauByServiceAsync(scope);
            var licenses = await data.GetLatestLicensesAsync(scope);
            var copilot = await data.GetCopilotUsageAsync(scope);

            var stamps = mau.Select(m => m.CollectedAt)
                .Concat(licenses.Select(l => l.CollectedAt))
                .Concat(copilot.Select(c => c.CollectedAt))
                .ToList();
            DateTime? asOf = stamps.Count > 0 ? stamps.Max() : null;

            return Results.Ok(new
            {
                tenantId,
                asOf,
                mau = mau
                    .OrderBy(m => m.ServiceName, StringComparer.Ordinal)
                    .Select(m => new { m.ServiceName, m.ActiveUserCount }),
                licenses = licenses
                    .OrderBy(l => l.SkuPartNumber, StringComparer.Ordinal)
                    .Select(l => new
                    {
                        l.SkuPartNumber,
                        l.SkuId,
                        l.TotalLicenses,
                        l.ConsumedLicenses,
                        utilizationPct = l.TotalLicenses > 0
                            ? Math.Round((double)l.ConsumedLicenses / l.TotalLicenses * 100, 2)
                            : 0
                    }),
                copilot = copilot
                    .OrderBy(c => c.AppName, StringComparer.Ordinal)
                    .Select(c => new { c.AppName, c.ActiveUsers, c.TotalAssignedLicenses })
            });
        })
        .WithName("GetTenantSummary")
        .WithSummary("Per-tenant headline summary")
        .WithDescription(
            "Returns a single tenant's headline metrics in one call: latest-month `mau` (per service), " +
            "`licenses` (per SKU with `skuId` and computed `utilizationPct`) and `copilot` (per app). " +
            "`asOf` is the most recent collection timestamp across the three datasets. Tenant scope is " +
            "enforced from the read-API key: a tenant-bound key may only request its own tenant (any " +
            "other `{tenantId}` yields 403). Returns 400 for a malformed `tenantId`.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);
    }

    /// <summary>
    /// Narrows the key-resolved scope to a single tenant from client-supplied <paramref name="tenantId"/>,
    /// enforcing data isolation: an unrestricted (admin) key may target any tenant; a tenant-bound key
    /// may only ever target its own tenant. Returns false with an <paramref name="error"/> result on a
    /// malformed GUID (400) or a cross-tenant request (403). When <paramref name="tenantId"/> is null/blank,
    /// the original key scope is preserved unchanged.
    /// </summary>
    private static bool TryScopeToTenant(
        ResolvedScope resolved, string? tenantId, out IEnumerable<string>? scope, out IResult? error)
    {
        scope = resolved.TenantIds;
        error = null;

        if (string.IsNullOrWhiteSpace(tenantId))
            return true;

        if (!Guid.TryParse(tenantId, out _))
        {
            error = Results.BadRequest(new { error = "tenantId must be a GUID." });
            return false;
        }

        if (resolved.TenantIds is not null &&
            !resolved.TenantIds.Contains(tenantId, StringComparer.OrdinalIgnoreCase))
        {
            error = Results.Problem(
                "This API key is not authorized for the requested tenant.",
                statusCode: StatusCodes.Status403Forbidden);
            return false;
        }

        scope = [tenantId];
        return true;
    }
}
