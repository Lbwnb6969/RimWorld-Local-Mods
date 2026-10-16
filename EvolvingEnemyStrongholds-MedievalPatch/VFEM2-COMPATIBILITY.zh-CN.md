# Evolving Enemy Strongholds 与 Vanilla Expanded 基地兼容性说明

## 调查范围

本文只分析以下三个模组在 RimWorld 1.6 下的敌方据点生成关系，并为后续兼容补丁确定边界：

| Workshop ID | 模组 | Package ID | 本轮结论 |
|---|---|---|---|
| `3738072776` | Evolving Enemy Strongholds（EES） | `wowgag.EvolvingEnemyStrongholds` | 保留，负责 T1-T13 据点成长与强化 |
| `3209927822` | Vanilla Base Generation Expanded（VBGE） | `VanillaExpanded.BaseGeneration` | 建议移除 |
| `3444347874` | Vanilla Factions Expanded - Medieval 2（VFE Medieval 2） | `OskarPotocki.VFE.Medieval2` | 必须保留，后续做小范围协调补丁 |

本文所说的“兼容”分为两层：能否同时加载，以及同时加载后生成的基地是否合理。没有发现三者之间必然导致启动失败的静态依赖冲突，但两个基地生成系统能同时执行，不代表生成结果已经平衡或不会互相覆盖。

## “Vanilla Expanded” 指的是哪个模组

EES 的 `About.xml` 明确声明：

> This mod replaces enemy faction base generation. Do not use it together with other faction base generation overhaul mods.

结合实际 Def，页面所说的 “Vanilla Expanded” 应理解为 **Vanilla Base Generation Expanded**，而不是泛指所有名称中带 Vanilla Expanded 的模组。

原因如下：

- VBGE 的用途就是全局改造敌方基地生成，并通过 `KCSG.CustomGenOption` 修改原版帝国、部落、外来者和海盗等基础 FactionDef。
- VFE Medieval 2 是派系与中世纪内容模组。它也会替换自己所属派系的敌方据点，但范围限定在其王国、维京氏族和商人公会，不是对全部原版敌方派系的全局基地改造。
- VFE Medieval 2 是用户当前玩法的必要内容和依赖目标，不能因为页面上的简称而误删。

## 三个模组分别如何生成基地

### Vanilla Base Generation Expanded

VBGE 给多个原版派系基类加入 `KCSG.CustomGenOption`，从 `VBGE_` 开头的生产、采矿、奴役、伐木、防御、农田等布局中选择基地。它与 EES 都以“全面替换或强化普通敌方 Settlement”为核心目的，职责直接重叠。

### Vanilla Factions Expanded - Medieval 2

VFE Medieval 2 确实会把所属派系的据点替换为中世纪布局，不只是添加兵种和建筑。静态 Def 中存在以下绑定：

| 派系 Def | 科技等级 | KCSG 据点布局 |
|---|---|---|
| `VFEM2_KingdomCivil`、`VFEM2_KingdomRough`、`VFEM2_KingdomSavage` | `Medieval` | 继承并选择 `VFEM2_MedievalFactionBase` |
| `VFEM2_ClanRough`、`VFEM2_ClanSavage`、`VFEM2_CivilClan` | `Medieval` | 继承并选择 `VFEM2_VikingFactionBase` |
| `VFEM2_MerchantGuild` | `Medieval` | 选择 `VFEM2_MerchantFactionBase` |

这些 KCSG 布局包含 VFE2 自己的城堡墙、低矮城墙、木栅栏、城门、石弩炮台、火绳枪炮台以及中世纪生产和生活设施。因此，VFE Medieval 2 的“敌方据点替换”应当保留为这些派系的视觉与基础布局来源。

### Evolving Enemy Strongholds

EES 在普通敌方 Settlement 的基础生成完成后继续构造强据点，增加层级、外围、核心、防御、驻军、角色设施和战利品。它包含针对已有自定义布局的叠加路径，因此 VFE2 的 KCSG 基地通常不会简单消失，而是先生成 VFE2 中世纪基地，再由 EES 叠加强化内容。

当前 `EvolvingEnemyStrongholds-MedievalPatch` 按 `FactionDef.techLevel == Medieval` 处理派系，所以 VFE2 的上述派系会进入中世纪 T1-T13 成长、研究门槛、无电防御和中世纪奖励替换逻辑。

## 兼容性结论

### EES 与 VBGE：不建议共用

两者技术上可能同时完成地图生成，但不属于推荐兼容组合。主要风险是：

- VBGE 先生成一整套大型 KCSG 基地，EES 再增加自己的外围和核心，导致基地尺寸及建筑密度失控。
- 两套系统都可能加入防御、驻军和战利品，使威胁和收益重复膨胀。
- EES 墙体或中心区可能覆盖、切断 VBGE 房间和道路。
- 当前中世纪补丁主要约束 EES 新增内容，不能保证清除 VBGE 底层布局中的电力、工业炮台和现代设施。
- EES 作者已明确要求不要与其他全局 faction base generation overhaul 共用。

