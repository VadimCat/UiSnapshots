using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Ji2.UiSnapshots.Editor
{
    public sealed class SnapViewport
    {
        public string Name;
        public int Width;
        public int Height;
        public int Top;
        public int Bottom;
        public override string ToString() => $"{Name} {Width}x{Height} insets {Top}/{Bottom}";
    }

    /// <summary>
    /// Project configuration. Scenarios, matrix and traces live outside Assets (default Tools/UiSnapshots)
    /// so Unity never imports them. Per-user overrides: UserSettings/UiSnapshots.json {"root","output"}.
    /// </summary>
    public static class SnapSettings
    {
        public const string DefaultRoot = "Tools/UiSnapshots";
        public static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static string TempDir => Path.Combine(ProjectRoot, "Temp", "UiSnapshots");

        private static JObject UserOverrides
        {
            get
            {
                string path = Path.Combine(ProjectRoot, "UserSettings", "UiSnapshots.json");
                if (!File.Exists(path)) return new JObject();
                try { return JObject.Parse(File.ReadAllText(path)); }
                catch (Exception e) { Debug.LogWarning($"[UiSnapshots] Ignoring invalid {path}: {e.Message}"); return new JObject(); }
            }
        }

        public static string Root
        {
            get
            {
                string root = (string)UserOverrides["root"];
                return Path.GetFullPath(Path.Combine(ProjectRoot, string.IsNullOrEmpty(root) ? DefaultRoot : root));
            }
        }

        public static string DefaultOutput
        {
            get
            {
                string output = (string)UserOverrides["output"];
                return Path.GetFullPath(Path.Combine(ProjectRoot, string.IsNullOrEmpty(output) ? "Logs/UiSnapshots" : output));
            }
        }

        public static string ScenariosDir => Path.Combine(Root, "Scenarios");

        public static IReadOnlyList<string> ScenarioIds =>
            Directory.Exists(ScenariosDir)
                ? Directory.GetFiles(ScenariosDir, "*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(s => s).ToArray()
                : Array.Empty<string>();

        public static string ResolveData(string relative) =>
            Path.IsPathRooted(relative) ? relative : Path.GetFullPath(Path.Combine(Root, relative));

        public sealed class Matrix
        {
            public Dictionary<string, SnapViewport> Viewports = new Dictionary<string, SnapViewport>();
            public Dictionary<string, List<string>> Presets = new Dictionary<string, List<string>>();
            public string Default;

            /// <summary>Expands preset and viewport names ("phones", "iphone") into ordered, unique viewports.</summary>
            public List<SnapViewport> Expand(IEnumerable<string> names)
            {
                var result = new List<SnapViewport>();
                var queue = new Queue<string>((names ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)));
                if (queue.Count == 0 && Default != null) queue.Enqueue(Default);
                int guard = 0;
                while (queue.Count > 0 && guard++ < 256)
                {
                    string name = queue.Dequeue().Trim();
                    if (Presets.TryGetValue(name, out var members)) { foreach (var m in members) queue.Enqueue(m); continue; }
                    if (!Viewports.TryGetValue(name, out var viewport))
                        throw new SnapException($"Unknown viewport or preset '{name}'. Known: {string.Join(", ", Viewports.Keys.Concat(Presets.Keys))}");
                    if (result.All(v => v.Name != viewport.Name)) result.Add(viewport);
                }
                if (result.Count == 0) throw new SnapException("No viewports selected and matrix.json has no default.");
                return result;
            }
        }

        public static Matrix LoadMatrix()
        {
            string path = Path.Combine(Root, "matrix.json");
            var matrix = new Matrix();
            if (!File.Exists(path))
            {
                matrix.Viewports["game-view"] = new SnapViewport { Name = "game-view", Width = 720, Height = 1600 };
                matrix.Default = "game-view";
                return matrix;
            }
            var json = JObject.Parse(File.ReadAllText(path));
            foreach (var property in ((JObject)json["viewports"] ?? new JObject()).Properties())
            {
                var v = property.Value.ToObject<SnapViewport>();
                v.Name = property.Name;
                if (v.Width < 320 || v.Height < 320) throw new SnapException($"Viewport '{v.Name}' must be at least 320x320.");
                matrix.Viewports[v.Name] = v;
            }
            foreach (var property in ((JObject)json["presets"] ?? new JObject()).Properties())
                matrix.Presets[property.Name] = property.Value.ToObject<List<string>>();
            matrix.Default = (string)json["default"];
            return matrix;
        }

        internal static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };
    }

    public sealed class SnapException : Exception
    {
        public SnapException(string message) : base(message) { }
    }
}
