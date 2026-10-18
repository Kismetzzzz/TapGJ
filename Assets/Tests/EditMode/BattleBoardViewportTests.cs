using System;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.View;
using UnityEngine;

namespace TapGJ.Tests
{
    public class BattleBoardViewportTests
    {
        static BattleBoardViewport CreateViewport()
        {
            var go = new GameObject("CalibrationTest", typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1000, 500);
            rect.pivot = Vector2.zero;
            var viewport = go.AddComponent<BattleBoardViewport>();
            viewport.SetCalibration(new[]
            {
                new BattleBoardViewport.CalibrationRow(0, new[] { 0f, .45f, 1f }),
                new BattleBoardViewport.CalibrationRow(.6f, new[] { .06f, .48f, .94f }),
                new BattleBoardViewport.CalibrationRow(1, new[] { .12f, .5f, .88f })
            });
            return viewport;
        }

        [Test]
        public void 背景校准_远近行距与不等宽列按实际格线排列()
        {
            var viewport = CreateViewport();
            try
            {
                // 背景前半棋盘占高度的 60%；中央格线也偏离几何中心。
                Assert.Less(Vector2.Distance(new Vector2(480, 300), viewport.BoardUvToLocal(new Vector2(.5f, .5f))), .01f);
                Assert.Less(Vector2.Distance(new Vector2(247.5f, 150), viewport.BoardUvToLocal(new Vector2(.25f, .25f))), .01f);
                Assert.Less(Vector2.Distance(new Vector2(700, 400), viewport.BoardUvToLocal(new Vector2(.75f, .75f))), .01f);
            }
            finally { UnityEngine.Object.DestroyImmediate(viewport.gameObject); }
        }

        [Test]
        public void 背景校准_显示位置反算后仍是同一个棋盘坐标()
        {
            var viewport = CreateViewport();
            try
            {
                for (int y = 0; y <= 16; y++)
                    for (int x = 0; x <= 16; x++)
                    {
                        var expected = new Vector2(x / 16f, y / 16f);
                        Assert.IsTrue(viewport.TryBoardUvAtLocalPoint(viewport.BoardUvToLocal(expected), out var actual),
                            $"边缘点也应能反算：uv={expected}，local={viewport.BoardUvToLocal(expected)}");
                        Assert.Less(Vector2.Distance(expected, actual), .00001f);
                    }
            }
            finally { UnityEngine.Object.DestroyImmediate(viewport.gameObject); }
        }

        [Test]
        public void 背景校准_斜边外与非法坐标不触发棋盘点击()
        {
            var viewport = CreateViewport();
            try
            {
                foreach (var point in new[] { new Vector2(30, 490), new Vector2(970, 490), new Vector2(500, -1),
                    new Vector2(500, 501), new Vector2(float.NaN, 100), new Vector2(100, float.PositiveInfinity) })
                    Assert.IsFalse(viewport.TryBoardUvAtLocalPoint(point, out _));
                Assert.Throws<ArgumentException>(() => viewport.SetCalibration(new[]
                {
                    new BattleBoardViewport.CalibrationRow(0, new[] { 0f, 1f }),
                    new BattleBoardViewport.CalibrationRow(.5f, new[] { .8f, .2f })
                }));
                Assert.Throws<ArgumentException>(() => viewport.SetCalibration(new[]
                {
                    new BattleBoardViewport.CalibrationRow(.5f, new[] { 0f, 1f }),
                    new BattleBoardViewport.CalibrationRow(.5f, new[] { 0f, 1f })
                }));
                Assert.Throws<ArgumentException>(() => viewport.SetCalibration(new[]
                {
                    new BattleBoardViewport.CalibrationRow(0, new[] { 0f, 1f }),
                    new BattleBoardViewport.CalibrationRow(1, new[] { 0f, .5f, 1f })
                }));
                Assert.IsTrue(viewport.TryBoardUvAtLocalPoint(new Vector2(480, 300), out _), "无效配置不能覆盖原有校准");
            }
            finally { UnityEngine.Object.DestroyImmediate(viewport.gameObject); }
        }

