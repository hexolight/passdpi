using System.Text.Json.Serialization;

namespace PassDpi.Core.Protocol;

/// <summary>Enum converter that accepts names only (no numeric values), so "command":5 is rejected.</summary>
public sealed class StrictEnumConverter<T> : JsonStringEnumConverter<T> where T : struct, Enum
{
    public StrictEnumConverter() : base(namingPolicy: null, allowIntegerValues: false) { }
}

/// <summary>
/// The complete, closed set of things the UI may ask the service to do.
/// Anything not in this enum is rejected by the deserializer.
/// </summary>
[JsonConverter(typeof(StrictEnumConverter<Command>))]
public enum Command
{
    Hello,
    GetStatus,
    GetLogs,
    StartTest,
    CancelTest,
    Apply,          // ProfileId = built-in id or "custom"; null => automatic (per-network memory)
    Stop,           // disable the engine (persisted)
    SetAutoStart,   // Flag = true => Automatic, false => Manual
    SetTestSites,   // Sites
    SetCustomArgs,  // CustomArgs (validated against the GoodbyeDPI flag whitelist)
    Uninstall,
    Connect,        // start the saved profile right away, no test (manual > this network's memory > last used)
}

[JsonConverter(typeof(JsonStringEnumConverter<MessageKind>))]
public enum MessageKind { Request, Response, Event }

[JsonConverter(typeof(JsonStringEnumConverter<EventType>))]
public enum EventType { Status, TestProgress, Log }

[JsonConverter(typeof(JsonStringEnumConverter<EngineState>))]
public enum EngineState { Off, Starting, Running, Testing, Error }

/// <summary>Single envelope type for requests, responses and pushed events.</summary>
public sealed class PipeMessage
{
    public MessageKind Kind { get; set; }
    public int Id { get; set; }

    // Request
    public Command? Command { get; set; }
    public string? ProfileId { get; set; }
    public bool? Flag { get; set; }
    public List<TestSite>? Sites { get; set; }
    public string? CustomArgs { get; set; }

    // Response
    public bool Ok { get; set; }
    public string? Error { get; set; }

    // Event / response payloads
    public EventType? Event { get; set; }
    public ServiceStatus? Status { get; set; }
    public TestProgress? Progress { get; set; }
    public List<string>? Logs { get; set; }
}

public sealed class TestSite
{
    public string Url { get; set; } = "";
    /// <summary>Control sites are expected to work without the engine; used to tell "blocked" from "offline".</summary>
    public bool IsControl { get; set; }
}

public sealed class ProfileInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Arguments { get; set; } = "";
}

public sealed class ServiceStatus
{
    public string Version { get; set; } = "";
    public EngineState State { get; set; }
    public bool Enabled { get; set; }
    public bool AutoStart { get; set; }
    public bool EngineInstalled { get; set; }
    public string? ActiveProfileId { get; set; }
    public string? ActiveProfileName { get; set; }
    /// <summary>null = automatic (test + per-network memory), otherwise the manually pinned profile.</summary>
    public string? ManualProfileId { get; set; }
    /// <summary>The profile "Connect" would use; null if there is none yet (run a test first).</summary>
    public string? SavedProfileId { get; set; }
    public string? SavedProfileName { get; set; }
    public string? NetworkLabel { get; set; }
    public bool NetworkKnown { get; set; }
    public string? LastMessage { get; set; }
    /// <summary>Machine-readable reason for <see cref="LastMessage"/>, so the UI can localize it.</summary>
    public string? LastCode { get; set; }
    public string? CustomArgs { get; set; }
    public List<TestSite> Sites { get; set; } = new();
    public List<ProfileInfo> Profiles { get; set; } = new();
}

public sealed class TestProgress
{
    public int Index { get; set; }
    public int Total { get; set; }
    public string ProfileId { get; set; } = "";
    public string ProfileName { get; set; } = "";
    /// <summary>Running | Result | Done | Failed | Cancelled</summary>
    public string Phase { get; set; } = "";
    public double? Score { get; set; }
    public int? Ok { get; set; }
    public int? Count { get; set; }
    public string? Message { get; set; }
    /// <summary>For Done/Failed/Cancelled: Found, NoBlocking, AllFailed, NoInternet, EngineBroken, Cancelled.</summary>
    public string? Code { get; set; }
}
