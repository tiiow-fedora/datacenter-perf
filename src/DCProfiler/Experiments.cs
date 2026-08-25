// Live sensitivity sweep.
//
// FrameTimingManager.IsFeatureEnabled() returns False in this build (it is a
// build-time player setting, unreachable from a mod), so the direct CPU-vs-GPU
// split is not available. The standard substitute is to change ONE variable at
// a time and watch what framerate does:
//
//   half resolution  -> isolates GPU fill/shading. Big gain = GPU-bound.
//   shadows off      -> isolates shadow rendering (extra geometry passes).
//   lodBias 0.3      -> isolates geometry + draw-call submission.
//   shadowDistance 5 -> cheaper than shadows-off, keeps nearby shadows.
//
// Context from the census: the dev has ALREADY tuned shadows hard (distance 15,
// 1 cascade, Low res, no realtime reflection probes, pixelLights 0), so the
// usual advice is spent. The outlier is 24834 cable renderers out of 39501
// total - 63% of the scene - which is the kind of number that bottlenecks the
// CPU render thread on culling and draw calls rather than the GPU. lodBias is
// the lever that tests that.
//
// Every toggle is reversible and restores the captured original on exit.
using UnityEngine;
using UnityEngine.InputSystem;

namespace DCProfiler;

internal static class Experiments
{
    private static bool _halfRes, _noShadows, _lowLod, _shortShadow;
    private static bool _captured;
    private static int _origW, _origH;
    private static FullScreenMode _origMode;
    private static ShadowQuality _origShadows;
    private static float _origLodBias, _origShadowDist;
    private static double _lastKey;

    /// <summary>Marker string recorded against every CSV row.</summary>
    internal static string Marker
    {
        get
        {
            if (!_halfRes && !_noShadows && !_lowLod && !_shortShadow) return "base";
            var s = "";
            if (_halfRes) s += "res50+";
            if (_noShadows) s += "noshadow+";
            if (_lowLod) s += "lod03+";
            if (_shortShadow) s += "shdist5+";
            return s.TrimEnd('+');
        }
    }

    private static void Capture()
    {
        if (_captured) return;
        _captured = true;
        _origW = Screen.width; _origH = Screen.height; _origMode = Screen.fullScreenMode;
        _origShadows = QualitySettings.shadows;
        _origLodBias = QualitySettings.lodBias;
        _origShadowDist = QualitySettings.shadowDistance;
        Sampler.Evt($"[{Sampler.Now:F2}] EXPERIMENT baseline captured: {_origW}x{_origH} {_origMode}, " +
                    $"shadows={_origShadows} lodBias={_origLodBias} shadowDistance={_origShadowDist}");
    }

    internal static void Tick()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        var now = Sampler.Now;
        // wasPressedThisFrame fires once per OS key-repeat, not once per press.
        if (now - _lastKey < 0.35) return;

        if (kb.f10Key.wasPressedThisFrame) { Capture(); _halfRes = !_halfRes; ApplyRes(); Announce("half resolution", _halfRes); _lastKey = now; }
        else if (kb.f11Key.wasPressedThisFrame) { Capture(); _noShadows = !_noShadows; ApplyShadows(); Announce("shadows OFF", _noShadows); _lastKey = now; }
        else if (kb.f12Key.wasPressedThisFrame) { Capture(); _lowLod = !_lowLod; ApplyLod(); Announce("lodBias 0.3", _lowLod); _lastKey = now; }
        else if (kb.f6Key.wasPressedThisFrame) { Capture(); _shortShadow = !_shortShadow; ApplyShadowDist(); Announce("shadowDistance 5", _shortShadow); _lastKey = now; }
        else if (kb.f5Key.wasPressedThisFrame) { RestoreAll(); _lastKey = now; }
    }

    private static void Announce(string what, bool on)
    {
        Sampler.Evt($"[{Sampler.Now:F2}] ===== EXPERIMENT {what} {(on ? "ON" : "OFF")} -> marker '{Marker}' =====");
        Mod.Log.Msg($"experiment: {what} {(on ? "ON" : "OFF")}  [{Marker}]");
    }

    private static void ApplyRes()
    {
        try
        {
            if (_halfRes) Screen.SetResolution(Math.Max(320, _origW / 2), Math.Max(240, _origH / 2), _origMode);
            else Screen.SetResolution(_origW, _origH, _origMode);
        }
        catch (Exception e) { Mod.Log.Warning($"resolution toggle failed: {e.Message}"); }
    }

    private static void ApplyShadows()
    {
        try { QualitySettings.shadows = _noShadows ? ShadowQuality.Disable : _origShadows; }
        catch (Exception e) { Mod.Log.Warning($"shadow toggle failed: {e.Message}"); }
    }

    private static void ApplyLod()
    {
        try { QualitySettings.lodBias = _lowLod ? 0.3f : _origLodBias; }
        catch (Exception e) { Mod.Log.Warning($"lodBias toggle failed: {e.Message}"); }
    }

    private static void ApplyShadowDist()
    {
        try { QualitySettings.shadowDistance = _shortShadow ? 5f : _origShadowDist; }
        catch (Exception e) { Mod.Log.Warning($"shadowDistance toggle failed: {e.Message}"); }
    }

    /// <summary>Put everything back. Also called on quit so the game is never left altered.</summary>
    internal static void RestoreAll()
    {
        if (!_captured) return;
        _halfRes = _noShadows = _lowLod = _shortShadow = false;
        ApplyRes(); ApplyShadows(); ApplyLod(); ApplyShadowDist();
        Sampler.Evt($"[{Sampler.Now:F2}] ===== EXPERIMENTS RESET to baseline =====");
        Mod.Log.Msg("experiments reset to baseline");
    }
}
