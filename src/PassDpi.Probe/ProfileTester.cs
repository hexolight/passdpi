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
    /// <summary>
    /// Probe rounds per profile; a site must open in all of them. Measured on a real ISP, -9 broke Discord and
    /// Among Us in 20-60% of connections; 2 rounds let it through by luck, 3 rounds catch it ~94% of the time.
    /// </summary>
    public int Rounds { get; init; } = 3;

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
            var baseline = await SiteProber.ProbeAsync(sites, SiteTimeout, ct, Rounds).ConfigureAwait(false);
            LogResult("Baseline (engine off)", baseline);

            if (!baseline.ControlOk)
                return new TestReport(TestOutcome.NoInternet, null, 0, "Control sites are unreachable even without the engine. Check your Internet connection.");

            // A "control" site that never opens without the engine is actually blocked here (a common
            // misconfiguration): test it as a target, otherwise a profile that fails it would look perfect.
            var blockedControls = baseline.Sites.Where(s => s.IsControl && s.OkRounds == 0).Select(s => s.Url).ToHashSet();
            foreach (var u in blockedControls)
                _log.Warn($"Control site {new Uri(u).Host} is blocked on this network; testing it as a target. Unmark it as Control in Sites.");
            if (blockedControls.Count > 0)
            {
                sites = sites.Select(s => blockedControls.Contains(s.Url) ? new TestSite { Url = s.Url, IsControl = false } : s).ToList();
                baseline = Reclassify(baseline, blockedControls);
            }
            // Controls that reliably open without the engine must keep opening with it.
            var healthyControls = baseline.Sites.Where(s => s.IsControl && s.Ok).Select(s => s.Url).ToHashSet();
            progress(Result(0, total, "baseline", "Baseline", baseline));

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
                var r = await SiteProber.ProbeAsync(sites, SiteTimeout, ct, Rounds).ConfigureAwait(false);
                _engine.Stop();

                var clean = IsClean(r, healthyControls);
                LogResult(p.Name + (clean ? "" : " [breaks normal sites]"), r);
                progress(Result(i + 1, total, p.Id, p.Name, r));

                if (bestResult == null || IsBetter(r, clean, bestResult, IsClean(bestResult, healthyControls)))
                {
                    best = p; bestResult = r;
                }
                if (clean && r.Score >= 1.0) break; // perfect and harmless: no need to try the rest
            }

            if (best == null || bestResult == null || bestResult.Score <= baseline.Score)
                return new TestReport(TestOutcome.AllFailed, null, 0, "No profile improved access on this network. The engine was left off so your connection is unchanged.");

            var bestClean = IsClean(bestResult, healthyControls);
            if (!bestClean)
                _log.Warn($"No profile was both effective and harmless; {best.Name} also breaks some normal sites.");
            return new TestReport(TestOutcome.Found, best, bestResult.Score,
                $"Best profile: {best.Name} ({bestResult.TargetOk}/{bestResult.TargetCount} sites{(bestClean ? "" : ", breaks some normal sites")}).");
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

    /// <summary>A profile is clean when every control that reliably opened without the engine still opens with it.</summary>
    public static bool IsClean(ProbeResult r, IReadOnlySet<string> healthyControls) =>
        r.Sites.Where(s => healthyControls.Contains(s.Url)).All(s => s.Ok);

    /// <summary>
    /// Ranking: a profile that keeps normal sites working beats one that breaks them (that is what users feel
    /// as slowdowns, game errors and disconnects); then more blocked sites opened; then clearly faster.
    /// </summary>
    public static bool IsBetter(ProbeResult r, bool rClean, ProbeResult incumbent, bool incumbentClean)
    {
        // Only prefer a clean profile if it actually opens something.
        var rUseful = r.Score > 0;
        var iUseful = incumbent.Score > 0;
        if (rUseful && iUseful && rClean != incumbentClean) return rClean;
        if (r.Score != incumbent.Score) return r.Score > incumbent.Score;
        if (rClean != incumbentClean) return rClean;
        return r.Score > 0 && r.AvgMillis < incumbent.AvgMillis * 0.7;
    }

    private static ProbeResult Reclassify(ProbeResult r, IReadOnlySet<string> asTargets) =>
        new(r.Sites.Select(s => asTargets.Contains(s.Url) ? s with { IsControl = false } : s).ToList());

    private void LogResult(string name, ProbeResult r)
    {
        var detail = string.Join(", ", r.Sites.Select(s =>
            $"{new Uri(s.Url).Host}{(s.IsControl ? "*" : "")}=" +
            (s.Ok ? $"ok {s.Millis}ms" : s.OkRounds > 0 ? $"flaky {s.OkRounds}/{s.Rounds} ({s.Error})" : s.Error)));
        _log.Info($"{name}: score {r.Score:P0} | {detail}");
    }

    private static TestProgress Result(int i, int total, string id, string name, ProbeResult r) => new()
    {
        Index = i, Total = total, ProfileId = id, ProfileName = name, Phase = "Result",
        Score = r.Score, Ok = r.TargetOk, Count = r.TargetCount,
        Message = r.ControlOk ? null : "control sites failed",
    };
}
