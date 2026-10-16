# World Tile Mutator Editor 设计方案

## 1. 目标

制作一个独立、轻量的 RimWorld 1.6 / Odyssey 模组，为选中的世界地图格提供两类编辑：

1. 查看、添加和移除当前游戏已加载的 `TileMutatorDef`。
2. 修改该格的山地等级：平地、小丘陵、大丘陵、山地、不可通行。

典型目标包括“动物繁多”“沃土”“野生七色花”等 Odyssey 或其他模组提供的地图元素。元素名称必须从运行时 Def 读取，不能把原版清单写死。

建议名称：`World Tile Mutator Editor`

建议 packageId：`Local.WorldTileMutatorEditor`

## 2. 可行性结论

方案可行，且不需要修改 World Edit 2.0 的文件或复用其程序集。

原版 `RimWorld.Planet.Tile` 已提供：

- `IList<TileMutatorDef> Mutators`
- `AddMutator(TileMutatorDef mutator)`
- `RemoveMutator(TileMutatorDef mutator)`
- `GetGizmos()`
- `ExposeData()`

地图元素通过 `DefDatabase<TileMutatorDef>.AllDefsListForReading` 动态枚举，因此能自动看到 Odyssey、Geological Landforms 以及其他已加载模组新增的元素。`Tile` 的存档字段使用 Def 引用保存，不需要另建 WorldComponent 保存元素列表。

山地值是 `RimWorld.Planet.Hilliness` 枚举：

| 枚举 | 中文显示 |
|---|---|
| `Flat` | 平地 |
| `SmallHills` | 小丘陵 |
| `LargeHills` | 大丘陵 |
| `Mountainous` | 山地 |
| `Impassable` | 不可通行 |

World Edit 2.0 当前 1.6 版本仍主要编辑旧的生物群落、道路、河流和山地字段，没有调用 `Tile.AddMutator` / `RemoveMutator`，因此无法解决用户遇到的地图元素缺失问题。

## 3. 与其他模组的关系

- 必需：RimWorld 1.6；使用地图元素时必需 Odyssey。
- 可选：Harmony，仅在实现 `Tile.GetGizmos()` 的 Postfix 入口时使用。
- 不依赖：World Edit 2.0、Geological Landforms、Prepare Landing。
- 不为单个内容模组制作兼容补丁。运行时读取 Def 是兼容策略。
- Geological Landforms 的地图形态在地图生成阶段生成；本模组只编辑其暴露为 `TileMutatorDef` 的内容，不修改其 landform XML 或节点编辑器。

## 4. UI 与操作流程

### 4.1 入口

在世界地图选中一个世界格时，通过 `Tile.GetGizmos()` 添加“编辑地图元素 / 山地” Gizmo。入口在以下阶段都可见，但按钮状态根据地块状态变化：

- 创建世界后、确认降落点前：可完整编辑。
- 已有殖民地但正在世界地图浏览其他格：未生成格可编辑，已生成格默认只读。
- 任务地点、敌方据点等已生成地图：默认只读并显示原因。

不增加全世界批量修改，避免误操作和额外性能负担。

### 4.2 窗口布局

窗口分为四区：

1. 地块摘要：坐标、层、生物群落、当前山地等级、是否已有本地地图。
2. 当前元素：逐项显示 `mutator.Label(tile)`、来源模组和移除按钮。
3. 可添加元素：搜索框、来源模组筛选、类别筛选、兼容状态和添加按钮。
4. 山地编辑：下拉选择五个 `Hilliness` 值，显示当前值和变更影响，单独确认。

显示名称必须调用 `Label(tile)`；不能只显示 Def 的静态 `label`，因为 `AnimalHabitat`、`WildPlants` 等 Worker 可能按动物或植物动态生成标签。说明使用 `Description(tile)`。

## 5. 有效性、冲突与确认规则

### 5.1 元素枚举

启动窗口时动态获取全部 Def，并先过滤 `EverValid() == true`。对每个候选项调用 `IsValidTile(tile, tile.Layer)`。

### 5.2 默认规则

- 已存在的同一 Def 禁止重复添加。
- `IsValidTile` 为 false 的元素默认禁用，并显示不满足的条件。
- 添加必须调用原生 `tile.AddMutator(def)`，移除必须调用 `tile.RemoveMutator(def)`，不能直接改 `Mutators` 列表。
- 原生类别、覆盖类别和优先级规则是最终裁决。添加可能替换同类别旧元素时，先显示替换预览并要求确认。
- 第一版不提供绕过 `IsValidTile` 或强制叠加冲突的模式。

需在界面展示的限制包括：`categories`、`overrideCategories`、`priority`、生物群落白/黑名单、山地范围、海岸边数、河流/道路/地标条件、污染/温度/动植物密度范围，以及自定义 Worker 的有效性。

### 5.3 山地修改规则

山地修改只写入地块的 `Hilliness` 属性/字段，并提示该值会影响可通行性、矿物/洞穴生成条件、地标和部分地图生成器。修改后刷新世界渲染层；不刷新、不重建已经存在的本地地图。

如果新等级与当前已有元素的 `minHilliness` / `maxHilliness` 或 Worker 有冲突，先列出受影响元素并要求确认。默认仍禁止把已生成地图改成不可通行。

## 6. 地图生成阶段与可用范围

“随时可用”不等于“随时重建地图”。规则如下：

