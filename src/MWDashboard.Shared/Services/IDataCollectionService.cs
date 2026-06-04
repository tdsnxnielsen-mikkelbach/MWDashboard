namespace MWDashboard.Shared.Services;

public interface IDataCollectionService
{
    Task CollectForTenantAsync(string tenantId, string tenantName, CancellationToken ct = default);
}
