# UI Snapshots package rules

- Maintain documentation in English. This repository is the canonical source for `com.ji2.ui-snapshots`; follow `MAINTENANCE.md`.
- Nothing may reach players beyond the `Snap` facade: keep every marker `[Conditional("UNITY_EDITOR")]`, void and without out/ref parameters, and keep all tooling in Editor-only assemblies.
- Reuse existing modules instead of copying them: viewports and input replay come from `com.ji2.creative-replay`, cheat commands from `com.ji2.cheats`.
- Keep game rules, scenes, prefs keys and state recipes in consumer adapters. Do not add game-specific names here.
- Keep the shot key format and manifest fields backward compatible; baselines from older runs must stay comparable.
- The usage skill lives in `Skills~/ui-snapshots`; update it with every behaviour change.
