using PassDpi.Core.Protocol;

namespace PassDpi.Core.Validation;

/// <summary>Validates the user-editable test site list before the service stores or uses it.</summary>
public static class SiteValidator
{
    public const int MaxSites = 20;
    public const int MaxUrlLength = 200;

    public static bool TryNormalize(string? input, out string url)
    {
        url = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        var s = input.Trim();
        if (s.Length > MaxUrlLength) return false;
        if (!s.Contains("://", StringComparison.Ordinal)) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttps) return false;           // HTTPS only: we judge success by a valid TLS handshake
        if (!string.IsNullOrEmpty(u.UserInfo)) return false;
        if (u.HostNameType != UriHostNameType.Dns) return false;    // hostnames only, no raw IPs
        if (!u.IsDefaultPort) return false;
        if (u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || !u.Host.Contains('.')) return false;
        url = u.GetLeftPart(UriPartial.Path);
        return true;
    }

    public static List<TestSite>? Normalize(IEnumerable<TestSite>? sites, out string? error)
    {
        error = null;
        if (sites == null) { error = "empty"; return null; }
        var result = new List<TestSite>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in sites)
        {
            if (!TryNormalize(s.Url, out var url)) { error = $"invalid: {s.Url}"; return null; }
            if (seen.Add(url)) result.Add(new TestSite { Url = url, IsControl = s.IsControl });
        }
        if (result.Count == 0) { error = "empty"; return null; }
        if (result.Count > MaxSites) { error = "too many"; return null; }
        if (!result.Any(s => s.IsControl)) { error = "need at least one control site"; return null; }
        if (!result.Any(s => !s.IsControl)) { error = "need at least one blocked-site candidate"; return null; }
        return result;
    }

    public static List<TestSite> Defaults() => new()
    {
        new() { Url = "https://discord.com/" },
        new() { Url = "https://www.roblox.com/" },
        new() { Url = "https://www.wattpad.com/" },
        new() { Url = "https://www.google.com/generate_204", IsControl = true },
        new() { Url = "https://www.cloudflare.com/", IsControl = true },
        // More controls make it likelier to notice a profile that breaks normal sites.
        new() { Url = "https://github.com/", IsControl = true },
    };
}
