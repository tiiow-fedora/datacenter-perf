using MelonLoader;

[assembly: MelonInfo(typeof(DCProfiler.Mod), "DCProfiler", "0.6.0", "tiiow-fedora")]
// Developer string comes from Data Center_Data/app.info and IS case sensitive -
// "WASEKU" makes MelonLoader skip the mod as incompatible.
[assembly: MelonGame("Waseku", "Data Center")]

namespace DCProfiler;

/// <summary>
/// DIAGNOSTIC ONLY. This mod measures; it never changes game behaviour.
/// Nothing in here may ship in the optimisation mod - see README.
/// </summary>
public class Mod : MelonMod
{
    internal static MelonLogger.Instance Log;

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        Sampler.Init();
        PatchCoroutines();
    }

    /// <summary>
    /// A coroutine's real per-frame work happens in the compiler-generated state
    /// machine's MoveNext, not in the method that returns the IEnumerator. Those
    /// generated type names (_EvaluateAllRoutesCoroutine_d__73) can't be written
    /// as Harmony attribute targets, so bind them by name at runtime.
    /// </summary>
    private void PatchCoroutines()
    {
        Hook("Il2Cpp.WaypointInitializationSystem+_EvaluateAllRoutesCoroutine_d__73",
             nameof(RouteMovePrefix), nameof(RouteMovePostfix));
        Hook("Il2Cpp.WaypointInitializationSystem+_UpdateAllUI_d__80",
             nameof(UiMovePrefix), nameof(UiMovePostfix));
    }

    private void Hook(string typeName, string prefix, string postfix)
    {
        try
        {
            var t = HarmonyLib.AccessTools.TypeByName(typeName);
            if (t == null) { Log.Warning($"coroutine type not found: {typeName}"); return; }
            var m = HarmonyLib.AccessTools.Method(t, "MoveNext");
            if (m == null) { Log.Warning($"MoveNext not found on {typeName}"); return; }

            HarmonyInstance.Patch(m,
                new HarmonyLib.HarmonyMethod(typeof(Mod).GetMethod(prefix,
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)),
                new HarmonyLib.HarmonyMethod(typeof(Mod).GetMethod(postfix,
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
            Log.Msg($"hooked {typeName}.MoveNext");
        }
        catch (Exception e)
        {
            Log.Warning($"failed to hook {typeName}: {e.Message}");
        }
    }

    private static void RouteMovePrefix() => Routes.MoveBegin();
    private static void RouteMovePostfix() => Routes.MoveEnd();
    private static void UiMovePrefix() => Heavy.EnterSampled("Waypoint.UpdateAllUI.MoveNext");
    private static void UiMovePostfix() => Heavy.ExitSampled("Waypoint.UpdateAllUI.MoveNext");

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        => Sampler.OnSceneLoaded(sceneName);

    public override void OnUpdate() => Sampler.Tick();

    public override void OnApplicationQuit()
    {
        // Never leave the player's settings altered by a diagnostic build.
        Experiments.RestoreAll();
        Sampler.Shutdown();
    }
}
