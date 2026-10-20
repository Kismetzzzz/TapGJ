using System;
using System.Collections.Generic;
using System.Text;
using TapGJ.Core;

namespace TapGJ.Playthrough
{
    // 关卡机制的纯 C# 复刻：逻辑与 Assets/Scripts/Levels/Mechanics/*.cs 一一对应，
    // 只是把 MonoBehaviour / Mathf 换成普通类 / Math。数值从预制体 YAML 里读。

    public abstract class RuleBase : ILevelRule
    {
        public string CustomGoalDescription;

        public virtual string GoalDescription => CustomGoalDescription ?? DefaultDescription;
        protected abstract string DefaultDescription { get; }

        public virtual void OnLevelStart(GameEngine engine) { }
        public virtual void OnRoundStart(GameEngine engine) { }
        public virtual void OnEncounter(GameEngine engine, EncounterInfo info) { }

        public virtual bool TryExtraWin(GameEngine engine, out string message)
        {
            message = null;
            return false;
        }

        public virtual bool TryExtraLose(GameEngine engine, out string message)
        {
            message = null;
            return false;
        }

        public virtual GoalProgress GetGoalProgress(GameEngine engine) => default;

        protected static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    public sealed class FusionCountRule : RuleBase
    {
        public int Count = 1;
        protected override string DefaultDescription => $"成功融合 {Count} 次复合元素";

        public override bool TryExtraWin(GameEngine engine, out string message)
        {
            int got = engine?.TotalFusions ?? 0;
            if (got >= Count)
            {
                message = $"胜利：本局已经成功融合 {got} 次（≥ {Count}）";
                return true;
            }
            message = null;
            return false;
        }

        public override GoalProgress GetGoalProgress(GameEngine engine) => new GoalProgress
        {
            Description = GoalDescription,
            Fill = Count <= 0 ? 1f : Clamp01((float)(engine?.TotalFusions ?? 0) / Count),
            Label = $"融合 {engine?.TotalFusions ?? 0} / {Count}",
        };
    }

    public sealed class DecomposeCountRule : RuleBase
    {
        public int Count = 1;
        public bool RequireNoComposites;

        protected override string DefaultDescription => RequireNoComposites
            ? $"分解 {Count} 次复合元素，且场上不再剩复合元素"
            : $"成功分解 {Count} 次复合元素";

        public override bool TryExtraWin(GameEngine engine, out string message)
        {
            if (engine == null) { message = null; return false; }

            int got = engine.TotalDecompositions;
            if (got < Count) { message = null; return false; }
            if (RequireNoComposites && engine.CompositeCount > 0) { message = null; return false; }

            message = RequireNoComposites
                ? $"胜利：已分解 {got} 次，且场上不再有复合元素"
                : $"胜利：本局已经成功分解 {got} 次（≥ {Count}）";
            return true;
        }

        public override GoalProgress GetGoalProgress(GameEngine engine) => new GoalProgress
        {
            Description = GoalDescription,
            Fill = Count <= 0 ? 1f : Clamp01((float)(engine?.TotalDecompositions ?? 0) / Count),
            Label = $"分解 {engine?.TotalDecompositions ?? 0} / {Count} · 场上复合 {engine?.CompositeCount ?? 0}",
        };
    }

    public sealed class ElementKindsRule : RuleBase
    {
        public int Kinds = 2;
        protected override string DefaultDescription => $"场上恰好只剩 {Kinds} 种元素";

        public override bool TryExtraWin(GameEngine engine, out string message)
        {
            if (engine == null) { message = null; return false; }

            int now = engine.Board.DistinctElementCount();
            if (now != Kinds)
            {
                message = null;
                return false;
            }
            message = $"胜利：场上恰好只剩 {now} 种元素";
            return true;
        }

