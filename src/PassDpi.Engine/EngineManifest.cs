namespace PassDpi.Engine;

/// <summary>
/// The one GoodbyeDPI build PassDPI accepts. The official release has no checksum file, so the
/// hashes below were computed from the asset downloaded from the official GitHub release and
/// are pinned here. Bumping the engine version is a deliberate code change.
/// </summary>
public static class EngineManifest
{
    public const string Version = "0.2.3rc3";

    public const string ZipUrl =
        "https://github.com/ValdikSS/GoodbyeDPI/releases/download/0.2.3rc3/goodbyedpi-0.2.3rc3-2.zip";

    public const string ZipSha256 = "37F96B32D050DADCC930A639EBA68E1CCD57ED5C04A5F77DFCA908F01905A4C5";

    /// <summary>Folder inside the zip that holds the 64-bit binaries.</summary>
    public const string ZipX64Folder = "goodbyedpi-0.2.3rc3-2/x86_64/";
    public const string ZipLicenseFolder = "goodbyedpi-0.2.3rc3-2/licenses/";

    public const string ExeName = "goodbyedpi.exe";

    /// <summary>Every file that must exist in the engine folder, with its SHA-256.</summary>
    public static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["goodbyedpi.exe"] = "8D412B094BB9C137FF25BA9A794D1122ECC84BB776DEBFF6C249723A13CC31CD",
        ["WinDivert.dll"] = "6110BFA44667405179C3E15E12AF1B62037E447ED59B054B19042032995E6C7E",
        ["WinDivert64.sys"] = "E69B5BA3F0CD6CFB2983E442636E7F0B342B61B15264B0328317D4559C82CF50",
    };
}
