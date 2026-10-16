# RimDark 40k - Mankind's Finest 中世纪适配补丁设计文档

## 1. 文档状态

- 状态：第一版补丁已完成；本文件保留设计范围、已知限制和后续检查项
- 目标原模组：`RimDark 40k - Mankinds Finest`
- Workshop 目录：`3505068525`
- 原模组版本：`1.1.14`
- 首个实现目标：RimWorld 1.6
- 必需依赖：Biotech、RimDark 40k - Framework、Fantasy Biotech
- 中世纪基因模组：Workshop `3632363090`，包 ID `vr.fantasy.biotech`
- 默认实现形式：独立兼容补丁，不直接修改 Workshop 文件

## 2. 目标

让科技水平停留在中世纪的玩家，能够从研究开始，完整走通 Mankind's Finest 的生物改造内容：

1. 雷霆战士（Thunder Warrior）
2. 初生星际战士（Firstborn Space Marine）
3. 原铸星际战士（Primaris Space Marine）
4. 禁军（Custodes）
5. 基因原体（Primarch）
6. 军团基因材料、军团归属和相关基因种子循环
7. 初生转化为原铸的 Rubicon 手术
8. 基因种子提取和再生产循环

这里的“中世纪适配”是指：不修改原版或 Biotech 的研究 Def，只修改 Mankind's Finest 自己的 Def，并把它的生物路线接到 Fantasy Biotech 的中世纪研究上。核心流程不要求玩家自行制造工业/高级组件、超科技药物、中性胺或路西法合剂，也不要求建立电网。启用 Medieval Overhaul 时，后期流程会使用其“秘银”（内部 Def 仍为 `Plasteel`）作为可研究、可冶炼的中世纪高级材料。

补丁不会削弱最终单位的能力。平衡手段主要是研究时间、高技能要求、贵金属、大量人肉/普通肉、草药和长时间培育。

## 3. 明确不做的内容

- 不把动力甲、头盔或武器改造成中世纪可制造物；相反，中世纪补丁明确移除其制造入口。
- 不修改爆弹枪、近战武器、弹药和其他武器研究；研究可以存在，但不能在中世纪模式制造对应装备。
- 不改变帝国派系和星际战士派系的科技等级。
- 不重做贴图、声音、基因能力、军衔或战斗数值。
- 不把 Biotech 依赖移除；原模组的基因和胚胎系统依赖该 DLC。
- 不修改 `FertilityProcedures`、`Xenogermination`、`Archogenetics`、`MedicineProduction` 或其他原版/Biotech 研究 Def。
- 不修改 Fantasy Biotech 的研究、建筑、配方或程序集。
- 第一版不强制依赖 Medieval Overhaul 等第三方中世纪模组；以原版中世纪局可获得的资源为基线。
- 不直接编辑 `3505068525`，以免被 Steam Workshop 更新覆盖。

## 4. 已确认的原模组阻塞点

### 4.1 研究阻塞

所有帝国研究继承自 `BEWH_ImperiumResearchBase`，该父 Def 当前要求：

- `techLevel = Spacer`
- `HiTechResearchBench`
- `MultiAnalyzer`

生物路线还引用了下列原版高科技前置。补丁移除无法在中世纪完成的原版高科技要求，但将 BEWH 入口接到 Fantasy Biotech 的完整基因改造路线之后：

| Mankind's Finest 研究 | 当前外部前置 | 中世纪补丁处理 |
| --- | --- | --- |
| `BEWH_GeneseedCreation` | `FertilityProcedures`、`Xenogermination` | 改为前置 `VR_MasterArchiteGenetics`（大师级嵌合术） |
| `BEWH_StasisResurrection` | `MedicineProduction` | 移除工业医学前置，只保留 `BEWH_GeneseedPurification` |
| `BEWH_PrimarchProject` | `Archogenetics` | 移除外部前置，只保留 `BEWH_CustodesCreation` |

其他 BEWH 生物研究之间的原模组前置关系保留。补丁不得用 XPath 指向或覆盖任何原版、Biotech、Fantasy Biotech 的 `ResearchProjectDef`。

不能直接修改抽象父 Def，因为动力甲和武器研究也继承它。补丁必须只覆盖生物路线上的具体 `ResearchProjectDef`，从而保证装备路线仍保持原状。

### 4.2 建筑阻塞

核心设施当前使用高级材料并耗电：