        public override GoalProgress GetGoalProgress(GameEngine engine) => new GoalProgress
        {
            Description = GoalDescription,
            Fill = engine == null ? 0f : Clamp01(1f - Math.Abs(engine.Board.DistinctElementCount() - Kinds) / 5f),
            Label = $"元素种类 {engine?.Board.DistinctElementCount() ?? 0} / {Kinds}",
        };
    }

    public sealed class OnlyTheseElementsRule : RuleBase
    {
        public ElementMask Allowed = ElementMask.None;
        public bool RequireEachAllowedPresent = true;

        protected override string DefaultDescription =>
            $"场上只剩「{Allowed.Names()}」" + (RequireEachAllowedPresent ? "，且每种都有" : "");

        public override bool TryExtraWin(GameEngine engine, out string message)
            => TryWin(engine, Allowed, RequireEachAllowedPresent, out message);

        public static bool TryWin(GameEngine engine, ElementMask allowed, bool requireEach, out string message)
        {
            message = null;
            if (engine == null || allowed == ElementMask.None) return false;

            int allowedOnBoard = 0;
            var leftovers = new StringBuilder();

            foreach (var e in ElementDefs.All)
            {
                int n = engine.Board.CountOf(e);
                if (allowed.Has(e))
                {
                    allowedOnBoard += n;
                    if (n == 0 && requireEach) return false;
                    continue;
                }
                if (n <= 0) continue;
                if (leftovers.Length > 0) leftovers.Append('、');
                leftovers.Append(ElementDefs.Name(e)).Append(n);
            }

            if (leftovers.Length > 0) { message = $"还没清掉：{leftovers}"; return false; }
            if (allowedOnBoard <= 0) { message = "场上一个允许的元素都没有"; return false; }

            message = $"胜利：场上只剩「{allowed.Names()}」";
            return true;
        }

        public override GoalProgress GetGoalProgress(GameEngine engine)
        {
            if (engine == null) return default;
            int allowed = 0, forbidden = 0;
            foreach (var e in ElementDefs.All)
            {
                int n = engine.Board.CountOf(e);
                if (Allowed.Has(e)) allowed += n; else forbidden += n;
            }
            bool won = TryWin(engine, Allowed, RequireEachAllowedPresent, out _);
            int visible = allowed + forbidden;
            return new GoalProgress
            {
                Description = GoalDescription,
                Fill = won ? 1f : (visible <= 0 ? 0f : Clamp01((float)allowed / visible)),
                Label = $"允许 {allowed} / 场上 {visible}",
            };
        }
    }

    public sealed class EqualElementCountsRule : RuleBase
    {
        public bool RequireAllPoolElements;
        public int Tolerance;

        protected override string DefaultDescription => RequireAllPoolElements
            ? "场上每种可用元素都有，且数量都相等"
            : "场上出现过的元素数量都相等（只剩一种也算）";

        public override bool TryExtraWin(GameEngine engine, out string message)
        {
            if (!IsBalanced(engine, RequireAllPoolElements, Tolerance, out var detail)) { message = null; return false; }
            message = $"胜利：{detail}";
            return true;
        }

        public static bool IsBalanced(GameEngine engine, bool requireAll, int tolerance, out string detail)
        {
            detail = null;
            if (engine == null) return false;

            var considered = Considered(engine, requireAll);
            if (considered.Count == 0) { detail = "场上没有元素"; return false; }

            int min = int.MaxValue, max = 0, total = 0;
            foreach (var e in considered)
            {
                int c = engine.Board.CountOf(e);
                if (c <= 0 && requireAll) { detail = $"还缺{ElementDefs.Name(e)}元素"; return false; }
                if (c < min) min = c;
                if (c > max) max = c;
                total += c;
            }

            if (total <= 0) { detail = "场上没有元素"; return false; }
            if (max - min > tolerance)
            {
                detail = $"最多 {max} 个、最少 {min} 个，差 {max - min}（允许 {tolerance}）";
                return false;
            }

            detail = considered.Count == 1 ? $"只剩{ElementDefs.Name(considered[0])}元素" : "每种元素数量都相等";
            return true;
        }

