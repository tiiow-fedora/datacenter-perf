# datacenter-perf

Performance work for the Steam game **Data Center** (appid 4170200, Unity 6000.4.12f1, IL2CPP + DOTS/Burst).

Current stage: **profiling**. No optimisation code exists yet, by design — nothing ships that the numbers haven't confirmed helps.

## DCProfiler (v0.1.0) — diagnostic only

`src/DCProfiler` measures; it never changes game behaviour. Every Harmony hook is a
diagnostic prefix/postfix that alters no argument, return value or control flow.

**This must never ship.** The optimisation mod will be a separate assembly.
(RealCustomers v1.2.0 shipped probe code by accident and spammed
`MissingMethodException` every frame — grep for probe/diagnostic code before any release.)

### What it measures

| Instrument | Answers |
|---|---|
| Frame timing (`Sampler`) | mean/p50/p95/p99/max ms, fps, individual frames over 50 ms |
| IL2CPP heap (`Sampler`) | `Profiler.GetMonoUsedSizeLong` per interval + delta — allocation churn |
| Device churn (`Churn`) | creates vs destroys, object **lifetime**, hierarchy path, camera distance, scene validity |
| Tick cost (`Ticks`) | `TickTimer` calls/interval, wall-clock ms, frames spanned, burst count and **cadence** |

All counters accumulate on the main thread and flush once every 5 s. No per-frame
allocation, no `Resources.FindObjectsOfTypeAll` — a probe that perturbs what it
measures is worthless (lesson from RackColors v1.0.0's polling microstutter).

Uses the Unity profiler for heap numbers, **not** `GC.GetTotalMemory` — a MelonLoader
mod runs on its own .NET 6 heap, separate from the game's IL2CPP heap, so the
mod's own GC counters would measure the wrong thing entirely.

### Output

- `UserData/DCProfiler/session-<stamp>.csv` — one row per 5 s interval, 20 columns
- `UserData/DCProfiler/events-<stamp>.log` — spikes, churn detail, tick cadence
- A compact summary line per interval in `MelonLoader/Latest.log`

### Hotkeys

| Key | Action |
|---|---|
| `F7` | cycle phase marker: still → walk → shop (tags every CSV row) |
| `F8` | dump cumulative churn totals immediately |

Both debounced 0.35 s — `wasPressedThisFrame` fires once per OS key-repeat, not
once per physical press.

## Leading hypotheses (static recon, all still unproven)

1. **`DeviceTimerManager`** — singleton with `activeDevices` + `tickSnapshot` lists and
   a `TimerLoop()` coroutine. `ITimedDevice` has exactly two implementers, `Server` and
   `NetworkSwitch` — the same pair observed respawning every ~5 s. The `tickSnapshot`
   list implies a per-tick copy sized by device count.
2. **Not auto-repair.** `CommandCenter.AutoRepairRoutine` and
   `Technician.positionOfDeviceToBeReplaced` would destroy+recreate devices, but the live
   2.9 MB save holds only 4 `RepairJobSaveData` records against ~430 device name tokens.
   A 4-job queue cannot produce ~7 new devices every 5 s.
3. **Not `RackMount.InstantiateRack`** — interaction/`OnLoad` driven, not timed.
4. Churn may partly be **shop previews or pooled carry models**
   (`ComputerShop.RefreshSlotPreview`, `CarryModelPool.Get` + `StripGameplayComponents`)
   rather than real racked devices. The path/scene/camera-distance columns separate these.

## Build

```
dotnet build -c Release src/DCProfiler
```

.NET SDK 8 lives at `%LOCALAPPDATA%\Microsoft\dotnet` and is **not on PATH**.
Il2Cpp references resolve straight out of the game install.

## Mod parking

`restore-mods.sh` puts RackColors + RealCustomers back and removes the profiler.
Run with the game closed — loaded DLLs are file-locked.

## Environment notes

- Il2CppAssemblyFixer is **mandatory** for any mod in this game.
- `[MelonGame]` developer string is exactly `"Waseku"`, case sensitive.
- Launch via **Steam only** — the exe directly throws Steam DRM "No licenses".
