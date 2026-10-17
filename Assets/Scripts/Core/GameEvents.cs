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
        /// <summary>相生：输的一方就地变成赢家元素（uid 不变，只换元素）。</summary>
        Transform,
        /// <summary>相克：输的一方直接消失。</summary>
        Vanish,
        /// <summary>反应生成了新的元素小怪（出牌相生时的「+1」）。</summary>
        Spawn,
        /// <summary>反应本应生成，但周围没有空格。</summary>
        SpawnBlocked,
        /// <summary>元素收集进度增加，可一次增加多点。</summary>
        BottleFill,
        /// <summary>瓶子满，产出一张元素牌。</summary>
        GainCard,
        /// <summary>抽牌阶段抽到一张牌。</summary>
        DrawCard,
        /// <summary>弃牌阶段弃掉一张牌。</summary>
        DiscardCard,
        /// <summary>复合元素出现在棋盘上（融合成功）。</summary>
        CompositeSpawn,
        /// <summary>复合元素从棋盘上消失（分解 / 被机制移除）。</summary>
        CompositeVanish,
        Message,
        Win,
        Lose,
        /// <summary>当前集齐门槛与剩余点数发生变化。</summary>
        BottleCapacityChanged,
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
        /// <summary>资源进度的变化量与结算快照，表现层不从最终状态反推中间进度。</summary>
        public int Amount;
        public int BottleCount;
        public int BottleCapacity;
        /// <summary>本次获得的真实手牌 ID；0 表示该事件没有关联手牌。</summary>
        public int CardId;

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
        /// <summary>这一步是「打出元素牌在空格上召唤小怪」（图1 第 2 条）：To = 召唤到的那一格。</summary>
        public bool IsSummon;
        /// <summary>这一步是「打出融合牌」：From = 元素1 的位置，To = 复合元素出现的位置（同一个格）。</summary>
        public bool IsFusion;
        /// <summary>这一步是「打出分解牌」：From = 被拆掉的复合元素的位置。</summary>
        public bool IsDecompose;
        /// <summary>这一步是「复合元素产生元素」（回合开始 / 融合当回合）。</summary>
        public bool IsCompositeEffect;
        public bool CreepWentAway;

        public bool HasVisualMovement => Path.Count > 1;

        public override string ToString()
        {
            if (IsDrawPhase) return $"[抽牌]{Events.Count} 张";
            if (IsSummon) return $"[召唤]{ElementDefs.Name(Element)} @{To}";
            if (IsFusion) return $"[融合牌]@{To}";
            if (IsDecompose) return $"[分解牌]@{From}";
            if (IsCompositeEffect) return $"[复合元素产生]@{From}";
            if (IsPlayerCard) return $"[出牌]{ElementDefs.Name(Element)} @{To}";
            return $"#{Uid}{ElementDefs.Name(Element)} {From}→{To} 走{Distance}格";
        }
    }
}
