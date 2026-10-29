using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Ji2.UiSnapshots.Editor
{
    public sealed class SnapStepCall
    {
        public int Index;
        public string Name;
        public JToken Value;          // scalar shorthand ({ "load": 12 }) or null
        public JObject Args = new JObject();
        public string Source;         // original JSON, for errors and the manifest
        public override string ToString() => $"#{Index + 1} {Source}";
    }

    public sealed class SnapScenario
    {
        public string Id;
        public string Path;
        public string Description;
        public List<string> Viewports = new List<string>();
        public List<string> Skins = new List<string>();
        public bool RestartPerViewport;
        public JObject Setup = new JObject();
        public List<SnapStepCall> Steps = new List<SnapStepCall>();

        public static SnapScenario LoadById(string id)
        {
            string path = File.Exists(id) ? id : System.IO.Path.Combine(SnapSettings.ScenariosDir, id.EndsWith(".json") ? id : id + ".json");
            if (!File.Exists(path))
                throw new SnapException($"Scenario '{id}' not found in {SnapSettings.ScenariosDir}. Known: {string.Join(", ", SnapSettings.ScenarioIds)}");
            return Parse(File.ReadAllText(path), path);
        }

        public static SnapScenario Parse(string json, string path = null, IReadOnlyCollection<string> knownSteps = null)
        {
            knownSteps ??= SnapSteps.All.Keys.ToArray();
            JObject root;
            try { root = JObject.Parse(json); }
            catch (Exception e) { throw new SnapException($"{path ?? "scenario"}: invalid JSON: {e.Message}"); }

            var scenario = new SnapScenario
            {
                Path = path,
                Id = (string)root["id"] ?? (path != null ? System.IO.Path.GetFileNameWithoutExtension(path) : "scenario"),
                Description = (string)root["description"],
                RestartPerViewport = (bool?)root["restartPerViewport"] ?? false,
                Setup = root["setup"] as JObject ?? new JObject()
            };
            scenario.Viewports = Names(root["viewports"] ?? root["matrix"]);
            scenario.Skins = Names(root["skins"]);

            if (!(root["steps"] is JArray steps) || steps.Count == 0)
                throw new SnapException($"{scenario.Id}: 'steps' must be a non-empty array.");
            for (int i = 0; i < steps.Count; i++)
                scenario.Steps.Add(ParseStep(steps[i], i, scenario.Id, knownSteps));
            return scenario;
        }

        public static List<string> Names(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return new List<string>();
            if (token.Type == JTokenType.String) return ((string)token).Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            return token.Values<string>().ToList();
        }

        public static SnapStepCall ParseStep(JToken token, int index, string scenarioId, IReadOnlyCollection<string> knownSteps)
        {
            string where = $"{scenarioId} step #{index + 1}";
            if (!(token is JObject step) || !step.HasValues) throw new SnapException($"{where}: a step is an object such as {{ \"capture\": \"hud\" }}.");
            var names = step.Properties().Where(p => knownSteps.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (names.Length != 1)
            {
                string keys = string.Join(", ", step.Properties().Select(p => p.Name));
                throw new SnapException(names.Length == 0
                    ? $"{where}: none of [{keys}] is a known step. Known steps: {string.Join(", ", knownSteps.OrderBy(s => s))}"
                    : $"{where}: several step names in one object: {keys}");
            }
            var call = new SnapStepCall
            {
                Index = index,
                Name = knownSteps.First(k => string.Equals(k, names[0].Name, StringComparison.OrdinalIgnoreCase)),
                Source = step.ToString(Newtonsoft.Json.Formatting.None)
            };
            if (names[0].Value is JObject named) foreach (var p in named.Properties()) call.Args[p.Name] = p.Value;
            else call.Value = names[0].Value;
            foreach (var p in step.Properties().Where(p => p != names[0])) call.Args[p.Name] = p.Value;
            return call;
        }
    }
}