        public static List<Element> Considered(GameEngine engine, bool requireAll)
        {
            var list = new List<Element>(5);
            foreach (var e in ElementDefs.All)
            {
                bool include = requireAll ? engine.Rules.ElementPool.Has(e) : engine.Board.CountOf(e) > 0;
                if (include) list.Add(e);
            }
            return list;
        }

        public override GoalProgress GetGoalProgress(GameEngine engine)
        {
            if (engine == null) return default;
            var considered = Considered(engine, RequireAllPoolElements);
            if (considered.Count == 0 || engine.CreepCount == 0) return default;

            int min = int.MaxValue, max = 0;
            foreach (var e in considered)
            {
                int c = engine.Board.CountOf(e);
                if (c < min) min = c;
                if (c > max) max = c;
            }
            bool won = IsBalanced(engine, RequireAllPoolElements, Tolerance, out _);
            return new GoalProgress
            {
                Description = GoalDescription,
                Fill = won ? 1f : Clamp01(1f - (float)(max - min) / Math.Max(1, max)),
                Label = $"差 {max - min}",
            };
        }
    }

    public sealed class ReactionCountRule : RuleBase
    {
        public int Target = 8;
        public bool PlayerCardsOnly;
        int _count;

        protected override string DefaultDescription => $"累计完成 {Target} 次相生转换";

        public override void OnLevelStart(GameEngine engine) => _count = 0;

        public override void OnEncounter(GameEngine engine, EncounterInfo info)
        {
            if (!info.IsGeneration) return;
            if (PlayerCardsOnly && !info.IsPlayerCard) return;
            _count++;
        }

        public override bool TryExtraWin(GameEngine engine, out string message)
        {
            if (_count < Target) { message = null; return false; }
            message = $"胜利：累计完成 {_count} 次相生转换（目标 {Target} 次）";
            return true;
        }

        public int Count => _count;

        public override GoalProgress GetGoalProgress(GameEngine engine) => new GoalProgress
        {
            Description = GoalDescription,
            Fill = Clamp01((float)_count / Math.Max(1, Target)),
            Label = $"相生转换 {_count} / {Target}",
        };
    }

    public sealed class SpawnOnRoundRule : RuleBase
    {
        public Element Element = Element.Fire;
        public int EveryRounds = 3;
        public int CountPerTime = 1;
        public bool Announce = true;

        protected override string DefaultDescription =>
            $"每 {EveryRounds} 回合场上多出 {CountPerTime} 个{ElementDefs.Name(Element)}元素";

        public override void OnRoundStart(GameEngine engine)
        {
            if (EveryRounds <= 0) return;
            if (engine.Round % EveryRounds != 0) return;

            for (int i = 0; i < CountPerTime; i++)
            {
                var cell = engine.RandomEmptyCell();
                if (cell.IsNone) return;
                engine.SpawnAt(cell, Element);
            }
        }
    }

    public sealed class BottleProducedRule : RuleBase
    {
        public Element Element = Element.Fire;
        public int Count = 5;

        protected override string DefaultDescription =>
            $"让{ElementDefs.Name(Element)}瓶子产出 {Count} 张{ElementDefs.Name(Element)}牌";

        public override bool TryExtraWin(GameEngine engine, out string message)
        {
            message = null;
            if (engine == null) return false;
            int got = engine.Bottle.CardsGranted(Element);
            if (got < Count) return false;
            message = $"胜利：{ElementDefs.Name(Element)}瓶子已产出 {got} 张牌（≥ {Count}）";
            return true;
        }

