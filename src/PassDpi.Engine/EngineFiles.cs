using System.IO.Compression;
using System.Security.Cryptography;
using PassDpi.Core;

namespace PassDpi.Engine;

/// <summary>
/// Gets the pinned GoodbyeDPI binaries into the protected engine folder and verifies them.
/// Files are read fully into memory, hashed, and the very same bytes are written out, so
/// nothing can be swapped between "verify" and "copy".
/// </summary>
public static class EngineFiles
{
    public static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    /// <summary>True when every pinned file exists in <paramref name="dir"/> with the right hash.</summary>
    public static bool Verify(string dir, out string? problem)
    {
        problem = null;
        foreach (var (name, sha) in EngineManifest.Files)
        {
            var path = Path.Combine(dir, name);
            if (!File.Exists(path)) { problem = $"missing {name}"; return false; }
            byte[] data;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (fs.Length > 8 * 1024 * 1024) { problem = $"{name} too large"; return false; }
                data = new byte[fs.Length];
                fs.ReadExactly(data);
            }
            catch (IOException ex) { problem = $"{name}: {ex.Message}"; return false; }
            if (!string.Equals(Hash(data), sha, StringComparison.OrdinalIgnoreCase))
            { problem = $"hash mismatch: {name}"; return false; }
        }
        return true;
    }

    /// <summary>Downloads the pinned release zip from GitHub and installs the x64 files.</summary>
    public static async Task InstallFromGitHubAsync(string engineDir, ILog log, CancellationToken ct)
    {
        log.Info($"Downloading GoodbyeDPI {EngineManifest.Version} from the official GitHub release");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PassDPI");
        var zip = await http.GetByteArrayAsync(EngineManifest.ZipUrl, ct).ConfigureAwait(false);
        InstallFromZipBytes(zip, engineDir, log);
    }

    internal static void InstallFromZipBytes(byte[] zip, string engineDir, ILog log)
    {
        var actual = Hash(zip);
        if (!string.Equals(actual, EngineManifest.ZipSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Release zip SHA-256 mismatch (got {actual}).");
        log.Info("Release zip SHA-256 verified");

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var licenses = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
        {
            foreach (var e in archive.Entries)
            {
                if (e.FullName.StartsWith(EngineManifest.ZipX64Folder, StringComparison.Ordinal) &&
                    EngineManifest.Files.ContainsKey(e.Name))
                    files[e.Name] = ReadEntry(e);
                else if (e.FullName.StartsWith(EngineManifest.ZipLicenseFolder, StringComparison.Ordinal) &&
                         e.Name.StartsWith("LICENSE-", StringComparison.Ordinal) && e.Name.EndsWith(".txt", StringComparison.Ordinal))
                    licenses[e.Name] = ReadEntry(e);
            }
        }
        WriteVerified(files, licenses, engineDir, log);
    }

    /// <summary>
    /// Installs from an existing GoodbyeDPI folder (its root or its x86_64 subfolder).
    /// Only the pinned version is accepted. Must be called from the elevated installer.
    /// </summary>
    public static void InstallFromFolder(string sourceDir, string engineDir, ILog log)
    {
        var candidates = new[] { Path.Combine(sourceDir, "x86_64"), sourceDir };
        var dir = candidates.FirstOrDefault(d => File.Exists(Path.Combine(d, EngineManifest.ExeName)))
                  ?? throw new FileNotFoundException("goodbyedpi.exe (x86_64) not found in the selected folder.");

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in EngineManifest.Files.Keys)
        {
            var p = Path.Combine(dir, name);
            if (!File.Exists(p)) throw new FileNotFoundException($"{name} not found in {dir}");
            using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length > 8 * 1024 * 1024) throw new InvalidDataException($"{name} is too large");
            var data = new byte[fs.Length];
            fs.ReadExactly(data);
            files[name] = data;
        }

        var licenses = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var licDir = Path.Combine(Path.GetDirectoryName(dir.TrimEnd('\\')) ?? dir, "licenses");
        if (Directory.Exists(licDir))
            foreach (var f in Directory.EnumerateFiles(licDir, "LICENSE-*.txt"))
                if (new FileInfo(f).Length < 256 * 1024) licenses[Path.GetFileName(f)] = File.ReadAllBytes(f);

        WriteVerified(files, licenses, engineDir, log);
    }

    private static byte[] ReadEntry(ZipArchiveEntry e)
    {
        if (e.Length > 8 * 1024 * 1024) throw new InvalidDataException($"{e.Name} is too large");
        using var s = e.Open();
        var ms = new MemoryStream((int)e.Length);
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static void WriteVerified(Dictionary<string, byte[]> files, Dictionary<string, byte[]> licenses, string engineDir, ILog log)
    {
        foreach (var (name, sha) in EngineManifest.Files)
        {
            if (!files.TryGetValue(name, out var data)) throw new FileNotFoundException($"{name} missing");
            var h = Hash(data);
            if (!string.Equals(h, sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{name}: SHA-256 mismatch (expected {EngineManifest.Version}). Got {h}");
        }

        Directory.CreateDirectory(engineDir);
        foreach (var (name, data) in files) File.WriteAllBytes(Path.Combine(engineDir, name), data);
        if (licenses.Count > 0)
        {
            var lic = Path.Combine(engineDir, "licenses");
            Directory.CreateDirectory(lic);
            foreach (var (name, data) in licenses) File.WriteAllBytes(Path.Combine(lic, name), data);
        }
        log.Info($"GoodbyeDPI {EngineManifest.Version} installed and verified in {engineDir}");
    }
}
