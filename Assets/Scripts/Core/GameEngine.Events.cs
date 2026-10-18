using System;
using System.Collections.Generic;

namespace TapGJ.Core
{
    /// <summary>
    /// 回合事件的调度与注入 API（按 README 第十一节的设计）。
    ///
    /// 拆成 partial 的理由和 <see cref="GameEngine.LevelRules"/> 一样：GameEngine.cs 只留玩法主流程，
    /// 这里全是「什么时候抽事件、抽哪一条、事件能对棋盘做什么」。
    ///
    /// 三条刻意的设计（改之前先读）：
    ///   1. **调度在 Core 里**，用引擎自己的 Seed 派生出的**独立随机流**（<see cref="_eventRng"/>）：
    ///      同一个种子下「第几回合来事件、来哪几条」与玩家怎么打**无关** —— 预告和复现都靠它。
    ///      如果用共用的 _rng，玩家多打一张牌就会把事件时间表整个挤歪。
    ///   2. **不放回**：同一局里一条事件最多出现一次；池子抽空后重洗（不会出现「后面几回合没事件」）。
    ///   3. **不适用的跳过、且不算抽过**：本关用不了的事件（池外元素 / 没开秘术牌）不会被消耗掉。
    /// </summary>
    public sealed partial class GameEngine
    {
        readonly List<IRoundEvent> _eventPool = new List<IRoundEvent>();
        /// <summary>本局已经抽到过的事件 Id（不放回）。</summary>
        readonly HashSet<string> _drawnEventIds = new HashSet<string>();
        /// <summary>本回合生效中的事件（判定阶段逐个 Revert 后清空）。</summary>
        readonly List<IRoundEvent> _activeEvents = new List<IRoundEvent>();

        /// <summary>事件专用随机流：由 Seed 派生，和玩法随机（移动 / 抽牌 / 出生格）完全分开。</summary>
        Random _eventRng;

        /// <summary>下一次事件落在第几回合（0 = 本局不会再有事件）。</summary>
        int _nextEventRound;

        /// <summary>事件生效期间「事件自己的步骤」——Apply 里产生的事件都追加到它上面，最后一次性排给表现层。</summary>
        StepRecord _eventRecord;

        /// <summary>当前生效中的事件（正常最多一个；留成列表是为了以后做「同时两条」）。</summary>
        public IReadOnlyList<IRoundEvent> ActiveEvents => _activeEvents;

        /// <summary>本回合的事件；没有就返回 null。HUD 常驻显示用它。</summary>
        public IRoundEvent ActiveEvent => _activeEvents.Count > 0 ? _activeEvents[0] : null;

        /// <summary>下一次事件落在第几回合（0 = 本局不会再有）。HUD 的「下一次：第 N 回合」用它。</summary>
        public int NextEventRound => _nextEventRound;

        /// <summary>本关挂没挂事件池（没挂 = 这个引擎的行为与加事件系统之前逐位一致）。</summary>
        public bool EventsEnabled => _eventPool.Count > 0;

        /// <summary>本局已经触发过几次事件（不含被跳过的不适用事件）。</summary>
        public int EventTriggerCount { get; private set; }

        /// <summary>
        /// 挂上本关启用的事件池。和 <see cref="AttachRule"/> 一样，Start 之前调最自然；
        /// Start 之后再调也能用（会立刻从当前回合开始排下一次）。
        ///
        /// 传 null / 空数组 = 这一关没有事件（<see cref="EventsEnabled"/> 为 false，什么都不发生）。
        /// </summary>
        public void AttachEventPool(IEnumerable<IRoundEvent> pool)
        {
            _eventPool.Clear();
            if (pool != null)
            {
                foreach (var e in pool)
                {
                    if (e == null) continue;
                    _eventPool.Add(e);
                }
            }

            // 已经开局了才排；没开局的话 Start 会排。
            ScheduleFirstEventIfNeeded();
        }

        // ---------------------------------------------------------------- 调度

        /// <summary>
        /// 排第一次事件：**第 1 回合不触发**，所以第一次落在 `1 + [min,max]`（默认 4~7 回合）。
        /// 之后每次触发都重新随机间隔（见 <see cref="FireRoundEvent"/>）。
        /// </summary>
        void ScheduleFirstEventIfNeeded()
        {
            if (!EventsEnabled) return;
            if (_nextEventRound > 0) return;
            if (Round <= 0) return; // 还没开局：Start 末尾会调

            _nextEventRound = Round + NextEventInterval();
        }

        /// <summary>本次间隔（含端点的均匀分布）。参数在 <see cref="Rules.EventIntervalMin/Max"/>。</summary>
        int NextEventInterval()
        {
            int min = Math.Max(1, Rules.EventIntervalMin);
            int max = Math.Max(min, Rules.EventIntervalMax);
            return min + _eventRng.Next(max - min + 1);
        }