| 建筑 Def | 用途 | 当前主要阻塞 |
| --- | --- | --- |
| `BEWH_GeneManipulationTable` | 制作基因矩阵、Rubicon 套件、原体胚胎 | 工业/高级组件，250 W |
| `BEWH_GeneseedGestator` | 将矩阵培育为升格药瓶 | 塑钢、工业/高级组件，1500 W |
| `BEWH_PrimarchGrowthVat` | 培育基因原体 | 塑钢、工业/高级组件，2000 W |
| `BEWH_SangprimusPortum` | 保存军团/原体材料并支持军团基因种子 | 塑钢、工业/高级组件，500 W |

基因矩阵储存、药瓶储存和原体胚胎容器也依赖高级材料及电力，但它们属于便利设施，不应成为核心路线的硬前置。第一版仍会一并适配，避免中世纪局只能把关键材料丢在普通仓库中。

### 4.3 配方和手术阻塞

已确认核心配方使用以下不可稳定自给的资源：

- `MedicineUltratech`
- `Neutroamine`
- `Luciferium`
- `ComponentIndustrial`
- `ComponentSpacer`
- `Plasteel`

例如，原体矩阵当前需要 35 个超科技药、50 个路西法合剂、500 份人肉和 350 个中性胺。只修改研究等级并不能让中世纪局完成这条生产链。

## 5. 推荐玩法流程

补丁保留原模组的路线结构，仅改变进入条件和资源语言：

```text
嵌合术 -> 高级嵌合术 -> 拱元嵌合术 -> 大师级嵌合术
                                      |
                                      v
                                基因种子创造
   ├─ 雷霆战士
   ├─ 禁军创造 ─> 原体计划 ─> 军团阿斯塔特
   └─ 基因种子纯化 ─> 初生星际战士
                          ├─ 基因种子提取
                          └─ 基因种子完善 ─> 原铸星际战士 / Rubicon
```

玩家循环：

1. 在普通研究台完成 Fantasy Biotech 从“嵌合术”到“大师级嵌合术”的完整基因改造研究。
2. 完成大师级嵌合术后，开始 Mankind's Finest 的“基因种子创造”。
3. 建造“基因炼制台”和“基因种子培育器”。名称可汉化为中世纪风格，但内部 Def 不改名。
4. 用草药、贵金属、植物性炼金材料和肉制作基因矩阵。
5. 将矩阵放入培育器，获得升格药瓶。
6. 由高医术角色执行植入或转化手术。
7. 解锁更高级研究，逐步制作初生、原铸、禁军和原体。
8. 原体路线先对成年女性执行中世纪取卵手术，再由男性殖民者为卵子受精，获得带有有效双亲和遗传信息的人类胚胎。
9. 将人类胚胎与原体升格药瓶合成为原体胚胎，放入原体培养槽并持续补充营养浆。
10. 从成熟星际战士提取基因种子，形成可持续循环。
11. 从原体取得军团材料，通过 Sangprimus Portum 制作带军团特征的星际战士。

原体胚胎路线沿用 Biotech 的生育规则，因此需要在叙事者设置中允许儿童与生育，并准备一名成年可育女性和一名成年可育男性殖民者。这是生物流程条件，不是科技或材料门槛。

## 6. 具体适配规则

### 6.1 研究项目

仅对下列生物研究进行覆盖：

- `BEWH_GeneseedCreation`
- `BEWH_StasisResurrection`
- `BEWH_GeneseedPurification`
- `BEWH_AdvancedProteinSubstitution`
- `BEWH_GeneseedPerfection`
- `BEWH_GeneseedExtractionFirstborn`
- `BEWH_GeneseedExtractionPrimaris`
- `BEWH_RubiconSurgery`
- `BEWH_CustodesCreation`
- `BEWH_PrimarchProject`
- `BEWH_LegionAstartes`

每个 BEWH 项目应显式设置：

- `techLevel = Medieval`
- `requiredResearchBuilding = SimpleResearchBench`
- 清空继承的 `requiredResearchFacilities`

只在 Mankind's Finest 项目内替换以下前置节点：

- `BEWH_GeneseedCreation/prerequisites`：替换为 `VR_MasterArchiteGenetics`，使大师级嵌合术成为 BEWH 生物路线入口门槛
- `BEWH_StasisResurrection/prerequisites`：只保留 `BEWH_GeneseedPurification`
- `BEWH_PrimarchProject/prerequisites`：只保留 `BEWH_CustodesCreation`

