using System;
using System.Collections.Generic;

namespace TapGJ.Core
{
    /// <summary>
    /// 关卡机制的挂载点与「注入 API」。
    ///
    /// 分成 partial 是为了让 GameEngine.cs 只保留玩法主流程：这里全是给
    /// ILevelRule 用的东西（挂钩子、发通知、改棋盘）。
    ///
    /// 时序：
    ///   LevelLoader 先 AttachRule(...) 一批 → 再 Start(setup)（铺盘 + 发牌）
    ///   → FireLevelStart → BeginRound → FireRoundStart（第 1 回合）
    /// 如果钩子是 Start 之后才挂上来的，会立刻补发一次 OnLevelStart。
    /// </summary>
    public sealed partial class GameEngine
    {
        readonly List<ILevelRule> _levelRules = new List<ILevelRule>();
        bool _levelStarted;

        /// <summary>当前挂着的关卡机制（顺序 = 调用顺序；抛过异常的会被置为 null）。</summary>
        public IReadOnlyList<ILevelRule> LevelRules => _levelRules;

        // ---------------------------------------------------------------- 挂载

        /// <summary>挂一个机制。Start 之前挂会等 Start 结束后再回调 OnLevelStart。</summary>
        public void AttachRule(ILevelRule rule)
        {
            if (rule == null) return;
            _levelRules.Add(rule);
            if (_levelStarted) Guard(_levelRules.Count - 1, r => r.OnLevelStart(this));
        }

        public void AttachRules(IEnumerable<ILevelRule> rules)
        {
            if (rules == null) return;
            foreach (var rule in rules) AttachRule(rule);
        }

        public void ClearRules()
        {
            _levelRules.Clear();
            _levelStarted = false;
        }

        // ---------------------------------------------------------------- 生命周期通知

        void FireLevelStart()
        {
            _levelStarted = true;
            for (int i = 0; i < _levelRules.Count; i++)
                Guard(i, r => r.OnLevelStart(this));
        }

        void FireRoundStart()
        {
            for (int i = 0; i < _levelRules.Count; i++)
                Guard(i, r => r.OnRoundStart(this));
        }

        void FireEncounter(EncounterInfo info)
        {
            for (int i = 0; i < _levelRules.Count; i++)
                Guard(i, r => r.OnEncounter(this, info));
        }

        /// <summary>
        /// 跑一个钩子，抛异常就把它摘掉（记一条事件）。
        /// 不这么做的后果是：一个写坏的机制会在每回合、每次相遇时刷屏，还可能把主循环带崩。
        /// </summary>
        void Guard(int index, Action<ILevelRule> call)
        {
            var rule = _levelRules[index];
            if (rule == null) return;

            try
            {
                call(rule);
            }
            catch (Exception ex)
            {
                _levelRules[index] = null;
                PushEvent(new GameEvent
                {
                    Type = GameEventType.Message,
                    Text = $"关卡机制 {rule.GetType().Name} 出错，已停用：{ex.Message}",
                });
            }
        }

        // ---------------------------------------------------------------- 判定钩子

        /// <summary>
        /// 问所有机制「算不算赢」。
        ///
        /// 两种语义由 <see cref="Rules.RequireAllWinRules"/> 决定：
        ///   · 默认（false）：**任一**返回 true 即赢（第一个生效）
        ///   · 勾上（true）：**全部**返回 true 才赢（用来表达「A 且 B」型目标）
        ///
        /// 没有挂任何机制时一律返回 false（`Custom` 目标没挂机制的情况在关卡校验里已经报错了）。
        /// </summary>
        bool CheckExtraWin(out string reason)
        {
            bool requireAll = Rules.RequireAllWinRules;
            bool anyEvaluated = false;
            string firstMessage = null;

            for (int i = 0; i < _levelRules.Count; i++)
            {
                var rule = _levelRules[i];
                if (rule == null) continue;

                bool won;
                string message;
                try
                {
                    won = rule.TryExtraWin(this, out message);
                }
                catch (Exception ex)
                {
                    _levelRules[i] = null;
                    PushEvent(new GameEvent
                    {
                        Type = GameEventType.Message,
                        Text = $"关卡机制 {rule.GetType().Name} 出错，已停用：{ex.Message}",
                    });
                    continue;
                }

                anyEvaluated = true;

                if (!requireAll)
                {
                    if (!won) continue;
                    reason = string.IsNullOrEmpty(message) ? "胜利：关卡机制判定达成" : message;
                    return true;
                }

                // AND：有一个没满足就不算赢（直接返回，不用等其它机制）
                if (!won)
                {
                    reason = null;
                    return false;
                }

                if (firstMessage == null && !string.IsNullOrEmpty(message)) firstMessage = message;
            }

            reason = null;
            if (!requireAll || !anyEvaluated) return false;

            reason = firstMessage ?? "胜利：所有关卡条件都已满足";
            return true;
        }

        /// <summary>问所有机制「算不算输」。第一个返回 true 的生效。</summary>
        bool CheckExtraLose(out string reason)
        {
            for (int i = 0; i < _levelRules.Count; i++)
            {
                var rule = _levelRules[i];
                if (rule == null) continue;

                bool lost;
                string message;
                try
                {
                    lost = rule.TryExtraLose(this, out message);
                }
                catch (Exception ex)
                {
                    _levelRules[i] = null;
                    PushEvent(new GameEvent
                    {
                        Type = GameEventType.Message,
                        Text = $"关卡机制 {rule.GetType().Name} 出错，已停用：{ex.Message}",
                    });
                    continue;
                }

                if (!lost) continue;
                reason = string.IsNullOrEmpty(message) ? "失败：关卡机制判定未达成" : message;
                return true;
            }

            reason = null;
            return false;
        }

