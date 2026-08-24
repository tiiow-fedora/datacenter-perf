// Device churn characterisation - the #1 unknown.
//
// Previous session established that Server.Start / NetworkSwitch.Start fire
// with NEW InstanceIDs roughly 7 times every 5 seconds, i.e. genuinely new
// GameObjects. What that measurement could NOT say:
//   - are these real racked devices, or shop previews / pooled carry models?
//     (ComputerShop.RefreshSlotPreview and CarryModelPool.Get both exist and
//      would also trip a Start hook)
//   - do creates pair with destroys, or is something leaking?
//   - how long does a churned object live?
//   - where is it relative to the camera - is this camera-driven streaming?
// This class answers all four.
using System.Text;
using UnityEngine;

namespace DCProfiler;

internal static class Churn
{
    internal static int CreatedInterval;
    internal static int DestroyedInterval;
    private static int _createdTotal;
    private static int _destroyedTotal;

    private const int MaxDetailSamples = 6;   // full detail per interval
    private const int LifetimeMapCap = 20000;
    private const double LifetimeMapMaxAge = 300.0;

    // instanceID -> (birth time, path) so OnDestroy can report a lifetime.
    private static readonly Dictionary<int, (double born, string path)> Live = new();

    private static readonly List<string> Samples = new();
    private static readonly Dictionary<string, int> PathHist = new();
    private static readonly List<double> Lifetimes = new();

    private static Camera _cam;
    private static double _camRefreshed;

    internal static void Reset()
    {
        Live.Clear(); Samples.Clear(); PathHist.Clear(); Lifetimes.Clear();
        CreatedInterval = DestroyedInterval = 0;
        _createdTotal = _destroyedTotal = 0;
    }

    internal static void EndInterval()
    {
        CreatedInterval = 0;
        DestroyedInterval = 0;
        Samples.Clear();
        PathHist.Clear();
        Lifetimes.Clear();
    }

    internal static void OnCreated(GameObject go, string kind)
    {
        if (go == null) return;
        try
        {
            CreatedInterval++; _createdTotal++;
            var now = Sampler.Now;
            var id = go.GetInstanceID();
            var path = Path(go);

            PathHist.TryGetValue(path, out var n);
            PathHist[path] = n + 1;

            if (Live.Count < LifetimeMapCap) Live[id] = (now, path);

            if (Samples.Count < MaxDetailSamples)
                Samples.Add($"    +{kind,-14} id={id} scene={(go.scene.IsValid() ? "Y" : "N")} " +
                            $"camDist={CamDist(go):F1} path={path}");
        }
        catch { }
    }

    internal static void OnDestroyed(GameObject go, string kind)
    {
        if (go == null) return;
        try
        {
            DestroyedInterval++; _destroyedTotal++;
            var id = go.GetInstanceID();
            double life = -1;
            var path = "?";
            if (Live.TryGetValue(id, out var rec))
            {
                life = Sampler.Now - rec.born;
                path = rec.path;
                Live.Remove(id);
                Lifetimes.Add(life);
            }

            if (Samples.Count < MaxDetailSamples)
                Samples.Add($"    -{kind,-14} id={id} lived={(life < 0 ? "pre-existing" : life.ToString("F1") + "s")} " +
                            $"camDist={CamDist(go):F1} path={path}");
        }
        catch { }
    }

    private static float CamDist(GameObject go)
    {
        // Camera.main does a tag search; cache it rather than paying per event.
        if (_cam == null || Sampler.Now - _camRefreshed > 5.0)
        {
            _cam = Camera.main;
            _camRefreshed = Sampler.Now;
        }
        if (_cam == null) return -1f;
        return Vector3.Distance(_cam.transform.position, go.transform.position);
    }

    private static string Path(GameObject go)
    {
        var sb = new StringBuilder(go.name);
        var t = go.transform.parent;
        var depth = 0;
        while (t != null && depth++ < 5)
        {
            sb.Insert(0, t.gameObject.name + "/");
            t = t.parent;
        }
        return sb.ToString();
    }

    internal static void FlushDetail()
    {
        if (CreatedInterval == 0 && DestroyedInterval == 0) return;

        Sampler.Evt($"[{Sampler.Now:F2}] CHURN +{CreatedInterval} -{DestroyedInterval} " +
                    $"(session +{_createdTotal} -{_destroyedTotal}, tracked-live {Live.Count})");

        if (Lifetimes.Count > 0)
        {
            Lifetimes.Sort();
            var med = Lifetimes[Lifetimes.Count / 2];
            Sampler.Evt($"    lifetimes: n={Lifetimes.Count} min={Lifetimes[0]:F1}s " +
                        $"med={med:F1}s max={Lifetimes[^1]:F1}s");
        }

        if (PathHist.Count > 0)
        {
            var top = PathHist.OrderByDescending(kv => kv.Value).Take(4);
            foreach (var kv in top) Sampler.Evt($"    path x{kv.Value}: {kv.Key}");
        }

        foreach (var s in Samples) Sampler.Evt(s);

        PruneLiveMap();
    }

    // Bounded-growth discipline: this dictionary must never track runtime churn
    // without an upper bound (lesson from RealCustomers' growing collections).
    private static void PruneLiveMap()
    {
        if (Live.Count < LifetimeMapCap / 2) return;
        var cutoff = Sampler.Now - LifetimeMapMaxAge;
        var dead = Live.Where(kv => kv.Value.born < cutoff).Select(kv => kv.Key).ToList();
        foreach (var k in dead) Live.Remove(k);
        Sampler.Evt($"    (pruned {dead.Count} stale lifetime entries, {Live.Count} remain)");
    }

    internal static void DumpDetail()
    {
        Sampler.Evt($"[{Sampler.Now:F2}] ===== F8 MANUAL DUMP =====");
        Sampler.Evt($"    session created={_createdTotal} destroyed={_destroyedTotal} " +
                    $"net={_createdTotal - _destroyedTotal} tracked-live={Live.Count}");
        Mod.Log.Msg($"F8: session +{_createdTotal} -{_destroyedTotal} net={_createdTotal - _destroyedTotal}");
    }
}
