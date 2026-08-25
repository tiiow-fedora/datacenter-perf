// Network route evaluation - the current leading candidate.
//
// Run 3 eliminated the cable MESH path: CreateTubeMesh ran 1640 times for
// ~29MB, essentially all of it at load (1614 cables in the save), while the
// cable-work phase allocated 4314MB through only 26 tube calls. Cables are
// cheap to DRAW. The expensive thing is evidently what happens to the NETWORK
// when they change.
//
// WaypointInitializationSystem is a managed SystemBase (not Burst, so it is
// reachable) carrying EvaluateAllRoutes + EvaluateAllRoutesCoroutine over a
// graph of 1614 cables and 588 devices. It fits the signature exactly: huge
// allocation spread across many frames with no single blocking spike, firing
// both periodically and on network change.
//
// Telling detail: the developers already built RentPathList/RentIntSet/
// RentStringSet/RecycleCableUsage pooling here, plus SetEvaluationCooldown and
// a static SetEvaluationMsBudget. They knew this path was hot. If it is the
// source, the game's own throttle may be the whole fix.
using System.Diagnostics;
using Profiler = UnityEngine.Profiling.Profiler;

namespace DCProfiler;

internal static class Routes
{
    internal static int EvalCalls;
    internal static int RequestCalls;
    internal static int MoveNextCalls;
    internal static int SystemUpdateCalls;
    internal static double EvalMs;
    internal static double MoveNextMs;
    internal static double MoveNextMb;

    private static long _t0, _heap0;
    private static long _evalTotal, _moveTotal;
    private static bool _settingsLogged;

    internal static void Reset()
    {
        EvalCalls = RequestCalls = MoveNextCalls = SystemUpdateCalls = 0;
        EvalMs = MoveNextMs = MoveNextMb = 0;
        _evalTotal = _moveTotal = 0;
        _settingsLogged = false;
    }

    internal static void EndInterval()
    {
        EvalCalls = RequestCalls = MoveNextCalls = SystemUpdateCalls = 0;
        EvalMs = MoveNextMs = MoveNextMb = 0;
    }

    internal static void CountRequest() => RequestCalls++;
    internal static void CountSystemUpdate() => SystemUpdateCalls++;

    internal static void EvalBegin() => _t0 = Stopwatch.GetTimestamp();

    internal static void EvalEnd()
    {
        EvalMs += (Stopwatch.GetTimestamp() - _t0) * 1000.0 / Stopwatch.Frequency;
        EvalCalls++; _evalTotal++;
    }

    // MoveNext is where a coroutine's per-frame work actually happens, so this
    // is the measurement that matters - the outer method returns instantly.
    internal static void MoveBegin()
    {
        _t0 = Stopwatch.GetTimestamp();
        try { _heap0 = Profiler.GetMonoUsedSizeLong(); } catch { _heap0 = 0; }
    }

    internal static void MoveEnd()
    {
        MoveNextMs += (Stopwatch.GetTimestamp() - _t0) * 1000.0 / Stopwatch.Frequency;
        try
        {
            var d = (Profiler.GetMonoUsedSizeLong() - _heap0) / 1048576.0;
            if (d > 0) MoveNextMb += d;
        }
        catch { }
        MoveNextCalls++; _moveTotal++;
    }

    /// <summary>Read back the game's own throttle settings once they exist.</summary>
    internal static void LogSettings()
    {
        if (_settingsLogged) return;
        try
        {
            var budget = Il2Cpp.WaypointInitializationSystem.GetEvaluationMsBudget();
            Sampler.Evt($"[{Sampler.Now:F2}] ROUTE SETTINGS: evaluation ms budget = {budget}");
            Mod.Log.Msg($"route evaluation ms budget = {budget}");
            _settingsLogged = true;
        }
        catch (Exception e)
        {
            Sampler.Evt($"[{Sampler.Now:F2}] ROUTE SETTINGS unavailable: {e.Message}");
            _settingsLogged = true;
        }
    }

    internal static void FlushDetail()
    {
        if (EvalCalls == 0 && MoveNextCalls == 0 && RequestCalls == 0) return;
        Sampler.Evt($"[{Sampler.Now:F2}] ROUTES request={RequestCalls} eval={EvalCalls} ({EvalMs:F1}ms) " +
                    $"coroutine_movenext={MoveNextCalls} ({MoveNextMs:F1}ms, {MoveNextMb:F1}MB) " +
                    $"sysupdate={SystemUpdateCalls} | session eval={_evalTotal} movenext={_moveTotal}");
    }
}
