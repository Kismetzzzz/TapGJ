# 主菜单美术替换

场景：`Assets/Scenes/MainMenu.unity`。美术资源放到 `Assets/UI/Art/`。页面和卡片模板保存为 uGUI 对象，运行时按 `Assets/UI/Data/ChapterCatalog.asset` 的数组数量复制卡片；控制器填写文案并绑定每章的插画，模板的底图与文字样式继续继承。字体统一由 `Assets/UI/Data/UiFontTheme.asset` 控制。

## 开始与设置参考布局

入口和设置页使用 `Art/ReferenceMenu/` 中从用户参考图提取的美术。入口背景保存山水、题字、适龄标识与健康游戏提示；开始、公告、用户、设置按钮均有独立 Sprite。设置背景保留面板、题字和静态标签，返回、账号、帮助、关于与双滑块均为独立场景控件，背景中已清除固定的滑块刻度。

`Navigation/EntryPage/ReferenceArtwork` 是入口背景，`SettingsPanel` 的 Image 是设置背景。两页按 1280 × 720 的参考坐标等比缩放，完整显示构图；其他比例的画边继续使用原场景背景。右上工具栏与入口使用相同缩放。

这些参考按钮保留 `MenuButton.prefab` 关联，但底图在场景实例中有意覆盖；此处替换 Sprite 应编辑具体按钮。按钮内的 `Label` 保留名称和文案，文字颜色为透明，避免与图片上的书法文字重叠；换成无字底图时把 Label 颜色恢复即可。入口开始按钮关闭了 `FocusRing` 子对象，聚焦时改用按钮本身变色，避免叠加额外圆角外框；其他按钮继续使用各自的焦点反馈。用户和账号保持不可操作，不会进入未开放的排行服务。

音量滑块使用 `SliderTrack`、`SliderFill`、`SliderHandle` 三张独立素材，位置读取保存的真实音量；不要改成整张固定刻度图。帮助和关于关闭后回到设置，最终返回时恢复原页面和焦点。修改后的场景已保存，日常调整直接编辑对象即可。一次性布局脚本、素材提取脚本与验证截图存于 `Artifacts/ReferenceMenu/`。

## 独立菜单素材

用户补充的 7 张 PNG 原图完整保存在 `Art/MenuElements/`，导入为保留透明通道的 Sprite。导入区域裁掉透明留边，原始 PNG 不改写；返回按钮区域忽略透明度为 1 的孤立噪点。纹理尺寸上限为 4096，保留原始分辨率。

| 素材 | 场景用途 |
|---|---|
| `StartButton.png` | `Navigation/EntryPage/EnterButton`，点击开始游戏 |
| `ReturnButton.png` | `SettingsPanel/DoneButton` 与 `NoticePanel/CloseButton`，统一返回按钮样式 |
| `SettingsButton.png`、`UserButton.png`、`NoticeButton.png` | `Navigation/UtilityButtons` 下的对应按钮 |
| `NoticeArtwork.png` | `NoticePanel/ReferenceArtwork`，公告山水背景及题字 |
| `DialogPanel.png` | `NoticePanel/InformationArtwork`，帮助与关于的通用绿色面板 |

公告、帮助与关于共用现有窗口流程，按内容切换两种背景。公告隐藏重复标题文字，帮助与关于显示各自标题。返回按钮采用与设置页相同的红金色素材和尺寸，放在面板内底部，正文与按钮之间留出间距，避免压住底层入口图的健康游戏提示文字。按钮隐藏重复文字及额外焦点外框，以按钮本身变色提示选中；关闭后仍按原流程恢复页面和焦点。正文、返回按钮与点击区域保持独立；背景装饰不拦截点击。公告窗口同入口、设置页按 1280 × 720 构图等比适配宽度和高度。用户入口继续保持未开放。素材来源记录、处理前场景备份和运行截图位于 `Artifacts/MenuElements/`。

## 四章插画与卷轴

章节选择页 `Navigation/LevelsPanel` 按用户提供的卷轴及四张独立插画组成。原始 PNG 完整保存在 `Art/ChapterSelection/`；卡片仅在导入设置中裁掉透明留边，未改动原图像素。`ScrollArtwork` 显示背景，四张插画分别来自 `ChapterCatalog.asset` 中对应章节的 `Artwork`，调整章节顺序时插画随配置一起移动。

章节卡片模板 `ChapterCard.prefab` 的 `Artwork` 是独立 Image，`ArtworkHitArea` 负责插画的点击区域。有插画时隐藏通用底图与重复文案，保留独立焦点描边；没有插画时显示原有名称、说明和状态文字。四章按 1280 × 720 构图等比缩放，在窄屏上仍同时显示全部四张；新增章节继续横向滚动，键盘选中屏外章节时自动滚动。返回按钮保留在卷轴之外，章内关卡与解锁流程沿用现有配置。运行截图和一次性安装脚本位于 `Artifacts/ChapterSelection/`。

