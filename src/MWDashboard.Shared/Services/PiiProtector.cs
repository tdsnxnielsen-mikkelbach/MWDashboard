using System.Security.Cryptography;
using System.Text;

namespace MWDashboard.Shared.Services;

/// <summary>
/// Produces deterministic, non-reversible pseudonyms for personally identifiable information
/// (e.g. user principal names, display names) so per-user report data can be stored and trended
/// without retaining the raw identity.
///
/// The same input + tenant + salt always maps to the same token (so a user can be followed
/// day-over-day), but the token cannot be reversed without the salt. The tenant id is mixed into
/// the HMAC input so the same UPN in two tenants yields different tokens (prevents cross-tenant
/// correlation).
/// </summary>
public static class PiiProtector
{
    /// <summary>
    /// Returns a short (16 hex char / 64-bit) tenant-scoped pseudonym for <paramref name="value"/>,
    /// or an empty string when the value is null/blank.
    /// </summary>
    public static string Pseudonymize(string? value, string tenantId, string salt)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var key = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(salt) ? "mwdashboard" : salt);
        var message = Encoding.UTF8.GetBytes($"{tenantId}|{value.Trim().ToLowerInvariant()}");

        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(message);
        // 8 bytes (64 bits) is ample to avoid collisions within a single tenant while staying compact.
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}
