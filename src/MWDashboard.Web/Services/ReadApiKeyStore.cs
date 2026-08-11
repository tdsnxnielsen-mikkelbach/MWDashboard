using System.Security.Cryptography;
using System.Text;

namespace MWDashboard.Web.Services;

/// <summary>
/// Resolves programmatic read-API keys to a tenant data scope. Keys are configured under
/// <c>ReadApi:Keys</c>, each with a <c>Key</c>, an optional <c>TenantId</c>, and a <c>Name</c>.
/// A key bound to a tenant is restricted to that tenant; a key with an empty or <c>"all"</c>
/// TenantId is unrestricted (home/admin). Presented keys are matched in constant time against
/// the SHA-256 hash of each configured key, so tenant scope is always derived server-side —
/// never from client input.
/// </summary>
public sealed class ReadApiKeyStore
{
    private sealed record KeyEntry(byte[] Hash, string? TenantId, string Name);

    private readonly List<KeyEntry> _keys = [];

    public ReadApiKeyStore(IConfiguration config)
    {
        foreach (var child in config.GetSection("ReadApi:Keys").GetChildren())
        {
            var key = child["Key"];
            if (string.IsNullOrWhiteSpace(key))
                continue;

            var tenantId = child["TenantId"];
            if (string.IsNullOrWhiteSpace(tenantId) || tenantId.Equals("all", StringComparison.OrdinalIgnoreCase))
                tenantId = null;

            var name = child["Name"] ?? "unnamed";
            _keys.Add(new KeyEntry(SHA256.HashData(Encoding.UTF8.GetBytes(key)), tenantId, name));
        }
    }

    /// <summary>True when at least one read-API key is configured.</summary>
    public bool Enabled => _keys.Count > 0;

    /// <summary>
    /// Validates the presented key in constant time. On success returns the resolved tenant
    /// scope (<c>null</c> = all tenants) and the key's friendly name.
    /// </summary>
    public bool TryResolve(string? presentedKey, out IEnumerable<string>? tenantScope, out string keyName)
    {
        tenantScope = null;
        keyName = string.Empty;
        if (string.IsNullOrEmpty(presentedKey))
            return false;

        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(presentedKey));
        KeyEntry? match = null;
        // Scan every entry without early-exit to keep timing independent of match position.
        foreach (var entry in _keys)
        {
            if (CryptographicOperations.FixedTimeEquals(entry.Hash, presentedHash))
                match = entry;
        }
        if (match is null)
            return false;

        tenantScope = match.TenantId is null ? null : [match.TenantId];
        keyName = match.Name;
        return true;
    }
}
