# 主菜单美术替换

场景：`Assets/Scenes/MainMenu.unity`。美术资源放到 `Assets/UI/Art/`。页面和卡片模板保存为 uGUI 对象，运行时按 `Assets/UI/Data/ChapterCatalog.asset` 的数组数量复制卡片；页面控制器只填名称、目标、状态，不覆盖 Sprite 或颜色。字体统一由 `Assets/UI/Data/UiFontTheme.asset` 控制。

## 快速换字体

点击菜单 **TapGJ → 打开 UI 字体配置**，把 `.ttf` 或 `.otf` 字体拖到 **界面正文** 槽位，即可统一替换主菜单、局内 UI 和动态卡片的文字。**大标题**、**数字** 可各选一种字体，留空则跟随正文；正文留空则保留原字体。推荐字体文件和许可已放到 `Assets/UI/Fonts/`，直接拖选即可。

字号、颜色和排版仍在对应 Text / Prefab 中调整。字体配置填入字体后，会覆盖 Text 自身的 Font 选择；需要某段文字保留特殊字体时，可禁用该 Text 上的 `UiFontBinding`。

## 按类型统一替换美术

三类 UI 各用一个独立 Prefab，没有相互继承或嵌套。双击对应文件进入 Prefab Mode，修改根对象上的 **Image → Source Image**，以及文字、图标和选中描边即可统一更新这一类。

| Prefab | 用途 | 专用文字 |
|---|---|---|
| `Assets/UI/Prefabs/MenuButton.prefab` | 入口、主菜单、工具栏、返回、关闭、设置和排行榜的普通按钮 | Label |
| `Assets/UI/Prefabs/ChapterCard.prefab` | 章节卡片 | Number、Label、Description、Availability |
| `Assets/UI/Prefabs/LevelCard.prefab` | 关卡卡片 | Number、Label、Description、Availability |

两类卡片的场景实例位于 `Navigation/CardTemplates`，运行时按配置数量复制它们。统一更换章节或关卡素材时编辑相应 Prefab；场景模板保留 Prefab 的素材和文字样式继承。章节/关卡的名称、目标、数量仍在 `ChapterCatalog.asset` 中编辑，Prefab 不绑定具体关卡或场景回调。

1. 图片导入为 **Sprite (2D and UI)**，按钮底图建议是没有文字的 PNG。开始按钮为 350 × 60，完成按钮为 240 × 52，章节卡片模板为 242 × 292，关卡卡片模板为 188 × 292；底图建议设置 Sprite Editor 的 **Border**，用九宫格保留边角。
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
| `LevelsPanel`、`ChapterPanel` | 章节和关卡页底图 | 最大 1160 × 560；窄屏按可见宽度缩窄；Image Type = Sliced |
| `SettingsPanel`、`NoticePanel` | 弹窗底图 | 640 × 400；Image Type = Sliced |
| `Navigation/EntryPage/WelcomeBar` | 公告横条 | 800 × 54；公告标签与正文独立 |
| `LeaderboardPanel` | 排行榜弹窗底图 | 960 × 620；Image Type = Sliced |
| `LeaderboardPanel/AccountCard` | 个人成绩和登录区域底图 | 896 × 102；最快用时、排名、登录状态均为独立 Text |
| `LeaderboardPanel/ScrollView/Viewport/Content/Row01`～`Row10` | 榜单行底图、排名、昵称、用时 | 每行 896 × 48；可统一替换底图；装饰图片不要开启 Raycast Target |
| `LeaderboardPanel/EmptyState` | 空榜提示和装饰 | 默认显示，不使用虚构玩家成绩 |
| `Navigation/CardTemplates/ChapterCard`、`LevelCard` | 动态卡片模板 | 默认隐藏；底图为 Sliced；Number、Label、Description、Availability 分别是序号、名称、目标和状态。修改模板样式后，动态复制的卡片保留该样式 |
| `LevelsPanel/ScrollView`、`ChapterPanel/ScrollView` | 横向滚动区 | HorizontalLayoutGroup 与 ContentSizeFitter 按卡片数量排布；支持滚轮、触摸与键盘自动滚动 |
| 两个选择页的 `BackButton` | 返回按钮底图 | 104 × 46；保留 Button 与控制器引用 |
| `SettingsPanel/MusicSlider/Track`、`EffectsSlider/Track` | 音量轨道底图 | 276 × 8；Sliced |
| 两条 Slider 的 `FillArea/Fill` | 已填充部分 | Sliced；保留 Slider 的 Fill Rect 引用 |
| 两条 Slider 的 `HandleArea/Handle` | 滑块手柄 | 22 × 22；Simple + Preserve Aspect，保留较大的拖动区域；不要删除 Handle Rect 和 Target Graphic 引用 |
| `TopAccent` | 顶部装饰线 | 可换图、改色或直接关闭 |

装饰文字、Logo、Icon 不拦截点击（Raycast Target 关闭）。按钮、面板遮挡和 Slider 根对象负责点击，换图时保留这些控件及其引用。

当前圆角、圆阵和细线都是可替换的占位资源，没有接入付费素材生成。`UiSurface`、`UiFocus`、`UiRing`、`UiDisc` 是普通 Sprite；正式底图直接替换对应 Source Image 即可。圆阵由普通 Image/Text 构成，可关闭整组，用预留插画插槽替换。不要把带文字的整页美术图盖在动态章节区域上，避免配置数量变化后与真实按钮不对应。

屏幕适配由 CanvasScaler 按高度保持字号，`MenuResponsiveLayout` 调整页面边距和滚动区宽度。卡片尺寸与正文样式来自模板，4:3 下通过横向滚动容纳更多卡片。它不覆盖 Source Image、字体或颜色；需要手工重排整个页面时，可禁用该组件。

## 换完后检查

在编辑器打开 MainMenu 并 Play：检查入口、三个主菜单按钮、用户/公告/设置、章节和关卡卡片、返回与 Esc、弹窗遮罩以及键盘反馈。修改配置中的章节与关卡数量，重新打开章节页确认排布和滚动。排行榜支持滚轮、触摸以及 Page Up / Page Down。切换 Game 窗口到 16:9、4:3 和手机横屏比例，确认文字、滑块和卡片没有裁切。音频可单独拖到 `GameAudio/Music` 的 AudioSource；音量设置继续有效。

## 排行榜页面接入点

排行榜界面和 10 个行槽都保存在 MainMenu 场景中，`LeaderboardPanel` 不在运行时生成或重设美术样式。当前排行榜入口暂时锁定，按钮显示锁图标和“暂未开放”，不可点击或用键盘进入；用户按钮也不能绕过锁定。登录与排行榜服务尚未连接，面板保留用于后续接入。

平台接入层订阅 `LoginRequested` 和 `RefreshRequested(Scope)`；登录完成后调用 `ShowAccount`，榜单返回后调用 `ShowEntries`（按榜单顺序传入前 10 名）。耗时统一使用整数毫秒，显示为 `分:秒.百分秒`。失败时调用 `ShowUnavailable`。异步响应需确认 `SelectedScope` 仍与请求对应，避免把全服榜结果显示到好友榜。
