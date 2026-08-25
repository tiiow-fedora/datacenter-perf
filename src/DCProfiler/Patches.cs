// Harmony hooks. All are diagnostic - none alter arguments, return values or
// control flow.
//
// Start()/OnDestroy()/TickTimer() on Server and NetworkSwitch are real managed
// methods declared directly on the type (verified via tools/Inspect), so Harmony
// binds cleanly. This is unlike IL2CPP field accessors, which bind and then
// silently no-op, and unlike Burst-compiled ISystems, which bind with no warning
// and never fire because Burst runs a native copy.
//
// Router and Firewall both derive from NetworkSwitch without redeclaring these
// methods, so one patch per method covers switches, routers and firewalls.
using HarmonyLib;
using Rope = Il2CppGogoGaga.OptimizedRopesAndCables.Rope;
using RopeMesh = Il2CppGogoGaga.OptimizedRopesAndCables.RopeMesh;

namespace DCProfiler;

// --- device churn ------------------------------------------------------------

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
    // Prefix, not postfix: after OnDestroy the GameObject may already be
    // unusable for reading name/transform.
    private static void Prefix(Il2Cpp.Server __instance)
    {
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
// static start-timestamp is cheaper and safer than Harmony __state.

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

// --- ropes / cables ----------------------------------------------------------
// Rope.Update runs once per cable per frame. These hooks are a single integer
// increment on purpose: anything heavier would perturb what it measures.

[HarmonyPatch(typeof(Rope), "Update")]
internal static class Patch_RopeUpdate
{
    private static void Postfix() => Ropes.RopeUpdateFrame++;
}

[HarmonyPatch(typeof(RopeMesh), "Update")]
internal static class Patch_RopeMeshUpdate
{
    private static void Postfix() => Ropes.MeshUpdateFrame++;
}

[HarmonyPatch(typeof(RopeMesh), "GenerateMesh")]
internal static class Patch_GenerateMesh
{
    private static void Postfix() => Ropes.GenMeshFrame++;
}

[HarmonyPatch(typeof(RopeMesh), "CreateRopeMesh")]
internal static class Patch_CreateRopeMesh
{
    private static void Postfix() => Ropes.CreateMeshFrame++;
}

// --- cables: the leading candidate ------------------------------------------
// Counters are cheap; the Heavy scopes carry the heap reads. CreateTubeMesh is
// per-cable-per-redraw, so a heap read per call is affordable at the observed
// rates but the counter is what proves the call volume.

[HarmonyPatch(typeof(Il2Cpp.CablePositions), "RedrawCable")]
internal static class Patch_RedrawCable
{
    private static void Prefix() { Cables.CountRedraw(); Heavy.Enter("CablePositions.RedrawCable"); }
    private static void Postfix() => Heavy.Exit("CablePositions.RedrawCable");
}

[HarmonyPatch(typeof(Il2Cpp.CablePositions), "CreateTubeMesh")]
internal static class Patch_CreateTubeMesh
{
    private static void Prefix() { Cables.CountTube(); Heavy.Enter("CablePositions.CreateTubeMesh"); }
    private static void Postfix() => Heavy.Exit("CablePositions.CreateTubeMesh");
}

[HarmonyPatch(typeof(Il2Cpp.CablePositions), "GenerateFinalPath")]
internal static class Patch_GenerateFinalPath
{
    private static void Prefix() { Cables.CountFinalPath(); Heavy.Enter("CablePositions.GenerateFinalPath"); }
    private static void Postfix() => Heavy.Exit("CablePositions.GenerateFinalPath");
}

[HarmonyPatch(typeof(Il2Cpp.CablePositions), "LoadCable")]
internal static class Patch_LoadCable
{
    private static void Prefix() => Heavy.Enter("CablePositions.LoadCable");
    private static void Postfix() => Heavy.Exit("CablePositions.LoadCable");
}

[HarmonyPatch(typeof(Il2Cpp.CablePositions), "AssignNewPosition")]
internal static class Patch_AssignNewPosition
{
    private static void Prefix() => Heavy.Enter("CablePositions.AssignNewPosition");
    private static void Postfix() => Heavy.Exit("CablePositions.AssignNewPosition");
}

[HarmonyPatch(typeof(Il2Cpp.CablePositions), "SetCableColor")]
internal static class Patch_SetCableColor
{
    private static void Prefix() => Heavy.Enter("CablePositions.SetCableColor");
    private static void Postfix() => Heavy.Exit("CablePositions.SetCableColor");
}

// --- network route evaluation: the leading candidate -------------------------
// WaypointInitializationSystem is a managed SystemBase, so unlike the Burst
// ISystems it is reachable by Harmony. The coroutine state machines are patched
// manually in Mod.cs because their generated names (_EvaluateAllRoutesCoroutine
// _d__73) cannot be written as attribute targets.

[HarmonyPatch(typeof(Il2Cpp.WaypointInitializationSystem), "EvaluateAllRoutes")]
internal static class Patch_EvaluateAllRoutes
{
    private static void Prefix() => Routes.EvalBegin();
    private static void Postfix() => Routes.EvalEnd();
}

[HarmonyPatch(typeof(Il2Cpp.WaypointInitializationSystem), "RequestRouteEvaluation")]
internal static class Patch_RequestRouteEvaluation
{
    private static void Postfix() => Routes.CountRequest();
}

[HarmonyPatch(typeof(Il2Cpp.WaypointInitializationSystem), "OnUpdate")]
internal static class Patch_WaypointOnUpdate
{
    private static void Postfix() => Routes.CountSystemUpdate();
}

[HarmonyPatch(typeof(Il2Cpp.WaypointInitializationSystem), "ActivateSpawnersForCable")]
internal static class Patch_ActivateSpawners
{
    private static void Prefix() => Heavy.EnterSampled("Waypoint.ActivateSpawnersForCable");
    private static void Postfix() => Heavy.ExitSampled("Waypoint.ActivateSpawnersForCable");
}

[HarmonyPatch(typeof(Il2Cpp.WaypointInitializationSystem), "CreateSpawner")]
internal static class Patch_CreateSpawner
{
    private static void Prefix() => Heavy.EnterSampled("Waypoint.CreateSpawner");
    private static void Postfix() => Heavy.ExitSampled("Waypoint.CreateSpawner");
}

// --- broad per-frame sweep ---------------------------------------------------
// If the allocation is NOT cables, it is some other MonoBehaviour running every
// frame. These are the gameplay-relevant Update() methods; the rest of the 41 in
// the assembly are TMPro examples and the on-screen-keyboard package, which
// never run here. Heavy samples the heap rather than reading it every call.

[HarmonyPatch(typeof(Il2Cpp.TimeController), "Update")]
internal static class Patch_TimeControllerUpdate
{
    private static void Prefix() => Heavy.EnterSampled("TimeController.Update");
    private static void Postfix() => Heavy.ExitSampled("TimeController.Update");
}

[HarmonyPatch(typeof(Il2Cpp.Technician), "Update")]
internal static class Patch_TechnicianUpdate
{
    private static void Prefix() => Heavy.EnterSampled("Technician.Update");
    private static void Postfix() => Heavy.ExitSampled("Technician.Update");
}

[HarmonyPatch(typeof(Il2Cpp.AICharacterControl), "Update")]
internal static class Patch_AIUpdate
{
    private static void Prefix() => Heavy.EnterSampled("AICharacterControl.Update");
    private static void Postfix() => Heavy.ExitSampled("AICharacterControl.Update");
}

[HarmonyPatch(typeof(Il2Cpp.PositionIndicator), "Update")]
internal static class Patch_PositionIndicatorUpdate
{
    private static void Prefix() => Heavy.EnterSampled("PositionIndicator.Update");
    private static void Postfix() => Heavy.ExitSampled("PositionIndicator.Update");
}

[HarmonyPatch(typeof(Il2Cpp.SteamManager), "Update")]
internal static class Patch_SteamUpdate
{
    private static void Prefix() => Heavy.EnterSampled("SteamManager.Update");
    private static void Postfix() => Heavy.ExitSampled("SteamManager.Update");
}

// --- the big freezes: shop and list UI ---------------------------------------

[HarmonyPatch(typeof(Il2Cpp.ComputerShop), "RefreshRoomUI")]
internal static class Patch_RefreshRoomUI
{
    private static void Prefix() => Heavy.Enter("ComputerShop.RefreshRoomUI");
    private static void Postfix() => Heavy.Exit("ComputerShop.RefreshRoomUI");
}

[HarmonyPatch(typeof(Il2Cpp.ComputerShop), "RefreshTemplateShopItems")]
internal static class Patch_RefreshTemplates
{
    private static void Prefix() => Heavy.Enter("ComputerShop.RefreshTemplateShopItems");
    private static void Postfix() => Heavy.Exit("ComputerShop.RefreshTemplateShopItems");
}

[HarmonyPatch(typeof(Il2Cpp.ComputerShop), "RefreshSlotPreview")]
internal static class Patch_RefreshSlotPreview
{
    private static void Prefix() => Heavy.Enter("ComputerShop.RefreshSlotPreview");
    private static void Postfix() => Heavy.Exit("ComputerShop.RefreshSlotPreview");
}

[HarmonyPatch(typeof(Il2Cpp.ServiceRequests), "RebuildUI")]
internal static class Patch_SRRebuildUI
{
    private static void Prefix() => Heavy.Enter("ServiceRequests.RebuildUI");
    private static void Postfix() => Heavy.Exit("ServiceRequests.RebuildUI");
}

[HarmonyPatch(typeof(Il2Cpp.DynamicScrollList), "Refresh")]
internal static class Patch_ScrollRefresh
{
    private static void Prefix() => Heavy.Enter("DynamicScrollList.Refresh");
    private static void Postfix() => Heavy.Exit("DynamicScrollList.Refresh");
}
