using System.Text.Json.Serialization;
using PassDpi.Core.Protocol;

namespace PassDpi.Settings;

public sealed class ServiceSettings
{
    /// <summary>The on/off switch in the UI. When false the engine never runs.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>SID of the user who installed PassDPI; the only account allowed on the control pipe.</summary>
    public string OwnerSid { get; set; } = "";

    /// <summary>null = automatic mode. Otherwise a profile id the user pinned manually.</summary>
    public string? ManualProfileId { get; set; }

    /// <summary>Whitelisted custom GoodbyeDPI arguments (already validated), or null.</summary>
    public string? CustomArgs { get; set; }

    public List<TestSite> Sites { get; set; } = new();
}

public sealed class NetworkRecord
{
    public string ProfileId { get; set; } = "";
    public double Score { get; set; }
    public DateTime TestedUtc { get; set; }
}

public sealed class NetworkMemoryFile
{
    /// <summary>Key = salted SHA-256 of the network fingerprint (hex).</summary>
    public Dictionary<string, NetworkRecord> Networks { get; set; } = new();
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ServiceSettings))]
[JsonSerializable(typeof(NetworkMemoryFile))]
internal partial class SettingsJsonContext : JsonSerializerContext { }
