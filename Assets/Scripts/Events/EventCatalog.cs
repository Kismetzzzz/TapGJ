using System.Collections.Generic;
using UnityEngine;

namespace TapGJ.Events
{
    /// <summary>
    /// 全部回合事件的目录（一个资产，列出所有事件定义）。
    ///
    /// 它不是运行期必需品（关卡自己用 `roundEvents` 直接引用事件资产），存在的理由是两个**人**的需求：
    ///   · 想「一眼看全有哪几条事件」时不用翻文件夹；
    ///   · 菜单 TapGJ ▸ 校验所有事件 用它检查重复 Id / 空槽位。
    /// </summary>
    [CreateAssetMenu(fileName = "EventCatalog", menuName = "TapGJ/事件目录 (EventCatalog)", order = 2)]
    public sealed class EventCatalog : ScriptableObject
    {
        [Tooltip("全部回合事件（顺序无关；关卡从这里挑自己要的那几条）")]
        public RoundEventDefinition[] events = new RoundEventDefinition[0];

        /// <summary>收集目录本身的问题（空槽位 / 重复 Id）。空列表 = 没问题。</summary>
        public void CollectIssues(List<string> into)
        {
            if (into == null) return;

            if (events == null || events.Length == 0)
            {
                into.Add("事件目录是空的：一条事件都没有。");
                return;
            }

            var seen = new HashSet<string>();
            for (int i = 0; i < events.Length; i++)
            {
                var e = events[i];
                if (e == null)
                {
                    into.Add($"目录第 {i + 1} 项是空槽位。");
                    continue;
                }

                if (!seen.Add(e.Id))
                    into.Add($"事件 Id「{e.Id}」在目录里出现了两次（{e.name}）—— 去重的账本按 Id 记，重了会被当成同一条。");
            }
        }

        /// <summary>目录里所有事件（跳过 None 洞）。</summary>
        public IEnumerable<RoundEventDefinition> All()
        {
            if (events == null) yield break;
            foreach (var e in events)
                if (e != null) yield return e;
        }
    }
}
