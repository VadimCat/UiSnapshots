using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ji2.UiSnapshots.Editor
{
    /// <summary>Generic uGUI helpers used by built-in steps. Text components are read through reflection so TMP stays optional.</summary>
    public static class SnapUi
    {
        public static string PathOf(Transform t)
        {
            var names = new List<string>();
            for (; t != null; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }

        /// <summary>Active objects whose name equals the target or whose hierarchy path ends with "/target".</summary>
        public static IEnumerable<GameObject> FindActive(string target)
        {
            if (string.IsNullOrEmpty(target)) yield break;
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!t.gameObject.activeInHierarchy) continue;
                if (t.name == target || (target.Contains("/") && PathOf(t).EndsWith("/" + target, StringComparison.Ordinal)) || PathOf(t) == target)
                    yield return t.gameObject;
            }
        }

        public static Selectable FindSelectable(string target, out int matches)
        {
            var found = FindActive(target).Select(g => g.GetComponent<Selectable>()).Where(s => s != null && s.IsInteractable() && s.isActiveAndEnabled).ToArray();
            matches = found.Length;
            return found.FirstOrDefault();
        }

        /// <summary>Clicks through the EventSystem (pointer enter/down/up/click) so press states, sounds and listeners all run.</summary>
        public static void Click(GameObject target)
        {
            var system = EventSystem.current;
            if (system == null)
            {
                var button = target.GetComponent<Button>();
                if (button == null) throw new SnapException($"No EventSystem and '{target.name}' is not a Button.");
                button.onClick.Invoke();
                return;
            }
            var data = new PointerEventData(system) { button = PointerEventData.InputButton.Left, position = ScreenCenter(target) };
            data.pointerPress = target;
            data.pointerEnter = target;
            ExecuteEvents.Execute(target, data, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(target, data, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(target, data, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(target, data, ExecuteEvents.pointerClickHandler);
            ExecuteEvents.Execute(target, data, ExecuteEvents.pointerExitHandler);
        }

        public static Vector2 ScreenCenter(GameObject target)
        {
            var rect = ScreenRect(target);
            return rect.HasValue ? rect.Value.center : Vector2.zero;
        }

        /// <summary>Screen-space rectangle (origin bottom-left) of a UI element or renderer.</summary>
        public static Rect? ScreenRect(UnityEngine.Object focus)
        {
            Transform t = focus switch
            {
                GameObject g => g.transform,
                Component c => c.transform,
                _ => null
            };
            if (t == null) return null;
            if (t is RectTransform rt)
            {
                var canvas = rt.GetComponentInParent<Canvas>();
                Camera camera = canvas == null || canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                var points = corners.Select(c => RectTransformUtility.WorldToScreenPoint(camera, c)).ToArray();
                return Bounds(points);
            }
            var renderers = t.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0 || Camera.main == null) return null;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            var min = bounds.min; var max = bounds.max;
            var cornersWorld = new[] { new Vector3(min.x, min.y, min.z), new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, min.z), new Vector3(max.x, min.y, max.z) };
            return Bounds(cornersWorld.Select(c => (Vector2)Camera.main.WorldToScreenPoint(c)).ToArray());
        }

        private static Rect Bounds(Vector2[] points)
        {
            float xMin = points.Min(p => p.x), xMax = points.Max(p => p.x), yMin = points.Min(p => p.y), yMax = points.Max(p => p.y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static readonly string[] TextTypes = { "TMPro.TMP_Text", "UnityEngine.UI.Text" };

        public static IEnumerable<string> VisibleTexts()
        {
            foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (behaviour == null || !behaviour.isActiveAndEnabled) continue;
                var type = behaviour.GetType();
                bool isText = false;
                for (var t = type; t != null && !isText; t = t.BaseType) isText = TextTypes.Contains(t.FullName);
                if (!isText) continue;
                var text = type.GetProperty("text", BindingFlags.Public | BindingFlags.Instance)?.GetValue(behaviour) as string;
                if (!string.IsNullOrWhiteSpace(text)) yield return text.Replace("\n", " ").Trim();
            }
        }
    }
}
