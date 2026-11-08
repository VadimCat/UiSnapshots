using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ji2.UiSnapshots.Editor
{
    /// <summary>
    /// Reference screenshots. A shot is checked against, in order:
    /// 1. the capture step's explicit "reference" image (a mockup or any PNG, path relative to Tools/UiSnapshots);
    /// 2. an approved golden frame References/&lt;scenario&gt;/&lt;capture&gt;@&lt;viewport&gt;[-&lt;skin&gt;].png;
    /// 3. a viewport-independent design References/&lt;scenario&gt;/&lt;capture&gt;.png.
    /// Same size → pixel diff; another size → shown side by side and as a slider in the report.
    /// </summary>
    public static class SnapReferences
    {
        public static string Dir => Path.Combine(SnapSettings.Root, "References");

        public static string Find(SnapShot shot, out string kind)
        {
            kind = null;
            if (!string.IsNullOrEmpty(shot.ReferenceSource))
            {
                kind = "explicit";
                return SnapSettings.ResolveData(shot.ReferenceSource);
            }
            string exact = Path.Combine(Dir, shot.Key + ".png");
            if (File.Exists(exact)) { kind = "approved"; return exact; }
            string design = Path.Combine(Dir, shot.Scenario, shot.Capture + ".png");
            if (File.Exists(design)) { kind = "design"; return design; }
            return null;
        }

        /// <summary>Edit Mode, after the run: copies each matching reference into the run and diffs it.</summary>
        public static int Apply(string runDir, SnapManifest manifest)
        {
            int mismatches = 0;
            foreach (var shot in manifest.Shots)
            {
                string source = Find(shot, out string kind);
                if (source == null) continue;
                if (!File.Exists(source)) { shot.Warnings.Add("Reference missing: " + source); continue; }
                shot.ReferenceKind = kind;
                shot.Reference = Path.Combine("refs", shot.Key + ".png").Replace('\\', '/');
                string copy = Path.Combine(runDir, shot.Reference);
                Directory.CreateDirectory(Path.GetDirectoryName(copy));
                File.Copy(source, copy, true);
                var (status, percent, diff, _, _) = SnapCompare.DiffImages(copy, Path.Combine(runDir, shot.File), runDir, Path.Combine("refs-diff", shot.Key + ".png"));
                shot.ReferenceStatus = status;
                shot.ReferenceChangedPercent = percent;
                shot.ReferenceDiff = diff;
                if (status == "changed" && kind == "approved") mismatches++;
            }
            manifest.Save(runDir);
            return mismatches;
        }

        /// <summary>Promotes shots of a finished run to approved references (glob on the shot key, default all).</summary>
        public static List<string> Approve(string runDir, string pattern = "*")
        {
            var manifest = SnapManifest.Load(runDir);
            var regex = SnapGlob.ToRegex(string.IsNullOrWhiteSpace(pattern) ? "*" : pattern);
            var approved = new List<string>();
            foreach (var shot in manifest.Shots.Where(s => regex.IsMatch(s.Key)))
            {
                string target = Path.Combine(Dir, shot.Key + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(Path.Combine(runDir, shot.File), target, true);
                approved.Add(shot.Key);
            }
            if (approved.Count == 0) throw new SnapException($"No shot in {runDir} matches '{pattern}'.");
            return approved;
        }

        /// <summary>Adds (or replaces) a design reference from any image for a scenario capture.</summary>
        public static string AddDesign(string imagePath, string scenario, string capture, string viewport = null, string skin = null)
        {
            if (!File.Exists(imagePath)) throw new SnapException("Image not found: " + imagePath);
            if (!imagePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new SnapException("References must be PNG files.");
            string key = viewport == null ? Path.Combine(scenario, capture) : SnapCapture.ShotKey(scenario, capture, viewport, skin);
            string target = Path.Combine(Dir, key + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(imagePath, target, true);
            return target;
        }
    }
}
