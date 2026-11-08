---
name: ui-snapshots
description: "Capture, re-capture and compare Unity game UI screenshots with com.ji2.ui-snapshots: code markers, JSON scenarios, viewport/skin matrix runs, reference screenshots (mockups, approved goldens), manifests and before/after reports. Use whenever UI work needs rendered evidence, a visual review, a branch-vs-master comparison, or new screenshot states/markers; do not hand-write one-off capture scripts."
---

# UI Snapshots

Rendered screens are the evidence for UI work (see `game-ui-craft`). In a project that has `Packages/com.ji2.ui-snapshots`, take them with this tool instead of writing a per-task capture script. Read the package `README.md` for the full contract; this skill is the working procedure.

## Before you capture

- Work in the task's own worktree and Unity Editor (the project's init script). One run at a time per Editor; never run while the user is playing in that Editor.
- See what exists: `ui-snap.ps1 -Project <worktree> -List` lists scenarios, viewports, presets, skins and steps. `-Validate` parses every scenario and binds its arguments without Play Mode.
- `$cli = "<worktree>\Packages\com.ji2.ui-snapshots\Cli~\ui-snap.ps1"`. From a Claude chat, run it through the project's Windows bridge; from Codex, run it directly in PowerShell.

## Capture

```powershell
& $cli -Project <worktree> -Scenario popups,hud -Viewports phones -Skins all -Label after -Output <artifacts>\ui-snapshots
```

- Pick the smallest set that proves the change: the affected scenarios, `quick` while iterating, the full preset (`phones`) and all skins for the final evidence.
- Exit code `0` clean, `2` warnings (unsettled frame, failed `expect`, step warning), `1` failed, `3` still running after `-TimeoutSec` (poll `-Status`, stop with `-Cancel`).
- A failing step ends only its pass; read the error in the CLI output or `manifest.json` → `Issues`, fix the scenario or code, rerun that scenario.

## Compare before/after

1. Baseline: run the same scenarios on the base ref with the same viewports and skins in a clean checkout of it (the project's Showroom script prepares one), label `before`, same `-Output` parent.
2. Change: run in the task worktree with `-Label after -Compare latest:before`, or later `-CompareRuns <beforeDir>,<afterDir>`.
3. Expect `same` everywhere you did not touch. A `changed` frame you did not intend is a regression to explain or fix; `added`/`removed` mean a scenario or capture name changed.
4. Sanity check of determinism when results look noisy: run the same ref twice and compare; it must report `same`. Fix the cause (an unsettled transition, an endless loop the adapter does not freeze, unseeded randomness) in the adapter rather than raising thresholds.

## Reference screenshots

- A mockup or design export for a state: `"reference": "References/mockups/pause.png"` on the capture step, or `-AddReference mockup.png -Scenario popups -Capture pause [-Viewport iphone]`. Different sizes are compared visually in the report (side by side, slider); use it to judge how far the build is from the design, not as pass/fail.
- Golden frames: after a person has inspected a run and accepted it, `-Approve <runDir> [-Shots "popups/*"]` stores the frames in `Tools/UiSnapshots/References`. Later runs report `same`/`changed` per frame and the CLI exits `2` on an approved mismatch. Re-approve deliberately after an intended visual change; never approve frames nobody looked at.
- References are copied into each run, so old reports keep showing what they were checked against.

## Look at the frames

Compilation, a green run and an attractive concept are not visual acceptance. Open `report.html` (grid by scenario/capture/viewport, side-by-side, slider, diff) and inspect the PNGs themselves at native size: composition, clipping at safe-area insets, text overflow, contrast, overlaps. Use `.focus.png` crops for popup detail. Report findings with the shot keys (`popups/revive-time@iphone-Original`).

## Extend coverage

- **Marker** — at a state that is hard to reach from outside or worth capturing exactly when it appears: `Snap.Point("area.state", focusRectTransform)` after the state is presented (for example after the entry animation starts; capture settles anyway). Name markers `area.state`; use `Snap.Tag(key, value)` for context. The runtime assembly must reference `Ji2.UiSnapshots`. Markers are void and compiled out of players; never compute anything for gameplay from them.
- **Scenario** — a JSON file in `Tools/UiSnapshots/Scenarios`; reuse existing game steps (`load`, `show`, `fail`...) and the generic `capture`/`wait`/`click`/`cheat`/`replay`/`expect`. Capture after the marker with `"at"`, add an `expect` where a wrong state would otherwise produce a plausible-looking frame.
- **Game step** — only when no step can reach the state: a static `[SnapStep("name")]` method in the game's Editor assembly (SnapContext first). Reuse the game's own flows, cheats and recorded traces; do not duplicate gameplay logic in the step.
- **Viewport/preset** — add to `Tools/UiSnapshots/matrix.json` with real device insets.
- After adding anything, run `-Validate` and then the scenario.

## Evidence and hygiene

- Keep run folders outside the repository (the project's artifacts folder). Put only selected PNGs into the repository when the project's documentation asks for committed evidence, and cite their shot keys and run id.
- In the task summary give the run id, commit, viewports/skins, the comparison summary and what you inspected.
- Never commit `Temp/UiSnapshots`, never leave the Editor in Play Mode, and never edit the installed package to fix it: change the module source (see its `MAINTENANCE.md`).