研究成本第一版保持原值。这样可以避免同时改变解锁条件和推进速度，便于单独评估平衡。若实测中世纪研究速度过慢，再以独立平衡补丁调整，不在兼容层内混做。

### 6.2 资源替换原则

Medieval Overhaul 是可选联动而非硬依赖；启用时使用其完整中世纪资源链，未启用时保留仅使用原版材料的回退。联动方案同时衡量市值、地图矿脉、贸易、种植、研究、图纸和技能门槛，不做一对一名称替换：

| 原资源 | Medieval Overhaul 联动 | 单位价值保留 | 获取门槛 |
| --- | --- | ---: | --- |
| `MedicineUltratech` ×1 | `MedicineHerbal` ×4 | 80% | 草药种植与储备 |
| `Neutroamine` ×1 | `PsychoidLeaves` ×2 | 63% | 精神叶种植周期 |
| `Luciferium` ×1 | `Gold` ×3 + `DankPyon_Ruby` ×1 | 67% | 金矿/贸易与红宝石矿脉/矿井 |
| `ComponentIndustrial` ×1 | `DankPyon_ComponentBasic` ×2 | 94% | Medieval Overhaul 的中世纪基础构件；每个需要 4 铁锭、手工 6 |
| `ComponentSpacer` ×1 | `Plasteel` ×20 | 90% | 秘银研究 4000、工艺图纸、手工 14 和冶炼链 |
| `Plasteel` ×1 | 保留 `Plasteel` | 100% | Medieval Overhaul 将其变成秘银锭 |

替换不能仅改 `fixedIngredientFilter`，必须同步修改每个 `ingredients` 节点，否则账单界面可能显示材料却无法选择，或选择后仍不满足计数。

生物配方的稀有材料总价值约保留原版的 67% 至 94%。没有完全按市值等价，是因为中世纪替代物还增加了种植面积、宝石矿脉、工艺图纸、熔炉和高技能工匠等非货币门槛。原体矩阵最终需要 140 草药、700 精神叶、150 黄金和 50 红宝石，除此之外仍保留 500 人肉/普通肉、120000 工作量和后续胚胎培养耗时。逐配方覆盖至少包括：

- 五类普通/精炼基因矩阵
- 雷霆战士与禁军植入
- 初生和原铸各军团植入
- 初生/原铸阶段成长手术
- 基因种子提取
- Rubicon 套件及手术
- 原体胚胎与营养浆
- 原体军团材料及复制配方

补丁新增 `BEWH_MedievalExtractOvum` 取卵手术，以 `BEWH_PrimarchProject` 为前置并只消耗草药。手术复用 Biotech 原生的 `Recipe_ExtractOvum`，取出的卵子再由男性殖民者通过原生受精工作生成 `HumanEmbryo`。这样得到的胚胎拥有有效的母体、父体和遗传集，可以安全交给 RimDark 的原体胚胎合成与培养逻辑；补丁不修改任何 Biotech RecipeDef 或 `HumanEmbryo` Def。

高级路线必须比低级路线明显更昂贵。建议价值顺序始终保持：雷霆战士 < 初生 < 原铸 < 禁军 < 原体。

### 6.3 建筑

核心建筑只使用中世纪可获得的钢铁、黄金、Medieval Overhaul 基础构件、宝石和秘银。这里的 `DankPyon_ComponentBasic` 是由 4 铁锭在中世纪工作台制作的齿轮、轮子和钉子，不是原版工业零件。不更换 `thingClass`、建筑尺寸、交互位置、存储设置或自定义 `modExtensions`。基因操作台、培育器和基础矩阵存储不要求秘银或宝石，避免入口流程被随机资源锁死；药瓶储存和 Sangprimus 从中后期开始加入少量秘银及单一宝石，原体胚胎容器和培养槽再提高秘银、基础零件及红宝石需求。

自定义程序集中的基因种子孕育器和原体培养槽会直接访问 `CompPowerTrader`，因此必须保留 `CompProperties_Power`。补丁将七座相关建筑的基础耗电改为 `-1 W`、待机耗电改为 `0`，并在电力组件上设置 `transmitsPower=true`。每座设施因此会建立独立电力网络，不需要电线或外部发电设施也能保持 `PowerOn`。

补丁只移除不再需要的电力开关 `CompProperties_Flickable` 和故障组件 `CompProperties_Breakdownable`。

