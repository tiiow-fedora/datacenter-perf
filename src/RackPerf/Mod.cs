using MelonLoader;
using MelonLoader.Utils;
using UnityEngine.InputSystem;

[assembly: MelonInfo(typeof(RackPerf.Mod), "RackPerf", "1.0.1", "tiiow-fedora")]
// Developer string comes from Data Center_Data/app.info and IS case sensitive -
// "WASEKU" makes MelonLoader skip the mod as incompatible.
[assembly: MelonGame("Waseku", "Data Center")]

namespace RackPerf;

public class Mod : MelonMod
{
    internal static MelonLogger.Instance Log;
    internal static Config Cfg;

    private static double _lastHotkey;
    private static bool _throttleOn = true;

    public override void OnInitializeMelon()
    {
        Log = LoggerInstance;
        Cfg = Config.LoadOrCreate(Path.Combine(MelonEnvironment.UserDataDirectory, "RackPerf"));
        Log.Msg(Cfg.Enabled
            ? $"enabled - route evaluation coalescing window {Cfg.EvaluationCooldownSeconds}s"
            : "DISABLED by config - game left untouched");
        if (Cfg.EnableHotkey) Log.Msg("F9 toggles the throttle for A/B comparison");
    }

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        => Throttle.OnSceneLoaded();

    public override void OnUpdate()
    {
        Throttle.Tick();
        Hotkey();
    }

    private static void Hotkey()
    {
        if (!Cfg.EnableHotkey) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        // wasPressedThisFrame fires once per OS key-repeat, not once per physical
        // press; without this debounce one tap toggles several times and lands
        // back where it started, which looks exactly like a dead hotkey.
        var now = UnityEngine.Time.realtimeSinceStartupAsDouble;
        if (now - _lastHotkey < 0.35) return;

        if (kb.f9Key.wasPressedThisFrame)
        {
            _lastHotkey = now;
            _throttleOn = !_throttleOn;
            if (_throttleOn) Throttle.Reapply(); else Throttle.Restore();
            Log.Msg($"throttle {(_throttleOn ? "ON" : "OFF (stock)")}");
        }
    }
}
