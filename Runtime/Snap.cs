using System.Diagnostics;

namespace Ji2.UiSnapshots
{
    /// <summary>
    /// Screenshot markers for game code. Every method is <c>[Conditional("UNITY_EDITOR")]</c>:
    /// player compilation removes the call together with its argument evaluation.
    /// Markers never return anything, so they cannot influence gameplay.
    /// </summary>
    public static class Snap
    {
        /// <summary>Marks a state worth capturing. <paramref name="focus"/> (a RectTransform, Component or GameObject) adds a cropped frame.</summary>
        [Conditional("UNITY_EDITOR")]
        public static void Point(string id, UnityEngine.Object focus = null, string note = null)
        {
#if UNITY_EDITOR
            SnapHook.Current?.OnPoint(id, focus, note);
#endif
        }

        /// <summary>Adds context (for example the active tutorial) to the following captures.</summary>
        [Conditional("UNITY_EDITOR")]
        public static void Tag(string key, string value)
        {
#if UNITY_EDITOR
            SnapHook.Current?.OnTag(key, value);
#endif
        }
    }

#if UNITY_EDITOR
    /// <summary>Editor-side receiver of markers. Installed only by the UI Snapshots Editor tooling.</summary>
    public interface ISnapListener
    {
        void OnPoint(string id, UnityEngine.Object focus, string note);
        void OnTag(string key, string value);
    }

    /// <summary>Editor-only hook. Null unless a scenario run or live capture is active.</summary>
    public static class SnapHook
    {
        public static ISnapListener Current { get; set; }
    }
#endif
}