        public override GoalProgress GetGoalProgress(GameEngine engine) => new GoalProgress
        {
            Description = GoalDescription,
            Fill = engine == null ? 0f : Clamp01((float)engine.Bottle.CardsGranted(Element) / Math.Max(1, Count)),
            Label = $"{ElementDefs.Name(Element)}牌 {engine?.Bottle.CardsGranted(Element) ?? 0} / {Count}"
                    + $"（瓶 {engine?.Bottle.Count(Element) ?? 0}/{engine?.Bottle.Capacity ?? 0}）",
        };
    }

    public static class MechanicFactory
    {
        static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "05b337849efd4e2bb7da59347630c383", "FusionCountRule" },
            { "7aeeef02496d4118a03d21ecfe7c8483", "DecomposeCountRule" },
            { "d5caae504bc34abaacc11d1b32ee0a82", "ElementKindsRule" },
            { "aece636443eafb746be5b2e4bf8a9b60", "EqualElementCountsRule" },
            { "1a02dd0b86af451a81dc8ef032f6e1ca", "OnlyTheseElementsRule" },
            { "30bf9b9789e706e44915ce54321cef53", "ReactionCountRule" },
            { "220f8259cd7ac264a8527d7f0275927a", "SpawnOnRoundRule" },
            { "c273c1672ed44f5bac7ea9d7512ce9ec", "BottleProducedRule" },
        };

        public static string NameOf(string scriptGuid)
            => scriptGuid != null && Names.TryGetValue(scriptGuid, out var n) ? n : "?";

        public static Func<ILevelRule> Create(string scriptGuid, YamlDoc doc)
        {
            if (scriptGuid == null) return null;

            switch (scriptGuid)
            {
                case "05b337849efd4e2bb7da59347630c383":
                    return () => new FusionCountRule { Count = doc.Int("count", 1), CustomGoalDescription = doc.Str("goalDescription") };

                case "7aeeef02496d4118a03d21ecfe7c8483":
                    return () => new DecomposeCountRule
                    {
                        Count = doc.Int("count", 1),
                        RequireNoComposites = doc.Bool("requireNoComposites"),
                        CustomGoalDescription = doc.Str("goalDescription"),
                    };

                case "d5caae504bc34abaacc11d1b32ee0a82":
                    return () => new ElementKindsRule { Kinds = doc.Int("kinds", 2), CustomGoalDescription = doc.Str("goalDescription") };

                case "aece636443eafb746be5b2e4bf8a9b60":
                    return () => new EqualElementCountsRule
                    {
                        RequireAllPoolElements = doc.Bool("requireAllPoolElements"),
                        Tolerance = doc.Int("tolerance"),
                        CustomGoalDescription = doc.Str("goalDescription"),
                    };

                case "1a02dd0b86af451a81dc8ef032f6e1ca":
                    return () => new OnlyTheseElementsRule
                    {
                        Allowed = (ElementMask)doc.Int("allowed"),
                        RequireEachAllowedPresent = doc.Bool("requireEachAllowedPresent", true),
                        CustomGoalDescription = doc.Str("goalDescription"),
                    };

                case "30bf9b9789e706e44915ce54321cef53":
                    return () => new ReactionCountRule
                    {
                        Target = doc.Int("target", 8),
                        PlayerCardsOnly = doc.Bool("playerCardsOnly"),
                        CustomGoalDescription = doc.Str("goalDescription"),
                    };

                case "220f8259cd7ac264a8527d7f0275927a":
                    return () => new SpawnOnRoundRule
                    {
                        Element = (Element)doc.Int("element", 3),
                        EveryRounds = doc.Int("everyRounds", 3),
                        CountPerTime = doc.Int("countPerTime", 1),
                        Announce = doc.Bool("announce", true),
                        CustomGoalDescription = doc.Str("goalDescription"),
                    };

                case "c273c1672ed44f5bac7ea9d7512ce9ec":
                    return () => new BottleProducedRule
                    {
                        Element = (Element)doc.Int("element", 3),
                        Count = doc.Int("count", 5),
                        CustomGoalDescription = doc.Str("goalDescription"),
                    };

                default:
                    return null;
            }
        }
    }
}
