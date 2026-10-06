using System.Collections.Generic;

namespace TapGJ.Core
{
    public enum GamePhase
    {
        NotStarted,
        /// <summary>准备阶段：小怪随机移动、相遇反应，玩家不能交互。</summary>
        Preparation,
        /// <summary>抽牌阶段：小怪静止，玩家把手牌抽到上限（可提前结束）。</summary>
        Draw,
        /// <summary>玩家行动阶段：小怪静止，玩家出牌。</summary>
        PlayerAction,
        /// <summary>弃牌阶段：玩家弃掉多余的手牌（不能出牌）。</summary>
        Discard,
        /// <summary>判定阶段。</summary>
        Judgment,
        Victory,
        Defeat,
    }

    /// <summary>
    /// 事件类型。名字特意避开 UnityEngine.EventType，否则表现层里会出现二义性。
    /// </summary>
    public enum GameEventType
    {
        Move,
        /// <summary>同元素相遇：禁止本次移动。</summary>
        Bump,
        /// <summary>不同元素相遇：发生反应（某个小怪消失）。</summary>
        Vanish,
        /// <summary>反应生成了新的元素小怪。</summary>
        Spawn,
        /// <summary>反应本应生成，但周围没有空格。</summary>
        SpawnBlocked,
        /// <summary>小瓶子计数 +1。</summary>
        BottleFill,
        /// <summary>瓶子满，产出一张元素牌。</summary>
        GainCard,
        /// <summary>抽牌阶段抽到一张牌。</summary>
        DrawCard,
        /// <summary>弃牌阶段弃掉一张牌。</summary>
        DiscardCard,        Message,
        Win,
        Lose,
    }

    public sealed class GameEvent
    {
        public GameEventType Type;
        public Pos From = Pos.None;
        public Pos To = Pos.None;
        public Element Element;
        /// <summary>消失的小怪 uid（Vanish / Bump 时用于表现层定位物体）。</summary>
        public int Uid = -1;
        public string Text;
        public int Round;
        /// <summary>该事件在本回合的第几个小怪步骤里产生（表现层按此分组播放）。</summary>
        public int StepIndex;

        public override string ToString()
        {
            var pos = !To.IsNone ? To.ToString() : (!From.IsNone ? From.ToString() : "");
            return string.IsNullOrEmpty(pos)
                ? $"[{Type}] {Text}"
                : $"[{Type}] {ElementDefs.Name(Element)}{pos} {Text}";
        }
    }

    /// <summary>
    /// 一个「步骤」= 一只小怪的一次移动决策、玩家打出的一张牌，或抽牌阶段补牌。
    /// 逻辑层一次性算完并排队，表现层逐条取走播放动画。
    /// </summary>
    public sealed class StepRecord
    {
        public int Index;
        /// <summary>-1 表示这不是某只小怪走的（出牌 / 抽牌）。</summary>
        public int Uid = -1;
        public Element Element;
        public Pos From = Pos.None;
        public Pos To = Pos.None;
        public int Distance;
        /// <summary>实际走位轨迹（含起点），供表现层做位移动画。</summary>
        public List<Pos> Path = new List<Pos>();
        public List<GameEvent> Events = new List<GameEvent>();
        public bool IsPlayerCard;
        /// <summary>抽牌阶段的补牌步骤（没有棋盘上的移动，只有手牌变化）。</summary>
        public bool IsDrawPhase;
        public bool CreepWentAway;

        public bool HasVisualMovement => Path.Count > 1;

        public override string ToString()
        {
            if (IsDrawPhase) return $"[抽牌]{Events.Count} 张";
            if (IsPlayerCard) return $"[出牌]{ElementDefs.Name(Element)} @{To}";
            return $"#{Uid}{ElementDefs.Name(Element)} {From}→{To} 走{Distance}格";
        }
    }
}
