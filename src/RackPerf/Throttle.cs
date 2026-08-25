// Route-evaluation throttling.
//
// MEASURED (DCProfiler run 4, 588 devices / 1614 cables):
//   WaypointInitializationSystem.EvaluateAllRoutesCoroutine accounts for ~68%
//   of ALL managed allocation - 468MB of 689MB standing still, 1121MB of
//   1664MB during cable work. It re-evaluates the entire network roughly every
//   12 seconds even when the player is standing perfectly still doing nothing,
//   each pass costing ~28 coroutine steps at ~2.4MB apiece, about 65MB per
//   evaluation. That is the source of the periodic ~86MB allocation bursts and
//   the GC pressure behind the felt stutter.
//
// The game already exposes its own throttle - evaluationCooldown, with public
// GetEvaluationCooldown/SetEvaluationCooldown, plus a static ms budget. The
// developers clearly knew this path was hot (they also built RentPathList /
// RentIntSet / RentStringSet pooling into it). So this mod turns the game's own
// knob rather than patching traversal logic: strictly less risky, and it cannot
// desynchronise the simulation because routes are still evaluated, just less
// often when nothing is changing.
//
// TRADE-OFF the user should understand: the cooldown is a MINIMUM INTERVAL
// between evaluations, so raising it also delays how quickly a newly connected
// cable starts carrying packets. That is why the default is modest and the
// value is configurable rather than hard-coded aggressive.
using UnityEngine;
using WP = Il2Cpp.WaypointInitializationSystem;

namespace RackPerf;

internal static class Throttle
{
    private static WP _cached;
    private static float _originalCooldown = -1f;
    private static int _originalBudget = -1;
    private static double _lastCheck;
    private static bool _reported;

    /// <summary>
    /// Re-applied rather than set once: the ECS World is torn down and rebuilt
    /// when a save loads, so Instance becomes a different object and any value
    /// written to the old one is gone. Cheap identity check, not a poll of the
    /// scene graph.
    /// </summary>
    internal static void Tick()
    {
        if (!Mod.Cfg.Enabled) return;

        var now = Time.realtimeSinceStartupAsDouble;
        if (now - _lastCheck < 2.0) return;
        _lastCheck = now;

        WP sys;
        try { sys = WP.Instance; }
        catch { return; }

        if (sys == null) return;
        if (ReferenceEquals(sys, _cached)) return;   // already configured this World

        _cached = sys;
        Apply(sys);
    }

    private static void Apply(WP sys)
    {
        try
        {
            var current = sys.GetEvaluationCooldown();
            var budget = WP.GetEvaluationMsBudget();

            // Capture the stock values once so the report is honest about what
            // was changed from what, and so Restore() can put them back.
            if (_originalCooldown < 0f) { _originalCooldown = current; _originalBudget = budget; }

            var target = Mod.Cfg.EvaluationCooldownSeconds > 0
                ? Mod.Cfg.EvaluationCooldownSeconds
                : _originalCooldown * Mod.Cfg.CooldownMultiplier;

            // Never go below stock - this mod may only ever make evaluation less
            // frequent, never more.
            if (target < _originalCooldown) target = _originalCooldown;

            sys.SetEvaluationCooldown(target);

            if (Mod.Cfg.EvaluationMsBudget > 0 && Mod.Cfg.EvaluationMsBudget != budget)
                WP.SetEvaluationMsBudget(Mod.Cfg.EvaluationMsBudget);

            if (!_reported)
            {
                _reported = true;
                Mod.Log.Msg($"route evaluation cooldown {_originalCooldown:F2}s -> {target:F2}s" +
                            $" (ms budget {budget}" +
                            $"{(Mod.Cfg.EvaluationMsBudget > 0 ? $" -> {Mod.Cfg.EvaluationMsBudget}" : " unchanged")})");
                Mod.Log.Msg("routes are still evaluated in full - only the minimum interval changed");
            }
            else
            {
                Mod.Log.Msg($"World rebuilt; re-applied cooldown {target:F2}s");
            }
        }
        catch (Exception e)
        {
            Mod.Log.Warning($"could not apply throttle: {e.Message}");
        }
    }

    /// <summary>Put the stock values back - used by the F9 A/B toggle.</summary>
    internal static void Restore()
    {
        try
        {
            if (_cached == null || _originalCooldown < 0f) return;
            _cached.SetEvaluationCooldown(_originalCooldown);
            if (_originalBudget > 0) WP.SetEvaluationMsBudget(_originalBudget);
            Mod.Log.Msg($"restored stock cooldown {_originalCooldown:F2}s");
        }
        catch { }
    }

    internal static void Reapply()
    {
        if (_cached != null) Apply(_cached);
    }

    internal static void OnSceneLoaded()
    {
        _cached = null;   // force re-apply against the new World
        _lastCheck = 0;
    }
}