        /// <summary>
        /// 自定义目标的文案：把所有给了描述的机制**连起来**，而不是「第一个说了算」。
        ///
        /// 为什么改：多目标关卡（「金瓶 10 张 **且** 木瓶 10 张」「五种瓶子各 5 张」）
        /// 只报第一条的话，HUD 左上角就只写了一个目标，玩家会以为只要干那一件事 —— 测试反馈过这一点。
        ///
        /// 连接词按语义取：<see cref="Rules.RequireAllWinRules"/>（且）用「且」，否则（或）用「或」。
        /// 重复的文案（同一个机制挂了两份）只留一份。
        /// </summary>
        string CustomGoalDescription()
        {
            var parts = new List<string>();
            foreach (var rule in _levelRules)
            {
                if (rule == null) continue;

                var text = rule.GoalDescription;
                if (string.IsNullOrEmpty(text)) continue;
                if (!parts.Contains(text)) parts.Add(text);
            }

            if (parts.Count == 0) return null;
            return string.Join(Rules.RequireAllWinRules ? "　且　" : "　或　", parts);
        }

        // ---------------------------------------------------------------- 注入 API（给机制改棋盘用）

        /// <summary>
        /// 在指定空格放一只元素。格子越界 / 是墙 / 已被占 / **该元素不在本关可用元素里** → 返回 null（不抛异常）。
        /// 走的是和反应生成完全相同的路径，所以也会计入 SpawnedCounts。
        /// </summary>
        public Creep SpawnAt(Pos cell, Element element)
        {
            if (!Rules.ElementPool.Has(element)) return null;
            if (!Board.IsEmpty(cell)) return null;

            var creep = new Creep(_nextUid++, element, cell);
            _creeps[creep.Uid] = creep;
            Board.Put(creep);

            TotalSpawned++;
            _spawned[element] = SpawnedCounts.TryGetValue(element, out var n) ? n + 1 : 1;
            return creep;
        }

        /// <summary>
        /// 让一只小怪消失（按 uid）。走的是正常移除路径，所以小瓶子照常 +1、
        /// 满了照样产牌 —— 机制不该绕过资源循环。
        /// </summary>
        public bool DestroyCreep(int uid)
        {
            if (!_creeps.TryGetValue(uid, out var creep) || !creep.Alive) return false;

            var record = new StepRecord
            {
                Uid = uid,
                Element = creep.Element,
                From = creep.Pos,
                To = creep.Pos,
            };
            RemoveCreep(creep, record, creep.Pos);
            // RemoveCreep 会先塞进「瓶子 +1」之类的事件，这条移除说明放在最前面更好读。
            record.Events.Insert(0, new GameEvent
            {
                Type = GameEventType.Vanish,
                From = creep.Pos,
                Element = creep.Element,
                Uid = uid,
                Text = $"{ElementDefs.Name(creep.Element)}元素被关卡机制移除",
            });
            QueueStep(record);
            return true;
        }

        /// <summary>让某格上的小怪消失。返回它原来的元素（没东西则返回 false）。</summary>
        public bool DestroyCreepAt(Pos pos, out Element element)
        {
            var creep = Board.At(pos);
            element = creep?.Element ?? Element.Metal;
            return creep != null && DestroyCreep(creep.Uid);
        }

        /// <summary>临时加 / 拆一堵墙（机制做地形变化用）。墙格上下不能放元素，放上去的会被拒绝。</summary>
        public void BlockCell(Pos cell, bool blocked = true) => Board.SetBlocked(cell, blocked);

        /// <summary>
        /// 把一个自定义步骤排进表现层队列（机制触发的效果也能播动画）。
        /// 步骤号由引擎分配，不要自己设 Index。
        /// </summary>
        public void QueueStep(StepRecord record)
        {
            if (record == null) return;
            record.Index = _stepIndex++;
            foreach (var e in record.Events) e.StepIndex = record.Index;
            Enqueue(record);
        }

        /// <summary>
        /// 排一条「只有一句话」的步骤：机制想告诉玩家发生了什么，又不改变棋盘时用。
        /// 它会和普通步骤一样被逐条播出来，所以不会插队到动画中间。
        /// </summary>
        public StepRecord QueueNotice(string text, Pos at, Element element)
        {
            var record = new StepRecord
            {
                IsDrawPhase = false,
                From = at,
                To = at,
                Element = element,
            };
            record.Events.Add(new GameEvent
            {
                Type = GameEventType.Message,
                From = at,
                Element = element,
                Text = text,
            });
            QueueStep(record);
            return record;
        }

        /// <summary>随机挑一个空格（用引擎自己的随机源，保证同一个种子可复现）。棋盘满了返回 Pos.None。</summary>
        public Pos RandomEmptyCell()
        {
            var free = new List<Pos>();
            for (int y = 0; y < Board.Height; y++)
                for (int x = 0; x < Board.Width; x++)
                {
                    var p = new Pos(x, y);
                    if (Board.IsEmpty(p)) free.Add(p);
                }

            if (free.Count == 0) return Pos.None;
            return free[_rng.Next(free.Count)];
        }
    }
}
