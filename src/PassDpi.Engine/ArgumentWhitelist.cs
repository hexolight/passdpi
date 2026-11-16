using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace PassDpi.Engine;

/// <summary>
/// Accepts only flags documented in the GoodbyeDPI README, each with a strictly validated value.
/// Notably excluded: --blacklist (takes a file path, which would let a user point the SYSTEM
/// process at arbitrary files).
/// </summary>
public static partial class ArgumentWhitelist
{
    private enum Kind { None, Required, Optional }

    private sealed record Rule(Kind Kind, Func<string, bool>? IsValid = null);

    private static Func<string, bool> IntRange(int min, int max) =>
        v => int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= min && n <= max;

    private static bool IsIPv4(string v) =>
        IPAddress.TryParse(v, out var a) && a.AddressFamily == AddressFamily.InterNetwork && v.Count(c => c == '.') == 3;

    private static bool IsIPv6(string v) =>
        IPAddress.TryParse(v, out var a) && a.AddressFamily == AddressFamily.InterNetworkV6 && !v.Contains('%');

    [GeneratedRegex(@"^\d{1,3}-\d{1,3}-\d{1,3}$")] private static partial Regex AutoTtlRx();
    [GeneratedRegex(@"^(?:[0-9A-Fa-f]{2}){1,1024}$")] private static partial Regex HexRx();
    [GeneratedRegex(@"^(?=.{1,253}$)(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,63}$")] private static partial Regex HostRx();

    private static readonly Dictionary<string, Rule> Rules = new(StringComparer.Ordinal)
    {
        ["-p"] = new(Kind.None), ["-q"] = new(Kind.None), ["-r"] = new(Kind.None), ["-s"] = new(Kind.None),
        ["-m"] = new(Kind.None), ["-n"] = new(Kind.None), ["-a"] = new(Kind.None), ["-w"] = new(Kind.None),
        ["-f"] = new(Kind.Required, IntRange(1, 65535)),
        ["-k"] = new(Kind.Required, IntRange(1, 65535)),
        ["-e"] = new(Kind.Required, IntRange(1, 65535)),
        ["--port"] = new(Kind.Required, IntRange(1, 65535)),
        ["--ip-id"] = new(Kind.Required, IntRange(0, 65535)),
        ["--dns-addr"] = new(Kind.Required, IsIPv4),
        ["--dns-port"] = new(Kind.Required, IntRange(1, 65535)),
        ["--dnsv6-addr"] = new(Kind.Required, IsIPv6),
        ["--dnsv6-port"] = new(Kind.Required, IntRange(1, 65535)),
        ["--dns-verb"] = new(Kind.None),
        ["--allow-no-sni"] = new(Kind.None),
        ["--frag-by-sni"] = new(Kind.None),
        ["--set-ttl"] = new(Kind.Required, IntRange(1, 255)),
        ["--auto-ttl"] = new(Kind.Optional, v => AutoTtlRx().IsMatch(v)),
        ["--min-ttl"] = new(Kind.Required, IntRange(1, 255)),
        ["--wrong-chksum"] = new(Kind.None),
        ["--wrong-seq"] = new(Kind.None),
        ["--native-frag"] = new(Kind.None),
        ["--reverse-frag"] = new(Kind.None),
        ["--fake-from-hex"] = new(Kind.Required, v => HexRx().IsMatch(v)),
        ["--fake-with-sni"] = new(Kind.Required, v => HostRx().IsMatch(v)),
        ["--fake-gen"] = new(Kind.Required, IntRange(1, 30)),
        ["--fake-resend"] = new(Kind.Required, IntRange(1, 30)),
        ["--max-payload"] = new(Kind.Optional, IntRange(0, 65535)),
        ["-1"] = new(Kind.None), ["-2"] = new(Kind.None), ["-3"] = new(Kind.None),
        ["-4"] = new(Kind.None), ["-5"] = new(Kind.None), ["-6"] = new(Kind.None),
        ["-7"] = new(Kind.None), ["-8"] = new(Kind.None), ["-9"] = new(Kind.None),
    };

    public const int MaxTokens = 48;
    public const int MaxLength = 4096;

    /// <summary>
    /// Returns the validated argument list, or null with a human-readable <paramref name="error"/>.
    /// Short options with an attached number (e.g. "-e1") are split into "-e" "1".
    /// </summary>
    public static List<string>? Validate(string? input, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(input)) { error = "empty"; return null; }
        if (input.Length > MaxLength) { error = "too long"; return null; }
        if (input.Any(c => char.IsControl(c) || c is '"' or '\'' or '&' or '|' or '<' or '>' or '^' or '%' or '`'))
        { error = "forbidden character"; return null; }

        var tokens = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (tokens.Count > MaxTokens) { error = "too many arguments"; return null; }

        var output = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];

            // "--flag=value" -> "--flag" "value"
            if (t.StartsWith("--", StringComparison.Ordinal) && t.IndexOf('=') is var eq and > 2)
            {
                tokens.Insert(i + 1, t[(eq + 1)..]);
                if (tokens[i + 1].Length == 0) { error = $"{t} needs a value"; return null; }
                t = t[..eq];
                if (Rules.TryGetValue(t, out var rEq) && rEq.Kind == Kind.None) { error = $"{t} takes no value"; return null; }
            }

            // "-e1" -> "-e" "1"
            if (t.Length > 2 && t[0] == '-' && t[1] != '-' && !Rules.ContainsKey(t))
            {
                var flag = t[..2];
                if (Rules.TryGetValue(flag, out var r0) && r0.Kind == Kind.Required)
                {
                    tokens.Insert(i + 1, t[2..]);
                    t = flag;
                }
            }

            if (!Rules.TryGetValue(t, out var rule))
            {
                error = t == "--blacklist" ? "--blacklist is not allowed (file paths are not accepted)" : $"unknown flag: {t}";
                return null;
            }
            switch (rule.Kind)
            {
                case Kind.None:
                    output.Add(t);
                    break;
                case Kind.Required:
                    if (i + 1 >= tokens.Count) { error = $"{t} needs a value"; return null; }
                    var v = tokens[++i];
                    if (!rule.IsValid!(v)) { error = $"invalid value for {t}: {v}"; return null; }
                    output.Add(t);
                    output.Add(v);
                    break;
                case Kind.Optional:
                    // getopt_long optional_argument only binds a value written as --flag=value.
                    if (i + 1 < tokens.Count && !tokens[i + 1].StartsWith('-'))
                    {
                        var ov = tokens[++i];
                        if (!rule.IsValid!(ov)) { error = $"invalid value for {t}: {ov}"; return null; }
                        output.Add($"{t}={ov}");
                    }
                    else output.Add(t);
                    break;
            }
        }
        return output;
    }
}
