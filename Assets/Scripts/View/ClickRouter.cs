using System.Collections.Generic;
using TapGJ.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TapGJ.View
{
    /// <summary>
    /// 输入分发：把一次鼠标点击翻译成「点了哪张手牌」或「点了哪个格子」。
    ///
    /// 场景里只需要一个 ClickRouter，GameSession 订阅它的两个事件。
    /// 它**不做任何规则判断**：能不能出这张牌、这个阶段能不能操作，都由 GameSession 决定。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ClickRouter : MonoBehaviour, ISceneWiringCheck
    {
        [Header("必需引用")]
        [Tooltip("把屏幕坐标换算成世界坐标用的相机，通常是 Main Camera（正交）")]
        [SerializeField] Camera worldCamera;

        /// <summary>由 GameSession 按阶段开关；关掉时连射线都不发。</summary>
        public bool InteractionEnabled = true;

        /// <summary>
        /// 额外的屏幕遮挡判断（像素坐标，左上原点为 0,0 的 GUI 坐标）。
        /// 现在给 IMGUI 的临时 HUD 用 —— IMGUI 不参与 EventSystem，光靠 EventSystem 挡不住它。
        /// 换成 uGUI Canvas 之后 EventSystem 会接管，这里保持为空即可。
        /// </summary>
        public System.Func<Vector2, bool> ExtraBlocker;

        public event System.Action<int> CardClicked;
        public event System.Action<Pos> CellClicked;

        /// <summary>最近一次点击是否被 UI 吃掉了（调试 / HUD 显示用）。</summary>
        public bool BlockedByUi { get; private set; }

        public Camera WorldCamera => worldCamera;

        void Update()
        {
            if (!InteractionEnabled) return;
            if (!Input.GetMouseButtonDown(0)) return;

            var screen = (Vector2)Input.mousePosition;

            BlockedByUi = IsOverUi(screen);
            if (BlockedByUi) return;

            var hit = Physics2D.OverlapPoint(ScreenToWorld(screen));
            if (hit == null) return;

            var card = hit.GetComponentInParent<CardView>();
            if (card != null)
            {
                CardClicked?.Invoke(card.CardId);
                return;
            }

            var cell = hit.GetComponentInParent<CellView>();
            if (cell != null) CellClicked?.Invoke(cell.Pos);
        }

        bool IsOverUi(Vector2 screen)
        {
            var es = EventSystem.current;
            if (es != null && es.IsPointerOverGameObject()) return true;

            return ExtraBlocker != null && ExtraBlocker(screen);
        }

        Vector2 ScreenToWorld(Vector2 screen)
        {
            if (worldCamera == null) return Vector2.zero;

            var world = worldCamera.ScreenToWorldPoint(
                new Vector3(screen.x, screen.y, -worldCamera.transform.position.z));
            return new Vector2(world.x, world.y);
        }

        // ---------------------------------------------------------------- 接线检查

        public void CollectWiringIssues(List<WiringIssue> into)
        {
            Wiring.Require(worldCamera, nameof(ClickRouter), nameof(worldCamera),
                "把渲染棋盘的正交相机（Main Camera）拖进来。没有它就没法把鼠标位置换算成棋盘坐标。", into);
        }

        /// <summary>纯函数版本，测试用。</summary>
        public static void CheckWiring(Camera camera, List<WiringIssue> into)
        {
            Wiring.Require(camera, nameof(ClickRouter), nameof(worldCamera),
                "把渲染棋盘的正交相机（Main Camera）拖进来。没有它就没法把鼠标位置换算成棋盘坐标。", into);
        }
    }
}
