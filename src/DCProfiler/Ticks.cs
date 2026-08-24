// DeviceTimerManager tick cost.
//
// Static recon: DeviceTimerManager is a singleton MonoBehaviour holding
// activeDevices + tickSnapshot lists and a TimerLoop() coroutine. ITimedDevice
// has exactly two implementers - Server and NetworkSwitch - which is the same
// type pair observed respawning every ~5s. That coincidence is the leading
// hypothesis, so measure the tick directly: how often it fires, how many
// devices it touches, and how much wall time it costs.
//
// TimerLoop is a coroutine (IEnumerator), awkward to patch. TickTimer on each
// concrete type is a plain managed method, so time that instead and infer the
// loop's shape from the call pattern.
using System.Diagnostics;
using UnityEngine;

namespace DCProfiler;

internal static class Ticks
{
    internal static int CallsInterval;
    internal static double MsInterval;
    internal static int DistinctFramesInterval;
    internal static int BurstsInterval;

    private static long _callStart;
    private static int _lastFrame = -999;
    private static int _lastBurstFrame = -999;
    private static double _lastBurstTime = -1;
    private static readonly List<double> BurstGaps = new();
    private static readonly Dictionary<string, int> KindCounts = new();
    private static long _callsTotal;

    internal static void Reset()
    {
        CallsInterval = 0; MsInterval = 0; DistinctFramesInterval = 0; BurstsInterval = 0;
        _lastFrame = -999; _lastBurstFrame = -999; _lastBurstTime = -1;
        BurstGaps.Clear(); KindCounts.Clear(); _callsTotal = 0;
    }

    internal static void EndInterval()
    {
        CallsInterval = 0; MsInterval = 0; DistinctFramesInterval = 0; BurstsInterval = 0;
        BurstGaps.Clear(); KindCounts.Clear();
    }

    internal static void Begin() => _callStart = Stopwatch.GetTimestamp();

    internal static void End(string kind)
    {
        try
        {
            var elapsed = (Stopwatch.GetTimestamp() - _callStart) * 1000.0 / Stopwatch.Frequency;
            MsInterval += elapsed;
            CallsInterval++;
            _callsTotal++;

            KindCounts.TryGetValue(kind, out var kc);
            KindCounts[kind] = kc + 1;

            var frame = Time.frameCount;
            if (frame != _lastFrame)
            {
                DistinctFramesInterval++;
                _lastFrame = frame;

                // A gap of a whole frame with no ticks means a new burst began.
                if (frame > _lastBurstFrame + 1)
                {
                    BurstsInterval++;
                    var now = Sampler.Now;
                    if (_lastBurstTime > 0) BurstGaps.Add(now - _lastBurstTime);
                    _lastBurstTime = now;
                }
                _lastBurstFrame = frame;
            }
        }
        catch { }
    }

    internal static void FlushDetail()
    {
        if (CallsInterval == 0) return;

        var kinds = string.Join(" ", KindCounts.Select(kv => $"{kv.Key}={kv.Value}"));
        Sampler.Evt($"[{Sampler.Now:F2}] TICKS calls={CallsInterval} ({kinds}) " +
                    $"cost={MsInterval:F2}ms over {DistinctFramesInterval} frame(s), " +
                    $"{BurstsInterval} burst(s), session_calls={_callsTotal}");

        if (BurstGaps.Count > 0)
            Sampler.Evt($"    burst cadence: {string.Join(", ", BurstGaps.Select(g => g.ToString("F2") + "s"))}");
    }
}