        [TestCase(8, 8)]
        [TestCase(4, 3)]
        public void 界面棋盘_所有格子中心映射到真实坐标(int columns, int rows)
        {
            const float size = 1, gap = .1f;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < columns; x++)
                {
                    var uv = new Vector2((x * (size + gap) + size / 2) / (columns * size + (columns - 1) * gap),
                        (y * (size + gap) + size / 2) / (rows * size + (rows - 1) * gap));
                    Assert.IsTrue(BattleBoardViewport.TryCellAtUv(uv, columns, rows, size, gap, out var cell));
                    Assert.AreEqual(new Pos(x, y), cell);
                }
        }

        [Test]
        public void 界面棋盘_间隙和区域外点击不落入相邻格子()
        {
            float width = 4 + 3 * .1f;
            Assert.IsFalse(BattleBoardViewport.TryCellAtUv(new Vector2(1.05f / width, .1f), 4, 3, 1, .1f, out _));
            foreach (var uv in new[] { new Vector2(-.01f, .5f), new Vector2(.5f, -1), Vector2.one,
                new Vector2(float.NaN, .5f), new Vector2(.5f, float.PositiveInfinity) })
                Assert.IsFalse(BattleBoardViewport.TryCellAtUv(uv, 4, 3, 1, .1f, out _));
        }

        [Test]
        public void 菱形背景_四角和内部显示位置可反算到原格子()
        {
            var viewport = CreateViewport();
            try
            {
                viewport.SetCornerCalibration(new[] { new Vector2(.5f, 0), new Vector2(1, .43f),
                    new Vector2(.48f, 1), new Vector2(0, .57f) });
                Assert.AreEqual(new Vector2(500, 0), viewport.BoardUvToLocal(Vector2.zero));
                Assert.AreEqual(new Vector2(0, 285), viewport.BoardUvToLocal(Vector2.up));
                for (int y = 0; y <= 16; y++)
                    for (int x = 0; x <= 16; x++)
                    {
                        var expected = new Vector2(x / 16f, y / 16f);
                        Assert.IsTrue(viewport.TryBoardUvAtLocalPoint(viewport.BoardUvToLocal(expected), out var actual));
                        Assert.Less(Vector2.Distance(expected, actual), .00001f);
                    }
                foreach (var point in new[] { new Vector2(20, 20), new Vector2(980, 20),
                    new Vector2(20, 480), new Vector2(980, 480) })
                    Assert.IsFalse(viewport.TryBoardUvAtLocalPoint(point, out _), "菱形外的山石与云雾不能响应出牌");
            }
            finally { UnityEngine.Object.DestroyImmediate(viewport.gameObject); }
        }

        [Test]
        public void 四角透视_中心位于背景对角线交点而非四角平均值()
        {
            var viewport = CreateViewport();
            try
            {
                viewport.SetCornerCalibration(new[] { Vector2.zero, Vector2.right,
                    new Vector2(.75f, 1), new Vector2(.25f, 1) });
                Assert.Less(Vector2.Distance(new Vector2(500, 1000f / 3), viewport.BoardUvToLocal(new Vector2(.5f, .5f))), .001f);
            }
            finally { UnityEngine.Object.DestroyImmediate(viewport.gameObject); }
        }

        [Test]
        public void 菱形背景_拒绝交叉退化和非法四角且保留已有校准()
        {
            var viewport = CreateViewport();
            try
            {
                foreach (var corners in new[]
                {
                    new[] { Vector2.zero, Vector2.one, Vector2.right, Vector2.up },
                    new[] { Vector2.zero, Vector2.right, Vector2.right, Vector2.up },
                    new[] { Vector2.zero, Vector2.right, Vector2.one, new Vector2(-.1f, 1) },
                    new[] { Vector2.zero, Vector2.right, Vector2.one, new Vector2(float.NaN, 1) },
                    new[] { Vector2.zero, Vector2.right, Vector2.one }
                }) Assert.Throws<ArgumentException>(() => viewport.SetCornerCalibration(corners));
                Assert.Less(Vector2.Distance(new Vector2(480, 300), viewport.BoardUvToLocal(new Vector2(.5f, .5f))), .01f);
            }
            finally { UnityEngine.Object.DestroyImmediate(viewport.gameObject); }
        }
    }
}
