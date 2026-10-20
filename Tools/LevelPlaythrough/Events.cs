using System;
using System.Collections.Generic;
using TapGJ.Core;

namespace TapGJ.Playthrough
{
    // 回合事件的纯 C# 复刻（对应 Assets/Scripts/Events/*.cs）。

    public abstract class EventBase : IRoundEvent
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }

        public virtual bool IsApplicable(GameEngine engine, out string reason)
        {
            reason = null;
            return true;
        }

        public abstract void Apply(GameEngine engine);
        public virtual void Revert(GameEngine engine) { }
    }

    public sealed class GiveElementCardsEvent : EventBase
    {
        public int Count = 3;

        public override bool IsApplicable(GameEngine engine, out string reason)
        {
            if (engine.Rules.PoolCards.Length > 0) { reason = null; return true; }
            reason = "本关牌池里一张元素牌都没有";
            return false;
        }

        public override void Apply(GameEngine engine) => engine.GiveElementCards(Count);
    }

    public sealed class GiveSpecialCardsEvent : EventBase
    {
        public int CountPerKind = 1;

        public override bool IsApplicable(GameEngine engine, out string reason)
        {
            if (!engine.Rules.UseCompositeElements) { reason = "本关没开复合元素"; return false; }
            if (!engine.Rules.HasSpecialCard(CardKind.FusionCard) || !engine.Rules.HasSpecialCard(CardKind.DecomposeCard))
            {
                reason = "融合牌与分解牌没有都启用";
                return false;
            }
            reason = null;
            return true;
        }

        public override void Apply(GameEngine engine) => engine.GiveSpecialCards(CountPerKind);
    }

    public sealed class BottleCapacityEvent : EventBase
    {
        public int Capacity = 3;

        public override bool IsApplicable(GameEngine engine, out string reason)
        {
            if (!engine.Rules.BottlesEnabled) { reason = "本关没有元素瓶"; return false; }
            if (Capacity < 1) { reason = "容量非法"; return false; }
            if (engine.Rules.BottleCapacity == Capacity) { reason = "容量本来就等于目标值"; return false; }
            reason = null;
            return true;
        }

        public override void Apply(GameEngine engine) => engine.ApplyBottleCapacity(Capacity);
        public override void Revert(GameEngine engine) => engine.RestoreBottleCapacity();
    }

    public sealed class SuppressMovementEvent : EventBase
    {
        public override bool IsApplicable(GameEngine engine, out string reason)
        {
            if (engine.Board.CreepCount() > 0) { reason = null; return true; }
            reason = "场上一个小怪都没有";
            return false;
        }

        public override void Apply(GameEngine engine) => engine.SetSuppressCreepMovement(true);
        public override void Revert(GameEngine engine) => engine.SetSuppressCreepMovement(false);
    }

    public sealed class ConvertCellsEvent : EventBase
    {
        public Element Element = Element.Metal;
        public int Mode;            // 0 Scatter / 1 RandomBlock / 2 FixedRect
        public int CellCount = 9;
        public int BlockSize = 3;
        public int RectX = 2, RectY = 2, RectWidth = 3, RectHeight = 3;

        public override bool IsApplicable(GameEngine engine, out string reason)
        {
            if (engine.Rules.ElementPool.Has(Element)) { reason = null; return true; }
            reason = $"本关可用元素只有「{engine.Rules.ElementPool.Names()}」";
            return false;
        }

        public override void Apply(GameEngine engine)
        {
            switch (Mode)
            {
                case 1: engine.ConvertBlockToElement(Element, BlockSize); break;
                case 2: engine.ConvertRectToElement(Element, RectX, RectY, RectWidth, RectHeight); break;
                default: engine.ConvertCellsToElement(Element, CellCount); break;
            }
        }
    }

    public static class EventFactory
    {
        public static Func<IRoundEvent> Create(string scriptGuid, YamlDoc doc)
        {
            if (scriptGuid == null) return null;

            EventBase Make(EventBase e)
            {
                e.Id = doc.Str("eventId") ?? doc.Str("m_Name");
                e.DisplayName = doc.Str("displayName") ?? doc.Str("m_Name");
                e.Description = doc.Str("description");
                return e;
            }

            switch (scriptGuid)
            {
                case "60d920e62cbd4c7c87580612fcd32b69":
                    return () => Make(new GiveElementCardsEvent { Count = doc.Int("count", 3) });

                case "82149325f7194b02b3e22829feefe8bd":
                    return () => Make(new GiveSpecialCardsEvent { CountPerKind = doc.Int("countPerKind", 1) });

                case "53c9767a3f824dbb813d5bfef4c16591":
                    return () => Make(new BottleCapacityEvent { Capacity = doc.Int("capacity", 3) });

                case "da759a9e1c5b411582a319e3c94d5dc0":
                    return () => Make(new SuppressMovementEvent());

                case "91f074e2750445e0a26e9d2061038bd4":
                    return () => Make(new ConvertCellsEvent
                    {
                        Element = (Element)doc.Int("element"),
                        Mode = doc.Int("mode"),
                        CellCount = doc.Int("cellCount", 9),
                        BlockSize = doc.Int("blockSize", 3),
                        RectX = doc.Int("rectX", 2),
                        RectY = doc.Int("rectY", 2),
                        RectWidth = doc.Int("rectWidth", 3),
                        RectHeight = doc.Int("rectHeight", 3),
                    });

                default:
                    return null;
            }
        }
    }
}
