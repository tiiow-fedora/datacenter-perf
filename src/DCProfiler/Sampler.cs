// Central sampler: frame timing, IL2CPP heap sampling, flush orchestration.
//
// Design rule inherited from the RackColors microstutter: a probe that
// perturbs the thing it measures is worthless. Everything here accumulates
// into plain counters on the main thread and writes once per FLUSH_SECONDS.
// No allocation-per-frame, no Resources.FindObjectsOfTypeAll, ever.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.InputSystem;
using Profiler = UnityEngine.Profiling.Profiler;

namespace DCProfiler;

internal static class Sampler
{
    private const double FlushSeconds = 5.0;
    private const double SpikeMs = 50.0;   // a frame over this is logged individually
    private const int MaxSpikeLines = 16;  // per flush interval, keeps the log readable
    private const int FrameBufCap = 8192;

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static double _lastFrameStamp;
    private static double _lastFlush;

    private static readonly double[] FrameMs = new double[FrameBufCap];
    private static int _frameCount;
    private static int _spikesThisInterval;
    private static readonly List<string> SpikeLines = new();

    private static long _monoUsedPrev;
    private static string _csvPath;
    private static string _evtPath;
    private static bool _headerWritten;
    private static double _lastHotkey;
    private static string _marker = "boot";
    private static int _phase;
    private static bool _censusPending = true;

    internal static double Now => Clock.Elapsed.TotalSeconds;

    internal static void Init()
    {
        var dir = Path.Combine(MelonEnvironment.UserDataDirectory, "DCProfiler");
        Directory.CreateDirectory(dir);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        _csvPath = Path.Combine(dir, $"session-{stamp}.csv");
        _evtPath = Path.Combine(dir, $"events-{stamp}.log");

        Mod.Log.Msg("=== DIAGNOSTIC BUILD - measures only, changes nothing ===");
        Mod.Log.Msg($"csv    -> {_csvPath}");
        Mod.Log.Msg($"events -> {_evtPath}");
        Mod.Log.Msg($"flush every {FlushSeconds}s, spike threshold {SpikeMs}ms");
        Mod.Log.Msg("F7 phase, F8 churn dump | EXPERIMENTS: F10 half-res, F11 shadows off, F12 lodBias 0.3, F6 shadowDist 5, F9 cables off, F5 reset");

        Evt($"# DCProfiler session {stamp}");
        Evt($"# spike threshold {SpikeMs}ms, flush {FlushSeconds}s");
        _lastFrameStamp = Now;
        _lastFlush = Now;
    }

    internal static void OnSceneLoaded(string sceneName)
    {
        Evt($"[{Now:F2}] SCENE LOADED '{sceneName}' - counters reset");
        Churn.Reset();
        Ticks.Reset();
        Ropes.Reset();
        Cables.Reset();
        Routes.Reset();
        Render.Reset();
        Alloc.Reset();
        Heavy.Reset();
        _frameCount = 0;
        _lastFrameStamp = Now;
        _lastFlush = Now;
        _censusPending = true;
    }

    internal static void Tick()
    {
        var now = Now;
        var dtMs = (now - _lastFrameStamp) * 1000.0;
        _lastFrameStamp = now;

        // Attribute this frame's heap growth to whatever ran during it, then
        // roll the per-frame rope counters into the interval totals.
        Alloc.SampleFrame(Ropes.GenMeshFrame, Ticks.CallsThisFrame);
        Render.SampleFrame();
        Ropes.EndFrame();
        Ticks.EndFrame();

        // Census once, a few seconds after load, so cables spawned during the
        // load burst are all present. One-shot - never on a timer.
        if (_censusPending && now - _lastFlush > 3.0)
        {
            _censusPending = false;
            Ropes.Census();
            Routes.LogSettings();
            Render.DumpSettings();
        }

        // Frame 1 after a load is a multi-second outlier; it would swamp the
        // percentiles and tells us nothing about steady-state stutter.
        if (dtMs < 2000.0)
        {
            if (_frameCount < FrameBufCap) FrameMs[_frameCount++] = dtMs;

            if (dtMs > SpikeMs)
            {
                _spikesThisInterval++;
                if (SpikeLines.Count < MaxSpikeLines)
                    SpikeLines.Add($"[{now:F2}] SPIKE {dtMs:F1}ms  frame={Time.frameCount}  " +
                                   $"created_since_flush={Churn.CreatedInterval} " +
                                   $"destroyed_since_flush={Churn.DestroyedInterval} " +
                                   $"tickcalls_since_flush={Ticks.CallsInterval}");
            }
        }

        Hotkeys();
        Experiments.Tick();

        if (now - _lastFlush >= FlushSeconds) Flush(now);
    }

