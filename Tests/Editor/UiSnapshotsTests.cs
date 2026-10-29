using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Ji2.UiSnapshots.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;

namespace Ji2.UiSnapshots.Tests
{
    public static class TestSteps
    {
        public static int LastLevel;
        public static string LastMode;

        [SnapStep("test-load")]
        public static Task Load(SnapContext context, int level, string mode = "default")
        {
            LastLevel = level;
            LastMode = mode;
            return Task.CompletedTask;
        }
    }

    public sealed class UiSnapshotsTests
    {
        [Test]
        public void MarkersCompileOutOfPlayers()
        {
            var methods = typeof(Snap).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            Assert.That(methods, Is.Not.Empty);
            foreach (var method in methods)
            {
                var conditional = method.GetCustomAttributes<ConditionalAttribute>().Select(a => a.ConditionString).ToArray();
                Assert.That(conditional, Does.Contain("UNITY_EDITOR"), $"{method.Name} must be [Conditional(\"UNITY_EDITOR\")]");
                Assert.That(method.ReturnType, Is.EqualTo(typeof(void)), $"{method.Name} must return void");
                Assert.That(method.GetParameters().Any(p => p.IsOut || p.ParameterType.IsByRef), Is.False, $"{method.Name} must not have out/ref parameters");
            }
        }

        [Test]
        public void PlayerAssembliesNeverReferenceEditorTooling()
        {
            var players = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies);
            var runtime = players.FirstOrDefault(a => a.name == "Ji2.UiSnapshots");
            Assert.That(runtime, Is.Not.Null, "Ji2.UiSnapshots must compile for players");
            Assert.That(runtime.assemblyReferences, Is.Empty, "the runtime facade must not depend on other assemblies");
            foreach (var assembly in players)
                Assert.That(assembly.assemblyReferences.Any(r => r.name.StartsWith("Ji2.UiSnapshots.Editor") || r.name.EndsWith(".UiSnapshots.Editor")), Is.False,
                    $"{assembly.name} references UI Snapshots Editor tooling");
            Assert.That(players.Any(a => a.name == "Ji2.UiSnapshots.Editor"), Is.False);
        }

        private static readonly string[] Known = { "capture", "wait", "load", "show" };

