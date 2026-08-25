// Instrumentation for the rare-but-huge freezes.
//
// Run 1 caught two of them: a 943ms frame at t=138.6 where the heap grew
// +363MB in a single interval and RESERVED heap jumped 492MB -> 800MB, then a
// 770ms frame at t=145 taking reserved to 878MB. Reserved-heap growth is a
// stop-the-world expansion, which is exactly what a ~1s freeze looks like.
// Both landed as the player approached and opened the shop.
//
// These methods are called rarely, so unlike the rope hooks they can afford a
// heap read per call.
using System.Diagnostics;
using Profiler = UnityEngine.Profiling.Profiler;

namespace DCProfiler;

internal static class Heavy
{
    private const double ReportMs = 15.0;      // only report calls worth looking at
    private const double ReportMb = 1.0;

    private struct Scope { public string Name; public long T0; public long Heap0; public bool Sampled; }

    private static readonly List<Scope> Stack = new();
    private static readonly Dictionary<string, (int n, double ms, double mb)> Totals = new();

    internal static void Reset() { Stack.Clear(); Totals.Clear(); SampleCounter.Clear(); }
    internal static void EndInterval() => Totals.Clear();

    // --- sampled variant, for methods called every frame -------------------
    // A heap read costs ~6us. That is nothing for a method called 200x/s but
    // real for one called every frame by many instances, so read the heap on
    // every Nth call only and scale the result. Timing is always exact.
    private const int HeapSampleEvery = 8;
    private static readonly Dictionary<string, int> SampleCounter = new();
    private static bool _sampledThisCall;

    internal static void EnterSampled(string name)
    {
        try
        {
            SampleCounter.TryGetValue(name, out var c);
            SampleCounter[name] = c + 1;
            _sampledThisCall = c % HeapSampleEvery == 0;

            long h = 0;
            if (_sampledThisCall)
            {
                try { h = Profiler.GetMonoUsedSizeLong(); } catch { }
            }
            Stack.Add(new Scope { Name = name, T0 = Stopwatch.GetTimestamp(), Heap0 = h, Sampled = _sampledThisCall });
        }
        catch { }
    }

    internal static void ExitSampled(string name)
    {
        try
        {
            if (Stack.Count == 0) return;
            var s = Stack[^1];
            Stack.RemoveAt(Stack.Count - 1);
            if (s.Name != name) return;

            var ms = (Stopwatch.GetTimestamp() - s.T0) * 1000.0 / Stopwatch.Frequency;
            double mb = 0;
            if (s.Sampled)
            {
                try { mb = (Profiler.GetMonoUsedSizeLong() - s.Heap0) / 1048576.0 * HeapSampleEvery; }
                catch { }
                if (mb < 0) mb = 0;   // a GC landed inside the sample; discard
            }

            Totals.TryGetValue(name, out var t);
            Totals[name] = (t.n + 1, t.ms + ms, t.mb + mb);
        }
        catch { }
    }

    internal static void Enter(string name)
    {
        try
        {
            long h = 0;
            try { h = Profiler.GetMonoUsedSizeLong(); } catch { }
            Stack.Add(new Scope { Name = name, T0 = Stopwatch.GetTimestamp(), Heap0 = h });
        }
        catch { }
    }

    internal static void Exit(string name)
    {
        try
        {
            if (Stack.Count == 0) return;
            var s = Stack[^1];
            Stack.RemoveAt(Stack.Count - 1);
            if (s.Name != name) return;   // mismatched nesting; drop rather than lie

            var ms = (Stopwatch.GetTimestamp() - s.T0) * 1000.0 / Stopwatch.Frequency;
            double mb = 0;
            try { mb = (Profiler.GetMonoUsedSizeLong() - s.Heap0) / 1048576.0; } catch { }

            Totals.TryGetValue(name, out var t);
            Totals[name] = (t.n + 1, t.ms + ms, t.mb + mb);

            if (ms >= ReportMs || mb >= ReportMb)
                Sampler.Evt($"[{Sampler.Now:F2}] HEAVY {name} {ms:F1}ms alloc={mb:F2}MB");
        }
        catch { }
    }

    internal static void FlushDetail()
    {
        if (Totals.Count == 0) return;
        foreach (var kv in Totals.OrderByDescending(k => k.Value.ms))
            Sampler.Evt($"    heavy {kv.Key}: n={kv.Value.n} {kv.Value.ms:F1}ms {kv.Value.mb:F2}MB");
    }
}
