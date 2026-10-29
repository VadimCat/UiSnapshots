using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Ji2.UiSnapshots.Editor
{
    /// <summary>
    /// Live mode: while you play by hand, every marker matching the filter is captured once per session.
    /// F9 (rebindable: "UI Snapshots/Capture Game View") captures the current frame at any time in Play Mode.
    /// </summary>
    [InitializeOnLoad]
    public static class SnapLive
    {
        private const string EnabledKey = "Ji2.UiSnapshots.Live";
        private const string FilterKey = "Ji2.UiSnapshots.LiveFilter";
        private const string RepeatKey = "Ji2.UiSnapshots.LiveRepeat";
        private static SnapContext _context;
        private static readonly HashSet<string> Captured = new HashSet<string>();
        private static Task _queue = Task.CompletedTask;

        public static bool Enabled { get => SessionState.GetBool(EnabledKey, false); set { SessionState.SetBool(EnabledKey, value); Sync(); } }
        public static string Filter { get => SessionState.GetString(FilterKey, "*"); set => SessionState.SetString(FilterKey, string.IsNullOrWhiteSpace(value) ? "*" : value); }
        public static bool Repeat { get => SessionState.GetBool(RepeatKey, false); set => SessionState.SetBool(RepeatKey, value); }
        public static string LastCapture { get; private set; }
        public static string OutputDir => _context?.OutputDir;

        static SnapLive()
        {
            EditorApplication.playModeStateChanged += _ => Sync();
            EditorApplication.delayCall += Sync;
        }

        private static void Sync()
        {
            bool want = EditorApplication.isPlaying && !SnapRunner.Busy;
            if (want && _context == null) Begin();
            else if (!want && _context != null) End();
            if (_context != null) SnapHook.Current = Enabled ? _context.Points : null;
        }

        private static void Begin()
        {
            var matrix = SnapSettings.LoadMatrix();
            var viewport = new SnapViewport { Name = "live", Width = Screen.width, Height = Screen.height };
            string dir = Path.Combine(SnapSettings.DefaultOutput, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-live");
            _context = new SnapContext
            {
                RunId = Path.GetFileName(dir),
                OutputDir = dir,
                Scenario = new SnapScenario { Id = "live" },
                Skin = "",
                Viewports = new[] { viewport },
                CurrentViewport = viewport,
                Adapter = SnapRunner.CreateAdapter(),
                Manifest = new SnapManifest { RunId = Path.GetFileName(dir), Label = "live", Project = SnapSettings.ProjectRoot, Unity = Application.unityVersion, StartedUtc = DateTime.UtcNow.ToString("o"), Scenarios = { "live" } },
                Points = new SnapPointLog()
            };
            _context.Points.Added += OnPoint;
            Captured.Clear();
        }

        private static void End()
        {
            var context = _context;
            _context = null;
            if (SnapHook.Current == context.Points) SnapHook.Current = null;
            if (context.Manifest.Shots.Count == 0) return;
            context.Manifest.Status = "done";
            context.Manifest.FinishedUtc = DateTime.UtcNow.ToString("o");
            context.Manifest.Save(context.OutputDir);
            SnapReport.Write(context.OutputDir, context.Manifest);
            Debug.Log($"[UiSnapshots] Live session saved {context.Manifest.Shots.Count} shot(s): {Path.Combine(context.OutputDir, SnapReport.FileName)}");
        }

        private static void OnPoint(SnapPoint point)
        {
            if (!Enabled || !SnapGlob.IsMatch(Filter, point.Id)) return;
            if (!Repeat && !Captured.Add(point.Id)) return;
            point.Consumed = true;
            string name = Repeat ? $"{point.Id}-{point.Seq:000}" : point.Id;
            Enqueue(name, point);
        }

        [Shortcut("UI Snapshots/Capture Game View", KeyCode.F9)]
        public static void CaptureNow()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[UiSnapshots] F9 capture works in Play Mode."); return; }
            if (_context == null) Begin();
            string screen = _context.Adapter.DescribeState().TryGetValue("screen", out var s) && s != null ? s.ToString() : "frame";
            Enqueue($"manual-{screen}-{DateTime.Now:HHmmss}", null);
        }

        private static void Enqueue(string name, SnapPoint point)
        {
            var context = _context;
            _queue = _queue.ContinueWith(async _ =>
            {
                if (context == null || !EditorApplication.isPlaying) return;
                context.Viewports = new[] { new SnapViewport { Name = "live", Width = Screen.width, Height = Screen.height } };
                context.CurrentViewport = context.Viewports[0];
                try
                {
                    await context.Capture(Sanitize(name), point);
                    LastCapture = name;
                }
                catch (Exception e) { Debug.LogWarning($"[UiSnapshots] Live capture '{name}' failed: {e.Message}"); }
            }, TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace('@', '_');
        }
    }
}