章节插画由 `MenuButtonFeedback` 提供轻量视觉反馈：悬停或键盘聚焦时提亮约 4.5% 并显示纸色描边，按下时在 0.1 秒内缩至 97%，松开后恢复。`ArtworkHitArea` 是固定的透明点击区域，插画与描边的微缩不会移动点击边界；不要删除这个对象。动画使用独立于游戏时间的计时，禁用按钮或离开页面会立即复位。其他菜单按钮保持原有反馈。

## 章内关卡卷轴背景

关卡选择页当前使用用户提供的卷轴图：`Art/LevelSelection/ParchmentScroll.png` 已去掉外部白底和固定的四个框，保留纸张、题字与折痕；场景入口为 `Navigation/ChapterPanel/ScrollArtwork`。关卡的边框与墨色文字统一在 `LevelCard.prefab` 中编辑，图中四格不限制实际关卡数量。内容继续横向滚动，窄屏减少可见卡片数，键盘选中屏外关卡会自动滚到该卡片。背景装饰不拦截点击，章节配置、解锁和进入关卡的规则不变。处理脚本及运行截图位于 `Artifacts/ScrollLevelSelection/`。

关卡卡片的普通边框与焦点边框共用纸面细线素材，选中和悬停时只加深同一条框线。标题、目标和状态在纸栏中居中排布；重复序号与卡内分隔线默认隐藏，避免叠加通用圆角卡片的视觉样式。相关样式均在 `LevelCard.prefab` 中继承，`Number` 和 `CardRule` 对象仍保留供后续换肤。

## 正常流程的水墨面板与返回按钮

`Art/InkUiCompletion/` 保存同一套青绿水墨、暖色纸面的五张透明 PNG。`ChapterBack.png` 用于 `Navigation/LevelsPanel/BackButton`，`LevelBack.png` 用于 `Navigation/ChapterPanel/BackButton`；两者都包含“返回”题字，原有 Label 保留文案但设为透明，避免叠字。按钮仍继承 `MenuButton.prefab`，专用 Sprite 在对应场景实例中覆盖；104 × 46 的点击区域、返回事件与键盘导航保持原有设置。

其余三张为局内设置、胜负共用结算和计时器面板底图，接线保存在 `BattleGameplay.prefab` 与 `BattleUI.unity`。面板只包含纸面和边饰，标题、时间、音量、胜负说明和按钮仍为独立控件。计时器按当前确认保留纯时间文字，底图与“计时器”标签保持隐藏，新纸面素材留作备用。素材、导入配置和修改后的场景均已保存；生成来源、原图、接线脚本与验证截图保存在 `Artifacts/InkUiCompletion/`。

## 快速换字体

点击菜单 **TapGJ → 打开 UI 字体配置**，把 `.ttf` 或 `.otf` 字体拖到 **界面正文** 槽位，即可统一替换主菜单、局内 UI 和动态卡片的文字。**大标题**、**数字** 可各选一种字体，留空则跟随正文；正文留空则保留原字体。推荐字体文件和许可已放到 `Assets/UI/Fonts/`，直接拖选即可。

字号、颜色和排版仍在对应 Text / Prefab 中调整。字体配置填入字体后，会覆盖 Text 自身的 Font 选择；需要某段文字保留特殊字体时，可禁用该 Text 上的 `UiFontBinding`。

## 按类型统一替换美术

三类 UI 各用一个独立 Prefab，没有相互继承或嵌套。双击对应文件进入 Prefab Mode，修改根对象上的 **Image → Source Image**，以及文字、图标和选中描边即可统一更新这一类。

| Prefab | 用途 | 专用文字 |
|---|---|---|
| `Assets/UI/Prefabs/MenuButton.prefab` | 入口、主菜单、工具栏、返回、关闭、设置和排行榜的普通按钮 | Label |
| `Assets/UI/Prefabs/ChapterCard.prefab` | 章节卡片 | Artwork；无图时显示 Label、Description、Availability |
| `Assets/UI/Prefabs/LevelCard.prefab` | 关卡卡片 | Number、Label、Description、Availability |

两类卡片的场景实例位于 `Navigation/CardTemplates`，运行时按配置数量复制它们。统一更换章节或关卡素材时编辑相应 Prefab；场景模板保留 Prefab 的素材和文字样式继承。章节/关卡的名称、目标、数量仍在 `ChapterCatalog.asset` 中编辑，Prefab 不绑定具体关卡或场景回调。

