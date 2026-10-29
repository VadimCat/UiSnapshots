using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ji2.UiSnapshots.Editor
{
    public sealed class SnapWindow : EditorWindow
    {
        private readonly HashSet<string> _scenarios = new HashSet<string>();
        private readonly HashSet<string> _viewports = new HashSet<string>();
        private readonly HashSet<string> _skins = new HashSet<string>();
        private string _label = "";
        private string _compare = "";
        private string _error;
        private Vector2 _scroll;
        private SnapSettings.Matrix _matrix;
        private IReadOnlyList<string> _availableSkins = Array.Empty<string>();

        [MenuItem("Tools/Development/UI Snapshots")]
        public static void Open() => GetWindow<SnapWindow>("UI Snapshots");

        private void OnEnable()
        {
            SnapRunner.Changed += OnChanged;
            Reload();
        }

        private void OnDisable() => SnapRunner.Changed -= OnChanged;
        private void OnChanged(SnapRunState _) => Repaint();
        private void OnInspectorUpdate() => Repaint();

        private void Reload()
        {
            try
            {
                _matrix = SnapSettings.LoadMatrix();
                _availableSkins = SnapRunner.CreateAdapter().Skins.Where(s => s != "").ToArray();
                _error = null;
            }
            catch (Exception e) { _error = e.Message; }
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_error != null) EditorGUILayout.HelpBox(_error, MessageType.Error);
            DrawRun();
            EditorGUILayout.Space(10);
            DrawLive();
            EditorGUILayout.Space(10);
            DrawHistory();
            EditorGUILayout.EndScrollView();
        }

        private void DrawRun()
        {
            EditorGUILayout.LabelField("Scenarios", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(SnapSettings.ScenariosDir, EditorStyles.miniLabel);
            foreach (var id in SnapSettings.ScenarioIds) Toggle(_scenarios, id);
            if (SnapSettings.ScenarioIds.Count == 0) EditorGUILayout.HelpBox("No scenarios yet. Add JSON files to the folder above.", MessageType.Info);

            if (_matrix != null)
            {
                EditorGUILayout.LabelField("Viewports (none = scenario default)", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                    foreach (var name in _matrix.Presets.Keys.Concat(_matrix.Viewports.Keys)) Toggle(_viewports, name, GUILayout.ExpandWidth(false));
            }
            if (_availableSkins.Count > 1)
            {
                EditorGUILayout.LabelField("Skins (none = scenario default)", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                    foreach (var name in _availableSkins.Prepend("all")) Toggle(_skins, name, GUILayout.ExpandWidth(false));
            }
            _label = EditorGUILayout.TextField("Label", _label);
            _compare = EditorGUILayout.TextField(new GUIContent("Compare with", "Run directory, run id, 'latest' or 'latest:<label>'"), _compare);

            var state = SnapRunner.LoadState();
            bool busy = state is { Terminal: false };
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(busy || _scenarios.Count == 0 || EditorApplication.isPlaying))
                    if (GUILayout.Button("Run")) Run();
                using (new EditorGUI.DisabledScope(!busy))
                    if (GUILayout.Button("Cancel")) SnapRunner.Cancel();
                if (GUILayout.Button("Reload", GUILayout.Width(70))) Reload();
            }
            if (state != null)
            {
                EditorGUILayout.HelpBox($"{state.Id}: {state.Phase}\n{state.Progress}{(state.Error != null ? "\n" + state.Error : "")}{(state.CompareSummary != null ? "\n" + state.CompareSummary : "")}",
                    state.Phase == "failed" ? MessageType.Error : MessageType.None);
                if (state.Terminal && state.Report != null && GUILayout.Button("Open report")) Application.OpenURL("file:///" + state.Report.Replace('\\', '/'));
            }
        }

        private void Run()
        {
            try
            {
                SnapRunner.Start(new SnapRunRequest
                {
                    Scenarios = _scenarios.ToList(),
                    Viewports = _viewports.ToList(),
                    Skins = _skins.ToList(),
                    Label = _label,
                    Compare = string.IsNullOrWhiteSpace(_compare) ? null : _compare
                });
                _error = null;
            }
            catch (Exception e) { _error = e.Message; }
        }

        private void DrawLive()
        {
            EditorGUILayout.LabelField("Live capture", EditorStyles.boldLabel);
            SnapLive.Enabled = EditorGUILayout.ToggleLeft("Capture markers while I play", SnapLive.Enabled);
            SnapLive.Filter = EditorGUILayout.TextField(new GUIContent("Marker filter", "Glob, e.g. decision.* or screen.*"), SnapLive.Filter);
            SnapLive.Repeat = EditorGUILayout.ToggleLeft("Capture repeated markers again", SnapLive.Repeat);
            EditorGUILayout.LabelField("F9 in Play Mode captures the current frame.", EditorStyles.miniLabel);
            if (SnapLive.LastCapture != null) EditorGUILayout.LabelField("Last: " + SnapLive.LastCapture, EditorStyles.miniLabel);
        }

        private void DrawHistory()
        {
            EditorGUILayout.LabelField("Recent runs", EditorStyles.boldLabel);
            string output = SnapSettings.DefaultOutput;
            if (!Directory.Exists(output)) { EditorGUILayout.LabelField(output, EditorStyles.miniLabel); return; }
            foreach (var dir in Directory.GetDirectories(output).OrderByDescending(d => d).Take(12))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(Path.GetFileName(dir));
                    string report = Path.Combine(dir, SnapReport.FileName);
                    using (new EditorGUI.DisabledScope(!File.Exists(report)))
                        if (GUILayout.Button("Report", GUILayout.Width(60))) Application.OpenURL("file:///" + report.Replace('\\', '/'));
                    if (GUILayout.Button("Use as baseline", GUILayout.Width(110))) _compare = dir;
                }
            }
        }

        private static void Toggle(HashSet<string> set, string name, params GUILayoutOption[] options)
        {
            bool on = set.Contains(name);
            bool now = EditorGUILayout.ToggleLeft(name, on, options);
            if (now && !on) set.Add(name);
            else if (!now && on) set.Remove(name);
        }
    }
}
