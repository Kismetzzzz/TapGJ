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

    /// <summary>棋盘上的一只元素小怪。uid 在一局内唯一，供表现层追踪同一个物体。</summary>
    public sealed class Creep
    {
        public int Uid { get; }

        /// <summary>
        /// 元素。**相生会让它就地变成另一种元素**（金遇水 → 金变水），所以不是只读的。
        /// 注意：uid 不变，表现层只需要换颜色和字，不需要换对象。
        /// </summary>
        public Element Element { get; internal set; }

        public Pos Pos { get; internal set; }
        public bool Alive { get; internal set; } = true;

        internal Creep(int uid, Element element, Pos pos)
        {
            Uid = uid;
            Element = element;
            Pos = pos;
        }

        public override string ToString() => $"#{Uid}{ElementDefs.Name(Element)}{Pos}";
    }

    /// <summary>
    /// 8x8 网格。每个格子最多存在一个元素（不变量：_cells 里死掉的小怪一律为 null）。
    /// 坐标 (0,0) 是左下角，(W-1,0) 是右下角。
    /// </summary>
    public sealed class Board
    {
        readonly Creep[] _cells;

        public int Width { get; }
        public int Height { get; }

        public Board(int width, int height)
        {
            Width = width;
            Height = height;
            _cells = new Creep[width * height];
        }

        public bool InBounds(Pos p) => p.X >= 0 && p.X < Width && p.Y >= 0 && p.Y < Height;

        public Creep At(Pos p) => InBounds(p) ? _cells[p.Y * Width + p.X] : null;

        public bool IsEmpty(Pos p) => InBounds(p) && _cells[p.Y * Width + p.X] == null;

        public void Clear(Pos p)
        {
            if (InBounds(p)) _cells[p.Y * Width + p.X] = null;
        }

        public void ClearAll()
        {
            for (int i = 0; i < _cells.Length; i++) _cells[i] = null;
        }

        public void Put(Creep creep)
        {
            if (!InBounds(creep.Pos))
                throw new ArgumentException($"坐标越界：{creep.Pos}");
            _cells[creep.Pos.Y * Width + creep.Pos.X] = creep;
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
                        if (!InBounds(p)) continue;
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
            if (!exclude.IsNone && InBounds(exclude) && _cells[exclude.Y * Width + exclude.X] == null)
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
