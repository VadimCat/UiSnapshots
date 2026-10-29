using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Ji2.UiSnapshots.Editor
{
    /// <summary>Game-independent steps. Game steps live in the consumer's Editor assembly.</summary>
    public static class BuiltInSteps
    {
        /// <summary>{ "capture": "pause", "at": "screen.PauseScreen", "focus": true, "reference": "References/mockups/pause.png" }</summary>
        [SnapStep("capture")]
        public static async Task Capture(SnapContext context, string name, string at = null, JToken focus = null,
            int timeoutMs = 8000, bool settle = true, int settleTimeoutMs = 3000, string[] viewports = null, string reference = null)
        {
            if (reference != null && !System.IO.File.Exists(SnapSettings.ResolveData(reference)))
                throw new SnapException($"capture '{name}': reference image not found: {SnapSettings.ResolveData(reference)}");
            SnapPoint point = null;
            if (!string.IsNullOrEmpty(at))
            {
                try { point = await context.WaitForPoint(at, timeoutMs); }
                catch (TimeoutException)
                {
                    context.Manifest.UnreachedCaptures.Add($"{context.Scenario.Id}{(context.Skin != "" ? "/" + context.Skin : "")}: {name} at '{at}'");
                    throw new SnapException($"Marker '{at}' did not fire within {timeoutMs} ms. Seen: {string.Join(", ", context.Points.Points.Select(p => p.Id).Distinct().TakeLast(12))}");
                }
            }

            UnityEngine.Object focusObject = null;
            if (focus != null && focus.Type == JTokenType.Boolean && (bool)focus)
            {
                focusObject = point?.Focus;
                if (focusObject == null) context.Warn($"capture '{name}': focus=true but marker '{at}' carries no focus object.");
            }
            else if (focus != null && focus.Type == JTokenType.String)
            {
                focusObject = SnapUi.FindActive((string)focus).FirstOrDefault();
                if (focusObject == null) context.Warn($"capture '{name}': focus '{focus}' not found.");
            }

            var selected = viewports == null || viewports.Length == 0
                ? context.Viewports
                : context.Viewports.Where(v => viewports.Contains(v.Name)).ToList();
            int before = context.Manifest.Shots.Count;
            await context.Capture(name, point, focusObject, settle, settleTimeoutMs, selected);
            if (reference != null)
                foreach (var shot in context.Manifest.Shots.Skip(before)) shot.ReferenceSource = reference;
            context.Manifest.Save(context.OutputDir);
        }

        /// <summary>{ "wait": 500 } · { "wait": { "idle": true } } · { "wait": { "point": "decision.*" } } · { "wait": { "frames": 3 } }</summary>
        [SnapStep("wait")]
        public static async Task Wait(SnapContext context, int ms = 0, int frames = 0, bool idle = false, string point = null, int timeoutMs = 10000)
        {
            if (frames > 0) await context.Frames(frames);
            if (ms > 0) await context.Delay(ms);
            if (!string.IsNullOrEmpty(point))
            {
                var p = await context.WaitForPoint(point, timeoutMs);
                p.Consumed = false; // waiting does not consume: a following capture may still use it
            }
            if (idle)
            {
                var (settled, _) = await context.Settle(timeoutMs);
                if (!settled) context.Warn($"wait idle: UI still busy after {timeoutMs} ms.");
            }
        }

        /// <summary>{ "click": "PauseButton" } or a hierarchy path suffix such as "Popup/Actions/Primary".</summary>
        [SnapStep("click")]
        public static async Task Click(SnapContext context, string target, int waitMs = 600, int timeoutMs = 5000)
        {
            UnityEngine.UI.Selectable selectable = null;
            int matches = 0;
            await context.WaitUntil(() => (selectable = SnapUi.FindSelectable(target, out matches)) != null, timeoutMs, $"interactable '{target}'");
            if (matches > 1) context.Warn($"click '{target}': {matches} matches, clicked {SnapUi.PathOf(selectable.transform)}.");
            SnapUi.Click(selectable.gameObject);
            if (waitMs > 0) await context.Delay(waitMs);
        }

        /// <summary>Soft check recorded in the manifest: { "expect": { "screen": "PauseScreen", "text": "Paused", "button": "Resume" } }</summary>
        [SnapStep("expect")]
        public static async Task Expect(SnapContext context, string screen = null, string text = null, string button = null, int timeoutMs = 2000)
        {
            void Record(string what, bool passed, string actual)
            {
                context.Manifest.Expectations.Add(new SnapExpectation { Scenario = context.Scenario.Id, Skin = context.Skin, Step = context.Step?.ToString(), What = what, Passed = passed, Actual = actual });
                if (!passed) context.Warn($"expect {what} failed; actual: {actual}");
            }
            if (screen != null)
            {
                bool ok = await context.WaitUntil(() => Equals(context.Adapter.DescribeState().TryGetValue("screen", out var s) ? s?.ToString() : null, screen), timeoutMs, null, false);
                context.Adapter.DescribeState().TryGetValue("screen", out var actual);
                Record($"screen={screen}", ok, actual?.ToString());
            }
            if (text != null)
            {
                bool ok = await context.WaitUntil(() => context.Adapter.VisibleTexts().Any(t => t.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0), timeoutMs, null, false);
                Record($"text~{text}", ok, string.Join(" | ", context.Adapter.VisibleTexts().Take(20)));
            }
            if (button != null)
            {
                bool ok = await context.WaitUntil(() => SnapUi.FindSelectable(button, out _) != null, timeoutMs, null, false);
                Record($"button={button}", ok, ok ? "present" : "missing");
            }
        }

        /// <summary>Escape hatch: { "call": "MyRecipes.OpenShop", "args": [1] } invokes a static method (Task/Awaitable/void).</summary>
        [SnapStep("call")]
        public static async Task Call(SnapContext context, string method, JArray args = null)
        {
            int dot = method.LastIndexOf('.');
            if (dot <= 0) throw new SnapException($"call '{method}': use Type.Method.");
            string typeName = method.Substring(0, dot), methodName = method.Substring(dot + 1);
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(typeName)).FirstOrDefault(t => t != null)
                       ?? TypeCache.GetTypesDerivedFrom<object>().FirstOrDefault(t => t.Name == typeName || t.FullName == typeName);
            if (type == null) throw new SnapException($"call: type '{typeName}' not found.");
            var candidates = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Where(m => m.Name == methodName).ToArray();
            int count = args?.Count ?? 0;
            var target = candidates.FirstOrDefault(m => m.GetParameters().Length == count)
                         ?? throw new SnapException($"call: no static {type.Name}.{methodName} with {count} parameter(s).");
            var parameters = target.GetParameters();
            var values = parameters.Select((p, i) => args[i].ToObject(p.ParameterType)).ToArray();
            var info = new SnapStepInfo { Name = "call", Method = target, Parameters = parameters };
            var result = await SnapSteps.Invoke(info, values);
            if (result != null) context.Log($"call {method} -> {result}");
        }

        [SnapStep("log")]
        public static void Log(SnapContext context, string message) => context.Log(message);

        /// <summary>Executes a command from the shared cheats registry: { "cheat": "AddCoins" } (provider optional).</summary>
        [SnapStep("cheat")]
        public static async Task Cheat(SnapContext context, string command, string provider = null, int waitMs = 300)
        {
#if JI2_CHEATS
            var registry = Ji2.Cheats.CheatRegistry.Shared;
            var owner = provider != null
                ? registry.Find(provider)
                : registry.Providers.FirstOrDefault(p => p.Commands.Any(c => c.Id == command));
            if (owner == null) throw new SnapException($"cheat '{command}': no registered provider exposes it. Providers: {string.Join(", ", registry.Providers.Select(p => p.Id))}");
            var result = registry.Execute(owner.Id, command);
            if (!result.Success) throw new SnapException($"cheat '{command}' rejected: {result.Message}");
            context.Log($"cheat {command}: {result.Message}");
            if (waitMs > 0) await context.Delay(waitMs);
#else
            await Task.CompletedTask;
            throw new SnapException("The cheat step needs com.ji2.cheats in this project.");
#endif
        }

        /// <summary>Replays a Unity input trace recorded with com.ji2.creative-replay: { "replay": "Traces/level1-win.inputtrace" }.</summary>
        [SnapStep("replay")]
        public static async Task Replay(SnapContext context, string trace, bool frameAccurate = true, int timeoutMs = 120000)
        {
            string path = SnapSettings.ResolveData(trace);
            if (!System.IO.File.Exists(path)) throw new SnapException($"replay: trace not found: {path}");
            using var session = new Ji2.CreativeReplay.Editor.InputTraceSession();
            session.BeginReplay(path, frameAccurate);
            await context.WaitUntil(() => session.Finished, timeoutMs, $"replay of {trace}");
            await context.Frames(2);
        }
    }
}
