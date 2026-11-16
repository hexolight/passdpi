using PassDpi.Core;
using PassDpi.Core.Protocol;
using PassDpi.Engine;

namespace PassDpi.Probe;

public enum TestOutcome { Found, NoBlocking, AllFailed, NoInternet, EngineBroken, Cancelled }

public sealed record TestReport(TestOutcome Outcome, EngineProfile? Best, double Score, string Message);

/// <summary>
/// Runs the automatic test: baseline without the engine, then each profile in order
/// (start engine, wait, probe, stop). Stops early on a perfect score. Always leaves the
/// engine stopped; the caller decides whether to apply the winner.
/// </summary>
public sealed class ProfileTester
{
    private readonly EngineRunner _engine;
    private readonly ILog _log;

    public TimeSpan SiteTimeout { get; init; } = TimeSpan.FromSeconds(4);
    public TimeSpan SettleDelay { get; init; } = TimeSpan.FromSeconds(1);

    public ProfileTester(EngineRunner engine, ILog log)
    {
        _engine = engine;
        _log = log;
    }

    public async Task<TestReport> RunAsync(IReadOnlyList<EngineProfile> profiles, IReadOnlyList<TestSite> sites,
        Action<TestProgress> progress, CancellationToken ct)
    {
        var total = profiles.Count;
        try
        {
            // 1) Baseline with the engine off: is there Internet, and is anything actually blocked?
            _engine.Stop();
            SiteProber.FlushDns();
            progress(new TestProgress { Index = 0, Total = total, ProfileId = "baseline", ProfileName = "Baseline", Phase = "Running" });
            var baseline = await SiteProber.ProbeAsync(sites, SiteTimeout, ct).ConfigureAwait(false);
            LogResult("Baseline (engine off)", baseline);
            progress(Result(0, total, "baseline", "Baseline", baseline));

            if (!baseline.ControlOk)
                return new TestReport(TestOutcome.NoInternet, null, 0, "Control sites are unreachable even without the engine. Check your Internet connection.");
            if (baseline.Score >= 1.0)
                return new TestReport(TestOutcome.NoBlocking, null, 1, "All test sites open without the engine. No DPI blocking detected on this network.");

            // 2) Profiles in order.
            EngineProfile? best = null;
            ProbeResult? bestResult = null;
            var engineEverStarted = false;

            for (var i = 0; i < profiles.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var p = profiles[i];
                progress(new TestProgress { Index = i + 1, Total = total, ProfileId = p.Id, ProfileName = p.Name, Phase = "Running" });

                if (!await _engine.StartAsync(p, ct).ConfigureAwait(false))
                {
                    progress(new TestProgress { Index = i + 1, Total = total, ProfileId = p.Id, ProfileName = p.Name, Phase = "Result", Score = 0, Message = "engine failed to start" });
                    // If the very first profile can't even start, the driver/engine is broken: don't waste time.
                    if (!engineEverStarted && i == 0)
                        return new TestReport(TestOutcome.EngineBroken, null, 0, "GoodbyeDPI could not start (WinDivert driver failed to load or files are invalid). See the log.");
                    continue;
                }
                engineEverStarted = true;

                await Task.Delay(SettleDelay, ct).ConfigureAwait(false);
                SiteProber.FlushDns();
                var r = await SiteProber.ProbeAsync(sites, SiteTimeout, ct).ConfigureAwait(false);
                _engine.Stop();

                LogResult(p.Name, r);
                progress(Result(i + 1, total, p.Id, p.Name, r));

                if (bestResult == null || r.Score > bestResult.Score ||
                    (r.Score == bestResult.Score && r.Score > 0 && r.AvgMillis < bestResult.AvgMillis * 0.7))
                {
                    best = p; bestResult = r;
                }
                if (r.Score >= 1.0) break; // perfect: no need to try the rest
            }

            if (best == null || bestResult == null || bestResult.Score <= baseline.Score)
                return new TestReport(TestOutcome.AllFailed, null, 0, "No profile improved access on this network. The engine was left off so your connection is unchanged.");

            return new TestReport(TestOutcome.Found, best, bestResult.Score,
                $"Best profile: {best.Name} ({bestResult.TargetOk}/{bestResult.TargetCount} sites).");
        }
        catch (OperationCanceledException)
        {
            return new TestReport(TestOutcome.Cancelled, null, 0, "Test cancelled.");
        }
        finally
        {
            _engine.Stop(); // never leave a half-tested profile running
        }
    }

    /// <summary>Quick check of an already running engine (used after a network change).</summary>
    public async Task<ProbeResult> VerifyAsync(IReadOnlyList<TestSite> sites, CancellationToken ct)
    {
        SiteProber.FlushDns();
        var r = await SiteProber.ProbeAsync(sites, SiteTimeout, ct).ConfigureAwait(false);
        LogResult("Verify", r);
        return r;
    }

    private void LogResult(string name, ProbeResult r)
    {
        var detail = string.Join(", ", r.Sites.Select(s =>
            $"{new Uri(s.Url).Host}{(s.IsControl ? "*" : "")}={(s.Ok ? $"ok {s.Millis}ms" : s.Error)}"));
        _log.Info($"{name}: score {r.Score:P0} | {detail}");
    }

    private static TestProgress Result(int i, int total, string id, string name, ProbeResult r) => new()
    {
        Index = i, Total = total, ProfileId = id, ProfileName = name, Phase = "Result",
        Score = r.Score, Ok = r.TargetOk, Count = r.TargetCount,
        Message = r.ControlOk ? null : "control sites failed",
    };
}