1. 图片导入为 **Sprite (2D and UI)**，按钮底图建议是没有文字的 PNG。开始按钮为 350 × 60，完成按钮为 240 × 52，章节卡片槽为 196 × 400，关卡卡片模板为 188 × 292；底图建议设置 Sprite Editor 的 **Border**，用九宫格保留边角。章节插画按 Sprite 的原始尺寸与 Pixels Per Unit 显示，本次四张原图采用约 177.97 PPU。
2. 底图使用 **Image Type = Sliced**。彩色素材通常把 Image 的 **Color 改成白色**，避免被当前占位色染暗。开始游戏和完成按钮保留了单独的绿色 Color 覆盖，换彩色图时也把这两处 Color 改成白色，或右键该属性选择 Revert。
3. 只有一张底图时保留 **Button → Transition = Color Tint**，设置高亮、按下和选中颜色。美术提供多张状态图时改成 **Sprite Swap**，设置 Highlighted / Pressed / Selected / Disabled Sprite；普通状态使用 Image 的 Source Image。给键盘导航的 Selected 状态留出明显反馈。
4. `Label` 是独立的 Text，大小、颜色、排版可在对应 Prefab 中统一调整；字体在 `UiFontTheme.asset` 中统一选择。卡片的序号、目标和状态文字也在各自 Prefab 中编辑样式。运行时只填配置文案。开始游戏和完成按钮的文字颜色有单独覆盖，换字体颜色时可分别调整或 Revert。
5. `Icon` 是默认隐藏的图片插槽，尺寸 32 × 32。为按钮实例指定图标后启用它，再按实际图标宽度调整 Label 的左边距。
6. `FocusRing` 是独立的选中描边，图片和颜色也可以替换。`MenuButtonFeedback` 只改变它的透明度，保留按钮底图、字体与颜色。美术已有选中效果时，可以关闭这个子对象，继续使用 Button 的 Selected 状态。

调整单个按钮的位置、尺寸和名称可以保留为场景覆盖。统一样式应在对应 Prefab Mode 中改，避免把位置、导航引用或单个按钮文字 Apply 到 Prefab。不要 Unpack 或删除 Button 组件，否则会断开统一样式或交互引用。卡片的 Number、Label、Description、Availability 名称是文案绑定入口，替换美术时保留这些文字对象及名称；Icon 和 FocusRing 可以独立换图或关闭。

## 其他替换位置

所有路径均从 `MainMenuCanvas` 开始：

页面位于 `Navigation` 下，以下表格中的 `Menu`、`LevelsPanel` 路径需要加上 `Navigation/`。入口为 `EntryPage`，通用关卡页为 `ChapterPanel`，用户/公告/设置在 `UtilityButtons`。章节与关卡数量、名称和目标在 `ChapterCatalog.asset` 中编辑；它们不会写死在页面或脚本中。

