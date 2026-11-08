using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Ji2.UiSnapshots.Editor
{
    /// <summary>
    /// Isolates Editor PlayerPrefs by temporarily suffixing PlayerSettings company/product names
    /// (Editor prefs are keyed by them). The suffix is recognisable, so recovery works even without the backup.
    /// The change stays in memory; restoration happens before any intentional asset save.
    /// </summary>
    public static class SnapPrefsSandbox
    {
        public const string Suffix = ".UiSnapshots";
        private static string BackupPath => Path.Combine(SnapSettings.TempDir, "identity.json");

        public static bool Active => PlayerSettings.productName.EndsWith(Suffix) || PlayerSettings.companyName.EndsWith(Suffix);

        public static void Enter()
        {
            if (Active) return;
            Directory.CreateDirectory(SnapSettings.TempDir);
            File.WriteAllText(BackupPath, new JObject { ["company"] = PlayerSettings.companyName, ["product"] = PlayerSettings.productName }.ToString());
            PlayerSettings.companyName += Suffix;
            PlayerSettings.productName += Suffix;
        }

        /// <summary>Clears sandbox prefs. Refuses to run outside the sandbox so real Editor prefs are never wiped.</summary>
        public static void Clear()
        {
            if (!Active) throw new SnapException("Refusing to clear PlayerPrefs outside the UI Snapshots sandbox.");
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
        }

        public static void Restore()
        {
            if (Active && !Application.isPlaying) Clear();
            if (File.Exists(BackupPath))
            {
                var backup = JObject.Parse(File.ReadAllText(BackupPath));
                PlayerSettings.companyName = (string)backup["company"];
                PlayerSettings.productName = (string)backup["product"];
                File.Delete(BackupPath);
            }
            if (PlayerSettings.companyName.EndsWith(Suffix)) PlayerSettings.companyName = PlayerSettings.companyName.Substring(0, PlayerSettings.companyName.Length - Suffix.Length);
            if (PlayerSettings.productName.EndsWith(Suffix)) PlayerSettings.productName = PlayerSettings.productName.Substring(0, PlayerSettings.productName.Length - Suffix.Length);
        }
    }
}
