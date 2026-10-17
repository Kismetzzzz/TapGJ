using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Events
{
    /// <summary>
    /// 「X 流漫溢」：本回合随机散落 N 格变成指定元素（图上的表是 9 格，五行各一条 = 5 个资产）。
    ///
    /// 语义（按设计确认过的版本）：
    ///   · **随机散落**，不要求 3×3 连通；
    ///   · 格上已经有元素 → **就地转换**（uid / 位置不变，**不喂瓶子**：元素没有「消失」）；
    ///   · 格是**空**的 → **在该格生成一只**（所以这条事件最多能凭空 +9 个元素，很强）；
    ///   · 墙格 / 复合元素占着的格子 → 跳过（不是合法的元素格）。
    ///
    /// ⚠ 目标元素不在本关 `ElementPool` 里时整条事件**不适用**（会被跳过并换下一条）：
    ///   往棋盘里塞池外元素会打破 README 里那条硬不变量「棋盘上永远只出现池内元素」。
    /// </summary>
    [CreateAssetMenu(fileName = "Event_流漫溢", menuName = "TapGJ/事件/流漫溢（转换为某元素）(ConvertCells)", order = 24)]
    public sealed class Event_ConvertCells : RoundEventDefinition
    {
        [Tooltip("变成哪种元素（一条流漫溢 = 一个元素，五行各建一个资产）")]
        [SerializeField] Element element = Element.Metal;

        [Tooltip("随机散落几格（图上的表写的是 9）")]
        [SerializeField, Min(1)] int cellCount = 9;

        public Element TargetElement => element;

        public override bool IsApplicable(GameEngine engine, out string reason)
        {
            if (engine.Rules.ElementPool.Has(element))
            {
                reason = null;
                return true;
            }

            reason = $"本关可用元素只有「{engine.Rules.ElementPool.Names()}」，"
                     + $"往棋盘上塞{ElementDefs.Name(element)}会打破「只出现本关可用元素」这条不变量";
            return false;
        }

        public override void Apply(GameEngine engine) => engine.ConvertCellsToElement(element, cellCount);
    }
}
