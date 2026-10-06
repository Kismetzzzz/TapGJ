using System;
using System.Collections.Generic;

namespace TapGJ.Core
{
    /// <summary>
    /// 给测试和编辑器沙盒用的白盒接口（正常游戏流程不会调到这里）。
    /// 分成 partial 是为了让 GameEngine.cs 只保留玩法主流程。
    /// </summary>
    public sealed partial class GameEngine
    {
        // ---------------------------------------------------------------- 摆盘

        /// <summary>清空棋盘和记分（瓶子不清空）。</summary>
        public void ClearBoard()
        {
            Board.ClearAll();
            _creeps.Clear();
            _removed.Clear();
            _spawned.Clear();
            TotalRemoved = 0;
            TotalSpawned = 0;
            SpawnBlockedCount = 0;
        }

        /// <summary>在指定空格放一只小怪。成功返回它，格子被占/越界返回 null。</summary>
        public Creep PlaceCreep(Element element, Pos pos)
        {
            if (!Board.IsEmpty(pos)) return null;
            var creep = new Creep(_nextUid++, element, pos);
            _creeps[creep.Uid] = creep;
            Board.Put(creep);
            return creep;
        }

        /// <summary>清空手牌后按指定顺序发牌。注意：抽牌阶段会把手牌补回上限。</summary>
        public void ForceHandForTest(IEnumerable<Element> elements)
        {
            _hand.Clear();
            foreach (var e in elements) DrawCard(e);
        }

        /// <summary>
        /// 把本局的手牌上限压到当前手牌数，这样抽牌阶段会「本来就满、一张都不抽」。
        /// 用于那些要精确摆一张特定手牌的测试。返回原来的上限，方便恢复。
        /// </summary>
        public int FreezeHandLimitForTest()
        {
            int old = Rules.HandLimit;
            Rules.HandLimit = _hand.Count;
            return old;
        }

        /// <summary>
        /// 摆好手牌后跳过准备阶段直达玩家行动阶段。
        /// 抽牌阶段会把手牌补到上限，所以想验证「某张特定手牌」的用例
        /// 要么把 HandLimit 设成和手牌数一致，要么在抽牌后再 ForceHandForTest。
        /// </summary>
        public void EndPreparationNow()
        {
            int guard = 0;
            while (Phase == GamePhase.Preparation && guard++ < Rules.CellCount * 4 + 8)
                AdvancePreparation();
        }
        // ---------------------------------------------------------------- 随机数控制

        Random _overrideRng;

        /// <summary>
        /// 用固定序列替换随机源，让测试完全可控。
        /// 之后每次 Next(max) 依次取 values（循环使用）。
        /// 注意：方向占 1 次（max=4），步数占 1 次（max=MaxMoveSteps+1），顺序是「先步数、后方向」。
        /// </summary>
        public void SetNextRandomValues(params int[] values)
        {
            _overrideRng = new ScriptedRandom(values);
        }

        /// <summary>恢复真实随机。</summary>
        public void ClearScriptedRandom()
        {
            _overrideRng = null;
        }

        sealed class ScriptedRandom : Random
        {
            readonly int[] _values;
            int _i;

            public ScriptedRandom(int[] values) : base(0)
            {
                _values = values == null || values.Length == 0 ? new[] { 0 } : values;
            }

            public override int Next(int maxValue)
            {
                if (maxValue <= 0) return 0;
                int raw = _values[_i++ % _values.Length];
                int v = raw % maxValue;
                return v < 0 ? v + maxValue : v;
            }

            public override int Next(int minValue, int maxValue)
            {
                int span = maxValue - minValue;
                return span <= 0 ? minValue : minValue + Next(span);
            }
        }

        // ---------------------------------------------------------------- 瓶子测试

        /// <summary>
        /// 造一次「某元素从棋盘上消失」的事件，用来单独验证小瓶子计数。
        /// 走的是和正常反应完全相同的 RemoveCreep 路径。
        /// </summary>
        public bool TryPlaceAndRemoveForTest(Element element)
        {
            var free = FindFreeCell();
            if (free.IsNone) return false;

            var creep = PlaceCreep(element, free);
            if (creep == null) return false;

            var record = new StepRecord { Index = _stepIndex++, Element = element, From = free, To = free };
            RemoveCreep(creep, record, free);
            Enqueue(record);
            return true;
        }

        Pos FindFreeCell()
        {
            for (int y = 0; y < Board.Height; y++)
                for (int x = 0; x < Board.Width; x++)
                    if (Board.IsEmpty(new Pos(x, y))) return new Pos(x, y);
            return Pos.None;
        }

        /// <summary>把所有元素瓶子的计数设成同一个值。测试里用来避免满瓶自动发牌干扰。</summary>
        public void SetupBottleForTest(int count)
        {
            try
            {
                var field = typeof(Bottle).GetField("_counts",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field == null) return;

                var counts = (int[])field.GetValue(Bottle);
                for (int i = 0; i < counts.Length; i++) counts[i] = count;
            }
            catch (Exception)
            {
                // 反射被裁剪/受限时忽略，测试里会自然走到「满瓶发牌」的分支
            }
        }

        // ---------------------------------------------------------------- 统计

        /// <summary>本回合已经行动过的小怪数量。</summary>
        public int MovedThisRound => _movedThisRound.Count;

        /// <summary>每个元素被反应消耗掉的次数。</summary>
        public int RemovedOf(Element e) => _removed.TryGetValue(e, out var n) ? n : 0;

        /// <summary>每个元素通过反应新生出来的次数。</summary>
        public int SpawnedOf(Element e) => _spawned.TryGetValue(e, out var n) ? n : 0;
    }
}
