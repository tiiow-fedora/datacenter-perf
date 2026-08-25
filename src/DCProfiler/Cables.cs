// Cable mesh instrumentation - the current leading candidate.
//
// Run 2 ruled out the bundled GogoGaga rope package outright: exactly 1 Rope in
// the scene all session and ZERO RopeMesh/GenerateMesh/CreateRopeMesh calls.
// But cables clearly exist, so the game must build them itself - and it does:
//
//   CablePositions.RedrawCable(id) -> CreateTubeMesh(List<Vector3> path)
//   fed by GenerateFinalPath / GenerateCornerBend / GenerateBentSegment
//
// GenerateCornerBend and GenerateBentSegment return IEnumerable<Vector3>, i.e.
// compiler-generated iterators, which allocate a state machine per call and are
// typically LINQ-chained. Combined with a fresh Mesh per redraw that is a very
// allocation-heavy path.
//
// Fits the run 2 signature: 40MB/s sustained for ~30s, spread over hundreds of
// frames, zero device churn - and the user was "messing with cables" during the
// t=576-586 burst.
//
// NOTE: patching the two IEnumerable methods would only time iterator CREATION,
// not enumeration, so they are deliberately measured via GenerateFinalPath,
// which consumes them.
namespace DCProfiler;

internal static class Cables
{
    internal static int RedrawInterval;
    internal static int TubeMeshInterval;
    internal static int FinalPathInterval;
    private static long _redrawTotal, _tubeTotal;

    internal static void Reset()
    {
        RedrawInterval = TubeMeshInterval = FinalPathInterval = 0;
        _redrawTotal = _tubeTotal = 0;
    }

    internal static void EndInterval()
    {
        RedrawInterval = TubeMeshInterval = FinalPathInterval = 0;
    }

    internal static void CountRedraw() { RedrawInterval++; _redrawTotal++; }
    internal static void CountTube() { TubeMeshInterval++; _tubeTotal++; }
    internal static void CountFinalPath() => FinalPathInterval++;

    internal static void FlushDetail(double span)
    {
        if (RedrawInterval == 0 && TubeMeshInterval == 0 && FinalPathInterval == 0) return;
        Sampler.Evt($"[{Sampler.Now:F2}] CABLES RedrawCable={RedrawInterval} " +
                    $"CreateTubeMesh={TubeMeshInterval} GenerateFinalPath={FinalPathInterval} " +
                    $"({TubeMeshInterval / span:F1} tube/s) session: redraw={_redrawTotal} tube={_tubeTotal}");
    }
}
