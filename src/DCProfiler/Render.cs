// Graphics-side profiling.
//
// Rationale from earlier runs: framerate swung 53 -> 205 fps depending purely
// on where the camera pointed, largely independent of heap or the CPU work we
// chased for six runs. That is the signature of a render-bound frame, which is
// what conventional graphics optimisation actually targets.
//
// The one measurement that settles it is FrameTimingManager, which reports
// cpuMainThreadFrameTime, cpuRenderThreadFrameTime and gpuFrameTime separately.
// Until we know which of those dominates, tuning any graphics setting is
// guessing - and this project has already paid for guessing four times.
//
// Nothing here changes a single setting; it reads and reports.
using UnityEngine;
using UnityEngine.Rendering;

namespace DCProfiler;

internal static class Render
{
    private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<FrameTiming> _buf;
    private static bool _available;
    private static bool _checked;
    private static bool _dumped;

    private static double _cpuMain, _cpuRender, _gpu, _present;
    private static int _samples;
    private static double _gpuMax, _cpuMax;

    internal static void Reset()
    {
        _cpuMain = _cpuRender = _gpu = _present = 0;
        _samples = 0; _gpuMax = _cpuMax = 0;
        _dumped = false;
    }

    internal static void EndInterval()
    {
        _cpuMain = _cpuRender = _gpu = _present = 0;
        _samples = 0; _gpuMax = _cpuMax = 0;
    }

    internal static void SampleFrame()
    {
        if (!_checked)
        {
            _checked = true;
            try
            {
                _available = FrameTimingManager.IsFeatureEnabled();
                if (_available)
                    _buf = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<FrameTiming>(1);
                Sampler.Evt($"[{Sampler.Now:F2}] FrameTimingManager enabled={_available}");
                Mod.Log.Msg($"FrameTimingManager available: {_available}");
            }
            catch (Exception e)
            {
                _available = false;
                Sampler.Evt($"[{Sampler.Now:F2}] FrameTimingManager unavailable: {e.Message}");
            }
        }
        if (!_available) return;

        try
        {
            FrameTimingManager.CaptureFrameTimings();
            var n = FrameTimingManager.GetLatestTimings(1, _buf);
            if (n == 0) return;

            var t = _buf[0];
            _cpuMain += t.cpuMainThreadFrameTime;
            _cpuRender += t.cpuRenderThreadFrameTime;
            _gpu += t.gpuFrameTime;
            _present += t.cpuMainThreadPresentWaitTime;
            if (t.gpuFrameTime > _gpuMax) _gpuMax = t.gpuFrameTime;
            if (t.cpuMainThreadFrameTime > _cpuMax) _cpuMax = t.cpuMainThreadFrameTime;
            _samples++;
        }
        catch { }
    }

    internal static double AvgGpu => _samples == 0 ? 0 : _gpu / _samples;
    internal static double AvgCpuMain => _samples == 0 ? 0 : _cpuMain / _samples;
    internal static double AvgCpuRender => _samples == 0 ? 0 : _cpuRender / _samples;
    internal static double AvgPresent => _samples == 0 ? 0 : _present / _samples;

    internal static void FlushDetail()
    {
        if (_samples == 0) return;
        var verdict = AvgGpu > AvgCpuMain * 1.15 ? "GPU-BOUND"
                    : AvgCpuMain > AvgGpu * 1.15 ? "CPU-BOUND"
                    : "balanced";
        Sampler.Evt($"[{Sampler.Now:F2}] FRAME cpuMain={AvgCpuMain:F2}ms cpuRender={AvgCpuRender:F2}ms " +
                    $"gpu={AvgGpu:F2}ms present_wait={AvgPresent:F2}ms " +
                    $"(max gpu={_gpuMax:F1} cpu={_cpuMax:F1}) -> {verdict}");
    }