**建议移除 `3209927822` Vanilla Base Generation Expanded。** 这是最小、最稳定的处理，不值得为两个职责相同的全局生成器编写复杂兼容层。

移除 VBGE 时只移除 `VanillaExpanded.BaseGeneration`。不要移除 `2023507013` Vanilla Expanded Framework（`OskarPotocki.VanillaFactionsExpanded.Core`），因为 VFE Medieval 2 仍依赖该框架。

### EES 与 VFE Medieval 2：可保留，但需要协调

这组组合应保留。VFE2 提供中世纪派系、兵种、建筑和派系专属 KCSG 据点，EES 提供从小定居点到 T13 超级要塞的长期成长。

静态检查没有发现必须二选一的加载级冲突，但当前结果仍存在以下需要补丁协调的风险：

- EES 可能在 VFE2 城堡或村镇外围继续造墙和房间，出现道路被截断、双层城墙或局部覆盖。
- 当前中世纪补丁只明确允许 Medieval Overhaul 来源的中世纪炮台成为派系自有炮台，尚未明确接纳 `OskarPotocki.VFE.Medieval2` 的无电炮台。
- 当前高阶墙体统一倾向 Medieval Overhaul 城堡墙，可能削弱 VFE2 王国和氏族原有的城堡、木栅栏视觉识别。
- VFE2 KCSG 原始布局不在 EES 新增区域的局部清理保证范围内，需要另行检查是否残留不符合当前中世纪整合环境的物件。
- 两边的驻军和奖励若同时完整叠加，可能让后期基地强度与收益超过原定成长曲线。

因此结论是：**可以同时保留和加载，但后续应做一个只针对 VFE Medieval 2 派系的小型协调补丁，不能把现状描述为无需处理的完全兼容。**

## 已实现的协调补丁

本轮已将下列协调写入现有 `EES.MedievalPatch.dll`，不新增第二个重复程序集：

- 通过 `OskarPotocki.VFE.Medieval2` 的 packageId 和六个具体敌对派系 Def 精确识别 VFE2；不匹配玩家王国、商人移动基地或其他 Vanilla Expanded 派系。
- 将 VFE2 的两种无电中世纪炮台加入派系炮台所有权和候选池，并明确限制为 `VFEM2_Turret_WallMountedArbalest` 与 `VFEM2_Turret_WallMountedArquebus`。
- VFE2 氏族 T1-T4 外墙优先使用 `VFEM2_Palisade`，VFE2 王国/氏族 T5 以上优先使用 `VFEM2_CastleWall`；其它中世纪派系继续走原 Medieval Overhaul 墙体逻辑。
- 在普通敌对 VFE2 Settlement 的 `GenStep_Settlement.ScatterAt` 生成后，对整张地图执行现有中世纪物资替换和现代建筑清理。清理范围包括电力建筑及原有现代设施黑名单。
- 不把 VFE2 的 2-4 格城门接入 EES 单格房门接口，避免多格建筑导致房间生成或道路布局失败；EES 新建房间仍使用 Medieval Overhaul 石板门。

### 本轮静态验收结果

- `dotnet build EES.MedievalPatch.csproj --no-restore`：通过，0 警告、0 错误。
- VFE2 王国、氏族、商人公会 FactionDef XML：解析通过；六个敌对派系、玩家派系和商人移动基地边界已核对。
- VFE2 墙体与炮台 Def：`VFEM2_Palisade`、`VFEM2_CastleWall`、两种 `VFEM2_Turret_WallMounted*` 均存在。
- 两种炮台均为 `TechLevel.Medieval`，未声明 `CompPowerTrader`；电力炮台会继续被中世纪过滤器移除。
- VFE2 KCSG `CustomGenDefs` 未发现通讯台、深钻、地质扫描器、高科技研究台、太阳能/地热发电、迫击炮等现代布局关键词。
- `GenStep_Settlement.ScatterAt` 当前程序集目标存在，但属于保护方法，补丁已改用 `AccessTools.Method` 反射定位。
- 本地补丁没有对 `VanillaExpanded.BaseGeneration` 的依赖，也没有修改 `ModsConfig.xml`。

### 全链路验收表

