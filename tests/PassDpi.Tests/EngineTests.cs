using PassDpi.Core;
using PassDpi.Engine;
using Xunit;

namespace PassDpi.Tests;

public class ArgumentWhitelistTests
{
    [Theory]
    [InlineData("-9", "-9")]
    [InlineData("-5 -e1 -q --fake-gen 5", "-5 -e 1 -q --fake-gen 5")]
    [InlineData("-9 --dns-addr 77.88.8.8 --dns-port 1253", "-9 --dns-addr 77.88.8.8 --dns-port 1253")]
    [InlineData("--dnsv6-addr 2a02:6b8::feed:0ff --dnsv6-port 1253", "--dnsv6-addr 2a02:6b8::feed:0ff --dnsv6-port 1253")]
    [InlineData("--auto-ttl 1-4-10 -f 2", "--auto-ttl=1-4-10 -f 2")]
    [InlineData("--auto-ttl=1-4-10", "--auto-ttl=1-4-10")]
    [InlineData("--max-payload", "--max-payload")]
    [InlineData("--max-payload 1200", "--max-payload=1200")]
    [InlineData("--dns-port=53", "--dns-port 53")]
    [InlineData("--fake-from-hex 160301FFFF --fake-with-sni www.example.com", "--fake-from-hex 160301FFFF --fake-with-sni www.example.com")]
    public void Accepts_documented_flags(string input, string expected)
    {
        var r = ArgumentWhitelist.Validate(input, out var err);
        Assert.Null(err);
        Assert.Equal(expected, string.Join(' ', r!));
    }

    [Theory]
    [InlineData("--blacklist C:\\list.txt")]
    [InlineData("--blacklist=..\\x.txt")]
    [InlineData("-9 & calc.exe")]
    [InlineData("-9 | whoami")]
    [InlineData("-9 \"x\"")]
    [InlineData("--unknown")]
    [InlineData("-e")]
    [InlineData("-e 0")]
    [InlineData("-e abc")]
    [InlineData("--dns-addr 999.1.1.1")]
    [InlineData("--dns-addr 2a02::1")]
    [InlineData("--dnsv6-addr 1.1.1.1")]
    [InlineData("--set-ttl 300")]
    [InlineData("--fake-from-hex ABC")]
    [InlineData("--fake-with-sni ../../etc")]
    [InlineData("-p=1")]
    [InlineData("")]
    public void Rejects_anything_else(string input)
    {
        Assert.Null(ArgumentWhitelist.Validate(input, out var err));
        Assert.NotNull(err);
    }

    [Fact]
    public void Every_builtin_profile_passes_the_whitelist()
    {
        foreach (var p in ProfileCatalog.BuiltIn)
        {
            var r = ArgumentWhitelist.Validate(p.ArgsText, out var err);
            Assert.True(r != null, $"{p.Id}: {err}");
            Assert.Equal(p.Args, r);
        }
    }

    [Fact]
    public void Builtin_profile_ids_are_unique_and_minus9_is_first()
    {
        var ids = ProfileCatalog.BuiltIn.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(27, ids.Count);
        Assert.StartsWith("m9", ids[0]);
    }

    [Fact]
    public void Fast_cloudflare_dns_is_tried_before_yandex()
    {
        var ids = ProfileCatalog.BuiltIn.Select(p => p.Id).ToList();
        Assert.Equal(new[] { "m9-cfdns", "m9-ydns", "m9" }, ids.Take(3));
    }

    [Fact]
    public void Custom_profile_requires_valid_args()
    {
        Assert.Null(ProfileCatalog.Find("custom", null));
        Assert.Null(ProfileCatalog.Find("custom", "--blacklist x"));
        Assert.NotNull(ProfileCatalog.Find("custom", "-7 -q"));
        Assert.Null(ProfileCatalog.Find("nope", null));
    }
}

public class EngineFilesTests
{
    [Fact]
    public void Rejects_zip_with_wrong_hash()
    {
        var bogus = new byte[] { 0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        var dir = Path.Combine(Path.GetTempPath(), "passdpi-test-" + Guid.NewGuid());
        Assert.Throws<InvalidDataException>(() => EngineFiles.InstallFromZipBytes(bogus, dir, NullLog.Instance));
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void Verify_fails_for_tampered_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "passdpi-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var name in EngineManifest.Files.Keys) File.WriteAllText(Path.Combine(dir, name), "not the real file");
            Assert.False(EngineFiles.Verify(dir, out var problem));
            Assert.Contains("hash mismatch", problem);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Install_from_folder_rejects_wrong_version()
    {
        var src = Path.Combine(Path.GetTempPath(), "passdpi-src-" + Guid.NewGuid());
        var dst = Path.Combine(Path.GetTempPath(), "passdpi-dst-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(src, "x86_64"));
        try
        {
            foreach (var name in EngineManifest.Files.Keys) File.WriteAllText(Path.Combine(src, "x86_64", name), "fake");
            Assert.Throws<InvalidDataException>(() => EngineFiles.InstallFromFolder(src, dst, NullLog.Instance));
            Assert.False(Directory.Exists(dst));
        }
        finally { Directory.Delete(src, true); }
    }
}
