namespace PassDpi.Engine;

public sealed record EngineProfile(string Id, string Name, IReadOnlyList<string> Args)
{
    public string ArgsText => string.Join(' ', Args);
}

/// <summary>
/// Built-in profiles. Flags and modesets come from the official GoodbyeDPI README
/// (https://github.com/ValdikSS/GoodbyeDPI#how-to-use). DNS variants mirror the bundled
/// "2_any_country_dnsredir.cmd" (Yandex DNS on port 1253) plus Cloudflare on 53.
/// </summary>
public static class ProfileCatalog
{
    public const string CustomId = "custom";

    private sealed record Dns(string Id, string Label, string[] Args);

    private static readonly Dns[] DnsVariants =
    {
        // Fastest first: Cloudflare answers in ~15 ms where Yandex :1253 can add ~100 ms to every
        // uncached lookup. The test stops at the first profile that opens every site, and a poisoned
        // or hijacked port-53 answer fails the certificate check, so the test falls through to Yandex.
        new("cfdns", "Cloudflare DNS", new[] { "--dns-addr", "1.1.1.1", "--dns-port", "53", "--dnsv6-addr", "2606:4700:4700::1111", "--dnsv6-port", "53" }),
        // From the official 2_any_country_dnsredir.cmd; the non-standard port survives port-53 hijacking.
        new("ydns", "Yandex DNS :1253", new[] { "--dns-addr", "77.88.8.8", "--dns-port", "1253", "--dnsv6-addr", "2a02:6b8::feed:0ff", "--dnsv6-port", "1253" }),
        new("", "", Array.Empty<string>()),
    };

    // Order = test order. -9 is the README's recommended default; modern modes before legacy ones.
    private static readonly (int Mode, string Label)[] Modes =
    {
        (9, "Modern -9 (QUIC block)"),
        (5, "Modern -5 (auto-ttl)"),
        (6, "Modern -6 (wrong-seq)"),
        (7, "Modern -7 (wrong-chksum)"),
        (8, "Modern -8 (seq+chksum)"),
        (1, "Legacy -1 (most compatible)"),
        (2, "Legacy -2"),
        (3, "Legacy -3"),
        (4, "Legacy -4 (fastest)"),
    };

    public static readonly IReadOnlyList<EngineProfile> BuiltIn = Build();

    private static List<EngineProfile> Build()
    {
        var list = new List<EngineProfile>();
        foreach (var (mode, label) in Modes)
            foreach (var dns in DnsVariants)
            {
                var id = dns.Id.Length == 0 ? $"m{mode}" : $"m{mode}-{dns.Id}";
                var name = dns.Label.Length == 0 ? label : $"{label} + {dns.Label}";
                var args = new List<string> { $"-{mode}" };
                args.AddRange(dns.Args);
                list.Add(new EngineProfile(id, name, args));
            }
        return list;
    }

    public static EngineProfile? Find(string? id, string? customArgs)
    {
        if (id == null) return null;
        if (id == CustomId)
        {
            if (customArgs == null) return null;
            var parsed = ArgumentWhitelist.Validate(customArgs, out _);
            return parsed == null ? null : new EngineProfile(CustomId, "Custom", parsed);
        }
        return BuiltIn.FirstOrDefault(p => p.Id == id);
    }
}