        /// <summary>
        /// 回合开始（准备阶段的第一步、**关卡机制之前**）抽并生效一条事件。
        /// 没有可用事件时只记一条日志，不打断回合。
        /// </summary>
        void FireRoundEvent()
        {
            if (!EventsEnabled) return;

            // 先排下一次：哪怕下面 Apply 抛异常，本局也不会永远停在第 X 回合。
            _nextEventRound = Round + NextEventInterval();

            var ev = DrawApplicableEvent(out var skipped);
            if (ev == null)
            {
                PushEvent(new GameEvent
                {
                    Type = GameEventType.Message,
                    Text = skipped == null
                        ? "—— 本回合事件：本关没有可抽的事件 ——"
                        : $"—— 本回合事件：没有适用的事件，跳过（{skipped}）——",
                });
                return;
            }

            EventTriggerCount++;
            _drawnEventIds.Add(ev.Id);
            _activeEvents.Add(ev);

            var record = new StepRecord { IsRoundEvent = true };
            record.Events.Add(new GameEvent
            {
                Type = GameEventType.RoundEvent,
                Text = $"—— 回合事件：{ev.DisplayName} —— {ev.Description}",
            });

            _eventRecord = record;
            try
            {
                ev.Apply(this);
            }
            catch (Exception ex)
            {
                // 和机制一样：一条写坏的事件只废掉它自己，不能把回合带崩。
                _activeEvents.Remove(ev);
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.Message,
                    Text = $"事件「{ev.DisplayName}」出错，本回合按没有事件处理：{ex.Message}",
                });
            }
            finally
            {
                _eventRecord = null;
            }

