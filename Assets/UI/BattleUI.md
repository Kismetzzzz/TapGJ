# 独立局内 UI

双击 `Assets/Scenes/BattleUI.unity`，按 Play 可独立预览。也可通过菜单 **TapGJ → 打开独立局内 UI** 打开。场景已单独加入构建列表。

背景使用提供的原图，保存于 `Assets/UI/Art/Battle/BattleBackground.jpg`，没有裁切或修改。界面以 1920×1080 为基准，背景、控件与棋盘容器整体等比适配安全区域；宽屏和竖屏会留边。

所有界面都是已保存的 uGUI 对象，可在 Hierarchy 中直接调整：

- `BattleUICanvas/Composition/Controls/TaskPanel`：任务目标、进度文字和进度条。
- `Controls/TimerPanel`：计时；下方显示回合和选牌状态。
- `Controls/ElementRail`：金、木、水、火、土五个按钮。
- `Controls/Hand`：五个手牌槽，按参考图排列成扇形。
- `Controls/NextRoundButton`、`DiscardButton`：下一回合、弃牌。
- `Controls/SettingsButton`：打开设置遮罩，暂停预览计时；点击继续、遮罩或 Esc 关闭。
- `Composition/BoardContent`：空的棋盘内容容器，位置对应背景上的棋盘。

预览模式只演示 UI：选牌抬起并描边，再点取消；选择后才能弃牌；弃牌隐藏该槽位；下一回合更新回合数字。场景不创建对局控制器，也不执行棋盘规则。未连接玩法时任务进度为示例值。

## 替换手牌

卡面统一配置在 **`Assets/UI/Data/BattleCardArt.asset`**，所有手牌共用这一份。当前已接入提供的木、水、火、土四张完整卡面，原图保存于 `Assets/UI/Art/Battle/Cards/`；金卡面留空时显示“金 / 卡面待补充”，不会使用其他元素的图片。

以后换图只需：

1. 把新图片拖进 Project 面板中的 `Assets/UI/Art/Battle/Cards/`（建议与当前卡面接近的竖版比例）。
2. 选中图片，将 **Texture Type** 设为 **Sprite (2D and UI)**，**Sprite Mode** 设为 **Single**，点击 **Apply**。
3. 选中 `BattleCardArt.asset`，把图片拖到 Inspector 中对应的 **金 / 木 / 水 / 火 / 土** 槽位。

当前四张图已开启缩小纹理过滤。替换为带细碎纹理的图片时，也可开启 **Generate Mip Maps**，减少小卡面上的噪点。

所有同元素手牌会自动更新，包括编辑器预览与运行中的卡牌；无需逐张修改场景、Prefab 或代码。配置会随工程保存，重新打开仍有效。也可用同名新图片覆盖 `Cards/` 中对应 JPG，保留原 `.meta` 文件，引用会继续生效。

`Assets/UI/Prefabs/BattleCard.prefab` 只负责共用尺寸、交互和选中效果：`Artwork/CardFace` 整张显示卡面并保留比例与原色，`FocusRing` 和 `SelectedRing` 是独立描边，`FallbackArtwork` 是缺图占位。新图的比例不同会在现有卡框内完整等比显示。需要统一调整手牌大小或描边时修改此预制体。

预览仍是五张手牌，顺序为火、土、水、木、火，四种卡面都能看到。换图不会改变手牌元素、回合、选中状态或交互。

中文字体包含常用汉字与 ASCII，来自 Noto Sans SC；许可见 `Assets/UI/Art/Battle/FontLicense.txt`。罕见字或扩展字符需要替换完整字体。

## 快速更换 UI 字体

菜单 **TapGJ → 打开 UI 字体配置** 会直接选中 `Assets/UI/Data/UiFontTheme.asset`。也可在 Project 中点击这一资产。

- **界面正文**：按钮、任务说明和普通文字。改这一项即可整体换字体。
- **大标题**：标题和面板标题；留空时跟随正文。
- **数字**：计时器、排行和章节序号；留空时跟随正文。

将新的 `.ttf` 或 `.otf` 文件拖入 `Assets/UI/Fonts/`，再拖到对应槽位即可。已提供 `WenKaiScreen.ttf`（霞鹜文楷屏幕阅读版）和 `MaShanZheng.ttf`（马善政毛笔楷体），许可证保存在同目录。

主菜单、独立局内 UI 和四类 UI 预制体统一读取这份配置。编辑器预览和运行时会自动更新；隐藏面板打开、新卡片创建时也会使用最新字体。替换只修改字体，保留原有文字、字号、颜色和按钮交互。正文暂时留空时保留已有字体。

新添的 uGUI Text 可添加 `UiFontBinding`，把相同配置拖入 **字体配置**，选择 **文字用途**，即可参与统一替换。卡面图片里绘制的字不属于 Text，需要通过卡面配置替换图片；旧对局原型的世界文字不在这套 UI 字体配置中。

## 接入玩法

选中 `BattleUICanvas`，在 `BattleUiController` 中关闭 **Preview Mode**。通过 `SetObjective`、`SetRound`、`SetElapsedSeconds`、`SetTimerRunning`、`SetActionsEnabled` 更新显示和交互状态。

Inspector 中的 `Next Round Requested`、`Discard Requested`、`Card Selected`、`Element Selected` 可绑定玩法回调；代码也可通过对应的 UnityEvent 属性添加监听。卡牌参数是固定槽位索引 0～4，取消选择为 -1；五行参数顺序是金、木、水、火、土。关闭预览后，按钮只发送事件，不改变手牌或回合数据；手牌数据由接入方维护。

聚焦按钮后可用方向键切换、Enter/空格确认。装饰文字、卡面、描边与背景不拦截点击。设置遮罩会阻止背景鼠标与键盘操作，关闭后恢复原焦点。

