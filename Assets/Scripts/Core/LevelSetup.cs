namespace TapGJ.Core
{
    /// <summary>
    /// 一关的初始布置（纯数据，放在 Core 里便于测试）。
    /// 由 BoardLayoutParser 从字符画生成，或由代码直接构造。
    /// </summary>
    public sealed class LevelSetup
    {
        /// <summary>长度 = W*H，null = 空格；下标 = y*W+x（行优先，与 Board 内部一致）。</summary>
        public Element?[] Cells;

        /// <summary>长度 = W*H，true = 墙（不可进入、不可生成）。</summary>
        public bool[] Blocked;

        /// <summary>固定起手牌；空数组 = 按 Rules.InitialHandSize 随机抽。</summary>
        public Element[] InitialHand;
    }
    /// <summary>
    /// 一个初始复合元素：摆在哪、摆哪种。
    /// 放在 <see cref="Rules.InitialComposites"/> 上（而不是只放在 LevelSetup 里），
    /// 因为「关卡布局留空走随机铺盘」时 LevelSetup 是 null，而初始复合元素照样要摆上去。
    /// </summary>
    [System.Serializable]
    public struct CompositePlacement
    {
        /// <summary>棋盘横坐标（0 = 最左）。</summary>
        public int X;

        /// <summary>棋盘纵坐标（0 = 最下；注意字符画布局的第 1 行是棋盘最上面一行）。</summary>
        public int Y;

        public CompositeKind Kind;

        public CompositePlacement(int x, int y, CompositeKind kind)
        {
            X = x;
            Y = y;
            Kind = kind;
        }

        public Pos Pos => new Pos(X, Y);

        public override string ToString() => $"{CompositeDefs.Name(Kind)}({X},{Y})";
    }
}
