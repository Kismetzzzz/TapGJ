using System.Linq;
using NUnit.Framework;
using TapGJ.Core;
using TapGJ.View;
using UnityEditor;
using UnityEngine;

namespace TapGJ.Tests
{
    public sealed class BattlePieceArtTests
    {
        const string ArtPath = "Assets/UI/Data/BattlePieceArt.asset";
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void 素材配置_十种棋子对应元素配方且保留透明通道()
        {
            var art = AssetDatabase.LoadAssetAtPath<BattlePieceArt>(ArtPath);
            Assert.IsNotNull(art);
            var sprites = ElementDefs.All.Select(art.GetSprite)
                .Concat(CompositeDefs.All.Select(art.GetSprite)).ToArray();
            CollectionAssert.AreEqual(new[] { "Metal", "Wood", "Water", "Fire", "Earth",
                "Ember", "Lava", "Ore", "ColdSteel", "SpiritSpring" }, sprites.Select(s => s != null ? s.name : "missing"));
            Assert.AreEqual(10, sprites.Distinct().Count());
            foreach (var sprite in sprites)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite));
                Assert.AreEqual(TextureImporterType.Sprite, importer.textureType);
                Assert.IsTrue(importer.alphaIsTransparency);
                Assert.AreEqual(512, sprite.texture.width);
                Assert.AreEqual(512, sprite.texture.height);
                Assert.AreEqual(Vector2.one, (Vector2)sprite.bounds.size,
                    "棋子素材应归一到一个单位，避免动画或相生换肤时尺寸突变");
            }
        }

        [Test]
        public void 相生转换_复用原棋子并切换到新元素素材()
        {
            var engine = new GameEngine(new Rules { CreepsPerElement = 0, InitialHandSize = 0, MaxMoveSteps = 1 }, 9);
            engine.Start();
            engine.ClearBoard();
            var earth = engine.PlaceCreep(Element.Earth, new Pos(1, 0));
            engine.PlaceCreep(Element.Metal, new Pos(2, 0));
            var view = CreateView(engine);
            var source = view.CreepViewOf(earth.Uid);
            Assert.AreSame(view.PieceArt.earth, source.Body.sprite);

            engine.SetNextRandomValues(1, 1);
            Assert.IsNotNull(engine.AdvancePreparation());
            view.Sync();

            Assert.AreSame(source, view.CreepViewOf(earth.Uid));
            Assert.AreSame(view.PieceArt.metal, source.Body.sprite);
            Assert.AreEqual(Color.white, source.Body.color, "原图应保留颜色");
            Assert.IsTrue(source.HasArtwork);
            Assert.IsFalse(source.Label.gameObject.activeSelf, "元素大字不应遮挡动物");
            Assert.AreEqual(2, view.CreepObjectCount);
        }

        [Test]
        public void 复合棋子_五种素材按配方显示且不叠加占位色块()
        {
            var engine = new GameEngine(new Rules { CreepsPerElement = 0, InitialHandSize = 0,
                UseCompositeElements = true }, 7);
            engine.ClearBoard();
            for (int i = 0; i < CompositeDefs.All.Length; i++)
                engine.PlaceComposite(CompositeDefs.All[i], new Pos(i, 0));
            var view = CreateView(engine);
            foreach (var source in view.CreepViews)
            {
                Assert.IsTrue(source.IsComposite);
                Assert.IsTrue(source.HasArtwork);
                Assert.AreSame(view.PieceArt.GetSprite(source.CompositeKind), source.Body.sprite);
                Assert.AreEqual(Color.white, source.Body.color);
                Assert.IsFalse(source.Label.gameObject.activeSelf);
                Assert.IsTrue(source.Ring == null || !source.Ring.enabled);
            }
            Assert.AreEqual(5, view.CompositeObjectCount);
        }

        [Test]
        public void 缺少素材_仍显示原有元素字与复合光环()
        {
            var engine = new GameEngine(new Rules { CreepsPerElement = 0, InitialHandSize = 0,
                UseCompositeElements = true }, 7);
            engine.ClearBoard();
            engine.PlaceCreep(Element.Water, new Pos(0, 0));
            engine.PlaceComposite(CompositeKind.Ember, new Pos(1, 0));
            var view = CreateView(engine, false);
            var water = view.CreepViewAt(new Pos(0, 0));
            Assert.IsFalse(water.HasArtwork);
            Assert.IsTrue(water.Label.gameObject.activeSelf);
            Assert.AreEqual("水", water.Label.text);
            Assert.AreEqual(ElementDefs.Tint(Element.Water), water.Body.color);
            var ember = view.CompositeViewAt(new Pos(1, 0));
            Assert.IsTrue(ember.Ring.enabled);
            Assert.IsTrue(ember.Label.gameObject.activeSelf);
            Assert.AreEqual("烬", ember.Label.text);
        }

        BoardView CreateView(GameEngine engine, bool withArt = true)
        {
            _root = new GameObject("PieceArtTest");
            var view = _root.AddComponent<BoardView>();
            if (withArt) view.PieceArt = AssetDatabase.LoadAssetAtPath<BattlePieceArt>(ArtPath);
            view.Setup(engine);
            view.Sync();
            return view;
        }
    }
}
