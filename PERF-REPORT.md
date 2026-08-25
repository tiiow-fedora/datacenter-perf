# Data Center — performance findings from external profiling

Two measured, reproducible issues in managed code, both with concrete numbers and both
in paths that already contain the machinery to fix them.

Short version:

1. **Network route evaluation has no cooldown** (`evaluationCooldown == 0`), so every cable
   edit triggers a full re-traversal of the network. Measured: **77 complete re-evaluations
   in 208 seconds**, ~114 MB allocated each. The throttle already exists in the code — it
   just isn't set.
2. **A single `DynamicScrollList.Refresh` call allocated 366 MB and blocked for 625 ms**, and
   the memory was not reclaimed — the managed heap floor rose permanently from ~300 MB to
   ~635 MB for the rest of the session.

Everything below is measured, not inferred, unless explicitly marked as a guess.

---

## Environment and method

| | |
|---|---|
| Build | Steam appid 4170200, Unity 6000.4.12f1, IL2CPP + DOTS/Burst, HDRP |
| Save | 588 devices, 1614 cables, 39,501 renderers, 2.9 MB save file |
| Display | 1920×1080 windowed, ULTRA quality, vSync off |
| Typical frame | ~10–11 ms (90–120 fps) standing still |

Measurements came from a purpose-built MelonLoader profiling mod (Harmony hooks +
`UnityEngine.Profiling.Profiler` for the IL2CPP heap). Cosmetic mods were removed for all
runs; the only mod present was the profiler.

**Profiler overhead was itself measured** at ~2.5 ms per 5-second interval (~0.05%), so it
is not a meaningful contributor to any number here.

Nine sessions were run. Findings that failed to reproduce were discarded — see
*Ruled out* below, which may save you reading time.

---

## Finding 1 — Route evaluation runs unthrottled during network edits

**Where:** `WaypointInitializationSystem.EvaluateAllRoutesCoroutine`

**Severity:** moderate. Not a crash, and idle play is unaffected — but it dominates
allocation whenever the player is building, which is most of the active play loop.

### Measured

At startup the profiler read back the system's own settings:

```
route evaluation ms budget = 20
route evaluation cooldown  = 0.00s
```

With the cooldown at zero, evaluations are effectively back-to-back. Over a 208-second
session of cable editing:

| metric | value |
|---|---|
| `ActivateSpawnersForCable` calls | **124,754** |
| ÷ 1614 cables | **≈ 77 full network passes** |
| effective rate | one complete re-evaluation every **~2.6 s** |
| allocation per pass | **~114 MB** |
| coroutine time | **600–1000 ms per 5 s** (~12–20% of wall time) |
| total allocated by this path | **~8.8 GB** over the session |

Attribution across two independent runs, measuring heap delta inside the coroutine's
`MoveNext`:

| phase | total allocation | attributable to route evaluation |
|---|---|---|
| standing still | 689 MB | 468 MB (**68%**) |
| editing cables | 1664 MB | 1121 MB (**67%**) |
| editing cables (later run) | 815 MB | 492 MB (**60%**) |

During editing this correlates with ~0.75 frames per second exceeding 50 ms.

### Correction to an earlier reading

An initial run suggested re-evaluation every ~12 s *while idle*. That did **not** hold up —
a later run recorded **zero** evaluations across 115 seconds of standing still. The early
figure was the network settling after load. **Idle play is genuinely cheap** (~1.2 MB/s, no
spikes). The issue is confined to active editing.

### Why this looks like an oversight rather than a design choice

The class already contains the parts needed to avoid this:

- `SetEvaluationCooldown(float)` / `GetEvaluationCooldown()`
- `SetEvaluationMsBudget(int)` (static)
- `RentPathList` / `ReturnPathList` / `RentIntSet` / `RentStringSet` / `RecycleCableUsage`

The pooling helpers suggest this path was already identified as hot. The cooldown appears
to be the intended throttle, sitting at its default of 0.

Also worth noting: `RequestRouteEvaluation()` was called **0 times** in every measured phase,
while evaluations ran continuously — so `needsToEvaluateRoutes` is being set somewhere that
bypasses that entry point. That may be where the redundancy actually originates.

### Suggested direction

A small **coalescing window** (order of 0.5–2 s) so a burst of edits collapses into one
evaluation once the player stops changing things. Rapid edits are exactly the case that
currently costs the most and benefits least from per-edit accuracy.

