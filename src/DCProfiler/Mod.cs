using MelonLoader;

[assembly: MelonInfo(typeof(DCProfiler.Mod), "DCProfiler", "0.3.0", "tiiow-fedora")]
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
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        => Sampler.OnSceneLoaded(sceneName);

    public override void OnUpdate() => Sampler.Tick();

    public override void OnApplicationQuit() => Sampler.Shutdown();
}