| 环节 | 验收结果 | 说明 |
|---|---|---|
| 派系识别 | 通过 | 只接受 VFE2 六个王国/氏族敌方 Def；玩家王国和商人公会不在名单内 |
| 基础布局 | 通过 | 王国与氏族的 `KCSG.CustomGenOption` 保持原样，分别保留城堡/村镇与维京布局 |
| 成长入口 | 通过 | 六个目标派系均继承 `TechLevel.Medieval`，继续进入现有 EES 中世纪成长逻辑 |
| T1-T13 上限 | 通过 | `EraFor` 中世纪分支仍替换为 `(1, 3, 13, 13)`，没有被本轮协调逻辑覆盖 |
| 研究进度 | 通过 | 六个顺序研究 Def 均存在；`TechProgress01` 仍按前置连续完成计算 |
| 角色设施 | 通过 | 保留补给、监牢、黑市、巫术祭坛、贵族庄园和指挥要塞；现代通讯、弹药库和迫击炮关闭 |
| 外墙视觉 | 通过 | 氏族低阶木栅栏，王国/氏族高阶 VFE2 城堡墙，其他中世纪派系保持 Medieval Overhaul 墙体 |
| 门与地面 | 通过 | 单格石板门和中世纪鹅卵石地面逻辑保持；多格 VFE2 城门不误接入单格生成接口 |
| 炮台 | 通过 | VFE2 两种明确白名单炮台可进入池；超中世纪或耗电候选继续过滤 |
| 驻军与 CE | 通过 | 中世纪口粮、指挥官类型限制、CE 机枪升级及 CE 弹药奖励逻辑保持 |
| 战利品 | 通过 | EES 房间与 VFE2 整张敌对 Settlement 地图均执行现有中世纪物资替换 |
| 现代设施清理 | 通过 | 原黑名单与所有耗电建筑在目标地图清理；VFE2 KCSG 未检出常见现代设施关键词 |
| 特殊地图排除 | 通过 | 生成后清理要求 `map.Parent is Settlement`、目标派系在六项白名单中且敌对；移动基地、Site 和玩家据点不匹配 |
| 可选兼容 | 通过 | VFE2 仅通过 packageId、Def 名和静默 Def 查询接入；未安装 VFE2 时不会形成程序集硬引用 |
| VBGE 移除 | 通过 | About、源码和运行补丁均不依赖 `VanillaExpanded.BaseGeneration` |

## 保留的风险边界

本轮不重写 VFE2 的 KCSG 布局，也不接管所有 Vanilla Expanded 模组。EES 对自定义布局的 overlay 路径继续负责外围扩建、驻军、指挥官和奖励；现有静态资料不足以证明这里必须再覆盖核心算法。若以后从实际生成日志或地图结果确认房间被切断、奖励重复或驻军超量，再针对可复现问题做局部修复。

### 不应处理

- 不删除 VFE Medieval 2 的 `KCSG.CustomGenOption`，否则会失去它的中世纪据点视觉和房间设计。
- 不让补丁作用于玩家派系 `VFEM2_PlayerKingdom`。
- 不把 `VFEM2_MerchantGuildCaravan` 移动基地强行改造成 EES 要塞；它使用 `VEF.Planet.MovingBaseDef` 和专用 WorldObject 类，应继续由 VFE2 管理。
- 不接管 VFE2 的城堡围攻、遭遇战、营地、任务 Site 或已摧毁商人营地。EES 的成长目标只限普通敌对 `Settlement`。
- 不为已经建议移除的 VBGE 编写布局级兼容补丁。
- 不修改 Workshop 原模组，也不自动修改 `ModsConfig.xml`。

## 推荐加载关系

概念上的顺序如下，实际仍由各模组的 `packageId` 与 `loadAfter` 决定：

1. Harmony
2. Vanilla Expanded Framework（`2023507013`）
3. Medieval Overhaul 及其必要依赖
4. Vanilla Factions Expanded - Medieval 2（`3444347874`）
5. Evolving Enemy Strongholds（`3738072776`）
6. Evolving Enemy Strongholds - Medieval Overhaul Patch

`3209927822` Vanilla Base Generation Expanded 不放入该组合。

## 已完成的静态验收

- 所有 VFE2 王国和氏族派系仍保有各自的 `chooseFromSettlements`，且玩家派系不受处理。
- EES 仍能识别这些派系为 `TechLevel.Medieval`，T1-T13 成长逻辑不被跳过。
- 两个 VFE2 炮台的电力 Comp、科技等级、射击组件和所属模组检查通过后才进入候选池。
- VFE2 王国、氏族与其他中世纪派系分别取得预期墙体和门，不发生全局视觉替换。
- KCSG 布局内的所有 ThingDef 引用均存在；被允许的 VFE2 中世纪设施不会被误删。
- 移动商人公会、Site、任务营地和玩家据点均不进入 EES 普通敌对 Settlement 路径。
- 不存在对 `VanillaExpanded.BaseGeneration` 的硬依赖；移除 VBGE 后本地补丁仍可加载。
- About 依赖和加载顺序只保留实际必要项，不修改 `ModsConfig.xml`。

## 最终建议

当前最合理的模组组合是：保留 VFE Medieval 2、EES、Vanilla Expanded Framework、Medieval Overhaul 和现有 EES 中世纪补丁；移除 Vanilla Base Generation Expanded。VFE2 普通敌对 Settlement 的派系专属协调逻辑已并入现有本地补丁。

这样既保留 VFE Medieval 2 将敌方据点替换为城堡、村镇和氏族营地的特色，也保留 EES 从小定居点成长到超级要塞的长期玩法，同时避免两个全局基地生成 overhaul 相互叠加。
