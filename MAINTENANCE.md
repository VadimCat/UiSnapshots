# Maintenance and consumer updates

Canonical repository: `https://github.com/VadimCat/UiSnapshots` (private). Local UnityGameModules storage: `D:/Codex/JigglyAI/UiSnapshots.git` (bare); implementation work uses isolated worktrees under `D:/Codex/JigglyAI/worktrees`.

1. Fetch `main` and create an isolated branch/worktree for each independent change.
2. Keep orchestration, capture, manifest, comparison and reporting generic here. Game states, scenes, skins, prefs keys, readiness and screen detection belong to the consumer's adapter and `[SnapStep]` methods.
3. Validate in a task-owned consumer Editor: compilation, this package's tests (`testables`), the consumer's EditMode/PlayMode suites, two identical scenario runs compared with each other (all `same`), and a player script compilation with marker strings absent from player assemblies.
4. Preserve Unity `.meta` GUIDs and the shot key format (`scenario/capture@viewport-skin`): baselines from older runs must stay comparable.
5. Update `package.json`, `CHANGELOG.md` and the usage skill in `Skills~/ui-snapshots` together. Review, merge and publish an immutable `vX.Y.Z` tag; never move a published tag.
6. In the consumer's task worktree, check out the exact release commit in the submodule, commit the pointer and re-run the affected checks. Never edit the installed submodule as an alternative source.
7. Update the UnityGameModules catalog and the installed skill (`D:/Codex/.codex/skills/ui-snapshots`) from the released source.