        [Test]
        public void ParsesShorthandNamedAndExtraArguments()
        {
            var scenario = SnapScenario.Parse(@"{ ""viewports"": ""phones"", ""skins"": [""A"",""B""], ""steps"": [
                { ""load"": 12 },
                { ""load"": { ""level"": 3, ""mode"": ""x"" } },
                { ""capture"": ""pause"", ""at"": ""screen.*"", ""focus"": true } ] }", "Scenarios/popups.json", Known);
            Assert.That(scenario.Id, Is.EqualTo("popups"));
            Assert.That(scenario.Viewports, Is.EqualTo(new[] { "phones" }));
            Assert.That(scenario.Skins, Is.EqualTo(new[] { "A", "B" }));
            Assert.That(scenario.Steps[0].Value.Value<int>(), Is.EqualTo(12));
            Assert.That((int)scenario.Steps[1].Args["level"], Is.EqualTo(3));
            Assert.That(scenario.Steps[1].Value, Is.Null);
            Assert.That((string)scenario.Steps[2].Value, Is.EqualTo("pause"));
            Assert.That((string)scenario.Steps[2].Args["at"], Is.EqualTo("screen.*"));
        }

        [Test]
        public void RejectsUnknownAndAmbiguousSteps()
        {
            var unknown = Assert.Throws<SnapException>(() => SnapScenario.Parse(@"{ ""steps"": [ { ""lod"": 1 } ] }", null, Known));
            StringAssert.Contains("not a known step", unknown.Message);
            var twice = Assert.Throws<SnapException>(() => SnapScenario.Parse(@"{ ""steps"": [ { ""load"": 1, ""show"": ""x"" } ] }", null, Known));
            StringAssert.Contains("several step names", twice.Message);
            Assert.Throws<SnapException>(() => SnapScenario.Parse(@"{ ""steps"": [] }", null, Known));
        }

        [Test]
        public async Task BindsArgumentsToDiscoveredSteps()
        {
            Assert.That(SnapSteps.All.ContainsKey("test-load"), Is.True);
            Assert.That(SnapSteps.All.ContainsKey("capture"), Is.True);
            var info = SnapSteps.All["test-load"];
            var known = SnapSteps.All.Keys.ToArray();

            var shorthand = SnapScenario.ParseStep(JObject.Parse(@"{ ""test-load"": 7 }"), 0, "t", known);
            await SnapSteps.Invoke(info, SnapSteps.Bind(info, shorthand, new SnapContext()));
            Assert.That(TestSteps.LastLevel, Is.EqualTo(7));
            Assert.That(TestSteps.LastMode, Is.EqualTo("default"));

            var named = SnapScenario.ParseStep(JObject.Parse(@"{ ""test-load"": { ""level"": 2 }, ""mode"": ""fast"" }"), 0, "t", known);
            await SnapSteps.Invoke(info, SnapSteps.Bind(info, named, new SnapContext()));
            Assert.That(TestSteps.LastLevel, Is.EqualTo(2));
            Assert.That(TestSteps.LastMode, Is.EqualTo("fast"));

            var typo = SnapScenario.ParseStep(JObject.Parse(@"{ ""test-load"": 1, ""mdoe"": ""x"" }"), 0, "t", known);
            StringAssert.Contains("mdoe", Assert.Throws<SnapException>(() => SnapSteps.Bind(info, typo, new SnapContext())).Message);
            var missing = SnapScenario.ParseStep(JObject.Parse(@"{ ""test-load"": { ""mode"": ""x"" } }"), 0, "t", known);
            StringAssert.Contains("needs 'level'", Assert.Throws<SnapException>(() => SnapSteps.Bind(info, missing, new SnapContext())).Message);
        }

        [Test]
        public void ExpandsMatrixPresetsInOrderWithoutDuplicates()
        {
            var matrix = new SnapSettings.Matrix { Default = "phones" };
            matrix.Viewports["a"] = new SnapViewport { Name = "a", Width = 720, Height = 1600 };
            matrix.Viewports["b"] = new SnapViewport { Name = "b", Width = 720, Height = 1280 };
            matrix.Presets["phones"] = new List<string> { "a", "b" };
            Assert.That(matrix.Expand(null).Select(v => v.Name), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(matrix.Expand(new[] { "b", "phones" }).Select(v => v.Name), Is.EqualTo(new[] { "b", "a" }));
            Assert.Throws<SnapException>(() => matrix.Expand(new[] { "tablet" }));
        }

        [Test]
        public void GlobMatchesMarkers()
        {
            Assert.That(SnapGlob.IsMatch("decision.*", "decision.OutOfTime"), Is.True);
            Assert.That(SnapGlob.IsMatch("decision.*", "screen.PauseScreen"), Is.False);
            Assert.That(SnapGlob.IsMatch("screen.Pause?creen", "screen.PauseScreen"), Is.True);
            var log = new SnapPointLog();
            log.OnPoint("screen.GameScreen", null, null);
            log.OnPoint("decision.OutOfTime", null, null);
            Assert.That(log.Take("decision.*").Id, Is.EqualTo("decision.OutOfTime"));
            Assert.That(log.Take("decision.*"), Is.Null, "a marker is consumed once");
        }

        [Test]
        public void ComparesRunsByShotKey()
        {
            string root = Path.Combine(Path.GetTempPath(), "UiSnapshotsTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                string before = MakeRun(root, "before", ("s/hud@a", Color.white), ("s/pause@a", Color.white), ("s/gone@a", Color.white));
                string after = MakeRun(root, "after", ("s/hud@a", Color.white), ("s/pause@a", Color.red), ("s/new@a", Color.white));
                var result = SnapCompare.Run(before, after);
                Dictionary<string, SnapComparePair> pairs = result.Pairs.ToDictionary(p => p.Key);
                Assert.That(pairs["s/hud@a"].Status, Is.EqualTo("same"));
                Assert.That(pairs["s/pause@a"].Status, Is.EqualTo("changed"));
                Assert.That(pairs["s/pause@a"].ChangedPercent, Is.EqualTo(100).Within(0.01));
                Assert.That(File.Exists(Path.Combine(after, pairs["s/pause@a"].Diff)), Is.True);
                Assert.That(pairs["s/gone@a"].Status, Is.EqualTo("removed"));
                Assert.That(pairs["s/new@a"].Status, Is.EqualTo("added"));
                Assert.That(SnapCompare.ResolveBaseline("latest:before", after), Is.EqualTo(before));
                Assert.That(File.Exists(SnapReport.Write(after, SnapManifest.Load(after), result)), Is.True);
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static string MakeRun(string root, string label, params (string key, Color color)[] shots)
        {
            string dir = Path.Combine(root, label);
            var manifest = new SnapManifest { RunId = label, Label = label, Status = "done", StartedUtc = DateTime.UtcNow.ToString("o") };
            foreach (var (key, color) in shots)
            {
                var tex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                tex.SetPixels(Enumerable.Repeat(color, 64).ToArray());
                tex.Apply();
                byte[] png = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);
                string file = "shots/" + key + ".png";
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dir, file)));
                File.WriteAllBytes(Path.Combine(dir, file), png);
                manifest.Shots.Add(new SnapShot { Key = key, File = file, Sha256 = SnapCapture.Hash(png), Width = 8, Height = 8 });
            }
            manifest.Save(dir);
            return dir;
        }
    }
}
