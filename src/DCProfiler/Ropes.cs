// Cable/rope instrumentation - the leading candidate for the steady allocation.
//
// The game bundles GogoGaga "OptimizedRopesAndCables". RopeMesh.Update() ->
// GenerateMesh() -> CreateRopeMesh(points[], radius, segmentsPerWire) builds
// tube geometry, which allocates vertex/triangle/normal/UV arrays. A built-out
// data centre is full of cables, so if that regenerates per-frame per-cable it
// would produce exactly the observed profile: steady MB/s, proportional to save
// size, completely independent of device churn.
//
// The package NAME claims it is optimised, and Rope exposes IsPointsMoved() /
// IsRopeSettingsChanged() guards, so it may well skip regeneration when nothing
// moves. That is the hypothesis under test - do not assume either way.
//
// Cost discipline: Rope.Update runs once per cable per frame, so these hooks
// must be an integer increment and nothing else.
using UnityEngine;

namespace DCProfiler;

internal static class Ropes
{
    // Per-frame counters, read and cleared by Sampler each frame.
    internal static int RopeUpdateFrame;
    internal static int MeshUpdateFrame;
    internal static int GenMeshFrame;
    internal static int CreateMeshFrame;

    // Per-interval totals.
    internal static long RopeUpdateInterval;
    internal static long MeshUpdateInterval;
    internal static long GenMeshInterval;
    internal static long CreateMeshInterval;

    private static int _ropeCensus = -1;
    private static int _meshCensus = -1;

    internal static void Reset()
    {
        RopeUpdateFrame = MeshUpdateFrame = GenMeshFrame = CreateMeshFrame = 0;
        RopeUpdateInterval = MeshUpdateInterval = GenMeshInterval = CreateMeshInterval = 0;
        _ropeCensus = _meshCensus = -1;
    }

    internal static void EndInterval()
    {
        RopeUpdateInterval = MeshUpdateInterval = GenMeshInterval = CreateMeshInterval = 0;
    }

    /// <summary>Roll this frame's counters into the interval totals and clear them.</summary>
    internal static void EndFrame()
    {
        RopeUpdateInterval += RopeUpdateFrame;
        MeshUpdateInterval += MeshUpdateFrame;
        GenMeshInterval += GenMeshFrame;
        CreateMeshInterval += CreateMeshFrame;
        RopeUpdateFrame = MeshUpdateFrame = GenMeshFrame = CreateMeshFrame = 0;
    }

    /// <summary>
    /// One-shot scene census. NEVER on a timer: Resources.FindObjectsOfTypeAll
    /// walks every loaded asset (~24000) and marshals the lot across the IL2CPP
    /// boundary - that alone was a measurable microstutter in RackColors v1.0.0.
    /// FindObjectsByType is scene-only, and this runs exactly once per load.
    /// </summary>
    internal static void Census()
    {
        try
        {
            var ropes = UnityEngine.Object.FindObjectsByType(
                Il2CppInterop.Runtime.Il2CppType.Of<Il2CppGogoGaga.OptimizedRopesAndCables.Rope>(),
                FindObjectsSortMode.None);
            var meshes = UnityEngine.Object.FindObjectsByType(
                Il2CppInterop.Runtime.Il2CppType.Of<Il2CppGogoGaga.OptimizedRopesAndCables.RopeMesh>(),
                FindObjectsSortMode.None);
            _ropeCensus = ropes?.Length ?? -1;
            _meshCensus = meshes?.Length ?? -1;
            Sampler.Evt($"[{Sampler.Now:F2}] ROPE CENSUS: {_ropeCensus} Rope, {_meshCensus} RopeMesh in scene");
            Mod.Log.Msg($"rope census: {_ropeCensus} Rope / {_meshCensus} RopeMesh");
        }
        catch (Exception e)
        {
            Sampler.Evt($"[{Sampler.Now:F2}] ROPE CENSUS failed: {e.Message}");
        }
    }

    internal static void FlushDetail(double span, int frames)
    {
        if (RopeUpdateInterval == 0 && MeshUpdateInterval == 0 && GenMeshInterval == 0) return;

        var f = Math.Max(frames, 1);
        Sampler.Evt($"[{Sampler.Now:F2}] ROPES census={_ropeCensus}/{_meshCensus} " +
                    $"Rope.Update={RopeUpdateInterval} ({RopeUpdateInterval / f}/frame) " +
                    $"RopeMesh.Update={MeshUpdateInterval} ({MeshUpdateInterval / f}/frame)");
        Sampler.Evt($"    GenerateMesh={GenMeshInterval} ({GenMeshInterval / (double)f:F1}/frame) " +
                    $"CreateRopeMesh={CreateMeshInterval} ({CreateMeshInterval / (double)f:F1}/frame)" +
                    $"{(GenMeshInterval == 0 ? "   <- ropes are NOT regenerating; not the alloc source" : "")}");
    }
}
