using PassDpi.Core.Native;
using PassDpi.Core.Protocol;
using PassDpi.Core.Validation;
using PassDpi.Probe;
using PassDpi.Settings;
using Xunit;

namespace PassDpi.Tests;

public class ProtocolTests
{
    [Fact]
    public void Roundtrip_request()
    {
        var m = new PipeMessage { Kind = MessageKind.Request, Id = 7, Command = Command.Apply, ProfileId = "m9" };
        var back = MessageFraming.Deserialize(MessageFraming.Serialize(m))!;
        Assert.Equal(Command.Apply, back.Command);
        Assert.Equal("m9", back.ProfileId);
        Assert.Equal(7, back.Id);
    }

    [Theory]
    [InlineData("{\"kind\":\"Request\",\"id\":1,\"command\":\"RunShell\"}")]
    [InlineData("{\"kind\":\"Request\",\"id\":1,\"command\":5}")]
    [InlineData("not json")]
    public void Unknown_or_malformed_commands_are_rejected(string json)
    {
        var m = MessageFraming.Deserialize(json);
        Assert.True(m == null || m.Command == null || !Enum.IsDefined(m.Command.Value));
    }

    [Fact]
    public async Task Framing_reads_lines_and_caps_size()
    {
        var ms = new MemoryStream();
        await MessageFraming.WriteAsync(ms, new PipeMessage { Id = 1 }, default);
        await MessageFraming.WriteAsync(ms, new PipeMessage { Id = 2 }, default);
        ms.Position = 0;
        var buf = new MemoryStream(); var chunk = new byte[16];
        Assert.Equal(1, MessageFraming.Deserialize((await MessageFraming.ReadLineAsync(ms, buf, chunk, default))!)!.Id);
        Assert.Equal(2, MessageFraming.Deserialize((await MessageFraming.ReadLineAsync(ms, buf, chunk, default))!)!.Id);
        Assert.Null(await MessageFraming.ReadLineAsync(ms, buf, chunk, default));

        var huge = new MemoryStream(new byte[MessageFraming.MaxMessageBytes + 10]);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            MessageFraming.ReadLineAsync(huge, new MemoryStream(), new byte[4096], default));
    }
}

public class SiteValidatorTests
{
    [Theory]
    [InlineData("discord.com", "https://discord.com/")]
    [InlineData("https://www.roblox.com", "https://www.roblox.com/")]
    [InlineData("https://www.google.com/generate_204", "https://www.google.com/generate_204")]
    public void Normalizes(string input, string expected)
    {
        Assert.True(SiteValidator.TryNormalize(input, out var url));
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData("http://discord.com")]
    [InlineData("https://1.1.1.1")]
    [InlineData("https://localhost")]
    [InlineData("https://user:pw@example.com")]
    [InlineData("https://example.com:8443")]
    [InlineData("file:///C:/Windows")]
    [InlineData("")]
    public void Rejects(string input) => Assert.False(SiteValidator.TryNormalize(input, out _));

    [Fact]
    public void List_needs_control_and_target()
    {
        Assert.Null(SiteValidator.Normalize(new[] { new TestSite { Url = "discord.com" } }, out _));
        Assert.Null(SiteValidator.Normalize(new[] { new TestSite { Url = "google.com", IsControl = true } }, out _));
        Assert.NotNull(SiteValidator.Normalize(SiteValidator.Defaults(), out _));
    }
}

public class SettingsTests
{
    [Fact]
    public void Fingerprint_hash_is_salted_and_stable()
    {
        var a = NetworkFingerprint.Compute(new byte[32], "Wireless|Home|192.168.1.1|AABB");
        var b = NetworkFingerprint.Compute(new byte[32], "Wireless|Home|192.168.1.1|AABB");
        var salt = new byte[32]; salt[0] = 1;
        var c = NetworkFingerprint.Compute(salt, "Wireless|Home|192.168.1.1|AABB");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.DoesNotContain("Home", a);
    }

    [Fact]
    public void Store_roundtrip_and_revalidates_sites()
    {
        var dir = Path.Combine(Path.GetTempPath(), "passdpi-set-" + Guid.NewGuid());
        try
        {
            var store = new SettingsStore(Path.Combine(dir, "s.json"), Path.Combine(dir, "n.json"));
            store.SaveSettings(new ServiceSettings { Enabled = false, OwnerSid = "S-1-5-21-1", Sites = new() { new TestSite { Url = "http://bad" } } });
            var s = store.LoadSettings();
            Assert.False(s.Enabled);
            Assert.Equal("S-1-5-21-1", s.OwnerSid);
            Assert.Equal(SiteValidator.Defaults().Count, s.Sites.Count); // invalid list replaced by defaults

            var mem = new NetworkMemoryFile();
            mem.Networks["ABC"] = new NetworkRecord { ProfileId = "m9", Score = 1, TestedUtc = DateTime.UtcNow };
            store.SaveNetworks(mem);
            Assert.Equal("m9", store.LoadNetworks().Networks["ABC"].ProfileId);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Service_sddl_rejects_malformed_sid()
    {
        Assert.Throws<ArgumentException>(() => ServiceControl.BuildServiceSddl("S-1-5;(A;;GA;;;WD)"));
        Assert.Contains("(A;;CCLCRPLORC;;;S-1-5-21-1-2-3-1001)", ServiceControl.BuildServiceSddl("S-1-5-21-1-2-3-1001"));
    }
}

public class ProbeScoreTests
{
    [Fact]
    public void Score_is_zero_when_control_fails()
    {
        var r = new ProbeResult(new[]
        {
            new SiteResult("https://a.com/", false, true, 100, null),
            new SiteResult("https://g.com/", true, false, 0, "timeout"),
        });
        Assert.Equal(0, r.Score);
    }

    [Fact]
    public void Score_is_share_of_targets()
    {
        var r = new ProbeResult(new[]
        {
            new SiteResult("https://a.com/", false, true, 100, null),
            new SiteResult("https://b.com/", false, false, 0, "timeout"),
            new SiteResult("https://g.com/", true, true, 50, null),
        });
        Assert.Equal(0.5, r.Score);
    }
}
