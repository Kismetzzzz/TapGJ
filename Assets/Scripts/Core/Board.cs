using System;
using System.Collections.Generic;

namespace TapGJ.Core
{
    public readonly struct Pos : IEquatable<Pos>
    {
        public readonly int X;
        public readonly int Y;

        public Pos(int x, int y) { X = x; Y = y; }

        public static readonly Pos None = new Pos(-1, -1);
        public bool IsNone => X < 0 || Y < 0;

        public bool Equals(Pos other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Pos p && Equals(p);
        public override int GetHashCode() => (X * 397) ^ Y;
        public override string ToString() => $"({X},{Y})";

        public static bool operator ==(Pos a, Pos b) => a.Equals(b);
        public static bool operator !=(Pos a, Pos b) => !a.Equals(b);
    }

    /// <summary>
    /// 棋盘上的一个「占格者」：元素小怪（<see cref="Creep"/>）或复合元素（<see cref="Composite"/>）。
    /// 两者共用 uid 空间，表现层用一个字典（uid → 显示对象）同时管它们。
    /// </summary>
    public interface IBoardOccupant
    {
        int Uid { get; }
        Pos Pos { get; }
        /// <summary>复合元素没有五行元素，这个属性只对 <see cref="Creep"/> 有意义。</summary>
        bool IsComposite { get; }
    }

    /// <summary>棋盘上的一只元素小怪。uid 在一局内唯一，供表现层追踪同一个物体。</summary>
    public sealed class Creep : IBoardOccupant
    {
        public int Uid { get; }

        /// <summary>
        /// 元素。**相生会让它就地变成另一种元素**（金遇水 → 金变水），所以不是只读的。
        /// 注意：uid 不变，表现层只需要换颜色和字，不需要换对象。
        /// </summary>
        public Element Element { get; internal set; }

        public Pos Pos { get; internal set; }
        public bool Alive { get; internal set; } = true;

        public bool IsComposite => false;

        internal Creep(int uid, Element element, Pos pos)
        {
            Uid = uid;
            Element = element;
            Pos = pos;
        }

        public override string ToString() => $"#{Uid}{ElementDefs.Name(Element)}{Pos}";
    }

    /// <summary>
    /// 棋盘上的一个**复合元素**（烬 / 熔岩 / 矿晶 / 寒钢 / 灵泉）。
    ///
    /// 和 <see cref="Creep"/> 的区别（这几条是玩法约定，不是实现细节）：
    ///   · **占据格子**：一格只能有一个占格者，复合元素压住的格子元素小怪走不进去、也生不出来
    ///   · **不会移动**：准备阶段只推进元素小怪，复合元素原地不动、也不会被撞
    ///   · **不受元素反应影响**：元素牌只能打元素小怪；复合元素只有「分解牌」能动它
    ///   · **每回合产生元素**：回合开始在四周生成自己的两种组成元素各若干份
    /// </summary>
    public sealed class Composite : IBoardOccupant
    {
        public int Uid { get; }
        public CompositeKind Kind { get; }

        public Pos Pos { get; internal set; }

        public bool IsComposite => true;

        /// <summary>本回合是不是已经「产生」过了（每回合只产生一次）。</summary>
        public bool ProducedThisRound { get; internal set; }

        internal Composite(int uid, CompositeKind kind, Pos pos)
        {
            Uid = uid;
            Kind = kind;
            Pos = pos;
        }

        public Element[] Ingredients => CompositeDefs.Ingredients(Kind);

        public override string ToString() => $"#{Uid}{CompositeDefs.Name(Kind)}{Pos}";
    }

    /// <summary>
    /// 8x8 网格。每个格子最多存在一个占格者（元素小怪 **或** 复合元素）。
    /// 坐标 (0,0) 是左下角，(W-1,0) 是右下角。
    /// 另外支持「墙格」（_blocked）：小怪不能走进去、不能在上面生成，用于关卡里的固定障碍。
    ///
    /// ⚠ 复合元素放在**另一个数组**（_composites）里，和 _cells 一一对应、互斥占位。
    /// 这样做的目的是让「元素」那套代码完全不用改：
    ///   <see cref="At"/> 只返回元素小怪（复合元素那格返回 null），
    ///   <see cref="IsEmpty"/> 对复合元素那格返回 false（小怪走不进去、也生不出来）。
    /// </summary>
    public sealed class Board
    {
        readonly Creep[] _cells;
        readonly Composite[] _composites;
        readonly bool[] _blocked;

        public int Width { get; }
        public int Height { get; }

        public Board(int width, int height)
        {
            Width = width;
            Height = height;
            _cells = new Creep[width * height];
            _composites = new Composite[width * height];
            _blocked = new bool[width * height];
        }

        public bool InBounds(Pos p) => p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Height;

        /// <summary>是不是墙：小怪不能进入、不能在上面生成。</summary>
        public bool IsBlocked(Pos p) => InBounds(p) && _blocked[p.Y * Width + p.X];

        /// <summary>小怪能不能走进去：在界内且不是墙。</summary>
        public bool IsWalkable(Pos p) => InBounds(p) && !_blocked[p.Y * Width + p.X];

        public void SetBlocked(Pos p, bool blocked)
        {
            if (!InBounds(p)) return;
            _blocked[p.Y * Width + p.X] = blocked;
        }

        public void ClearBlocked()
        {
            for (int i = 0; i < _blocked.Length; i++) _blocked[i] = false;
        }

        /// <summary>
        /// 格子上的**元素小怪**；空格 / 复合元素 / 越界 / **已经死掉但还没被清掉** 都返回 null。
        /// 「死了但还在数组里」这个中间态是存在的（先标记 Alive=false、再清格子），
        /// 中间如果被别处读到就会拿到一只死小怪，所以这里直接过滤掉。
        /// </summary>
        public Creep At(Pos p)
        {
            if (!InBounds(p)) return null;
            var creep = _cells[p.Y * Width + p.X];
            return creep != null && creep.Alive ? creep : null;
        }

        /// <summary>格子上的**复合元素**；没有就返回 null。</summary>
        public Composite CompositeAt(Pos p) => InBounds(p) ? _composites[p.Y * Width + p.X] : null;

        /// <summary>格子上有没有复合元素。</summary>
        public bool HasComposite(Pos p) => CompositeAt(p) != null;

        /// <summary>
        /// 格子上有没有占格者（元素小怪或复合元素）。越界 / 墙格返回 null。
        /// 「这格空不空」一律走这个方法，别再直接看 At()。
        /// </summary>
        public IBoardOccupant OccupantAt(Pos p)
        {
            if (!InBounds(p)) return null;
            int i = p.Y * Width + p.X;
            return _cells[i] != null ? (IBoardOccupant)_cells[i] : _composites[i];
        }

        /// <summary>
        /// 是不是可用的空格（界内、不是墙、**没有任何占格者**）。
        /// 复合元素占的格子不算空格 —— 所以元素走不进去、也生不出来。
        /// </summary>
        public bool IsEmpty(Pos p)
            => InBounds(p) && !_blocked[p.Y * Width + p.X]
               && _cells[p.Y * Width + p.X] == null && _composites[p.Y * Width + p.X] == null;

        public void Clear(Pos p)
        {
            if (InBounds(p)) _cells[p.Y * Width + p.X] = null;
        }

        /// <summary>清空所有元素 + 复合元素 + 墙（回到完全空白的棋盘）。</summary>
        public void ClearAll()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] = null;
                _composites[i] = null;
            }
            ClearBlocked();
        }

        public void Put(Creep creep)
        {
            if (!InBounds(creep.Pos))
                throw new ArgumentException($"坐标越界：{creep.Pos}");

            int i = creep.Pos.Y * Width + creep.Pos.X;
            if (_blocked[i])
                throw new ArgumentException($"不能把元素放在墙格上：{creep.Pos}");
            if (_composites[i] != null)
                throw new ArgumentException($"不能把元素放在复合元素上：{creep.Pos}");

            _cells[i] = creep;
        }

        /// <summary>把一个复合元素放上棋盘（不变量：那格不能已有元素或另一个复合元素）。</summary>
        public void PutComposite(Composite composite)
        {
            if (composite == null) return;
            if (!InBounds(composite.Pos))
                throw new ArgumentException($"坐标越界：{composite.Pos}");

            int i = composite.Pos.Y * Width + composite.Pos.X;
            if (_blocked[i])
                throw new ArgumentException($"不能把复合元素放在墙格上：{composite.Pos}");

            _composites[i] = composite;
        }

        public void ClearComposite(Pos p)
        {
            if (InBounds(p)) _composites[p.Y * Width + p.X] = null;
        }

        /// <summary>遍历棋盘上所有存活小怪，按行优先顺序（确定性）。</summary>
        public IEnumerable<Creep> Creeps()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                var c = _cells[i];
                if (c != null && c.Alive) yield return c;
            }
        }

        public List<Creep> Snapshot()
        {
            var list = new List<Creep>();
            foreach (var c in Creeps()) list.Add(c);
            return list;
        }

        public int CountOf(Element e)
        {
            int n = 0;
            foreach (var c in Creeps())
                if (c.Element == e) n++;
            return n;
        }

        public int CreepCount()
        {
            int n = 0;
            foreach (var c in Creeps()) n++;
            return n;
        }

        /// <summary>遍历棋盘上所有复合元素（按行优先顺序，确定性）。</summary>
        public IEnumerable<Composite> Composites()
        {
            for (int i = 0; i < _composites.Length; i++)
            {
                var c = _composites[i];
                if (c != null) yield return c;
            }
        }

        public int CompositeCount()
        {
            int n = 0;
            foreach (var c in Composites()) n++;
            return n;
        }

        /// <summary>场上出现的元素种类数（0 表示空场）。</summary>
        public int DistinctElementCount()
        {
            int mask = 0;
            foreach (var c in Creeps()) mask |= 1 << (int)c.Element;
            int n = 0;
            while (mask != 0) { n += mask & 1; mask >>= 1; }
            return n;
        }

        public bool IsEmptyBoard() => CreepCount() == 0;

        /// <summary>棋盘上所有仍存活的水元素数量（判定「只剩水」用）。</summary>
        public int WaterCount() => CountOf(Element.Water);

        /// <summary>场上第一个（行优先）元素的种类；空场时返回 Element.Water 作为占位。</summary>
        public Element FirstElementOrDefault()
        {
            foreach (var c in Creeps()) return c.Element;
            return Element.Water;
        }

        /// <summary>
        /// 出生格选择：距离 origin 1 格的格子优先（四个正交 → 四个斜角），
        /// 再往外扩到 2 格。组内随机；随机源由调用方传入，保证同一个种子能复现整局。
        /// 全满则返回 Pos.None（调用方记录一次「出生失败」）。
        /// 不变量：返回的格子一定是空的（或等于 exclude，表示它马上会被清空）。
        /// </summary>
        public Pos FindSpawnCell(Pos origin, Random rng) => FindSpawnCell(origin, rng, Pos.None);

        public Pos FindSpawnCell(Pos origin, Random rng, Pos exclude) => FindSpawnCell(origin, rng, exclude, 2);

        public Pos FindSpawnCell(Pos origin, Random rng, Pos exclude, int maxRadius)
        {
            for (int radius = 1; radius <= maxRadius; radius++)
            {
                var ring = new List<Pos>();
                for (int dy = -radius; dy <= radius; dy++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dy) != radius) continue; // 只取这一圈的边
                        var p = new Pos(origin.X + dx, origin.Y + dy);
                        if (!IsWalkable(p)) continue; // 界外或墙格都不能生
                        if (p == exclude) continue;
                        if (_cells[p.Y * Width + p.X] != null) continue;
                        ring.Add(p);
                    }
                }

                if (ring.Count == 0) continue;
                Shuffle(ring, rng);
                return ring[0];
            }

            // 外层都找不到，最后才考虑 exclude 本身（调用方保证它已经/即将为空）
            if (!exclude.IsNone && IsWalkable(exclude) && _cells[exclude.Y * Width + exclude.X] == null)
                return exclude;

            return Pos.None;
        }

        public static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public static int Manhattan(Pos a, Pos b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }
}