            QueueStep(record);
        }

        /// <summary>
        /// 抽一条**本关适用**的事件：先从「本局没抽过的」里随机，抽空则重洗（全部重新参与）。
        /// 不适用的跳过并返回原因；全都不适用时返回 null。
        ///
        /// ⚠ 不适用的**不会被记进 <see cref="_drawnEventIds"/>**：本关用不了的事件不该被消耗掉，
        /// 以后如果关卡中途改了状态（比如场上出现复合元素）它还能再被抽到。
        /// </summary>
        IRoundEvent DrawApplicableEvent(out string skippedReason)
        {
            skippedReason = null;
            if (!EventsEnabled) return null;

            var candidates = new List<IRoundEvent>();
            foreach (var e in _eventPool)
                if (!_drawnEventIds.Contains(e.Id)) candidates.Add(e);

            if (candidates.Count == 0)
            {
                // 池子抽空了：重洗，全部重新参与（否则后面几回合就没有事件了）
                _drawnEventIds.Clear();
                candidates.AddRange(_eventPool);
            }

            // 用事件自己的随机流洗牌：同种子 + 同一份池子 → 同样的顺序
            Board.Shuffle(candidates, _eventRng);

            var reasons = new List<string>();
            foreach (var e in candidates)
            {
                if (e.IsApplicable(this, out var reason))
                {
                    if (reasons.Count > 0) skippedReason = string.Join("；", reasons);
                    return e;
                }

                if (!string.IsNullOrEmpty(reason)) reasons.Add($"{e.DisplayName}：{reason}");
                else reasons.Add(e.DisplayName);
            }

            if (reasons.Count > 0) skippedReason = string.Join("；", reasons);
            return null;
        }

        /// <summary>判定阶段开头撤销本回合事件的临时效果（例如把瓶子容量改回本关的值）。</summary>
        void RevertRoundEvents()
        {
            if (_activeEvents.Count == 0) return;

            for (int i = _activeEvents.Count - 1; i >= 0; i--)
            {
                var ev = _activeEvents[i];
                try
                {
                    ev.Revert(this);
                }
                catch (Exception ex)
                {
                    PushEvent(new GameEvent
                    {
                        Type = GameEventType.Message,
                        Text = $"事件「{ev.DisplayName}」的恢复步骤出错：{ex.Message}",
                    });
                }
            }

            _activeEvents.Clear();
        }

        /// <summary>开新一局时把事件状态归零（池子本身留着 —— 它是关卡配置，不是局内状态）。</summary>
        void ResetEventState()
        {
            _activeEvents.Clear();
            _drawnEventIds.Clear();
            _nextEventRound = 0;
            EventTriggerCount = 0;
            _eventRecord = null;
            SuppressCreepMovement = false;
            _eventRng = new Random(EventStreamSeed(Seed));
        }

        /// <summary>
        /// 事件随机流的种子：从 <see cref="Seed"/> 派生，故意和玩法随机流错开。
        /// 常数是无意义的质数（只求两股流不相关）。
        /// </summary>
        static int EventStreamSeed(int seed)
        {
            unchecked
            {
                int s = seed * 486187739 + 0x5EED;
                return s == 0 ? 1 : s;
            }
        }

        // ---------------------------------------------------------------- 注入 API（给事件改棋盘用）

        /// <summary>本回合小怪**不移动**（静滞结界）：准备阶段直接跳过移动，复合元素的产出照旧。</summary>
        public bool SuppressCreepMovement { get; private set; }

        public void SetSuppressCreepMovement(bool suppressed) => SuppressCreepMovement = suppressed;

        /// <summary>
        /// 从本关**牌池**里均匀抽 <paramref name="count"/> 张元素牌塞进手牌，返回实际给了几张。
        /// **允许超过手牌上限**（和「瓶子满自动产牌」同一个口径，弃牌阶段玩家自己清）。
        /// </summary>
        public int GiveElementCards(int count)
        {
            if (count <= 0) return 0;

            var pool = Rules.PoolCards;
            if (pool.Length == 0) return 0;

            int given = 0;
            for (int i = 0; i < count; i++)
            {
                var element = pool[_eventRng.Next(pool.Length)];
                DrawCard(element);
                given++;
                AddEventStepEvent(new GameEvent
                {
                    Type = GameEventType.GainCard,
                    Element = element,
                    Text = $"事件给了 1 张{ElementDefs.Name(element)}牌",
                });
            }

            return given;
        }

        /// <summary>
        /// 每种**本关启用的**秘术牌各给 <paramref name="countPerKind"/> 张（正常 1 张融合 + 1 张分解）。
        /// 本关没启用的那种不会给 —— 给了也打不出去（出牌门槛是 Rules.HasSpecialCard）。
        /// </summary>
        public int GiveSpecialCards(int countPerKind)
        {
            if (countPerKind <= 0) return 0;
            if (!Rules.UseCompositeElements) return 0;

            int given = 0;
            var kinds = new[] { CardKind.FusionCard, CardKind.DecomposeCard };
            foreach (var kind in kinds)
            {
                if (!Rules.HasSpecialCard(kind)) continue;

                for (int i = 0; i < countPerKind; i++)
                {
                    DrawSpecialCard(kind);
                    given++;
                    AddEventStepEvent(new GameEvent
                    {
                        Type = GameEventType.GainCard,
                        Text = $"事件给了 1 张{(kind == CardKind.FusionCard ? "融合牌" : "分解牌")}",
                    });
                }
            }

            return given;
        }

        /// <summary>
        /// 本回合把瓶子容量改成 <paramref name="capacity"/>：**先把已经达标的瓶子结账产牌**，再改容量。
        /// 返回这次结账产出的牌数。
        ///
        /// 「结账」= 计数够一个新容量就换一张牌，余数留在瓶里（4/3 → 1 张牌 + 余 1）。
        /// 这样「玉翁敛息」的观感是「葫芦把憋着的气一次性吐出来」，而不是「上限变了但什么也没发生」。
        /// </summary>
        public int ApplyBottleCapacity(int capacity)
        {
            // 本关没有元素瓶（Rules.BottleMode = Disabled）：结不了账也改不了容量，直接当没事发生。
            // 返回 0 = 没结出任何牌，调用方（玉翁敛息）不用再判一次。
            if (!Rules.BottlesEnabled) return 0;

            capacity = Math.Max(1, capacity);

            int cards = 0;
            foreach (var element in ElementDefs.All)
            {
                int produced = Bottle.DrainAt(element, capacity);
                if (produced <= 0) continue;

                for (int i = 0; i < produced; i++)
                {
                    DrawCard(element);
                    AddEventStepEvent(new GameEvent
                    {
                        Type = GameEventType.GainCard,
                        Element = element,
                        Text = $"{ElementDefs.Name(element)}瓶子结账，获得 1 张{ElementDefs.Name(element)}牌",
                    });
                }

                cards += produced;
            }

            Bottle.SetCapacity(capacity);
            return cards;
        }

        /// <summary>把瓶子容量改回本关的值（事件结束时调，不结账、不产牌）。</summary>
        public void RestoreBottleCapacity() => Bottle.SetCapacity(Rules.BottleCapacity);

        /// <summary>
        /// 随机散落 <paramref name="count"/> 格变成 <paramref name="element"/>：
        ///   · 格上已有元素 → **就地转换**（uid / 位置不变，不喂小瓶子，因为元素没有「消失」）
        ///   · 空格 → **在该格生成一只**（走 <see cref="SpawnAt"/>，同样不喂小瓶子）
        ///   · 墙格 / 复合元素占着的格子 → 跳过（不是合法的元素格）
        /// 目标元素不在本关可用元素里时直接返回 0（不变量：棋盘上只出现池内元素）。
        /// </summary>
        public int ConvertCellsToElement(Element element, int count)
        {
            if (count <= 0) return 0;
            if (!Rules.ElementPool.Has(element)) return 0;

            var cells = ConvertibleCells();
            Board.Shuffle(cells, _eventRng);

            int changed = 0;
            for (int i = 0; i < cells.Count && changed < count; i++)
                if (ConvertOneCell(cells[i], element)) changed++;

            return changed;
        }

        /// <summary>
        /// **随机位置的一块 N×N 正方形**里所有格子变成 <paramref name="element"/>（「随机范围事件」用）。
        /// 块的位置用引擎自己的随机流选，保证同种子可复现；块一定完整落在棋盘内。
        /// 返回真的发生变化的格数。
        /// </summary>
        public int ConvertBlockToElement(Element element, int size)
        {
            if (size <= 0) return 0;
            if (!Rules.ElementPool.Has(element)) return 0;

            int w = Math.Min(size, Board.Width);
            int h = Math.Min(size, Board.Height);
            int x = w >= Board.Width ? 0 : _eventRng.Next(Board.Width - w + 1);
            int y = h >= Board.Height ? 0 : _eventRng.Next(Board.Height - h + 1);

            return ConvertRectToElement(element, x, y, w, h);
        }

        /// <summary>
        /// **固定的那一块矩形**里所有格子变成 <paramref name="element"/>（「固定范围事件」用）。
        /// 每次都作用在同一片区域（不像 <see cref="ConvertBlockToElement"/> 那样随机）。
        /// 越界部分会被自动裁掉；墙格 / 复合元素格跳过。返回真的发生变化的格数。
        /// </summary>
        public int ConvertRectToElement(Element element, int x, int y, int width, int height)
        {
            if (width <= 0 || height <= 0) return 0;
            if (!Rules.ElementPool.Has(element)) return 0;

            int changed = 0;
            for (int cy = y; cy < y + height; cy++)
            {
                for (int cx = x; cx < x + width; cx++)
                {
                    var pos = new Pos(cx, cy);
                    if (!Board.InBounds(pos)) continue;
                    if (ConvertOneCell(pos, element)) changed++;
                }
            }

            return changed;
        }

        /// <summary>所有「可以放元素」的格子（界内、不是墙、不是复合元素）。</summary>
        List<Pos> ConvertibleCells()
        {
            var cells = new List<Pos>(Board.Width * Board.Height);
            for (int y = 0; y < Board.Height; y++)
            {
                for (int x = 0; x < Board.Width; x++)
                {
                    var pos = new Pos(x, y);
                    if (!Board.IsWalkable(pos)) continue;   // 墙 / 界外
                    if (Board.HasComposite(pos)) continue;  // 复合元素不是元素格
                    cells.Add(pos);
                }
            }
            return cells;
        }

        /// <summary>
        /// 把一格变成 <paramref name="element"/>：已有元素 → 就地转换；空格 → 生成一只。
        /// 返回这一格算不算「发生了变化」（本来就是这个元素也算占掉一格）。
        /// </summary>
        bool ConvertOneCell(Pos pos, Element element)
        {
            if (!Board.IsWalkable(pos) || Board.HasComposite(pos)) return false;

            var creep = Board.At(pos);
            if (creep != null)
            {
                if (creep.Element != element)
                {
                    var from = creep.Element;
                    creep.Element = element;
                    AddEventStepEvent(new GameEvent
                    {
                        Type = GameEventType.Transform,
                        From = pos,
                        To = pos,
                        Element = from,
                        Uid = creep.Uid,
                        Text = $"事件：{pos} 的{ElementDefs.Name(from)}变成{ElementDefs.Name(element)}",
                    });
                }

                return true;
            }

            var spawned = SpawnAt(pos, element);
            if (spawned == null) return false;

            AddEventStepEvent(new GameEvent
            {
                Type = GameEventType.Spawn,
                From = pos,
                To = pos,
                Element = element,
                Uid = spawned.Uid,
                Text = $"事件：{pos} 是空格，生成了 1 个{ElementDefs.Name(element)}元素",
            });
            return true;
        }

        /// <summary>
        /// 往「本回合事件的步骤」里追加一条事件。
        /// 没有事件步骤时（测试直接调 API）自己排一步，保证表现层照样播得到。
        /// </summary>
        void AddEventStepEvent(GameEvent e)
        {
            PushEvent(e);

            if (_eventRecord != null)
            {
                _eventRecord.Events.Add(e);
                return;
            }

            var record = new StepRecord { IsRoundEvent = true };
            record.Events.Add(e);
            QueueStep(record);
        }
    }
}
