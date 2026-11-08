using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Ji2.UiSnapshots.Editor
{
    public sealed class SnapPoint
    {
        public int Seq;
        public string Id;
        public UnityEngine.Object Focus;
        public string Note;
        public int Frame;
        public int Step;
        public bool Consumed;
        public bool Synthetic;  // emitted by tooling (adapter/steps), not by a Snap.Point in game code
    }

    /// <summary>Receives Snap.Point/Snap.Tag from game code while a pass or live capture is active.</summary>
    public sealed class SnapPointLog : ISnapListener
    {
        public readonly List<SnapPoint> Points = new List<SnapPoint>();
        public readonly Dictionary<string, string> Tags = new Dictionary<string, string>();
        public int CurrentStep = -1;
        public event Action<SnapPoint> Added;

        public void OnPoint(string id, UnityEngine.Object focus, string note)
        {
            if (string.IsNullOrEmpty(id)) return;
            var point = new SnapPoint { Seq = Points.Count + 1, Id = id, Focus = focus, Note = note, Frame = Time.frameCount, Step = CurrentStep };
            Points.Add(point);
            Added?.Invoke(point);
        }

        public void OnTag(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (value == null) Tags.Remove(key); else Tags[key] = value;
        }

        /// <summary>First unconsumed point matching the glob, in order of arrival.</summary>
        public SnapPoint Take(string pattern)
        {
            var regex = SnapGlob.ToRegex(pattern);
            var point = Points.FirstOrDefault(p => !p.Consumed && regex.IsMatch(p.Id));
            if (point != null) point.Consumed = true;
            return point;
        }
    }

    public static class SnapGlob
    {
        public static Regex ToRegex(string pattern) =>
            new Regex("^" + Regex.Escape(pattern ?? "*").Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);
        public static bool IsMatch(string pattern, string value) => ToRegex(pattern).IsMatch(value ?? "");
    }

    /// <summary>Everything a step needs: timing helpers, markers, the adapter, the capture pipeline and the manifest.</summary>
    public sealed class SnapContext
    {
        public string RunId { get; internal set; }
        public string OutputDir { get; internal set; }
        public SnapScenario Scenario { get; internal set; }
        public string Skin { get; internal set; }
        public IReadOnlyList<SnapViewport> Viewports { get; internal set; }
        public SnapViewport Primary => Viewports[0];
        public SnapViewport CurrentViewport { get; internal set; }
        public ISnapAdapter Adapter { get; internal set; }
        public SnapManifest Manifest { get; internal set; }
        public SnapStepCall Step { get; internal set; }
        public SnapPointLog Points { get; internal set; }
        public CancellationToken Cancellation { get; internal set; }
        public IReadOnlyDictionary<string, string> Tags => Points.Tags;

        public void Log(string message) => Debug.Log($"[UiSnapshots] {Scenario?.Id}{(Skin != "" ? "/" + Skin : "")} {Step}: {message}");

        public void Warn(string message)
        {
            Debug.LogWarning($"[UiSnapshots] {Scenario?.Id} {Step}: {message}");
            Manifest?.Issues.Add(new SnapIssue { Scenario = Scenario?.Id, Skin = Skin, Step = Step?.ToString(), Severity = "warning", Message = message });
        }

        public void ThrowIfCancelled()
        {
            if (Cancellation.IsCancellationRequested) throw new OperationCanceledException("Run cancelled or pass timed out.");
        }

        /// <summary>Emits a marker from tooling code (for example a synthetic screen change).</summary>
        public void Emit(string id, UnityEngine.Object focus = null, string note = null)
        {
            Points.OnPoint(id, focus, note);
            if (Points.Points.Count > 0 && Points.Points[Points.Points.Count - 1].Id == id) Points.Points[Points.Points.Count - 1].Synthetic = true;
        }

        public async Task NextFrame()
        {
            ThrowIfCancelled();
            await Awaitable.NextFrameAsync();
        }

        public async Task Frames(int count)
        {
            for (int i = 0; i < Mathf.Max(1, count); i++) await NextFrame();
        }

        /// <summary>Unscaled delay: keeps working while the game sets Time.timeScale = 0.</summary>
        public async Task Delay(int milliseconds)
        {
            float until = Time.realtimeSinceStartup + milliseconds / 1000f;
            do await NextFrame(); while (Time.realtimeSinceStartup < until);
        }

        public async Task<bool> WaitUntil(Func<bool> condition, int timeoutMs, string what = null, bool throwOnTimeout = true)
        {
            float until = Time.realtimeSinceStartup + timeoutMs / 1000f;
            while (!SafeCheck(condition))
            {
                if (Time.realtimeSinceStartup > until)
                {
                    if (throwOnTimeout) throw new TimeoutException($"Timed out after {timeoutMs} ms waiting for {what ?? "condition"}.");
                    return false;
                }
                await NextFrame();
            }
            return true;
        }

        private static bool SafeCheck(Func<bool> condition)
        {
            try { return condition(); }
            catch (Exception) { return false; }
        }

        /// <summary>Waits for the next unconsumed marker matching the glob (markers fired during earlier steps count).</summary>
        public async Task<SnapPoint> WaitForPoint(string pattern, int timeoutMs)
        {
            SnapPoint point = null;
            await WaitUntil(() => (point = Points.Take(pattern)) != null, timeoutMs, $"marker '{pattern}'");
            return point;
        }

        /// <summary>Waits until the adapter reports a settled UI for two consecutive frames.</summary>
        public async Task<(bool settled, int ms)> Settle(int timeoutMs = 3000)
        {
            float start = Time.realtimeSinceStartup;
            int calm = 0;
            bool ok = await WaitUntil(() =>
            {
                calm = Adapter.IsSettled() ? calm + 1 : 0;
                return calm >= 2;
            }, timeoutMs, "settled UI", throwOnTimeout: false);
            Canvas.ForceUpdateCanvases();
            return (ok, Mathf.RoundToInt((Time.realtimeSinceStartup - start) * 1000));
        }

        /// <summary>Captures the current state in every viewport of the pass (or the given subset).</summary>
        public Task Capture(string name, SnapPoint point = null, UnityEngine.Object focus = null, bool settle = true,
            int settleTimeoutMs = 3000, IReadOnlyList<SnapViewport> viewports = null) =>
            SnapCapture.CaptureAsync(this, name, point, focus, settle, settleTimeoutMs, viewports ?? Viewports);
    }
}
