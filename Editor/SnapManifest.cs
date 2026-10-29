using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Ji2.UiSnapshots.Editor
{
    public sealed class SnapShot
    {
        public string Key;            // scenario/capture@viewport-skin, stable across runs
        public string Scenario;
        public string Skin;
        public string Viewport;
        public string Capture;
        public int Step;
        public string Point;
        public string File;           // relative to the run directory
        public string FocusFile;
        public string Sha256;
        public int Width;
        public int Height;
        public int RequestedWidth;
        public int RequestedHeight;
        public int InsetTop;
        public int InsetBottom;
        public bool Settled;
        public int SettleMs;
        public Dictionary<string, string> Tags;
        public IDictionary<string, object> State;
        public List<string> Warnings = new List<string>();
    }

    public sealed class SnapIssue
    {
        public string Scenario;
        public string Skin;
        public string Step;
        public string Severity;      // error | warning
        public string Message;
    }

    public sealed class SnapExpectation
    {
        public string Scenario;
        public string Skin;
        public string Step;
        public string What;
        public bool Passed;
        public string Actual;
    }

    public sealed class SnapPointRecord
    {
        public string Scenario;
        public string Skin;
        public string Id;
        public int Step;
        public string Note;
    }

    public sealed class SnapManifest
    {
        public const string FileName = "manifest.json";
        public string RunId;
        public string Label;
        public string Status = "running";
        public string Project;
        public string Commit;
        public string Branch;
        public string DirtyHash;
        public string Unity;
        public string StartedUtc;
        public string FinishedUtc;
        public double Seconds;
        public List<string> Scenarios = new List<string>();
        public List<string> Skins = new List<string>();
        public List<SnapViewport> Viewports = new List<SnapViewport>();
        public List<SnapShot> Shots = new List<SnapShot>();
        public List<SnapIssue> Issues = new List<SnapIssue>();
        public List<SnapExpectation> Expectations = new List<SnapExpectation>();
        public List<SnapPointRecord> UncapturedPoints = new List<SnapPointRecord>();
        public List<string> UnreachedCaptures = new List<string>();

        public void Save(string runDir) =>
            File.WriteAllText(Path.Combine(runDir, FileName), JsonConvert.SerializeObject(this, SnapSettings.Json));

        public static SnapManifest Load(string runDir)
        {
            string path = Path.Combine(runDir, FileName);
            if (!File.Exists(path)) throw new SnapException($"No {FileName} in {runDir}");
            return JsonConvert.DeserializeObject<SnapManifest>(File.ReadAllText(path));
        }
    }
}