    private static void Hotkeys()
    {
        // Key repeat fires wasPressedThisFrame once per OS repeat, not once per
        // physical press - without this debounce a single tap registers 4-5 times.
        var kb = Keyboard.current;
        if (kb == null) return;
        var now = Now;
        if (now - _lastHotkey < 0.35) return;

        if (kb.f7Key.wasPressedThisFrame)
        {
            _lastHotkey = now;
            _phase++;
            _marker = "p" + _phase;
            // Wall-clock too, so the log can be aligned against screen capture.
            Evt($"[{now:F2}] ===== PHASE {_phase} ===== (wall clock {DateTime.Now:HH:mm:ss})");
            Mod.Log.Msg($"===== PHASE {_phase} =====");
        }
        else if (kb.f8Key.wasPressedThisFrame)
        {
            _lastHotkey = now;
            Churn.DumpDetail();
        }
    }

    private static void Flush(double now)
    {
        var span = now - _lastFlush;
        _lastFlush = now;

        double p50 = 0, p95 = 0, p99 = 0, max = 0, mean = 0;
        if (_frameCount > 0)
        {
            var copy = new double[_frameCount];
            Array.Copy(FrameMs, copy, _frameCount);
            Array.Sort(copy);
            p50 = copy[(int)(_frameCount * 0.50)];
            p95 = copy[Math.Min(_frameCount - 1, (int)(_frameCount * 0.95))];
            p99 = copy[Math.Min(_frameCount - 1, (int)(_frameCount * 0.99))];
            max = copy[_frameCount - 1];
            double sum = 0;
            for (var i = 0; i < _frameCount; i++) sum += copy[i];
            mean = sum / _frameCount;
        }

        long monoUsed = 0, monoHeap = 0;
        try
        {
            monoUsed = Profiler.GetMonoUsedSizeLong();
            monoHeap = Profiler.GetMonoHeapSizeLong();
        }
        catch { /* profiler unavailable in some builds; metrics stay 0 */ }

        var monoDeltaMb = (monoUsed - _monoUsedPrev) / 1048576.0;
        _monoUsedPrev = monoUsed;

        var fps = _frameCount / Math.Max(span, 0.001);

        if (!_headerWritten)
        {
            Csv("t,marker,frames,fps,mean_ms,p50_ms,p95_ms,p99_ms,max_ms,spikes," +
                "created,destroyed,net,tick_calls,tick_ms,tick_frames,tick_bursts," +
                "mono_used_mb,mono_heap_mb,mono_delta_mb," +
                "alloc_mb,alloc_mb_s,alloc_ropegen_mb,alloc_tick_mb,alloc_quiet_mb," +
                "gc_events,gc_freed_mb,rope_update,ropemesh_update,gen_mesh,create_mesh," +
                "cable_redraw,cable_tube,cable_finalpath," +
                "route_request,route_eval,route_eval_ms,route_movenext,route_movenext_ms,route_movenext_mb," +
                "cpu_main_ms,cpu_render_ms,gpu_ms,present_wait_ms");
            _headerWritten = true;
        }

        Csv(string.Join(",", new[]
        {
            F(now), _phase > 0 ? $"p{_phase}.{Experiments.Marker}" : Experiments.Marker, _frameCount.ToString(), F(fps), F(mean), F(p50), F(p95), F(p99), F(max),
            _spikesThisInterval.ToString(),
            Churn.CreatedInterval.ToString(), Churn.DestroyedInterval.ToString(),
            (Churn.CreatedInterval - Churn.DestroyedInterval).ToString(),
            Ticks.CallsInterval.ToString(), F(Ticks.MsInterval),
            Ticks.DistinctFramesInterval.ToString(), Ticks.BurstsInterval.ToString(),
            F(monoUsed / 1048576.0), F(monoHeap / 1048576.0), F(monoDeltaMb),
            F(Alloc.MbTotal), F(Alloc.MbTotal / Math.Max(span, 0.001)),
            F(Alloc.MbInRopeGenFrames), F(Alloc.MbInTickFrames), F(Alloc.MbInQuietFrames),
            Alloc.GcEvents.ToString(), F(Alloc.MbFreedByGc),
            Ropes.RopeUpdateInterval.ToString(), Ropes.MeshUpdateInterval.ToString(),
            Ropes.GenMeshInterval.ToString(), Ropes.CreateMeshInterval.ToString(),
            Cables.RedrawInterval.ToString(), Cables.TubeMeshInterval.ToString(),
            Cables.FinalPathInterval.ToString(),
            Routes.RequestCalls.ToString(), Routes.EvalCalls.ToString(), F(Routes.EvalMs),
            Routes.MoveNextCalls.ToString(), F(Routes.MoveNextMs), F(Routes.MoveNextMb),
            F(Render.AvgCpuMain), F(Render.AvgCpuRender), F(Render.AvgGpu), F(Render.AvgPresent)
        }));

        foreach (var s in SpikeLines) Evt(s);
        SpikeLines.Clear();

        Churn.FlushDetail();
        Ticks.FlushDetail();
        Alloc.FlushDetail(span);
        Ropes.FlushDetail(span, _frameCount);
        Cables.FlushDetail(span);
        Routes.FlushDetail();
        Render.FlushDetail();
        Heavy.FlushDetail();

        // One compact line to the MelonLoader log so the run can be eyeballed live.
        Mod.Log.Msg($"[{Experiments.Marker}] {fps:F0}fps p50={p50:F1} p99={p99:F1} max={max:F1}ms " +
                    $"spikes={_spikesThisInterval} | dev +{Churn.CreatedInterval}/-{Churn.DestroyedInterval} " +
                    $"| alloc {Alloc.MbTotal / Math.Max(span, 0.001):F2}MB/s " +
                    $"(rope {Alloc.MbInRopeGenFrames:F1} tick {Alloc.MbInTickFrames:F1} quiet {Alloc.MbInQuietFrames:F1}) " +
                    $"| cpu {Render.AvgCpuMain:F1} gpu {Render.AvgGpu:F1}ms | route {Routes.MoveNextMb:F0}MB | heap {monoUsed / 1048576.0:F0}MB");

        _frameCount = 0;
        _spikesThisInterval = 0;
        Churn.EndInterval();
        Ticks.EndInterval();
        Alloc.EndInterval();
        Ropes.EndInterval();
        Cables.EndInterval();
        Routes.EndInterval();
        Render.EndInterval();
        Heavy.EndInterval();
    }

    private static string F(double d) => d.ToString("F2", CultureInfo.InvariantCulture);

    internal static void Evt(string line)
    {
        try { File.AppendAllText(_evtPath, line + Environment.NewLine, Encoding.UTF8); }
        catch { /* never let logging kill the game */ }
    }

    private static void Csv(string line)
    {
        try { File.AppendAllText(_csvPath, line + Environment.NewLine, Encoding.UTF8); }
        catch { }
    }

    internal static void Shutdown()
    {
        Evt($"[{Now:F2}] session end");
        Mod.Log.Msg($"session written to {_csvPath}");
    }
}
