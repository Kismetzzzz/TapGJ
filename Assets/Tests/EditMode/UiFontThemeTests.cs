using System;
using NUnit.Framework;
using TapGJ.View;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TapGJ.Tests
{
    public sealed class UiFontThemeTests
    {
        UiFontTheme _theme;

        [SetUp]
        public void Setup()
        {
            _theme = ScriptableObject.CreateInstance<UiFontTheme>();
            _theme.body = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_theme);

        [Test]
        public void 系统字体缺失或留空_正文标题数字使用项目回退字体()
        {
            foreach (string name in new[] { "TapGJ_Font_Not_Installed_4b8ad7", "", " " })
            {
                _theme.systemBodyFont = name;
                foreach (UiFontRole role in Enum.GetValues(typeof(UiFontRole)))
                    Assert.AreSame(_theme.body, _theme.GetFont(role));
            }
        }

        [Test]
        public void 标准楷体已安装_共享系统字体实例_更换配置后恢复回退()
        {
            if (!Array.Exists(Font.GetOSInstalledFontNames(), name => name == "KaiTi"))
                Assert.Ignore("此设备未安装 KaiTi，适用项目回退字体。");
            _theme.systemBodyFont = "KaiTi";
            var font = _theme.GetFont(UiFontRole.Body);
            Assert.IsNotNull(font);
            Assert.AreNotSame(_theme.body, font);
            CollectionAssert.Contains(font.fontNames, "KaiTi");
            Assert.IsTrue(font.HasCharacter('楷'));
            foreach (UiFontRole role in Enum.GetValues(typeof(UiFontRole)))
                Assert.AreSame(font, _theme.GetFont(role), "正文、标题、数字应复用同一个系统字体实例");
            _theme.systemBodyFont = "TapGJ_Font_Not_Installed_4b8ad7";
            Assert.AreSame(_theme.body, _theme.GetFont(UiFontRole.Body));
            Assert.IsTrue(font == null, "更换配置应释放旧的动态系统字体");
        }

        [Test]
        public void 单独配置标题字体_标题使用覆盖值_正文数字继续使用默认值()
        {
            var titleFont = new Font();
            try
            {
                _theme.title = titleFont;
                Assert.AreSame(titleFont, _theme.GetFont(UiFontRole.Title));
                Assert.AreSame(_theme.body, _theme.GetFont(UiFontRole.Body));
                Assert.AreSame(_theme.body, _theme.GetFont(UiFontRole.Number));
            }
            finally { Object.DestroyImmediate(titleFont); }
        }
    }
}