| 地块状态 | 查看 | 添加/移除元素 | 修改山地 |
|---|---:|---:|---:|
| 尚未生成本地地图 | 是 | 是 | 是 |
| 已生成但非玩家地图 | 是 | 默认只读 | 默认只读 |
| 玩家殖民地/任务地图存在 | 是 | 只读 | 只读 |

未生成格的修改会随世界保存，并在首次进入该格生成本地地图时由原版和相关模组读取。已生成格的地形、海岸、湖泊、沃土、古代结构和野生植物初始分布通常已经落地；自动重生成会破坏建筑、单位和存档，因此第一版明确不做。

如果未来需要编辑已生成地图，应另做显式的高风险“重建地图”功能，并要求备份存档；不属于本项目第一阶段。

## 7. 存档与排序

- 只保存原版 `Tile` 已保存的数据，不新增迁移格式。
- 移除本模组后，已写入的山地和地图元素仍是原版数据；缺少提供 Def 的内容模组时，存档会按 RimWorld 的缺失 Def 规则处理，文档需提醒用户不要在存档中途移除内容模组。
- 模组应放在 Odyssey 之后，World Edit 2.0 之后或之前均不依赖其实现；若使用 Harmony，声明依赖并让 RimSort 自动排序。

## 8. 分阶段实施

### 阶段 A：最小核心

- 建立 1.6 独立模组骨架和 packageId。
- 通过 Harmony Postfix 向 `Tile.GetGizmos()` 注入入口（若测试表明原生 Gizmo 足够，则改用无补丁入口）。
- 窗口显示当前元素和山地等级，完成只读状态判断。

验收：进入世界地图可打开窗口；不修改任何数据时无异常日志。

### 阶段 B：元素编辑

- 动态枚举 Def、搜索和来源显示。
- 实现 `IsValidTile` 检查、重复检查、冲突替换预览。
- 使用 `AddMutator` / `RemoveMutator` 写入。

验收：至少验证 `AnimalLife_Increased`、`Fertile`、`AnimalHabitat`、`PlantGrove`、`WildPlants`；动态标签和说明正确显示。

### 阶段 C：山地编辑

- 添加五级 `Hilliness` 选择。
- 处理与元素条件冲突的提示。
- 修改后调用世界渲染器的地块/图层刷新 API；不重建本地地图。

验收：五个枚举值可保存、读档后保持；未生成格首次落地时地图生成结果符合新值。

### 阶段 D：兼容与体验

- 检查 Odyssey、Geological Landforms、Prepare Landing、World Edit 2.0 同时启用。
- 增加中文和英文 Keyed 翻译。
- 处理 Def 在运行期缺失、无效或来源模组被移除时的只读提示。

验收：无 Harmony 错误、无重复 Gizmo、无批量误修改；RimSort 能按 About.xml 依赖排序。

## 9. 测试矩阵

1. 仅 Core + Odyssey：确认完整原版 Def 清单可见。
2. 加入 Geological Landforms：确认其 Tile Mutator 可见，未修改其 landform 配置。
3. 加入含自定义 Worker 的内容模组：确认无效地块被正确禁用。
4. 创建世界后、降落点确认前修改：确认保存并在首次生成地图时生效。
5. 已生成殖民地格：确认窗口只读，建筑和地图不被重建。
6. 冲突元素：确认显示将被替换的元素，取消确认后数据不变。
7. 存档/读档：确认山地和元素列表保持，缺失 Def 只产生明确提示。
8. 移除本模组后读档：确认不会留下自定义保存组件或类型错误。

## 10. 风险与验收标准

主要风险是把地图生成期数据误认为运行期数据，以及无视类别冲突强行叠加元素。第一版通过状态锁定、原生 API、冲突预览和不重建已有地图来控制风险。

项目完成标准：

- 运行时能列出所有已加载 `TileMutatorDef`，包括动态标签元素。
- 合法元素可添加、移除并正常保存。
- 山地五级可编辑并正常保存。
- 已生成地图不会被自动重建或破坏。
- 与 World Edit 2.0、Geological Landforms、Prepare Landing 同时启用时无重复入口和红字错误。
- 文档中的测试矩阵全部通过后，才进入实际程序集编写。

## 11. 明确不做的内容

- 不修改 World Edit 2.0 源码。
- 不编辑 Odyssey 或其他模组的 Def 文件。
- 不提供全世界批量刷元素。
- 不自动重建已有本地地图。
- 不承诺绕过原生有效性检查后所有组合仍然有合理的地图生成结果。

## 12. 当前实现状态

本轮已完成阶段 A 至阶段 C 的可编译首版：

- `1.6/Assemblies/WorldTileMutatorEditor.dll`：独立程序集。
- `Tile.GetGizmos()`：向选中世界格添加入口。
- 窗口：显示当前元素、搜索候选元素、添加/移除、元素说明和山地选择。
- 写入：使用原生 `AddMutator` / `RemoveMutator`；山地字段通过 Harmony `AccessTools` 写入，并刷新世界渲染层。
- 安全规则：有活动本地地图的格只读；类别冲突和山地条件冲突会二次确认。
- 翻译：English 与 ChineseSimplified Keyed 文件已加入。

本轮只做静态编译和 XML 校验，没有启动 RimWorld 进行进游戏验证。进入游戏前仍需按测试矩阵检查 Gizmo 是否在所有世界地图页面显示，以及具体内容模组的自定义 Worker 是否存在特殊副作用。
