using System;
using System.Collections.Generic;

namespace TapGJ.Core
{
    /// <summary>
    /// 五行元素的集合（位掩码）。Unity 的 Inspector 会把 [Flags] 枚举渲染成一个多选下拉框，
    /// 所以关卡设计师可以直接勾「本关出现哪些元素」「本关能抽到哪些牌」，不用写代码。
    ///
    /// 注意：<see cref="All"/> 之外的位没有意义，比较时用 <see cref="ElementSet"/> 里的工具。
    /// </summary>
    [Flags]
    public enum ElementMask
    {
        None = 0,
        Metal = 1 << 0,
        Wood = 1 << 1,
        Water = 1 << 2,
        Fire = 1 << 3,
        Earth = 1 << 4,
        All = Metal | Wood | Water | Fire | Earth,
    }

    /// <summary>ElementMask 的取值与显示工具（纯 C#，不引用 UnityEngine）。</summary>
    public static class ElementSet
    {
        /// <summary>单元素 → 掩码。</summary>
        public static ElementMask Of(Element e) => (ElementMask)(1 << (int)e);

        /// <summary>掩码里有没有这个元素。</summary>
        public static bool Has(this ElementMask mask, Element e) => (mask & Of(e)) != 0;

        /// <summary>掩码里有几个元素。</summary>
        public static int Count(this ElementMask mask)
        {
            int n = 0;
            foreach (var e in ElementDefs.All)
                if (mask.Has(e)) n++;
            return n;
        }

        /// <summary>掩码展开成元素数组，顺序固定是金木水火土（保证随机可复现）。</summary>
        public static Element[] ToArray(this ElementMask mask)
        {
            var list = new List<Element>(ElementDefs.Count);
            foreach (var e in ElementDefs.All)
                if (mask.Has(e)) list.Add(e);
            return list.ToArray();
        }

        /// <summary>元素序列 → 掩码。</summary>
        public static ElementMask ToMask(IEnumerable<Element> elements)
        {
            var mask = ElementMask.None;
            if (elements == null) return mask;
            foreach (var e in elements) mask |= Of(e);
            return mask;
        }

        /// <summary>人话：「金水」/「全部」/「（空）」—— 报错文案与 Inspector 都用它。</summary>
        public static string Names(this ElementMask mask)
        {
            if (mask == ElementMask.All) return "全部";
            if (mask == ElementMask.None) return "（空）";

            var parts = new List<string>(ElementDefs.Count);
            foreach (var e in ElementDefs.All)
                if (mask.Has(e)) parts.Add(ElementDefs.Name(e));
            return string.Join("", parts);
        }
    }
}
