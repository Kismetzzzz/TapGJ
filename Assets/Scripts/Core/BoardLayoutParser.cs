using System;

namespace TapGJ.Core
{
    /// <summary>
    /// 把「字符画」解析成 <see cref="LevelSetup"/>，让关卡作者能直接在 Inspector 里用文字画棋盘。
    ///
    /// 语法：`金木水火土` = 对应元素，`.` = 空格，`#` = 墙。
    /// 行数 = 棋盘高度，每行长度 = 棋盘宽度；空行会被忽略。
    ///
    /// 例（8 宽，第 1 行是 y=最高的那行）：
    ///   ........
    ///   ....#...
    ///   ...水....
    ///   ........
    ///   ....#...
    ///   ........
    ///   ...金....
    ///   ........
    /// </summary>
    public static class BoardLayoutParser
    {
        /// <summary>解析布局，不限制元素（等价于 ElementMask.All）。</summary>
        public static bool TryParse(string text, int width, int height, out LevelSetup setup, out string error)
            => TryParse(text, width, height, ElementMask.All, out setup, out error);

        /// <summary>
        /// 解析布局，并且只允许 <paramref name="allowed"/> 里的元素。
        /// 关卡把可用元素限制成子集时用这个重载 —— 画错元素会当场报出「第几行第几列」，
        /// 而不是等到 Play 才发现棋盘上冒出个本关不该有的元素。
        /// </summary>
        public static bool TryParse(string text, int width, int height, ElementMask allowed,
            out LevelSetup setup, out string error)
        {
            setup = null;
            error = null;

            var rows = (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (rows.Length == 0)
            {
                error = "布局是空的";
                return false;
            }

            if (rows.Length != height)
            {
                error = $"布局行数 {rows.Length}，应该是 {height} 行";
                return false;
            }

            var cells = new Element?[width * height];
            var blocked = new bool[width * height];

            for (int textRow = 0; textRow < height; textRow++)
            {
                var row = rows[textRow].Trim();
                if (row.Length != width)
                {
                    error = $"第 {textRow + 1} 行长度 {row.Length}，应该是 {width}";
                    return false;
                }

                // 文本第 1 行 = 棋盘最上面一行（Board 的 y 从下往上，所以 y = height-1-textRow）
                int y = height - 1 - textRow;

                for (int x = 0; x < width; x++)
                {
                    char c = row[x];
                    int idx = y * width + x;

                    if (c == '.') continue;
                    if (c == '#')
                    {
                        blocked[idx] = true;
                        continue;
                    }

                    var element = ElementFromChar(c);
                    if (element == null)
                    {
                        error = $"第 {textRow + 1} 行第 {x + 1} 列「{c}」不是合法字符（金木水火土 . #）";
                        return false;
                    }

                    if (!allowed.Has(element.Value))
                    {
                        error = $"第 {textRow + 1} 行第 {x + 1} 列「{c}」不在本关可用元素「{allowed.Names()}」里。"
                                + "要么改布局，要么把该元素加进可用元素。";
                        return false;
                    }

                    cells[idx] = element.Value;
                }
            }

            int blockedCount = 0;
            foreach (var b in blocked) if (b) blockedCount++;
            if (blockedCount >= width * height)
            {
                error = "墙太多，棋盘没有能站人的格子";
                return false;
            }

            setup = new LevelSetup { Cells = cells, Blocked = blocked };
            return true;
        }

        static Element? ElementFromChar(char c)
        {
            switch (c)
            {
                case '金': return Element.Metal;
                case '木': return Element.Wood;
                case '水': return Element.Water;
                case '火': return Element.Fire;
                case '土': return Element.Earth;
                default: return null;
            }
        }
    }
}
