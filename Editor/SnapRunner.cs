using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ji2.UiSnapshots.Editor
{
    public sealed class SnapRunRequest
    {
        public List<string> Scenarios = new List<string>();
        public List<string> Viewports = new List<string>();   // overrides scenario viewports when set
        public List<string> Skins = new List<string>();       // "all", names; overrides scenario skins when set
        public string Label;
        public string Output;                                  // parent folder for run directories
        public string Compare;                                 // baseline run dir, or "latest" / "latest:<label>"
        public int PassTimeoutSec = 900;
    }

    public sealed class SnapPass
    {
        public string Scenario;
        public string Path;           // scenario file
        public string Skin;
        public List<string> Viewports = new List<string>();
        public override string ToString() => $"{Scenario}{(Skin != "" ? "/" + Skin : "")} [{string.Join(",", Viewports)}]";
    }

    public sealed class SnapRunState
    {
        public string Id;
        public string Phase;          // prepare, entering, playing, exiting, finishing, cancelling | done, failed, cancelled
        public SnapRunRequest Request;
        public string OutputDir;
        public List<SnapPass> Passes = new List<SnapPass>();
        public int PassIndex;
        public string Progress;
        public string Error;
        public string StartedUtc;
        public string UpdatedUtc;
        public string Report;
        public int Shots;
        public int Errors;
        public int Warnings;
        public int Unsettled;
        public int FailedExpectations;
        public int ReferenceMismatches;
        public int ReferencesChecked;
        public string CompareSummary;
        public bool Terminal => Phase == "done" || Phase == "failed" || Phase == "cancelled";
    }

    /// <summary>
    /// Runs scenarios as a sequence of passes (scenario × skin [× viewport]). Each pass is a fresh Play Mode session,
    /// so DI scopes and statics start clean. State lives in Temp/UiSnapshots/run.json and survives domain reloads.
    /// </summary>
    [InitializeOnLoad]
    public static class SnapRunner
    {
        private static string StatePath => Path.Combine(SnapSettings.TempDir, "run.json");
        private static bool _passRunning;
        private static CancellationTokenSource _cancel;
        private static double _enterRequestedAt;

        public static event Action<SnapRunState> Changed;

        static SnapRunner()
        {
            EditorApplication.update += Tick;
            EditorApplication.delayCall += Recover;
        }

        public static SnapRunState Current => LoadState();
        public static bool Busy => LoadState() is { Terminal: false };

        public static ISnapAdapter CreateAdapter()
        {
            var types = TypeCache.GetTypesDerivedFrom<ISnapAdapter>().Where(t => !t.IsAbstract && !t.IsInterface && t != typeof(DefaultSnapAdapter)).ToArray();
            if (types.Length > 1) Debug.LogWarning($"[UiSnapshots] Several adapters found ({string.Join(", ", types.Select(t => t.FullName))}); using {types[0].FullName}.");
            return (ISnapAdapter)Activator.CreateInstance(types.Length > 0 ? types[0] : typeof(DefaultSnapAdapter));
        }

        // ---------- public API ----------

        public static SnapRunState Start(SnapRunRequest request)
        {
            if (Busy) throw new SnapException($"A run is already in progress: {LoadState().Id}. Cancel it first.");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new SnapException("Stop Play Mode before starting a run.");
            if (EditorApplication.isCompiling) throw new SnapException("Unity is compiling; retry when compilation has finished.");
            if (request.Scenarios == null || request.Scenarios.Count == 0) throw new SnapException("Choose at least one scenario.");

            var matrix = SnapSettings.LoadMatrix();
            var adapter = CreateAdapter();
            var passes = new List<SnapPass>();
            foreach (string id in request.Scenarios)
            {
                var scenario = SnapScenario.LoadById(id); // validates step names and JSON up front
                var viewports = matrix.Expand(request.Viewports.Count > 0 ? request.Viewports : scenario.Viewports).Select(v => v.Name).ToList();
                foreach (string skin in ResolveSkins(adapter, request.Skins.Count > 0 ? request.Skins : scenario.Skins))
                {
                    if (scenario.RestartPerViewport)
                        passes.AddRange(viewports.Select(v => new SnapPass { Scenario = scenario.Id, Path = scenario.Path, Skin = skin, Viewports = new List<string> { v } }));
                    else passes.Add(new SnapPass { Scenario = scenario.Id, Path = scenario.Path, Skin = skin, Viewports = viewports });
                }
            }

            string id2 = DateTime.Now.ToString("yyyyMMdd-HHmmss") + (string.IsNullOrEmpty(request.Label) ? "" : "-" + Sanitize(request.Label));
            string parent = string.IsNullOrEmpty(request.Output) ? SnapSettings.DefaultOutput : request.Output;
            string outputDir = Path.Combine(parent, id2);
            Directory.CreateDirectory(outputDir);

            var manifest = new SnapManifest
            {
                RunId = id2,
                Label = request.Label,
                Project = SnapSettings.ProjectRoot,
                Unity = Application.unityVersion,
                StartedUtc = DateTime.UtcNow.ToString("o"),
                Scenarios = request.Scenarios.ToList(),
                Skins = passes.Select(p => p.Skin).Distinct().ToList(),
                Viewports = passes.SelectMany(p => p.Viewports).Distinct().Select(n => matrix.Viewports.TryGetValue(n, out var v) ? v : new SnapViewport { Name = n }).ToList()
            };
            FillGit(manifest);
            manifest.Save(outputDir);

            var state = new SnapRunState
            {
                Id = id2,
                Phase = "prepare",
                Request = request,
                OutputDir = outputDir,
                Passes = passes,
                StartedUtc = manifest.StartedUtc,
                Progress = $"queued {passes.Count} pass(es)"
            };
            SaveState(state);
            return state;
        }

        public static SnapRunState Cancel()
        {
            var state = LoadState();
            if (state == null || state.Terminal) return state;
            state.Phase = "cancelling";
            state.Progress = "cancel requested";
            SaveState(state);
            _cancel?.Cancel();
            return state;
        }

        // ---------- state machine ----------

        private static void Tick()
        {
            if (EditorApplication.isCompiling) return;
            var state = LoadState();
            if (state == null || state.Terminal) return;
            try
            {
                switch (state.Phase)
                {
                    case "prepare":
                        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                        PreparePass(state);
                        state.Phase = "entering";
                        SaveState(state);
                        _enterRequestedAt = EditorApplication.timeSinceStartup;
                        EditorApplication.isPlaying = true;
                        break;
                    case "entering":
                        if (EditorApplication.isPlaying && !_passRunning)
                        {
                            state.Phase = "playing";
                            SaveState(state);
                            _ = RunPass(state);
                        }
                        else if (!EditorApplication.isPlayingOrWillChangePlaymode && _enterRequestedAt > 0 && EditorApplication.timeSinceStartup - _enterRequestedAt > 20)
                            throw new SnapException("Play Mode did not start (compile errors in the Console?).");
                        break;
                    case "playing":
                        if (!EditorApplication.isPlaying && !_passRunning)
                            Fail(state, $"Play Mode stopped during pass {state.Passes[state.PassIndex]}.");
                        break;
                    case "exiting":
                        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                        state.PassIndex++;
                        state.Phase = state.PassIndex < state.Passes.Count ? "prepare" : "finishing";
                        SaveState(state);
                        break;
                    case "finishing":
                        Finish(state, "done");
                        break;
                    case "cancelling":
                        if (EditorApplication.isPlaying) { _cancel?.Cancel(); if (!_passRunning) EditorApplication.isPlaying = false; return; }
                        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                        Finish(state, state.Error != null ? "failed" : "cancelled");
                        break;
                }
            }
            catch (Exception e)
            {
                Fail(state, e is SnapException ? e.Message : e.ToString());
            }
        }

        private static void PreparePass(SnapRunState state)
        {
            var pass = state.Passes[state.PassIndex];
            var scenario = SnapScenario.LoadById(pass.Path ?? pass.Scenario);
            var adapter = CreateAdapter();
            bool sandbox = !string.Equals((string)scenario.Setup["prefs"], "keep", StringComparison.OrdinalIgnoreCase);
            if (sandbox)
            {
                SnapPrefsSandbox.Enter();
                SnapPrefsSandbox.Clear();
            }
            adapter.Prepare(scenario.Setup, pass.Skin);
            PlayerPrefs.Save();
            var first = SnapSettings.LoadMatrix().Viewports[pass.Viewports[0]];
            Ji2.CreativeReplay.Editor.CreativeGameView.SetResolution(first.Width, first.Height);
            state.Progress = $"pass {state.PassIndex + 1}/{state.Passes.Count} {pass}: entering Play Mode";
        }

        private static async Task RunPass(SnapRunState state)
        {
            _passRunning = true;
            var pass = state.Passes[state.PassIndex];
            var manifest = SnapManifest.Load(state.OutputDir);
            var log = new SnapPointLog();
            _cancel = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(30, state.Request.PassTimeoutSec)));
            SnapContext context = null;
            EditorApplication.CallbackFunction tick = null;
            try
            {
                var scenario = SnapScenario.LoadById(pass.Path ?? pass.Scenario);
                var matrix = SnapSettings.LoadMatrix();
                context = new SnapContext
                {
                    RunId = state.Id,
                    OutputDir = state.OutputDir,
                    Scenario = scenario,
                    Skin = pass.Skin,
                    Viewports = pass.Viewports.Select(v => matrix.Viewports[v]).ToList(),
                    Adapter = CreateAdapter(),
                    Manifest = manifest,
                    Points = log,
                    Cancellation = _cancel.Token
                };
                SnapHook.Current = log;
                var ctx = context;
                tick = () => { if (EditorApplication.isPlaying) { try { ctx.Adapter.Tick(ctx); } catch (Exception e) { Debug.LogException(e); } } };
                EditorApplication.update += tick;

                await SnapCapture.ApplyViewport(context, context.Primary);
                await context.Adapter.BeginPass(context);
                foreach (var step in scenario.Steps)
                {
                    context.ThrowIfCancelled();
                    context.Step = step;
                    log.CurrentStep = step.Index;
                    Progress(state, $"pass {state.PassIndex + 1}/{state.Passes.Count} {pass}: step {step}");
                    var info = SnapSteps.All[step.Name];
                    var result = await SnapSteps.Invoke(info, SnapSteps.Bind(info, step, context));
                    if (result != null) context.Log($"-> {result}");
                }
                foreach (var point in log.Points.Where(p => !p.Consumed && !p.Synthetic))
                    manifest.UncapturedPoints.Add(new SnapPointRecord { Scenario = scenario.Id, Skin = pass.Skin, Id = point.Id, Step = point.Step + 1, Note = point.Note });
            }
            catch (Exception e)
            {
                string where = context?.Step != null ? $" at step {context.Step}" : "";
                string message = e is SnapException || e is TimeoutException || e is OperationCanceledException ? e.Message : e.ToString();
                manifest.Issues.Add(new SnapIssue { Scenario = pass.Scenario, Skin = pass.Skin, Step = context?.Step?.ToString(), Severity = "error", Message = message });
                Debug.LogError($"[UiSnapshots] Pass {pass} failed{where}: {message}");
            }
            finally
            {
                if (tick != null) EditorApplication.update -= tick;
                SnapHook.Current = null;
                manifest.Save(state.OutputDir);
                var latest = LoadState() ?? state;
                latest.Phase = latest.Phase == "cancelling" ? "cancelling" : "exiting";
                SaveState(latest);
                _passRunning = false;
                EditorApplication.isPlaying = false;
            }
        }

        private static void Finish(SnapRunState state, string phase)
        {
            SnapPrefsSandbox.Restore();
            var manifest = SnapManifest.Load(state.OutputDir);
            try { state.ReferenceMismatches = SnapReferences.Apply(state.OutputDir, manifest); }
            catch (Exception e) { manifest.Issues.Add(new SnapIssue { Severity = "warning", Message = "Reference check failed: " + e.Message }); }
            state.ReferencesChecked = manifest.Shots.Count(s => s.Reference != null);
            manifest.Status = phase;
            manifest.FinishedUtc = DateTime.UtcNow.ToString("o");
            manifest.Seconds = Math.Round((DateTime.UtcNow - DateTime.Parse(manifest.StartedUtc).ToUniversalTime()).TotalSeconds, 1);
            manifest.Save(state.OutputDir);

            SnapCompareResult compare = null;
            if (!string.IsNullOrEmpty(state.Request.Compare) && phase == "done")
            {
                try
                {
                    string baseline = SnapCompare.ResolveBaseline(state.Request.Compare, state.OutputDir);
                    compare = SnapCompare.Run(baseline, state.OutputDir);
                    state.CompareSummary = compare.Summary;
                }
                catch (Exception e) { state.CompareSummary = "compare failed: " + e.Message; }
            }
            state.Report = SnapReport.Write(state.OutputDir, manifest, compare);
            state.Shots = manifest.Shots.Count;
            state.Errors = manifest.Issues.Count(i => i.Severity == "error");
            state.Warnings = manifest.Issues.Count(i => i.Severity != "error") + manifest.Shots.Count(s => s.Warnings.Count > 0);
            state.Unsettled = manifest.Shots.Count(s => !s.Settled);
            state.FailedExpectations = manifest.Expectations.Count(e => !e.Passed);
            state.Phase = phase;
            state.Progress = $"{phase}: {state.Shots} shot(s), {state.Errors} error(s), {state.Warnings} warning(s)" +
                             (state.ReferencesChecked > 0 ? $", {state.ReferencesChecked} vs reference ({state.ReferenceMismatches} approved mismatch(es))" : "");
            SaveState(state);
            Debug.Log($"[UiSnapshots] Run {state.Id} {state.Progress}. Report: {state.Report}");
        }

        private static void Fail(SnapRunState state, string message)
        {
            Debug.LogError("[UiSnapshots] " + message);
            state.Error = message;
            if (EditorApplication.isPlaying) { state.Phase = "cancelling"; SaveState(state); _cancel?.Cancel(); EditorApplication.isPlaying = false; return; }
            try
            {
                var manifest = SnapManifest.Load(state.OutputDir);
                manifest.Issues.Add(new SnapIssue { Severity = "error", Message = message });
                manifest.Save(state.OutputDir);
                Finish(state, "failed");
            }
            catch (Exception e)
            {
                SnapPrefsSandbox.Restore();
                state.Phase = "failed";
                state.Error += " | " + e.Message;
                SaveState(state);
            }
        }

        /// <summary>After an Editor restart or a crash: never leave the prefs sandbox behind.</summary>
        private static void Recover()
        {
            var state = LoadState();
            bool idle = state == null || state.Terminal;
            if (idle && SnapPrefsSandbox.Active && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SnapPrefsSandbox.Restore();
                Debug.Log("[UiSnapshots] Restored PlayerSettings identity left by an interrupted run.");
            }
            if (!idle && state.Phase == "playing" && !EditorApplication.isPlayingOrWillChangePlaymode)
                Fail(state, "The Editor reloaded while a pass was playing.");
        }

        // ---------- persistence ----------

        private static void Progress(SnapRunState state, string text)
        {
            var latest = LoadState() ?? state;
            if (latest.Phase == "cancelling") _cancel?.Cancel();
            latest.Progress = text;
            SaveState(latest);
        }

        public static SnapRunState LoadState()
        {
            try { return File.Exists(StatePath) ? JsonConvert.DeserializeObject<SnapRunState>(File.ReadAllText(StatePath)) : null; }
            catch (Exception) { return null; }
        }

        private static void SaveState(SnapRunState state)
        {
            Directory.CreateDirectory(SnapSettings.TempDir);
            state.UpdatedUtc = DateTime.UtcNow.ToString("o");
            string tmp = StatePath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(state, SnapSettings.Json));
            if (File.Exists(StatePath)) File.Delete(StatePath);
            File.Move(tmp, StatePath);
            Changed?.Invoke(state);
        }

        // ---------- helpers ----------

        internal static IEnumerable<string> ResolveSkins(ISnapAdapter adapter, IReadOnlyList<string> requested)
        {
            var available = adapter.Skins;
            if (requested == null || requested.Count == 0) return new[] { available.First() };
            if (requested.Any(r => r.Equals("all", StringComparison.OrdinalIgnoreCase))) return available;
            foreach (var r in requested)
                if (!available.Contains(r)) throw new SnapException($"Unknown skin '{r}'. Available: {string.Join(", ", available.Where(s => s != ""))}");
            return requested.Distinct();
        }

        private static string Sanitize(string value) => new string(value.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '-').ToArray());

        private static void FillGit(SnapManifest manifest)
        {
            manifest.Commit = Git("rev-parse HEAD");
            manifest.Branch = Git("rev-parse --abbrev-ref HEAD");
            string status = Git("status --porcelain --untracked-files=no");
            manifest.DirtyHash = string.IsNullOrWhiteSpace(status) ? "clean" : SnapCapture.Hash(System.Text.Encoding.UTF8.GetBytes(status)).Substring(0, 12);
        }

        private static string Git(string arguments)
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = SnapSettings.ProjectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000);
                return output;
            }
            catch (Exception e) { return "unknown (" + e.Message + ")"; }
        }
    }
}
