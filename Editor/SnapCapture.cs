using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Ji2.CreativeReplay.Editor;
using UnityEngine;

namespace Ji2.UiSnapshots.Editor
{
    /// <summary>Viewport switching (via com.ji2.creative-replay's CreativeGameView) and native Game View grabs.</summary>
    public static class SnapCapture
    {
        public const int FocusPadding = 24;

        public static async Task ApplyViewport(SnapContext context, SnapViewport viewport)
        {
            if (Screen.width != viewport.Width || Screen.height != viewport.Height)
            {
                CreativeGameView.SetResolution(viewport.Width, viewport.Height);
                bool resized = await context.WaitUntil(() => Screen.width == viewport.Width && Screen.height == viewport.Height,
                    3000, $"Game View {viewport.Width}x{viewport.Height}", throwOnTimeout: false);
                if (!resized) context.Warn($"Game View stayed at {Screen.width}x{Screen.height} instead of {viewport.Width}x{viewport.Height}.");
            }
            context.CurrentViewport = viewport;
            context.Adapter.ApplyViewport(viewport);
            await context.Frames(2);
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>Native frame including Screen Space Overlay canvases.</summary>
        public static async Task<Texture2D> Grab()
        {
            await Awaitable.EndOfFrameAsync();
            return ScreenCapture.CaptureScreenshotAsTexture();
        }

        public static async Task CaptureAsync(SnapContext context, string name, SnapPoint point, UnityEngine.Object focus,
            bool settle, int settleTimeoutMs, IReadOnlyList<SnapViewport> viewports)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new SnapException("capture needs a name.");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains("@")) throw new SnapException($"Capture name '{name}' must be a plain file name without '@'.");
            focus ??= point?.Focus;
            foreach (var viewport in viewports)
            {
                if (context.CurrentViewport != viewport) await ApplyViewport(context, viewport);
                var (settled, ms) = settle ? await context.Settle(settleTimeoutMs) : (true, 0);
                context.Adapter.SetLoopsFrozen(true);
                Texture2D frame = null;
                try
                {
                    await context.NextFrame();
                    frame = await Grab();
                    Save(context, name, point, focus, viewport, frame, settled, ms);
                }
                finally
                {
                    context.Adapter.SetLoopsFrozen(false);
                    if (frame != null) UnityEngine.Object.DestroyImmediate(frame);
                }
            }
            if (context.CurrentViewport != context.Primary) await ApplyViewport(context, context.Primary);
        }

        public static string ShotKey(string scenario, string capture, string viewport, string skin) =>
            $"{scenario}/{capture}@{viewport}{(string.IsNullOrEmpty(skin) ? "" : "-" + skin)}";

        private static void Save(SnapContext context, string name, SnapPoint point, UnityEngine.Object focus, SnapViewport viewport,
            Texture2D frame, bool settled, int settleMs)
        {
            string key = ShotKey(context.Scenario.Id, name, viewport.Name, context.Skin);
            string relative = Path.Combine("shots", key + ".png").Replace('\\', '/');
            string full = Path.Combine(context.OutputDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            byte[] png = frame.EncodeToPNG();
            File.WriteAllBytes(full, png);

            var shot = new SnapShot
            {
                Key = key,
                Scenario = context.Scenario.Id,
                Skin = context.Skin,
                Viewport = viewport.Name,
                Capture = name,
                Step = context.Step?.Index + 1 ?? 0,
                Point = point?.Id,
                File = relative,
                Sha256 = Hash(png),
                Width = frame.width,
                Height = frame.height,
                RequestedWidth = viewport.Width,
                RequestedHeight = viewport.Height,
                InsetTop = viewport.Top,
                InsetBottom = viewport.Bottom,
                Settled = settled,
                SettleMs = settleMs,
                Tags = context.Tags.Count > 0 ? new Dictionary<string, string>(context.Tags) : null,
                State = SafeState(context)
            };
            if (!settled) shot.Warnings.Add($"UI did not settle within the timeout ({settleMs} ms).");
            if (frame.width != viewport.Width || frame.height != viewport.Height)
                shot.Warnings.Add($"Frame is {frame.width}x{frame.height}, requested {viewport.Width}x{viewport.Height}.");

            if (focus != null)
            {
                var rect = SnapUi.ScreenRect(focus);
                if (rect.HasValue)
                {
                    var r = rect.Value;
                    int x = Mathf.Clamp(Mathf.FloorToInt(r.xMin) - FocusPadding, 0, frame.width - 1);
                    int y = Mathf.Clamp(Mathf.FloorToInt(r.yMin) - FocusPadding, 0, frame.height - 1);
                    int w = Mathf.Clamp(Mathf.CeilToInt(r.width) + 2 * FocusPadding, 1, frame.width - x);
                    int h = Mathf.Clamp(Mathf.CeilToInt(r.height) + 2 * FocusPadding, 1, frame.height - y);
                    var crop = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    crop.SetPixels(frame.GetPixels(x, y, w, h));
                    crop.Apply();
                    shot.FocusFile = relative.Replace(".png", ".focus.png");
                    File.WriteAllBytes(Path.Combine(context.OutputDir, shot.FocusFile), crop.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(crop);
                }
                else shot.Warnings.Add($"Focus '{focus.name}' has no screen rectangle.");
            }

            context.Manifest.Shots.RemoveAll(s => s.Key == key);
            context.Manifest.Shots.Add(shot);
            context.Manifest.Save(context.OutputDir);
            context.Log($"captured {key}{(settled ? "" : " (unsettled)")}");
        }

        private static IDictionary<string, object> SafeState(SnapContext context)
        {
            try { return context.Adapter.DescribeState(); }
            catch (Exception e) { return new Dictionary<string, object> { ["error"] = e.Message }; }
        }

        public static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }
    }
}
