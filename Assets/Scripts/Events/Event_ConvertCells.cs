using TapGJ.Core;
using UnityEngine;

namespace TapGJ.Events
{
    /// <summary>
    /// 转换区域的三种取法（「固定范围事件」与「随机范围事件」的区别就在这里）。
    /// </summary>
    public enum ConvertAreaMode
    {
        /// <summary>随机散落 N 格（不要求连通）。默认，也是最早那一版。</summary>
        Scatter = 0,

        /// <summary>随机位置的一块 N×N 正方形 —— 每次的范围不一样。</summary>
        RandomBlock = 1,

        /// <summary>**固定**的一块矩形 —— 每次都在同一片区域（「固定范围事件」）。</summary>
        FixedRect = 2,
    }

    /// <summary>
    /// 「X 流漫溢」：本回合把一片区域变成指定元素（五行各建一个资产）。
    ///
    /// 语义（按设计确认过的版本）：
    ///   · 区域由 <see cref="mode"/> 决定：随机散落 N 格 / 随机 N×N 方块 / **固定矩形**；
    ///   · 格上已经有元素 → **就地转换**（uid / 位置不变，**不喂瓶子**：元素没有「消失」）；
    ///   · 格是**空**的 → **在该格生成一只**（所以这条事件能凭空加元素，很强）；
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

        [Tooltip("区域怎么取：Scatter = 随机散落 N 格；RandomBlock = 随机 N×N 方块；FixedRect = 固定矩形")]
        [SerializeField] ConvertAreaMode mode = ConvertAreaMode.Scatter;

        [Tooltip("Scatter 用：随机散落几格（图上的表写的是 9）")]
        [SerializeField, Min(1)] int cellCount = 9;

        [Tooltip("RandomBlock 用：方块边长（3 = 3×3 共最多 9 格）")]
        [SerializeField, Min(1)] int blockSize = 3;

        [Header("FixedRect 用：固定矩形的左下角与宽高（单位 = 格子）")]
        [SerializeField] int rectX = 2;
        [SerializeField] int rectY = 2;
        [SerializeField, Min(1)] int rectWidth = 3;
        [SerializeField, Min(1)] int rectHeight = 3;

        public Element TargetElement => element;
        public ConvertAreaMode Mode => mode;

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

        public override void Apply(GameEngine engine)
        {
            switch (mode)
            {
                case ConvertAreaMode.RandomBlock:
                    engine.ConvertBlockToElement(element, blockSize);
                    break;

                case ConvertAreaMode.FixedRect:
                    engine.ConvertRectToElement(element, rectX, rectY, rectWidth, rectHeight);
                    break;

                default:
                    engine.ConvertCellsToElement(element, cellCount);
                    break;
            }
        }
    }
}
