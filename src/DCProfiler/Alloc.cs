// Allocation attribution.
//
// Run 1 finding: the game allocates a steady ~1.4 MB/s on the IL2CPP managed
// heap while STANDING COMPLETELY STILL with zero device churn, and framerate
// decays with heap growth (133fps -> 91fps as heap climbed 300->363MB, snapping
// back to 162fps the instant a collection freed 41MB). Run 1 measured tick TIME
// but not tick ALLOCATION, so the source is still unattributed.
//
// This class buckets per-frame heap delta by what ran during that frame, which
// is what separates "the tick loop allocates" from "something per-frame does".
using UnityEngine;
using Profiler = UnityEngine.Profiling.Profiler;

namespace DCProfiler;

internal static class Alloc
{
    private static long _lastHeap = -1;

    internal static double MbTotal;
    internal static double MbInRopeGenFrames;
    internal static double MbInTickFrames;
    internal static double MbInQuietFrames;
    internal static int RopeGenFrames, TickFrames, QuietFrames;
    internal static int GcEvents;
    internal static double MbFreedByGc;
    internal static double ProbeCostMs;   // cost of the heap read itself

    internal static void Reset()
    {
        MbTotal = MbInRopeGenFrames = MbInTickFrames = MbInQuietFrames = 0;
        RopeGenFrames = TickFrames = QuietFrames = GcEvents = 0;
        MbFreedByGc = 0;
        _lastHeap = -1;
    }

    internal static void EndInterval() => Reset();

    /// <summary>
    /// Called once per frame from Sampler.Tick, AFTER the frame's counters are
    /// readable. Attributes this frame's heap delta to whatever ran in it.
    /// </summary>
    internal static void SampleFrame(int ropeGenCalls, int tickCalls)
    {
        long heap;
        var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try { heap = Profiler.GetMonoUsedSizeLong(); }
        catch { return; }
        ProbeCostMs += (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0
                       / System.Diagnostics.Stopwatch.Frequency;

        if (_lastHeap < 0) { _lastHeap = heap; return; }

        var deltaMb = (heap - _lastHeap) / 1048576.0;
        _lastHeap = heap;

        if (deltaMb < 0)
        {
            // Heap shrank: a collection ran. Not an allocation signal.
            GcEvents++;
            MbFreedByGc += -deltaMb;
            return;
        }

        MbTotal += deltaMb;

        // A frame can contain both; rope regen is the more specific signal so it
        // wins the attribution, and the tick bucket stays honest about overlap.
        if (ropeGenCalls > 0) { MbInRopeGenFrames += deltaMb; RopeGenFrames++; }
        else if (tickCalls > 0) { MbInTickFrames += deltaMb; TickFrames++; }
        else { MbInQuietFrames += deltaMb; QuietFrames++; }
    }

    internal static void FlushDetail(double span)
    {
        if (MbTotal <= 0.01 && GcEvents == 0) return;

        Sampler.Evt($"[{Sampler.Now:F2}] ALLOC {MbTotal:F2}MB in {span:F1}s = {MbTotal / span:F2}MB/s " +
                    $"| gc {GcEvents}x freed {MbFreedByGc:F1}MB | probe_cost {ProbeCostMs:F2}ms");
        Sampler.Evt($"    ropegen frames n={RopeGenFrames} {MbInRopeGenFrames:F2}MB " +
                    $"({Per(MbInRopeGenFrames, RopeGenFrames)} KB/frame)");
        Sampler.Evt($"    tick    frames n={TickFrames} {MbInTickFrames:F2}MB " +
                    $"({Per(MbInTickFrames, TickFrames)} KB/frame)");
        Sampler.Evt($"    quiet   frames n={QuietFrames} {MbInQuietFrames:F2}MB " +
                    $"({Per(MbInQuietFrames, QuietFrames)} KB/frame)");
        ProbeCostMs = 0;
    }

    private static string Per(double mb, int n)
        => n == 0 ? "-" : (mb * 1024.0 / n).ToString("F1");
}