光效、设施链接和自定义建筑类保持不变；电力组件仅修改上述自供能字段。

特别注意：`BEWH_SangprimusPortum` 的 `CompProperties_Facility` 必须保留，否则它无法连接基因操作台/培育器，军团基因种子路线会断裂。

### 6.4 技能与风险

第一版保留原模组所有 `Medicine`、`Intellectual`、`Crafting` 和 `Construction` 技能门槛，也保留工作量、培育时间和手术成功率。这些门槛负责替代被移除的科技门槛，防止中世纪开局过早量产顶级单位。

如果实测发现草药导致高阶手术成功率低到无法合理完成，应优先为补丁增加一种昂贵的中世纪“圣化药剂”，而不是直接提高全局手术成功率。该物品不属于第一版范围。

## 7. 实现结构

建议补丁模组结构：

```text
RimDark40k-MankindsFinest-MedievalPatch/
├─ About/
│  └─ About.xml
├─ 1.6/
│  └─ Patches/
│     ├─ Research_Biological.xml
│     ├─ Buildings_GeneProduction.xml
│     ├─ Buildings_GeneticStorage.xml
│     └─ Recipes_Biological.xml
├─ Languages/
│  └─ ChineseSimplified/
│     └─ Keyed/
│        └─ MedievalPatch.xml
└─ DESIGN.zh-CN.md
```

`About.xml` 应声明：

- 必需依赖 `Ludeon.RimWorld.Biotech`
- 必需依赖 `Phonicmas.RimDark.FrameworkCore`
- 必需依赖 `Phonicmas.RimDark.MankindsFinest`
- 必需依赖 `vr.fantasy.biotech`
- `loadAfter` 包含上述四个包

所有覆盖使用 `PatchOperationFindMod` 包裹，并以具体 `defName` 为 XPath 目标。若原模组未加载，补丁不应产生缺失 Def 错误。

不复制原模组完整 Def；只替换必要节点。这可以降低原模组更新后的冲突面。

## 8. 实施顺序与验证

### 阶段 A：研究可达

1. 创建独立补丁元数据。
2. 只修改 11 个 BEWH 生物研究项目，并移除三个会阻塞中世纪路线的外部高科技前置。
3. 新增中世纪取卵手术，并复用原生受精流程取得有效人类胚胎。
4. 新建中世纪科技水平测试殖民地。

验证：完成 `VR_MasterArchiteGenetics` 后，`BEWH_GeneseedCreation` 可在普通研究台开始，后续 BEWH 项目按原有内部顺序推进；原版、Biotech 和 Fantasy Biotech 研究本身未被本补丁修改；动力甲和武器研究仍保持原定义，但对应装备没有可用制造入口。

### 阶段 B：雷霆战士到原铸星际战士

1. 适配基因操作台、培育器和两类存储设施。
2. 适配矩阵、药瓶和阶段成长手术配方。
3. 完成雷霆战士、初生和原铸各一次完整转化。

验证：过程中不需要电力、高级组件、塑钢、超科技药、中性胺或路西法合剂；保存并重载后基因和成长阶段不丢失。

### 阶段 C：禁军、原体和军团循环

1. 适配禁军矩阵与植入。
2. 适配原体矩阵、胚胎、营养浆和培养仓。
3. 适配 Sangprimus Portum 和军团材料配方。

验证：成功生成禁军与原体；能从原体产生军团材料；能制作带军团基因的初生/原铸星际战士。

### 阶段 D：长期循环与兼容性

1. 测试基因种子成熟、提取、重新植入。
2. 测试 Rubicon 手术。
3. 检查开发者模式日志。
4. 与简体中文包一起加载测试。

验证：至少运行一个游戏年并保存/读档，日志中没有由本补丁引起的红色错误；装备仍可掉落和穿戴，但不能由中世纪殖民地制造。按当前用户要求，本轮仅做静态验证，不执行这一游戏内测试。

## 9. 验收标准

补丁完成必须同时满足：

- 中世纪科技殖民地只用普通研究台完成 `VR_MasterArchiteGenetics` 后，即可从 `BEWH_GeneseedCreation` 开始推进全部 BEWH 生物研究。
- 补丁没有修改任何原版、Biotech 或 Fantasy Biotech 研究 Def。
- 核心生产链完全不依赖电网和工业以上可制造资源。
- 雷霆战士、初生、原铸、禁军和原体均可由玩家主动制作。
- 军团材料、军团基因种子、提取再利用和 Rubicon 流程可用。
- 武器和盔甲的研究与成本不改；16 个可制造装备 Def 的 `recipeMaker` 被移除，确保中世纪模式无法制造。
- 原模组文件保持未修改状态。
- 新游戏与已有存档均能加载；移除补丁后不遗留自定义基因或单位 Def。
- RimWorld 日志没有补丁引起的 XML、XPath、缺失 Def 或空引用错误。

