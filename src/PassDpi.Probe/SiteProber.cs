using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using PassDpi.Core.Protocol;

namespace PassDpi.Probe;

public sealed record SiteResult(string Url, bool IsControl, bool Ok, int Millis, string? Error);

public sealed record ProbeResult(IReadOnlyList<SiteResult> Sites)
{
    public bool ControlOk => Sites.Where(s => s.IsControl).Any(s => s.Ok);
    public int TargetCount => Sites.Count(s => !s.IsControl);
    public int TargetOk => Sites.Count(s => !s.IsControl && s.Ok);
    /// <summary>Share of blocked-site candidates that opened. 0 if the control sites failed (we broke the connection).</summary>
    public double Score => !ControlOk || TargetCount == 0 ? 0 : (double)TargetOk / TargetCount;
    public double AvgMillis => Sites.Where(s => s.Ok).Select(s => (double)s.Millis).DefaultIfEmpty(9999).Average();
}

/// <summary>
/// Opens HTTPS connections to the test sites. A site counts as reachable when the TLS
/// handshake completes with a certificate valid for that host and any HTTP response arrives.
/// </summary>
public static partial class SiteProber
{
    [LibraryImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DnsFlushResolverCacheNative();

    /// <summary>Clears the Windows DNS cache so a new profile's DNS redirection is actually exercised.</summary>
    public static void FlushDns()
    {
        try { DnsFlushResolverCacheNative(); } catch { /* best effort */ }
    }

    public static async Task<ProbeResult> ProbeAsync(IReadOnlyList<TestSite> sites, TimeSpan timeout, CancellationToken ct)
    {
        // Fresh handler per round: no pooled connections or cached TLS sessions from a previous profile.
        using var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.Zero,
            PooledConnectionIdleTimeout = TimeSpan.Zero,
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = timeout,
        };
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) PassDPI-Probe");
        http.DefaultRequestVersion = HttpVersion.Version11;

        var tasks = sites.Select(s => ProbeOne(http, s, timeout, ct)).ToArray();
        return new ProbeResult(await Task.WhenAll(tasks).ConfigureAwait(false));
    }

    private static async Task<SiteResult> ProbeOne(HttpClient http, TestSite site, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, site.Url);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            return new SiteResult(site.Url, site.IsControl, true, (int)sw.ElapsedMilliseconds, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new SiteResult(site.Url, site.IsControl, false, (int)sw.ElapsedMilliseconds, "timeout");
        }
        catch (HttpRequestException ex)
        {
            var msg = ex.InnerException?.GetType().Name ?? ex.HttpRequestError.ToString();
            return new SiteResult(site.Url, site.IsControl, false, (int)sw.ElapsedMilliseconds, msg);
        }
    }
}