| 位置 | 需要替换的内容 | 当前尺寸 / 设置 |
|---|---|---|
| `Background` | 全屏背景，Image → Source Image | 参考画布 1280 × 720；建议 1920 × 1080 或更高，同为 16:9；换彩色图时 Color 设白色 |
| `Menu/Logo` | 游戏 Logo | 420 × 120；默认隐藏，Preserve Aspect 开启；放入图后启用，关闭 `Menu/Title`；若 Logo 自带品牌文字，也可关闭 `Menu/Brand` |
| `Menu/Title`、`Brand`、`Subtitle`、`Footer` | 字体和文字颜色 | 独立 Text；字体需要覆盖中文字符 |
| `Menu/Element_Metal`、`Element_Wood`、`Element_Water`、`Element_Fire`、`Element_Earth` | 五行小图标 | 30 × 30；Preserve Aspect 开启；可关闭对应 `Label`，换彩色图时 Color 设白色 |
| `Navigation/Menu/ArtIllustration` | 主菜单右侧主题插画 | 440 × 440；默认隐藏，白色、不拦截点击、Preserve Aspect；放入素材后启用，并关闭同级 `ElementSeal` |
| `Navigation/EntryPage/ArtIllustration` | 入口中央插画 | 320 × 320；替换后启用，并关闭同级 `ElementSeal` |
| `Navigation/EntryPage/Logo` | 入口 Logo | 500 × 100；默认隐藏；启用后关闭同级 `Title`，需要时关闭 `Brand` |
| `Atmosphere` | 细线占位背景装饰 | 可整组关闭，使用全屏 Background 正式素材 |
| `LevelsPanel/ScrollArtwork` | 四章卷轴背景 | 1280 × 720 整体构图；等比缩放 |
| `ChapterPanel` | 章内关卡页底图 | 最大 1160 × 560；窄屏按可见宽度缩窄；Image Type = Sliced |
| `SettingsPanel` | 设置页背景 | 1280 × 720；按参考画面等比适配；Image Type = Sliced |
| `NoticePanel` | 公告背景与帮助、关于的通用面板 | 1280 × 720 构图；背景见其下两个 Artwork 对象 |
| `Navigation/EntryPage/WelcomeBar` | 保留的公告横条 | 当前参考布局隐藏；公告由右上按钮打开 |
| `LeaderboardPanel` | 排行榜弹窗底图 | 960 × 620；Image Type = Sliced |
| `LeaderboardPanel/AccountCard` | 个人成绩和登录区域底图 | 896 × 102；最快用时、排名、登录状态均为独立 Text |
| `LeaderboardPanel/ScrollView/Viewport/Content/Row01`～`Row10` | 榜单行底图、排名、昵称、用时 | 每行 896 × 48；可统一替换底图；装饰图片不要开启 Raycast Target |
| `LeaderboardPanel/EmptyState` | 空榜提示和装饰 | 默认显示，不使用虚构玩家成绩 |
| `Navigation/CardTemplates/ChapterCard`、`LevelCard` | 动态卡片模板 | 默认隐藏；底图为 Sliced；Number、Label、Description、Availability 分别是序号、名称、目标和状态。修改模板样式后，动态复制的卡片保留该样式 |
| `LevelsPanel/ScrollView`、`ChapterPanel/ScrollView` | 横向滚动区 | HorizontalLayoutGroup 与 ContentSizeFitter 按卡片数量排布；支持滚轮、触摸与键盘自动滚动 |
| 两个选择页的 `BackButton` | 返回按钮底图 | 104 × 46；保留 Button 与控制器引用 |
| `SettingsPanel/MusicSlider/Track`、`EffectsSlider/Track` | 音量轨道底图 | 208 × 14；Sliced |
| 两条 Slider 的 `FillArea/Fill` | 已填充部分 | Sliced；保留 Slider 的 Fill Rect 引用 |
| 两条 Slider 的 `HandleArea/Handle` | 滑块手柄 | 36 × 36；Simple + Preserve Aspect，根控件高 48；不要删除 Handle Rect 和 Target Graphic 引用 |
| `TopAccent` | 顶部装饰线 | 可换图、改色或直接关闭 |

装饰文字、Logo、Icon 不拦截点击（Raycast Target 关闭）。按钮、面板遮挡和 Slider 根对象负责点击，换图时保留这些控件及其引用。

当前圆角、圆阵和细线都是可替换的占位资源，没有接入付费素材生成。`UiSurface`、`UiFocus`、`UiRing`、`UiDisc` 是普通 Sprite；正式底图直接替换对应 Source Image 即可。圆阵由普通 Image/Text 构成，可关闭整组，用预留插画插槽替换。不要把带文字的整页美术图盖在动态章节区域上，避免配置数量变化后与真实按钮不对应。

屏幕适配由 CanvasScaler 与 `MenuResponsiveLayout` 共同处理。章节卷轴整体等比缩放，四张插画保持完整；章内关卡页按可见宽度调整滚动区。卡片正文样式来自模板，章节插画来自章节配置。布局组件不覆盖 Source Image、字体或颜色；需要手工重排整个页面时，可禁用该组件。

## 换完后检查

在编辑器打开 MainMenu 并 Play：检查入口、三个主菜单按钮、用户/公告/设置、章节和关卡卡片、返回与 Esc、弹窗遮罩以及键盘反馈。修改配置中的章节与关卡数量，重新打开章节页确认排布和滚动。排行榜支持滚轮、触摸以及 Page Up / Page Down。切换 Game 窗口到 16:9、4:3 和手机横屏比例，确认文字、滑块和卡片没有裁切。音频可单独拖到 `GameAudio/Music` 的 AudioSource；音量设置继续有效。

## 排行榜页面接入点

排行榜界面和 10 个行槽都保存在 MainMenu 场景中，`LeaderboardPanel` 不在运行时生成或重设美术样式。当前排行榜入口暂时锁定，按钮显示锁图标和“暂未开放”，不可点击或用键盘进入；用户按钮也不能绕过锁定。登录与排行榜服务尚未连接，面板保留用于后续接入。

平台接入层订阅 `LoginRequested` 和 `RefreshRequested(Scope)`；登录完成后调用 `ShowAccount`，榜单返回后调用 `ShowEntries`（按榜单顺序传入前 10 名）。耗时统一使用整数毫秒，显示为 `分:秒.百分秒`。失败时调用 `ShowUnavailable`。异步响应需确认 `SelectedScope` 仍与请求对应，避免把全服榜结果显示到好友榜。