    /// <summary>
    /// One-shot census of the settings and the scene's render load. Never on a
    /// timer - FindObjectsByType is scene-only but still walks the scene graph.
    /// </summary>
    internal static void DumpSettings()
    {
        if (_dumped) return;
        _dumped = true;
        try
        {
            var q = QualitySettings.names;
            Sampler.Evt($"[{Sampler.Now:F2}] ===== GRAPHICS SETTINGS =====");
            Sampler.Evt($"    quality level {QualitySettings.GetQualityLevel()}" +
                        $"{(q != null && q.Length > QualitySettings.GetQualityLevel() ? " '" + q[QualitySettings.GetQualityLevel()] + "'" : "")}" +
                        $" of {(q?.Length ?? 0)}");
            Sampler.Evt($"    lodBias={QualitySettings.lodBias} maximumLODLevel={QualitySettings.maximumLODLevel} " +
                        $"lodCrossFade={QualitySettings.enableLODCrossFade}");
            Sampler.Evt($"    shadows={QualitySettings.shadows} distance={QualitySettings.shadowDistance} " +
                        $"cascades={QualitySettings.shadowCascades} res={QualitySettings.shadowResolution} " +
                        $"mask={QualitySettings.shadowmaskMode}");
            Sampler.Evt($"    vSync={QualitySettings.vSyncCount} AA={QualitySettings.antiAliasing} " +
                        $"aniso={QualitySettings.anisotropicFiltering} mipLimit={QualitySettings.globalTextureMipmapLimit}");
            Sampler.Evt($"    realtimeReflectionProbes={QualitySettings.realtimeReflectionProbes} " +
                        $"pixelLights={QualitySettings.pixelLightCount} maxQueuedFrames={QualitySettings.maxQueuedFrames}");
            Sampler.Evt($"    targetFrameRate={Application.targetFrameRate} " +
                        $"screen={Screen.width}x{Screen.height} fullscreen={Screen.fullScreenMode}");
            var rp = QualitySettings.renderPipeline;
            Sampler.Evt($"    renderPipeline={(rp != null ? rp.name : "(none / graphics default)")}");
        }
        catch (Exception e)
        {
            Sampler.Evt($"    settings dump failed: {e.Message}");
        }

        // Scene render load: how much geometry, and how much of it casts shadows.
        // Shadow casters are redrawn per cascade, so in a hall of 588 devices and
        // 1614 cables this number matters more than the raw renderer count.
        try
        {
            var arr = UnityEngine.Object.FindObjectsByType(
                Il2CppInterop.Runtime.Il2CppType.Of<Renderer>(), FindObjectsSortMode.None);
            int total = 0, casters = 0, enabled = 0;
            var byKind = new Dictionary<string, int>();
            var castersByKind = new Dictionary<string, int>();

            foreach (var o in arr)
            {
                var r = o.TryCast<Renderer>();
                if (r == null) continue;
                total++;
                if (r.enabled) enabled++;
                var casts = r.shadowCastingMode != ShadowCastingMode.Off;
                if (casts) casters++;

                var kind = Classify(r.gameObject.name);
                byKind.TryGetValue(kind, out var a); byKind[kind] = a + 1;
                if (casts) { castersByKind.TryGetValue(kind, out var b); castersByKind[kind] = b + 1; }
            }

            Sampler.Evt($"    renderers total={total} enabled={enabled} shadowCasters={casters}");
            foreach (var kv in byKind.OrderByDescending(k => k.Value).Take(8))
            {
                castersByKind.TryGetValue(kv.Key, out var c);
                Sampler.Evt($"      {kv.Key,-16} {kv.Value,6} renderers, {c,6} cast shadows");
            }
            Mod.Log.Msg($"render census: {total} renderers, {casters} shadow casters");
        }
        catch (Exception e)
        {
            Sampler.Evt($"    renderer census failed: {e.Message}");
        }

        try
        {
            var lights = UnityEngine.Object.FindObjectsByType(
                Il2CppInterop.Runtime.Il2CppType.Of<Light>(), FindObjectsSortMode.None);
            int n = 0, shadowed = 0, active = 0;
            var byType = new Dictionary<string, int>();
            foreach (var o in lights)
            {
                var l = o.TryCast<Light>();
                if (l == null) continue;
                n++;
                if (l.enabled) active++;
                if (l.shadows != LightShadows.None) shadowed++;
                // lightmapBakeType is editor-only; bakingOutput is the runtime view
                // and tells us whether the user's "looks prebaked" read is right.
                var mode = "unknown";
                try { mode = l.bakingOutput.lightmapBakeType.ToString(); } catch { }
                var key = $"{l.type}/{mode}";
                byType.TryGetValue(key, out var c); byType[key] = c + 1;
            }
            Sampler.Evt($"    lights={n} enabled={active} casting shadows={shadowed}");
            foreach (var kv in byType.OrderByDescending(k => k.Value).Take(6))
                Sampler.Evt($"      {kv.Key,-28} {kv.Value}");
        }
        catch (Exception e)
        {
            Sampler.Evt($"    light census failed: {e.Message}");
        }
    }

    private static string Classify(string n)
    {
        if (n.StartsWith("Server")) return "Server";
        if (n.StartsWith("Switch") || n.StartsWith("Router") || n.StartsWith("Firewall")) return "NetworkGear";
        if (n.Contains("Cable")) return "Cable";
        if (n.Contains("Rack")) return "Rack";
        if (n.Contains("PatchPanel")) return "PatchPanel";
        return "other";
    }
}
