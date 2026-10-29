using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ji2.UiSnapshots.Editor
{
    /// <summary>
    /// Stable entry points for automation (UnityCLI run_script via Cli~/UiSnapCliEntry.cs).
    /// Every method returns a JSON string and never throws: errors come back as {"ok":false,"error":...}.
    /// </summary>
    public static class SnapCli
    {
        /// <summary>{"scenarios":["popups"],"viewports":["phones"],"skins":["all"],"label":"after","output":"D:/...","compare":"latest:before"}</summary>
        public static string Start(string requestJson) => Guard(() =>
        {
            var request = JsonConvert.DeserializeObject<SnapRunRequest>(string.IsNullOrWhiteSpace(requestJson) ? "{}" : requestJson) ?? new SnapRunRequest();
            var state = SnapRunner.Start(request);
            return new JObject { ["ok"] = true, ["runId"] = state.Id, ["output"] = state.OutputDir, ["passes"] = state.Passes.Count };
        });

        public static string Status() => Guard(() =>
        {
            var state = SnapRunner.LoadState();
            if (state == null) return new JObject { ["ok"] = true, ["phase"] = "none" };
            var json = JObject.FromObject(state);
            json["ok"] = true;
            json.Remove("Request");
            json.Remove("Passes");
            json["Pass"] = state.PassIndex < state.Passes.Count ? state.Passes[state.PassIndex].ToString() : null;
            json["PassCount"] = state.Passes.Count;
            return json;
        });

        public static string Cancel() => Guard(() =>
        {
            var state = SnapRunner.Cancel();
            return new JObject { ["ok"] = true, ["phase"] = state?.Phase ?? "none" };
        });

        /// <summary>Compares two finished runs (directories) and rewrites the after-run report.</summary>
        public static string Compare(string before, string after) => Guard(() =>
        {
            string baseline = SnapCompare.ResolveBaseline(before, after);
            var result = SnapCompare.Run(baseline, after);
            string report = SnapReport.Write(after, SnapManifest.Load(after), result);
            return new JObject { ["ok"] = true, ["summary"] = result.Summary, ["report"] = report, ["baseline"] = baseline };
        });

        /// <summary>Scenarios, viewports, presets, skins and steps available in this project.</summary>
        public static string List() => Guard(() =>
        {
            var matrix = SnapSettings.LoadMatrix();
            var adapter = SnapRunner.CreateAdapter();
            return new JObject
            {
                ["ok"] = true,
                ["root"] = SnapSettings.Root,
                ["output"] = SnapSettings.DefaultOutput,
                ["adapter"] = adapter.GetType().FullName,
                ["scenarios"] = new JArray(SnapSettings.ScenarioIds),
                ["viewports"] = JObject.FromObject(matrix.Viewports.ToDictionary(k => k.Key, v => v.Value.ToString())),
                ["presets"] = JObject.FromObject(matrix.Presets),
                ["defaultViewports"] = matrix.Default,
                ["skins"] = new JArray(adapter.Skins.Where(s => s != "")),
                ["steps"] = new JArray(SnapSteps.All.Values.OrderBy(s => s.Name).Select(s => s.Signature))
            };
        });

        /// <summary>Parses scenarios without running them: step names, argument binding and viewports.</summary>
        public static string Validate(string scenarioIds) => Guard(() =>
        {
            var matrix = SnapSettings.LoadMatrix();
            var ids = string.IsNullOrWhiteSpace(scenarioIds) ? SnapSettings.ScenarioIds.ToArray() : scenarioIds.Split(',').Select(s => s.Trim()).ToArray();
            var report = new JArray();
            foreach (var id in ids)
            {
                try
                {
                    var scenario = SnapScenario.LoadById(id);
                    matrix.Expand(scenario.Viewports);
                    foreach (var step in scenario.Steps) SnapSteps.Bind(SnapSteps.All[step.Name], step, new SnapContext());
                    SnapRunner.ResolveSkins(SnapRunner.CreateAdapter(), scenario.Skins).ToList();
                    report.Add(new JObject { ["scenario"] = id, ["ok"] = true, ["steps"] = scenario.Steps.Count });
                }
                catch (Exception e) { report.Add(new JObject { ["scenario"] = id, ["ok"] = false, ["error"] = e.Message }); }
            }
            return new JObject { ["ok"] = report.All(r => (bool)r["ok"]), ["scenarios"] = report };
        });

        private static string Guard(Func<JObject> body)
        {
            try { return body().ToString(Formatting.None); }
            catch (Exception e) { return new JObject { ["ok"] = false, ["error"] = e is SnapException ? e.Message : e.ToString() }.ToString(Formatting.None); }
        }
    }
}
