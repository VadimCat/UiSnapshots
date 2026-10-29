# JI2 UI Snapshots

Reusable UPM package `com.ji2.ui-snapshots`: Editor-only UI screenshots for Unity games. Mark interesting states in game code, describe journeys as JSON scenarios, run them across a viewport × skin matrix, and compare runs (for example a branch against `master`) in an HTML report. Players receive only an empty marker facade.

## What it gives you

- **Markers in code** — `Snap.Point("decision.OutOfTime", panel)` and `Snap.Tag("tutorial", "Hammer")`. Both are `[Conditional("UNITY_EDITOR")]`: player compilation removes the calls and their argument evaluation. A marker returns nothing, so it cannot change gameplay.
- **Scenarios** — JSON step lists in `Tools/UiSnapshots/Scenarios` (outside `Assets`: never imported, no recompilation on edit). Generic steps ship with the package; games add their own with `[SnapStep]`.
- **Matrix** — `Tools/UiSnapshots/matrix.json` names viewports (size and simulated safe-area insets) and presets. Each capture is taken in every viewport of the pass; skins run as separate passes.
- **Deterministic frames** — each pass is a fresh Play Mode session with an isolated PlayerPrefs sandbox. A capture waits for the adapter's "settled" signal, then freezes endless loops for the frame.
- **Reference screenshots** — a capture can name a reference image (a mockup or design export), and frames can be approved as golden references. Every run checks its frames against them: a pixel diff when sizes match, side-by-side and slider when they do not.
- **Evidence** — every run writes PNGs, `manifest.json` (commit, branch, dirty hash, viewport, insets, skin, marker, settle result, SHA-256 per frame, uncaptured markers, failed expectations) and a self-contained `report.html`. `Compare` pairs two runs by shot key and writes heat-map diffs.
- **Three entry points** — `Tools > Development > UI Snapshots` window, live capture while you play (marker filter, F9 for a manual frame), and `Cli~/ui-snap.ps1` for agents and CI-like use through the Unity CLI.

## Requirements