## 10. 已知风险

1. 原模组更新可能重命名 Def、改变配方继承结构或改变自定义建筑对电力组件的访问方式。XPath 和程序集调用都应作为版本升级检查的一部分。
2. 直接把超科技药替换为草药会显著影响手术成功率，需要实测而不是预先修改数值。
3. `HumanEmbryo` 来自 Biotech；本补丁增加了中世纪取卵手术，后续仍使用 Biotech 原生受精流程，不再要求高科技胚胎获取建筑。
4. 1.5 与 1.6 的程序集和 Def 可能不同。第一版通过 1.6 后，再对 1.5 做差异检查，不直接复用未经验证的补丁文件。

## 11. 当前结论

这不是单纯修改 `techLevel` 的任务。完整中世纪适配至少包含研究台要求、外部研究前置、设施成本、供电、配方过滤器和材料来源六个方面。

当前实现保持为纯 XML 独立补丁，并以“雷霆战士完整转化”和“有效人类胚胎到原体出生”作为两条垂直验证链。供电组件虽被自定义代码硬依赖，但保留组件并令其建立独立自供能网络即可，无需额外 C# 补丁。

## 12. 2026-08-07 静态流程审计

本次审计覆盖原模组 1.6 Def 与补丁合并后的最终结构，不启动 RimWorld。73 个活动底层 PatchOperation 全部命中，补丁 XML 全部可解析；11 项生物研究均可在完成 `VR_MasterArchiteGenetics` 后从 `BEWH_GeneseedCreation` 到达，且没有原版或工业以上研究前置。

| 路线 | 已确认的闭合流程 | 保留的生物条件 |
| --- | --- | --- |
| 雷霆战士 | 研究 -> 普通/精炼矩阵 -> 培育器 -> 药瓶 -> 植入 | 推荐 18 至 45 岁；医学 7 |
| 初生星际战士 | 研究 -> 普通/精炼矩阵 -> 培育器 -> 药瓶 -> 植入 -> 三阶段手术 | 植入年龄 10 至 14 岁；医学 8 |
| 原铸星际战士 | 研究 -> 普通/精炼矩阵 -> 培育器 -> 药瓶 -> 植入 -> 三阶段手术 | 植入年龄 10 至 13 岁；医学 10 |
| 禁军 | 研究 -> 普通/精炼矩阵 -> 培育器 -> 药瓶 -> 植入 | 植入年龄 0 至 3 岁；医学 13 |
| 原体 | 研究 -> 普通/精炼矩阵 -> 培育器 -> 药瓶 -> 取卵及受精 -> 原体胚胎 -> 培养槽及营养浆 -> 出生 | 启用儿童与生育，并有成年可育男女殖民者 |

普通矩阵会在完成 `BEWH_AdvancedProteinSubstitution` 后按原模组逻辑隐藏；五个精炼矩阵均继承或显式要求该研究，并产出相同矩阵，因此不存在研究完成后配方断档。初生、原铸的六项阶段手术、Rubicon、基因种子提取、原体军团材料和军团植入引用均存在。

启用 Medieval Overhaul 时，七座设施使用分级的钢铁、黄金、基础零件、宝石和秘银，并以 `-1 W`、`transmitsPower=true` 形成独立自供能网络。所有 BEWH 生物 RecipeDef 中的超科技药、中性胺、路西法合剂和工业/高级组件均有中世纪替代；原 `Plasteel` 在该环境中保留为秘银。94 个终端 Xenotype、Hediff 和 Gene 引用全部解析成功。武器和护甲研究不改，但 16 个装备制造入口已由 `Gear_Acquisition.xml` 禁用。普通护甲和武器不进入奖励池；高阶护甲由 Masters 独立移植中的专属后期遗迹任务提供。

## 13. 2026-08-07 平衡修订