We tested this from the outside by setting the cooldown to 1.0 s. It made **no measurable
difference**, which is consistent with passes already being ~2.6 s apart — the window has to
exceed the natural pass rate to bind. We did not push further, because from outside the code
we could not tell which evaluations were genuinely required.

---

## Finding 2 — `DynamicScrollList.Refresh` allocates 366 MB in one call and retains it

**Severity:** higher, in our view. Single-call, user-visible freeze, plus a permanent
heap-floor increase.

### Measured

```
HEAVY DynamicScrollList.Refresh   625.2 ms   alloc = 365.98 MB
HEAVY DynamicScrollList.Refresh   269.0 ms   alloc =   0.00 MB
HEAVY DynamicScrollList.Refresh   270.6 ms   alloc =   0.02 MB
```

Across one session: 5 calls, **394 MB**, 901 ms total.

The allocation is **not reclaimed**. Managed heap around that call:

| time | heap used | heap reserved |
|---|---|---|
| t=420 s | 332 MB | 451 MB |
| **t=425 s** | **636 MB** | **867 MB** |
| t=466 s | 737 MB | 867 MB |

The heap floor had been stable at ~300 MB for the preceding eight minutes and never
returned below ~635 MB afterwards. Reserved heap growth is a stop-the-world expansion, which
matches the observed ~625 ms and ~770 ms stalls.

### Guess (unverified — we can't see the source)

366 MB for one list refresh suggests a row or entry being built per item without
virtualisation or reuse, over a collection that scales with device count (588 here). If the
list rebuilds rather than recycles, both the stall and the retention would follow.

---

## Ruled out (so you don't re-investigate)

Each of these was a hypothesis we measured and discarded:

- **Device create/destroy churn** — an earlier report of ~7 devices respawning every 5 s did
  not reproduce. Two sessions showed 588 created at load and essentially nothing after
  (−12 destroys across ten minutes).
- **`DeviceTimerManager` tick** — 490 devices tick on an exact 1.00 s cadence for
  **1.2–1.4 ms per five seconds**. Negligible.
- **Cable mesh generation** — `CreateTubeMesh` ran 1640 times for ~29 MB, essentially all at
  load. Cables are cheap to draw; they are expensive to *re-route* (Finding 1).
- **The bundled GogoGaga rope package** — 1 `Rope` in the scene, 0 `RopeMesh`, zero mesh
  regenerations. Not used at scale.
- **Managed heap growth as a cause of framerate decay** — tempting (171 fps @ 309 MB early
  vs 77 fps @ 826 MB late) but false: r = −0.44, and one interval ran 120 fps on a 900 MB
  heap while another ran 53 fps on 316 MB. Framerate tracks camera direction, not heap.

---

## Rendering: already well optimised

Worth stating plainly, since it was where we expected to find wins and did not.

A one-variable-at-a-time sweep from a fixed viewpoint:

| change | frame time delta |
|---|---|
| half resolution (¼ the pixels) | −1.69 ms |
| shadows off | **0.00 ms** |
| `lodBias` 1 → 0.3 | **0.00 ms** |
| 23,204 renderers disabled (59% of scene) | **0.00 ms** |

Disabling well over half the scene's renderers changed nothing, because:

```
GPUResidentDrawer: supported=True  active=True  occlusionCulling=True
```

The Unity 6 GPU-driven path with instance occlusion culling is enabled, so per-object
culling is already off the CPU. Combined with shadows at 15 m / 1 cascade / Low, no realtime
reflection probes and zero pixel lights, the usual optimisation levers are already pulled.

~8 ms of the ~11 ms frame is CPU-side work invisible to every graphics setting. We could not
attribute it further from outside, as the remaining cost appears to sit in Burst-compiled
systems that managed hooks cannot reach.

---

## Caveats

- All measurements are from **one machine and one save**. Scale-dependent effects (1614
  cables) may not represent typical play.
- Timings were taken with a profiling mod attached. Its overhead was measured (~0.05%) but
  its Harmony hooks do add trampolines to the instrumented methods.
- We could not read method bodies (IL2CPP), so every "why" above is inference from call
  counts, timings and heap deltas — the numbers are measured, the explanations are not.
- Several of our own hypotheses in this investigation were wrong before the data corrected
  them. Treat the *measurements* as reliable and the *suggestions* as outside guesses.
