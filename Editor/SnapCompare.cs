using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Ji2.UiSnapshots.Editor
{
    public sealed class SnapComparePair
    {
        public string Key;
        public string Status;      // same | changed | resized | added | removed
        public double ChangedPercent;
        public string Before;      // path relative to the after-run directory
        public string After;
        public string Diff;
        public int Width;
        public int Height;
    }

    public sealed class SnapCompareResult
    {
        public const string FileName = "compare.json";
        public string Baseline;
        public string BaselineCommit;
        public string BaselineBranch;
        public int Threshold;
        public double NoisePercent;
        public List<SnapComparePair> Pairs = new List<SnapComparePair>();
        public string Summary => $"{Pairs.Count(p => p.Status == "changed")} changed, {Pairs.Count(p => p.Status == "resized")} resized, " +
                                 $"{Pairs.Count(p => p.Status == "added")} added, {Pairs.Count(p => p.Status == "removed")} removed, {Pairs.Count(p => p.Status == "same")} same";
    }

    /// <summary>Pairs shots by key (scenario/capture@viewport-skin) and writes heat-map diffs. Edit Mode only, no Play Mode needed.</summary>
    public static class SnapCompare
    {
        public const int DefaultThreshold = 24;      // max per-channel delta treated as noise (antialiasing, dithering)
        public const double DefaultNoisePercent = 0.05;

        public static string ResolveBaseline(string spec, string currentRunDir)
        {
            if (Directory.Exists(spec)) return Path.GetFullPath(spec);
            string parent = Path.GetDirectoryName(Path.GetFullPath(currentRunDir));
            if (spec.StartsWith("latest", StringComparison.OrdinalIgnoreCase))
            {
                string label = spec.Contains(":") ? spec.Substring(spec.IndexOf(':') + 1) : null;
                var candidates = Directory.GetDirectories(parent)
                    .Where(d => !PathsEqual(d, currentRunDir) && File.Exists(Path.Combine(d, SnapManifest.FileName)))
                    .Select(d => (dir: d, manifest: SnapManifest.Load(d)))
                    .Where(x => x.manifest.Status == "done" && (label == null || string.Equals(x.manifest.Label, label, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(x => x.manifest.StartedUtc)
                    .ToList();
                if (candidates.Count == 0) throw new SnapException($"No finished run{(label != null ? $" labelled '{label}'" : "")} in {parent}.");
                return candidates[0].dir;
            }
            string sibling = Path.Combine(parent, spec);
            if (Directory.Exists(sibling)) return sibling;
            throw new SnapException($"Baseline '{spec}' not found (a run directory, a run id next to this run, 'latest' or 'latest:<label>').");
        }

        public static SnapCompareResult Run(string beforeDir, string afterDir, int threshold = DefaultThreshold, double noisePercent = DefaultNoisePercent)
        {
            var before = SnapManifest.Load(beforeDir);
            var after = SnapManifest.Load(afterDir);
            var result = new SnapCompareResult
            {
                Baseline = Path.GetFullPath(beforeDir),
                BaselineCommit = before.Commit,
                BaselineBranch = before.Branch,
                Threshold = threshold,
                NoisePercent = noisePercent
            };
            var keys = before.Shots.Select(s => s.Key).Union(after.Shots.Select(s => s.Key)).OrderBy(k => k);
            foreach (string key in keys)
            {
                var b = before.Shots.FirstOrDefault(s => s.Key == key);
                var a = after.Shots.FirstOrDefault(s => s.Key == key);
                var pair = new SnapComparePair { Key = key };
                if (b != null) pair.Before = Relative(afterDir, Path.Combine(beforeDir, b.File));
                if (a != null) pair.After = a.File;
                if (b == null) pair.Status = "added";
                else if (a == null) pair.Status = "removed";
                else if (b.Sha256 == a.Sha256) { pair.Status = "same"; pair.Width = a.Width; pair.Height = a.Height; }
                else Diff(Path.Combine(beforeDir, b.File), Path.Combine(afterDir, a.File), afterDir, key, threshold, noisePercent, pair);
                result.Pairs.Add(pair);
            }
            File.WriteAllText(Path.Combine(afterDir, SnapCompareResult.FileName), JsonConvert.SerializeObject(result, SnapSettings.Json));
            return result;
        }

        private static void Diff(string beforePath, string afterPath, string afterDir, string key, int threshold, double noisePercent, SnapComparePair pair)
        {
            var b = Load(beforePath);
            var a = Load(afterPath);
            try
            {
                pair.Width = a.width; pair.Height = a.height;
                if (a.width != b.width || a.height != b.height) { pair.Status = "resized"; pair.ChangedPercent = 100; return; }
                var pb = b.GetPixels32();
                var pa = a.GetPixels32();
                var heat = new Color32[pa.Length];
                int changed = 0;
                for (int i = 0; i < pa.Length; i++)
                {
                    int d = Math.Max(Math.Abs(pa[i].r - pb[i].r), Math.Max(Math.Abs(pa[i].g - pb[i].g), Math.Abs(pa[i].b - pb[i].b)));
                    byte gray = (byte)((pa[i].r * 30 + pa[i].g * 59 + pa[i].b * 11) / 100 / 3 + 170);
                    if (d > threshold) { changed++; heat[i] = new Color32(230, 40, 40, 255); }
                    else heat[i] = new Color32(gray, gray, gray, 255);
                }
                pair.ChangedPercent = Math.Round(100.0 * changed / pa.Length, 3);
                pair.Status = pair.ChangedPercent > noisePercent ? "changed" : "same";
                if (pair.Status == "changed")
                {
                    var tex = new Texture2D(a.width, a.height, TextureFormat.RGBA32, false);
                    tex.SetPixels32(heat);
                    tex.Apply();
                    pair.Diff = Path.Combine("diff", key + ".png").Replace('\\', '/');
                    string full = Path.Combine(afterDir, pair.Diff);
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    File.WriteAllBytes(full, tex.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(tex);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
            }
        }

        private static Texture2D Load(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(File.ReadAllBytes(path))) throw new SnapException("Cannot read " + path);
            return tex;
        }

        public static string Relative(string fromDir, string toPath)
        {
            var from = new Uri(Path.GetFullPath(fromDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar);
            var to = new Uri(Path.GetFullPath(toPath));
            return from.Scheme != to.Scheme ? toPath.Replace('\\', '/') : Uri.UnescapeDataString(from.MakeRelativeUri(to).ToString());
        }

        private static bool PathsEqual(string a, string b) =>
            string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }
}