- Unity 6000.0+, `com.unity.nuget.newtonsoft-json`, uGUI.
- `com.ji2.creative-replay` 1.0.1+ in the project (viewport control through `CreativeGameView`, and the `replay` step). It is not on a registry, so it is not listed in `package.json`; the Editor assembly references `Ji2.CreativeReplay.Editor` directly.
- Optional: `com.ji2.cheats` enables the `cheat` step (uses the shared `CheatRegistry`, so scenarios reuse the game's existing cheat commands).
- The Unity CLI (`unity.exe command run_script`) for `ui-snap.ps1`.

## Integrate into a game

1. Add the package (a pinned Git submodule under `Packages/com.ji2.ui-snapshots`, or an exact Git tag) and `"testables": ["com.ji2.ui-snapshots"]` in `Packages/manifest.json` to run its guarantee tests.
2. Reference `Ji2.UiSnapshots` from runtime assemblies that place markers, and `Ji2.UiSnapshots` + `Ji2.UiSnapshots.Editor` from the Editor assembly that holds the adapter.
3. Implement one adapter (derive from `SnapAdapter`, Editor assembly, discovered automatically):
   - `Skins` — skin ids, or a single `""`.
   - `Prepare(setup, skin)` — Edit Mode, inside the cleared prefs sandbox: seed prefs from the scenario `setup`, select the skin, open the boot scene.
   - `BeginPass(context)` — Play Mode: wait for the first screen.
   - `IsSettled()` — no transition or finite tween/animation running.
   - `ApplyViewport(viewport)` — apply simulated safe-area insets and relayout.
   - `SetLoopsFrozen(bool)` — pause endless loops at a fixed phase around a capture.
   - `Tick(context)` — optional synthetic markers (for example `screen.<Type>` when the top screen changes).
   - `DescribeState()` — scene, screen, level... for the manifest and `expect`.
4. Add game steps as `static` methods with `[SnapStep("name")]` and `SnapContext` as the first parameter. Return `Task`, `Awaitable` or `void`.
5. Create `Tools/UiSnapshots/matrix.json` and scenarios.

## Scenario format

```json
{
  "description": "Decision popups on level 12",
  "viewports": "phones",
  "skins": ["Original", "Voodoo"],
  "restartPerViewport": false,
  "setup": { "level": 12, "coins": 250 },
  "steps": [
    { "load": 12 },
    { "show": "pause" },
    { "capture": "pause", "at": "screen.PauseScreen" },
    { "fail": "TimeEnded" },
    { "capture": "revive-time", "at": "decision.OutOfTime*", "focus": true },
    { "expect": { "screen": "DecisionScreen", "text": "Time" } }
  ]
}
```

A step object has exactly one key that names a step. A scalar value binds to the step's first parameter (`{ "load": 12 }`); an object value or the remaining keys bind by parameter name (`{ "load": { "level": 12 } }`, `{ "capture": "x", "at": "y" }`). Unknown steps, unknown parameters and missing required parameters fail validation before Play Mode starts. `setup.prefs: "keep"` disables the PlayerPrefs sandbox (it is on by default).

Built-in steps:

| Step | Purpose |
| --- | --- |
| `capture(name, at?, focus?, timeoutMs?, settle?, settleTimeoutMs?, viewports?, reference?)` | Wait for the next unconsumed marker matching the glob `at` (markers fired during earlier steps count), settle, capture in every viewport. `focus: true` adds a crop around the marker's object; `focus: "Path/To/Object"` crops a named object. `reference` names an image to check the frame against (see below). |
| `wait(ms?, frames?, idle?, point?, timeoutMs?)` | Unscaled delays (work while `Time.timeScale == 0`), settle, or a marker without consuming it. |
| `click(target, waitMs?, timeoutMs?)` | Pointer enter/down/up/click through the EventSystem on an interactable object by name or hierarchy-path suffix. |
| `expect(screen?, text?, button?, timeoutMs?)` | Soft check recorded in the manifest and report; the run continues. |
| `cheat(command, provider?, waitMs?)` | Executes a command from `com.ji2.cheats`. |
| `replay(trace, frameAccurate?, timeoutMs?)` | Plays a `.inputtrace` (path relative to `Tools/UiSnapshots`). |
| `call(method, args?)` | Escape hatch: invokes a static `Type.Method`. |
| `log(message)` | Writes to the Console. |

A failing step ends its pass with an error in the manifest; other passes still run and every frame taken so far is kept.

## Run

Window: `Tools > Development > UI Snapshots` — scenarios, viewport presets, skins, label, baseline, live capture and recent runs.

CLI (Windows PowerShell 5.1+):

```powershell
$cli = "<project>\Packages\com.ji2.ui-snapshots\Cli~\ui-snap.ps1"
& $cli -Project <project> -List                     # scenarios, viewports, skins, steps
& $cli -Project <project> -Validate                 # parse and bind every scenario
& $cli -Project <project> -Scenario popups,hud -Viewports phones -Skins all -Label after -Output D:\artifacts\ui-snapshots
& $cli -Project <project> -Scenario popups -Label after -Compare latest:before
& $cli -Project <project> -CompareRuns <beforeRunDir>,<afterRunDir>
& $cli -Project <project> -Status    # or -Cancel
```

Exit codes: `0` clean; `2` finished with warnings (unsettled frames, failed `expect`, step warnings); `1` failed or cancelled; `3` the wait timed out (the run continues in the Editor). A run executes asynchronously in the Editor (`EditorApplication.update` state machine persisted in `Temp/UiSnapshots/run.json`), so CLI call time limits and domain reloads do not interrupt it.

Output defaults to `Logs/UiSnapshots` (ignored by Unity's standard `.gitignore`); override per user with `UserSettings/UiSnapshots.json` (`{"output": "...", "root": "..."}`) or per run with `-Output`. A run directory contains `shots/<scenario>/<capture>@<viewport>-<skin>.png` (+ `.focus.png`), `manifest.json`, `report.html`, `refs/` and `refs-diff/` when references matched and, after a comparison, `compare.json` and `diff/`.

`Compare` keys: `scenario/capture@viewport-skin`. Statuses: `same`, `changed` (more than 0.1 % of pixels differ by more than 24 per channel), `resized`, `added`, `removed`. Baselines: a run directory, a sibling run id, `latest` or `latest:<label>`.

## Reference screenshots

Each frame is checked against the first reference found:

1. the capture step's `reference` (path relative to `Tools/UiSnapshots` or absolute) — typically a mockup;
2. an approved golden frame `Tools/UiSnapshots/References/<scenario>/<capture>@<viewport>[-<skin>].png`;
3. a viewport-independent design `Tools/UiSnapshots/References/<scenario>/<capture>.png`.

The reference is copied into the run (`refs/`), so reports stay valid after references change. Same size → pixel diff (`same`/`changed`, heat map in `refs-diff/`); another size → `resized`, compared visually in the report (side by side and a slider). A frame that differs from an approved reference makes the CLI exit with `2`; mockup differences are informational.

```powershell
& $cli -Project <project> -Approve <runDir> [-Shots "popups/*@iphone-*"]                      # promote frames to approved references
& $cli -Project <project> -AddReference mockup.png -Scenario popups -Capture pause [-Viewport iphone] [-Skin Original]
```

The window's run history has an **Approve** button. Approve only frames a person has inspected; commit `References/` when the team should share the same goldens.

## Guarantees that nothing reaches a player

| Layer | Mechanism |
| --- | --- |
| Marker calls | `[Conditional("UNITY_EDITOR")]` removes them, including argument evaluation |
| Runtime facade `Ji2.UiSnapshots` | Method bodies under `#if UNITY_EDITOR`; no state, a few KB in players |
| Runner, steps, capture, report | `Ji2.UiSnapshots.Editor`, Editor platform only |
| Scenarios, matrix, traces | Outside `Assets` |
| Prefs sandbox | Suffixes PlayerSettings company/product in memory, restores on finish, failure and Editor reload |

`Tests/Editor` verifies the marker contract and that no player assembly references the Editor tooling. Consumers should also compile player scripts for their target and confirm that marker strings are absent from the player assemblies.

## Validation

Package tests run in a consumer with the package listed in `testables`. Validate changes in a task-owned consumer Editor: compilation, these tests, the consumer's test suites, a real run of its scenarios twice with `-Compare` (identical runs must report `same`), and a player script compilation.

Provenance: authored for PushColorMerge on 29 October 2026, replacing per-review capture scripts (`VisualReviewQA`, `SkinVisualQA`). See `MAINTENANCE.md` for releases and consumer updates.