研究主链调整为：`VR_MasterArchiteGenetics -> BEWH_GeneseedCreation -> BEWH_GeneseedPurification -> BEWH_GeneseedPerfection -> BEWH_CustodesCreation -> BEWH_PrimarchProject -> BEWH_LegionAstartes`。玩家必须先完成 Fantasy Biotech 的大师级嵌合术；禁军也不能再绕过初生/原铸的基因种子提纯与完善阶段。

七座设施改为“稳定材料承担主体成本、随机材料只标记阶段”的阶梯。入口操作台为 250 钢/24 基础零件/12 黄金，培育器为 350/30/18，两者不再需要宝石；基础矩阵存储同样不需要宝石。药瓶存储从 40 秘银和 5 绿宝石起步，Sangprimus 使用 70 秘银和 8 蓝宝石，原体胚胎容器与培养槽分别提高到 90/140 秘银和 10/15 红宝石。施工门槛仍为：基因操作台 8000 工作/建造 8，培育器 15000/10，Sangprimus 12000/10，三类储存设施分别为 6000/8、8000/10、10000/12，原体培养槽为 30000/14。自供能移除了持续电力成本，因此稳定资源、技能和施工时间继续承担主要平衡压力。

本轮仍只做静态验证，不进入游戏。静态成功标准包括：XML 可解析、补丁 XPath 可命中、所有材料 Def 在对应条件分支存在、研究图无环、雷霆战士/初生/原铸/禁军/原体/军团流程均有可达配方。

本结论只代表静态 Def、XPath、研究图和引用闭合验证。按用户要求未进行进游戏、存读档或实际手术成功率验证。

## 14. 材料获取难度复核

材料替代按“稳定生产门槛 + 稀有财富门槛”组合，而不是只比较市值。Medieval Overhaul 基础构件可在完成中世纪“工程学”（前置锻造）后，用 4 铁锭在不消耗零件的木制工作台稳定制造，负责前期可规划性；`Plasteel` 在 Medieval Overhaul 中实际进入秘银体系，需要 4000 点研究、工艺图纸、手工 14、原秘银/铁锭/煤炭冶炼链，负责锁定中后期。黄金可通过采矿和贸易积累，红/蓝/绿宝石则来自矿脉、原料商人及矿井的低概率奖励，只用于提高后期成本，避免随机宝石单独卡死主流程。玻璃钢仍属于工业资源，翡翠也不够稀有，不适合作为最高层材料；因此不使用它们替代高级组件。

生物配方保留原工作量、培养耗时和技能门槛，并按原资源单位价值与供应难度换算：1 超科技药替换为 4 草药，1 中性胺替换为 2 精神叶，1 路西法合剂替换为 3 黄金 + 1 红宝石，1 工业组件替换为 2 基础零件，1 高级组件替换为 20 秘银。关键终端成本依次为：雷霆矩阵 12 草药/120 精神叶；初生矩阵 28 草药/100 精神叶/6 黄金/2 红宝石；原铸矩阵 60/230/12/4；禁军矩阵 80/260/60/20；原体矩阵 140/700/150/50，另保留 500 肉和原工作量。这样高级路线始终显著贵于低级路线，但不会依赖不可制造的工业物资。

设施成本不再追求逐座严格市值等价，因为基础零件还包含铁锭与制作工时，秘银还包含研究、图纸、手工 14 和冶炼链，而宝石供应具有随机性。具体阶梯如下：

| 流程层级 | 设施 | 钢 | 秘银 | 基础零件 | 黄金 | 宝石 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 入口 | 基因操作台 | 250 | 0 | 24 | 12 | 0 |
| 入口 | 基因种子培育器 | 350 | 0 | 30 | 18 | 0 |
| 基础存储 | 基因矩阵存储 | 160 | 0 | 14 | 12 | 0 |
| 中后期存储 | 药瓶停滞库 | 200 | 40 | 18 | 18 | 5 绿宝石 |
| 军团后期 | Sangprimus Portum | 240 | 70 | 24 | 25 | 8 蓝宝石 |
| 原体 | 原体胚胎容器 | 220 | 90 | 24 | 30 | 10 红宝石 |
| 原体终端 | 原体培养槽 | 400 | 140 | 32 | 40 | 15 红宝石 |

这个分配让可生产的钢、基础零件和秘银承担主要成本，同时把单座设施的宝石要求压到 5 至 15 颗，并且每座最多只要求一种宝石。原体培养槽仍保留 30000 工作量和建造 14；配合原体矩阵本身的高额材料、胚胎条件与培养耗时，不会因为降低设施宝石量而提前开放原体量产。
