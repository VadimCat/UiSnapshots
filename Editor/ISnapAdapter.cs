using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Ji2.UiSnapshots.Editor
{
    /// <summary>
    /// The game's side of the tool. Implement exactly one non-abstract adapter in an Editor assembly
    /// (derive from <see cref="SnapAdapter"/>); it is discovered automatically.
    /// The package owns orchestration; the game owns state, scenes, skins and readiness.
    /// </summary>
    public interface ISnapAdapter
    {
        /// <summary>Selectable skins/themes. A single empty string means the game has no skins.</summary>
        IReadOnlyList<string> Skins { get; }

        /// <summary>Edit Mode, after the PlayerPrefs sandbox is active and cleared: seed prefs from the scenario setup and select the skin.</summary>
        void Prepare(JObject setup, string skin);

        /// <summary>Play Mode, before the first step: wait until the game finished booting.</summary>
        Task BeginPass(SnapContext context);

        /// <summary>True when no UI transition or animation would change the next frame.</summary>
        bool IsSettled();

        /// <summary>Called after the Game View switched resolution: apply simulated safe-area insets and relayout.</summary>
        void ApplyViewport(SnapViewport viewport);

        /// <summary>Pause (true) or resume (false) endless loops such as idle animations right around a capture.</summary>
        void SetLoopsFrozen(bool frozen);

        /// <summary>Called every frame during a pass; may emit synthetic markers (for example screen changes).</summary>
        void Tick(SnapContext context);

        /// <summary>Small state summary for the manifest: scene, screen, level...</summary>
        IDictionary<string, object> DescribeState();

        /// <summary>Visible texts for <c>expect</c> checks.</summary>
        IEnumerable<string> VisibleTexts();
    }

    /// <summary>Default behaviour for games without special needs.</summary>
    public abstract class SnapAdapter : ISnapAdapter
    {
        public virtual IReadOnlyList<string> Skins => new[] { "" };
        public virtual void Prepare(JObject setup, string skin) { }
        public virtual Task BeginPass(SnapContext context) => Task.CompletedTask;
        public virtual bool IsSettled() => true;
        public virtual void ApplyViewport(SnapViewport viewport) { }
        public virtual void SetLoopsFrozen(bool frozen) { }
        public virtual void Tick(SnapContext context) { }
        public virtual IDictionary<string, object> DescribeState() => new Dictionary<string, object>
        {
            ["scene"] = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
        };
        public virtual IEnumerable<string> VisibleTexts() => SnapUi.VisibleTexts();
    }

    internal sealed class DefaultSnapAdapter : SnapAdapter { }
}
