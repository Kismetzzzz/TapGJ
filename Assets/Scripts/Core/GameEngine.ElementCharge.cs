using System;

namespace TapGJ.Core
{
    public sealed partial class GameEngine
    {
        /// <summary>事件或关卡机制增加元素收集进度，满额发牌并保留余量。</summary>
        public void AddBottleCharge(Element element, int amount)
        {
            if ((int)element < 0 || (int)element >= ElementDefs.Count)
                throw new ArgumentOutOfRangeException(nameof(element));
            if (!Rules.ElementPool.Has(element)) throw new ArgumentException("本关不可收集该元素", nameof(element));
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount == 0) return;
            var record = _eventRecord ?? new StepRecord { Element = element };
            FillBottle(element, amount, record, Pos.None);
            if (_eventRecord == null) QueueStep(record);
        }

        /// <summary>事件临时修改本回合集齐门槛；下回合恢复关卡配置，不丢失已收集的点数。</summary>
        public void SetBottleCapacityForRound(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (capacity != Bottle.Capacity) ChangeBottleCapacity(capacity);
        }

        int ChangeBottleCapacity(int capacity, bool redeem = true)
        {
            if (!Rules.BottlesEnabled) return 0;
            Bottle.SetCapacity(capacity);
            var record = _eventRecord ?? new StepRecord();
            int firstChange = record.Events.Count;
            int cards = 0;
            foreach (var element in ElementDefs.All)
            {
                while (redeem && Bottle.Redeem(element))
                {
                    record.Events.Add(BottleFillEvent(element, 0, capacity, Pos.None));
                    GrantBottleCard(element, record);
                    cards++;
                }
                record.Events.Add(new GameEvent
                {
                    Type = GameEventType.BottleCapacityChanged,
                    Element = element,
                    BottleCapacity = capacity,
                    BottleCount = Bottle.Count(element),
                    Text = $"{ElementDefs.Name(element)}元素集齐门槛改为 {capacity}",
                });
            }
            for (int i = firstChange; i < record.Events.Count; i++) PushEvent(record.Events[i]);
            if (_eventRecord == null) QueueStep(record);
            return cards;
        }

        void FillBottle(Element element, int amount, StepRecord record, Pos where)
        {
            if (!Rules.BottlesEnabled) return;
            while (amount > 0)
            {
                // 恢复较低的基准门槛时先保留进度，下次充能再结清已有份数。
                int part = Math.Min(amount, Math.Max(0, Bottle.Capacity - Bottle.Count(element)));
                bool completed = Bottle.Add(element, part);
                record.Events.Add(BottleFillEvent(element, part, completed ? Bottle.Capacity : Bottle.Count(element), where));
                if (completed) GrantBottleCard(element, record);
                amount -= part;
            }
        }

        GameEvent BottleFillEvent(Element element, int amount, int count, Pos where) => new GameEvent
        {
            Type = GameEventType.BottleFill,
            From = where,
            Element = element,
            Amount = amount,
            BottleCount = count,
            BottleCapacity = Bottle.Capacity,
            Text = $"{ElementDefs.Name(element)}元素收集 +{amount}（{count}/{Bottle.Capacity}）",
        };

        void GrantBottleCard(Element element, StepRecord record)
        {
            var card = DrawCard(element);
            record.Events.Add(new GameEvent
            {
                Type = GameEventType.GainCard,
                Element = element,
                CardId = card.Id,
                BottleCapacity = Bottle.Capacity,
                Text = $"{ElementDefs.Name(element)}元素集齐，获得 1 张{ElementDefs.Name(element)}牌",
            });
        }
    }
}
