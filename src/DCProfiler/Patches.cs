// Harmony hooks. All postfix/prefix pairs are diagnostic - none alter arguments,
// return values or control flow.
//
// Start() and OnDestroy() on both types are real managed methods declared
// directly on the type (verified via tools/Inspect), so Harmony binds cleanly.
// This is unlike IL2CPP field accessors, which bind and then silently no-op.
//
// Router and Firewall both derive from NetworkSwitch and do not redeclare these
// methods, so one patch per method covers switches, routers and firewalls.
using HarmonyLib;

namespace DCProfiler;

[HarmonyPatch(typeof(Il2Cpp.Server), "Start")]
internal static class Patch_ServerStart
{
    private static void Postfix(Il2Cpp.Server __instance)
    {
        if (__instance != null) Churn.OnCreated(__instance.gameObject, "Server");
    }
}

[HarmonyPatch(typeof(Il2Cpp.Server), "OnDestroy")]
internal static class Patch_ServerDestroy
{
    private static void Prefix(Il2Cpp.Server __instance)
    {
        // Prefix, not postfix: after OnDestroy the GameObject may already be
        // unusable for reading name/transform.
        if (__instance != null) Churn.OnDestroyed(__instance.gameObject, "Server");
    }
}

[HarmonyPatch(typeof(Il2Cpp.NetworkSwitch), "Start")]
internal static class Patch_SwitchStart
{
    private static void Postfix(Il2Cpp.NetworkSwitch __instance)
    {
        if (__instance != null) Churn.OnCreated(__instance.gameObject, "NetworkSwitch");
    }
}

[HarmonyPatch(typeof(Il2Cpp.NetworkSwitch), "OnDestroy")]
internal static class Patch_SwitchDestroy
{
    private static void Prefix(Il2Cpp.NetworkSwitch __instance)
    {
        if (__instance != null) Churn.OnDestroyed(__instance.gameObject, "NetworkSwitch");
    }
}

// --- DeviceTimerManager tick cost -------------------------------------------
// Calls are sequential on the main thread and never reentrant, so a single
// static start-timestamp is safe and cheaper than Harmony __state.

[HarmonyPatch(typeof(Il2Cpp.Server), "TickTimer")]
internal static class Patch_ServerTick
{
    private static void Prefix() => Ticks.Begin();
    private static void Postfix() => Ticks.End("Server");
}

[HarmonyPatch(typeof(Il2Cpp.NetworkSwitch), "TickTimer")]
internal static class Patch_SwitchTick
{
    private static void Prefix() => Ticks.Begin();
    private static void Postfix() => Ticks.End("NetworkSwitch");
}
