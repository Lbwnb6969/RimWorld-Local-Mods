# Medieval Kingdoms 写实阵营与 VFE2 / EES 兼容性方案

## 1. 本轮范围与结论

本方案按阶段实施。当前已完成阶段 A 的 XML 可加载性补丁、阶段 B 的 EES 运行时协调、阶段 C 的库存与奖励平衡及阶段 D 的组合与成长验收，并通过 RimWorld 1.6 静态验收。实施不修改 Workshop 原模组，不修改 `ModsConfig.xml`，也不启用这些模组。文中的“写实阵营系列”按作者 `Athelas`、`athelas.medievalkingdoms.*` packageId、共享核心和依赖关系归类。

结论：**这套阵营在设计上可以与 Vanilla Factions Expanded - Medieval 2（VFE2）和 Evolving Enemy Strongholds（EES）并存，也适合接入现有“从小定居点到 T13 超级要塞”的中世纪成长体系；但下载到本地的当前版本不能直接全开后称为完美兼容。**

主要风险不是 VFE2，而是 Medieval Kingdoms 系列自身原本存在跨模组重复 Def、法国复用神圣罗马帝国城镇 Def、所有城镇库存含大量现代物资。阶段 A 已解决系列自身的 XML/Def 重名；阶段 B 已把 13 个 NPC 派系接入 EES 敌对普通据点的整图清理；阶段 C 已限制七套实际城镇的 KCSG 库存，并补齐 EES 现代奖励兜底和后期文化装备奖励。

达到可启用状态的最低标准：

1. 多阵营同时加载时不产生重复 Def 错误。
2. 每个 NPC 派系仍保留自己的兵种、文化、KCSG 城镇和世界派系身份。
3. 敌对普通 `Settlement` 可进入 EES T1-T13 成长，不接管玩家据点、Site 或特殊世界对象。
4. KCSG 底图和 EES 新增区域都不生成电力设施、工业库存、动力甲或机械血清。
5. 不修改 VFE2 的六个王国/氏族识别、墙体、炮台与清理路径。

## 2. 本地已下载的完整系列

| Workshop ID | 模组 / packageId | 新增 NPC 派系 | `naturalEnemy` 字段 |
|---|---|---:|---|
| `3587945805` | Medieval Kingdoms: Core / `athelas.medievalkingdoms.core` | 共享核心，无独立 NPC 派系 | 不适用 |
| `3581984858` | The Turks / `athelas.medievalkingdoms.theturks` | `BA_Turkic_NobleHouse` | 是 |
| `3587946464` | Poland / `athelas.medievalkingdoms.poland` | `BA_Poland_NobleHouse` | 否 |
| `3592035540` | The Holy Roman Empire / `athelas.medievalkingdoms.hre` | `BA_HRE_NobleHouse` | 否 |
| `3594382864` | Byzantine Empire / `athelas.medievalkingdoms.byzantium` | `BA_Byzantium_NobleHouse` | 否 |
| `3596004032` | France / `athelas.medievalkingdoms.france` | `BA_France_NobleHouse` | 是 |
| `3597673469` | Iberia / `athelas.medievalkingdoms.iberia` | `BA_Spain_NobleHouse`、`BA_Portugal_NobleHouse`、`BA_Moor_NobleHouse` | 西班牙是；其余否 |
| `3598392600` | the Mongols / `athelas.medievalkingdoms.mongols` | `BA_Mongol_NobleHouse` | 是 |
| `3632552850` | Shogunate / `athelas.medievalkingdoms.shogunate` | `BA_Takeda_NobleHouse`、`BA_Tokugawa_NobleHouse`、`BA_Uesugi_NobleHouse`、`BA_Mori_NobleHouse` | 武田、德川是；其余否 |

八个阵营包共增加 13 个 NPC 派系。它们都继承核心的 `BA_Medieval_Base`，因此具备：

- `techLevel = Medieval`
- `settlementGenerationWeight = 1`
- `requiredCountAtGameStart = 1`
- `canMakeRandomly = true`
- `canStageAttacks = true`
- 普通 `Settlement` pawn group
- `KCSG.CustomGenOption` 专属城镇入口

所有声明的硬依赖目前均已下载，包括 Harmony、Vanilla Expanded Framework、五个 Tasty Armory 包、Sengoku 包和 Medieval Kingdoms: Core。Medieval Overhaul 是各包的可选适配入口，当前环境已具备。

## 3. 与 VFE2 的关系

### 3.1 可直接兼容的部分

- Medieval Kingdoms 与 VFE2 都依赖 Vanilla Expanded Framework，并使用其 KCSG 自定义基地能力；不需要 Vanilla Base Generation Expanded。
- 对两个系列的 1.6 XML 进行 `defName` 集合比对，**直接重名数量为 0**。Medieval Kingdoms 使用 `BA_` 前缀，VFE2 使用 `VFEM2_` 前缀。
- Medieval Kingdoms 不修改 `VFEM2_Kingdom*`、`VFEM2_Clan*`、`VFEM2_MerchantGuild`，也没有替换 VFE2 的 FactionDef。
- 两者是并列新增派系，不存在必须二选一的静态依赖冲突。

### 3.2 需要协调的部分

- 两套模组都会增加中世纪派系及 KCSG 城镇。八个阵营包一次全开会增加 13 个 NPC 派系，稀释 VFE2 王国/氏族和其他敌人的世界占比、袭击权重与 EES 据点遭遇率。
- Medieval Kingdoms 城镇大多声明为 `100 x 100`，再叠加 EES 外围、驻军和功能区后可能过密。拜占庭声明为 `25 x 25`，却引用大型 Constantinople 结构，需单独做尺寸/越界静态检查。
- 不应让 Medieval Kingdoms 复用 VFE2 专属的氏族木栅栏判断。其高阶外墙继续采用现有通用 Medieval Overhaul 城堡墙即可；若以后需要文化视觉，再按蒙古、幕府、欧洲、伊斯兰分组设计，而不是把所有派系改成 VFE2 城堡。
- 不应改动现有 VFE2 两种无电炮台白名单。Medieval Kingdoms 自带城镇明确关闭炮台和迫击炮，EES 可继续只从中世纪、无电候选池选防御物。

## 4. 与 EES 的关系

### 4.1 已经能工作的链路

现有 EES 中世纪补丁的通用入口是：

`humanlikeFaction && FactionDef.techLevel == TechLevel.Medieval`

因此 13 个 Medieval Kingdoms NPC 派系在**实际与玩家敌对且目标是普通 Settlement**时，可以直接进入现有链路：

- 中世纪 T1-T13 成长与研究进度
- 生存口粮改为肉干
- 关闭 CE 枪械弹药升级
- 过滤超中世纪或耗电炮台
- 关闭现代通讯室、弹药库、迫击炮阵地和高科技保险库
- 使用中世纪门、地面和通用/Medieval Overhaul 墙体
- 替换 EES 自己生成区内的部分现代战利品

友好派系不会因为安装补丁而被强制改造成敌对要塞；只有实际敌对据点才应进入 EES 的敌方成长流程。

### 4.2 整图清理与库存缺口（已解决）

阶段 B 前，整图清理 Harmony 补丁只识别六个 VFE2 敌对王国/氏族。现在它会额外精确识别 Medieval Kingdoms 的 13 个 NPC 派系，并只在地图父对象是普通 `Settlement` 且该派系当前与玩家敌对时清理整张 KCSG 底图。玩家派系、友好据点、Site 和特殊 WorldObject 不匹配。

八个城镇的 `fillWithDefs` 都包含：

- `MedicineIndustrial`
- `ComponentIndustrial`
- `Neutroamine`
- `Chemfuel`
- `Hyperweave`
- `Penoxycyline`
- `WakeUp`
- `ReinforcedBarrel`
- `MechSerumHealer`
- `Apparel_PowerArmor`
- `MechSerumResurrector`

阶段 B 只接通了整图替换和建筑清理入口。阶段 C 现已直接替换 KCSG 库存池，并补齐 Hyperweave、Penoxycyline、Wake-Up、Yayo、Flake、Go-Juice、Luciferium、强化炮管和两种机械血清的替换规则。对来源为官方内容、科技等级高于中世纪且类别为物品的其他漏网项，再按单件市场价值换成黄金；这条兜底不处理 RimDark、Tasty 或其他第三方模组奖励。

## 5. 系列自身的启用阻断项

### 5.1 跨包重复 Def

按 Def 类型分组的完整静态扫描发现 **130 组跨阵营包重复 `defName`**，共 365 个声明，其中 235 个是多余副本：

| 类别 | 重名数量 | 情况 |
|---|---:|---|
| BackstoryDefs | 88 | 法国复制神圣罗马帝国背景定义 |
| NamerDefs | 22 | 幕府包夹带伊比利亚命名定义 |
| ThingCategoryDef | 15 | Core 和每个阵营包均重复携带同一份分类 |
| MemeDef | 1 | 阵营包重复携带同一意识形态 Meme |
| Apparel | 2 | 幕府包夹带两件伊比利亚头盔定义 |
| StructureLayoutDef | 1 | 法国与神圣罗马帝国都定义 `nordlingen` |
| SettlementLayoutDef | 1 | 法国与神圣罗马帝国都定义 `BA_HRETown` |

抽样哈希确认下列文件是逐字相同副本，而不是同名不同内容：Core/土耳其 `ThingCategories.xml`、法国/神罗城镇、法国/神罗 Nordlingen 布局、法国/神罗成人背景、伊比利亚/幕府阿拉伯骑兵头盔。

阶段 A 已采用**按 Def 类型的条件去重**，保留先加载的规范副本，不重命名全部 130 组 Def。130 组重复节点的规范化 XML 内容均一致。Core 是所有阵营的硬依赖并先加载，15 个分类以 Core 为规范来源；法国/神罗及伊比利亚/幕府的其余副本只保留一份。去重 XPath 明确限制 Def 类型，避免误删不同 Def 类型之间合法共用的 `defName`。

### 5.2 法国视觉并非独立城镇

法国当前逐字复用神圣罗马帝国的 `BA_HRETown` 和 `nordlingen`。去重后法国仍可生成城镇，但视觉上就是神罗城镇，不应在验收中误报为“法国专属基地已保留”。

第一版兼容补丁建议接受这一作者原始设计，优先保证稳定生成；若用户之后要求法国专属视觉，再单独建立 `BA_FranceTown` 和法国结构，不把大型布局重写混入本次兼容修复。

### 5.3 奖励池与经济

KCSG 城镇的 `stockpileValueMultiplier = 2.0`，并允许填满储存建筑。叠加 EES 分级战利品后，可能同时获得底图库存与要塞奖励。兼容补丁应先把底图库存限制为中世纪常规补给，稀有奖励仍由 EES 后期层级和既有专属任务体系负责，避免挤压原版及 RimDark 稀有奖励池。

## 6. 推荐实施方案

### 阶段 A：XML 预处理，先解决可加载性（已完成）

已在现有 `EvolvingEnemyStrongholds-MedievalPatch` 内增加 `1.6/Patches/MedievalKingdoms_SeriesFix.xml`，所有操作仅在对应 packageId 存在时生效：

1. 按 Def 类型去除 130 组中后加载的相同副本，包括 15 组 ThingCategoryDef。
2. 为波兰、神罗/法国、拜占庭、伊比利亚、蒙古、幕府的 KCSG 城镇拆分唯一父模板，并同步具体城镇的 `ParentName`。
3. 保留一份 `BA_HRETown` 和 `nordlingen`，让神罗与法国共同引用作者原有的同一套布局。
4. 为波兰专属 childhood 父模板改用唯一名称，保留其 `Polish_*` spawn categories；修正土耳其/波兰帽子模板重名。
5. 删除拜占庭不完整的商人基类副本，统一继承 Core 的完整 `BA_MedievalTradeKindBase`。
6. 不修改 Workshop 原模组文件，不创建第二套基地生成器。

静态验证结果：Core 加八个阵营包的 256 种启用组合全部达到同类型 `defName` 重复为 0、同类型模板 `Name` 重复为 0、`BA_` 父级引用断链为 0；全阵营反向加载顺序复验也通过。未进入游戏，因此实际 Player.log 仍作为以后启用时的补充证据。

### 阶段 B：扩展现有 EES 中世纪运行时协调（已完成）

已在现有 `EES.MedievalPatch.dll` 内新增精确识别：packageId 必须以 `athelas.medievalkingdoms.` 开头，FactionDef 必须属于上表 13 个 NPC 派系，并继续满足 `humanlikeFaction` 和 `TechLevel.Medieval`。没有只凭 `BA_` 前缀匹配，也没有新增对阵营程序集的硬引用。

对这些派系的敌对普通 `Settlement`：

1. 保留 KCSG `CustomGenOption`，让文化城镇先生成。
2. 继续由 EES 叠加当前层级的外围、核心、驻军和角色设施。
3. 在 `GenStep_Settlement.ScatterAt` 完成后执行整图中世纪清理，范围与现有 VFE2 路径一致。
4. 玩家派系、非敌对城镇、任务 Site、遭遇地图和特殊 WorldObject 不匹配。
5. 不加入 VFE2 专属墙体/炮台分支；使用通用 Medieval Overhaul 墙体和现有中世纪炮台过滤。

静态验证结果：13 个 NPC FactionDef 与代码白名单一一对应，7 个玩家开局 FactionDef 的白名单交集为 0。整图清理仍要求 `map.Parent is Settlement` 和 `faction.HostileTo(Faction.OfPlayer)`；VFE2 六派系继续通过原判断进入，其他中世纪模组不会只因科技等级匹配而被整图清理。Release 程序集构建为 0 警告、0 错误。

### 阶段 C：库存与奖励平衡（已完成）

阶段 C 已在 XML 中把七套实际 `SettlementLayoutDef.fillWithDefs` 改成明确的中世纪池，避免先生成现代物品再销毁。神罗和法国共用 `BA_HRETown`，因此八个阵营包对应七套库存定义。每套池保留 12 项资源：

- 肉干、草药、白银、黄金、翡翠
- 木材、花岗岩块、铁锭、亚麻和丝绸
- Medieval Overhaul 基础零件与其体系中作为秘银使用的 `Plasteel`

每套池再加入 2 项对应文化的 Tasty 武器。原版工业零件不保留；这里的 `DankPyon_ComponentBasic` 是 Medieval Overhaul 基础零件，完成 `DankPyon_Engineering` 后可在中世纪工作台用 4 个铁锭制作，也存在于文化商队库存中，因此不会形成工业科技死锁。

Medieval Kingdoms 人员携带的对应 Tasty Armory 武器和护甲属于原模组的兵种装备链，不属于本阶段要替换的库存。阶段 C 只调整 KCSG 库存与 EES 战利品，不改 PawnKind、武器标签、服装要求、人员装备抽取或随身品质。奖励池可以少量出现不同品质的 Tasty / Medieval Overhaul 装备，但必须与人员装备路径隔离，并继续以 Medieval Kingdoms 自有内容和普通资源为主体。

七城的 `fillStorageBuildings` 已统一设为 `false`。这是必要限制：KCSG 在该值为 `true` 时会填充每一个空储物格，`fillChance` 不能限制这条路径。具体城镇的 `stockpileValueMultiplier` 从父模板继承的 `2.0` 覆盖为 `0.75`，避免底图战利品和 EES 分级奖励同时膨胀。

EES 金库只为 13 个精确 Medieval Kingdoms NPC 派系追加对应文化装备。T1-T4 不追加；T5-T6、T7-T8、T9-T10、T11-T12 的单件出现率依次为 25%、40%、60%、80%；T13 第一件必出，第二件 65% 出现。中期品质从劣质/普通逐步提升为普通/良好和良好/优秀；T13 为优秀 65%、大师 30%、传奇 5%。非 T13 每座金库最多一件，T13 最多两件，EES 原有奖励仍是主体。

静态验证结果：两个七城公共 XPath 各命中 7 个节点，七个文化池 XPath 各命中 1 个节点；模拟应用后每城恰好有一个 `fillStorageBuildings=false`、一个 `stockpileValueMultiplier=0.75` 和 14 项库存。21 个唯一库存 Def 与 27 个唯一文化奖励装备 Def 全部存在，库存现代禁物为 0。整图运行时清理继续作为防漏措施。

### EES 的 T1-T13 如何升级

每个普通敌对据点按 Settlement ID 拥有独立保存的 `StrongholdData`。首次纳入 EES 时从 T1-T3 稳定起步；此后在据点数据被访问且距上次检查至少 2500 tick 时，EES 重新计算研究、玩家财富和经过时间形成的进度。研究是主因，财富和时间只是补量；中世纪研究链的六个累计节点分别提供 `0.08 / 0.20 / 0.38 / 0.60 / 0.80 / 1.00` 进度，爬升使用平方曲线，所以前期缓慢、后期加速。

普通据点的计算结果只升不降，并允许一次跨越多个层级。T1-T13 在实际内容上分成七档：T1-T2、T3-T4、T5-T6、T7-T8、T9-T10、T11-T12、T13。升级后会重算军力、防御、补给、指挥官、设施和驻军，但不会在已经打开的地图上实时施工；据点地图新生成时，Medieval Kingdoms 的 KCSG 文化城镇先生成，EES 再按保存层级添加外围和功能区。

文化底图、建筑风格、兵种和对应 Tasty 随身装备必须继续遵循 Medieval Kingdoms 原定义。EES 的附加区只能强化据点，不能覆盖文化核心或把不同国家统一成通用城堡外观；这是阶段 D 的硬验收项。

### 阶段 D：组合与成长验收（已完成）

静态调用链确认：Medieval Kingdoms 的 KCSG 城镇先生成，EES 在普通生成路径未执行时由 `MapGenerator.GenerateContentsIntoMap` fallback 调用 overlay 强化；`StrongholdData` 以 Settlement ID 保存层级、角色、军力、设施、指挥官和驻军方案。已打开地图不会实时施工，下一次新生成该据点地图时才按保存层级重建叠加内容。

阶段 D 修正了三类缺口：外环沙袋与 CE 射击孔改成可穿越木制路障并移除铁丝网；补给点用营火和木桶替代营养膏设备，黑市用木箱填补台球桌删除后的用途；中世纪 KCSG overlay 取消炮台数量 1.4 倍膨胀。默认密度下 T1-T13 炮台曲线回到 `0 / 2 / 3 / 4 / 6 / 8 / 10 / 13 / 15 / 17 / 19 / 21 / 25`，侦察估算同步处理，迫击炮仍为 0。

角色链静态模拟通过：T1-T2 为补给点、监牢；T3-T4 加黑市、灵能祭坛；T5-T6 加贵族庄园；T7-T13 加指挥要塞。文化奖励的期望件数从 T5-T6 的 0.25 平滑上升到 T11-T12 的 0.80，T13 为 1.65；T13 后期可见性足够，但仍最多两件且不进入全局任务奖励池。

Core 加八阵营包的 256 种组合和反向阵营加载顺序继续通过系列 Def/模板/父级检查。13 个 NPC 白名单、7 个玩家派系边界和 VFE2 六个敌对派系均复验；当前 EES 的 27 个 Harmony 目标、RimWorld `GenStep_Settlement.ScatterAt` 和两个 Transpiler 唯一模式全部存在。补丁 XML 可解析，Release 构建为 0 警告、0 错误。

## 7. 推荐加载关系

概念顺序如下，实际以 packageId 依赖和补丁 `loadAfter` 为准：

1. Harmony
2. Vanilla Expanded Framework
3. Tasty Armory Core 及所需分包
4. Medieval Overhaul 及相关依赖
5. Medieval Kingdoms: Core
6. 选用的 Medieval Kingdoms 阵营包
7. Vanilla Factions Expanded - Medieval 2
8. Evolving Enemy Strongholds
9. Evolving Enemy Strongholds - Medieval Patch（包含后续本系列条件修复）

Medieval Kingdoms 与 VFE2 的先后没有直接 Def 覆盖要求，但两者都必须在最终兼容补丁之前。`VanillaExpanded.BaseGeneration` 不应重新加入；VEF 的 KCSG 功能已经满足两个系列的专属基地需要。

## 8. 推荐启用策略

阶段 A-D 完成后，Medieval Kingdoms: Core 与任意阵营包已具备 XML 静态可加载条件，已接入 EES 敌对普通据点的整图中世纪清理，KCSG 库存、EES 金库、外围视觉、角色设施和 overlay 炮台密度也已完成静态平衡。当前仍没有修改激活列表。

兼容补丁完成后，技术上可以全开；玩法上不建议默认生成全部 13 个新 NPC 派系。较稳妥的首轮组合是 Core + 3 至 5 个文化差异明显的阵营包，并在世界生成界面控制派系数量。全开会显著稀释 VFE2 敌对王国/氏族和其他内容的出现率，这不是代码错误，但会改变整套模组库的遭遇节奏。

若目标是尽可能覆盖文化，建议先选：拜占庭、土耳其、蒙古、幕府，再从神罗/法国/波兰中选一个。伊比利亚和幕府单包会各自增加多个派系，开启时应计入总量。

## 9. “完美兼容”的最终判定

静态层面可实现接近完整兼容，且不需要删除 VFE2 或 EES：

- VFE2 和 Medieval Kingdoms 没有直接 Def 命名冲突。
- 全部 Medieval Kingdoms NPC 派系天然符合 EES 的 Medieval 入口。
- 两套 KCSG 视觉可以分别保留，EES 继续只做成长叠加。
- 系列自身重复 Def 和现代库存可以通过条件补丁解决。

阶段 A-D 已完成并通过静态验收，因此当前可以判定为“**XML 可加载、EES 生成与保存路径已接通、文化布局与装备链保持隔离、库存/奖励/外围/炮台已完成静态兼容**”。还不能写成“进游戏完美兼容”：用户已说明不要求进游戏验证。现有结论以 XML 解析、Def 引用、程序集和反编译调用链、组合及数值模拟为证据；实际地图拥挤、寻路、地图离开/重进、存档往返和画面观感仍属于保留风险。

## 10. 阶段 A 实施与静态验收记录

- 九个系列模组身份、packageId、依赖和 `loadFolders.xml` 已核对。
- 所有声明硬依赖均在本地存在。
- 这些模组当前均未加入 `ModsConfig.xml` 激活列表。
- 八个阵营包的 1.6 XML 全部可由 XML 解析器读取。
- 已确认 13 个 NPC 派系继承 `BA_Medieval_Base` 和 `TechLevel.Medieval`。
- 已确认每个 NPC 派系使用 `KCSG.CustomGenOption` 与普通 Settlement pawn group。
- 已确认 Medieval Kingdoms 与 VFE2 的直接 `defName` 交集为 0。
- 已确认系列内部存在 130 组同 Def 类型的跨包重复 `defName`，所有组的规范化 XML 内容一致。
- 已新增条件补丁 `1.6/Patches/MedievalKingdoms_SeriesFix.xml`，未修改任何 Workshop 原模组。
- 已在本地补丁 `About.xml` 的 `loadAfter` 中加入 Core 和八个阵营 packageId，没有增加硬依赖。
- 已对 Core 加八个阵营包的全部 256 种启用组合执行合并 Def 模拟：重复 `defName`、重复模板 `Name`、未解析 `BA_` 父模板均为 0。
- 已用反向阵营加载顺序复验全组合，结果同样通过。
- 已确认六种独立 KCSG 城镇父模板、神罗/法国共享模板和土耳其原有 `BA_Base_Settlement_1` 均保留正确引用。
- 已确认波兰背景继续继承含 `Polish_*` spawn categories 的独立模板，拜占庭商人继续继承 Core 完整库存基类。
- 已确认八个 KCSG 城镇关闭自带炮台/迫击炮，但库存池包含现代物资。
- 已确认现有 EES 通用中世纪成长会匹配本系列，现有 VFE2 整图清理不会匹配本系列。
- 本阶段没有修改补丁程序集、库存内容或激活列表；这些分别留给阶段 B、C 和用户最终启用。

## 11. 阶段 B 实施与静态验收记录

- 已在 `MedievalUtility` 中加入 `athelas.medievalkingdoms.` packageId 前缀和 13 项 NPC FactionDef 白名单。
- 精确识别同时要求派系为 humanlike、`TechLevel.Medieval`、来源 packageId 匹配且 Def 名在白名单内。
- 已将原 VFE2 整图清理入口扩展为 VFE2 六个敌对派系或 Medieval Kingdoms 13 个 NPC 派系；VFE2 墙体和炮台专属分支未改动。
- 清理仍挂接唯一的 `GenStep_Settlement.ScatterAt`，并要求 `map.Parent is Settlement` 和派系当前与玩家敌对。
- 13 个 NPC Def 与源码白名单完全一致；缺失 0、额外 0，全部 13 个保留 `KCSG.CustomGenOption`。
- 7 个玩家开局 FactionDef 与 NPC 白名单交集为 0；友好 NPC 据点由敌对关系判断排除，Site 和特殊 WorldObject 由父对象类型排除。
- 八个阵营包的 packageId 全部通过前缀校验；程序集没有新增 Medieval Kingdoms/Athelas 硬引用，未安装系列时不会形成程序集依赖错误。
- `dotnet build EES.MedievalPatch.csproj -c Release --no-restore`：通过，0 警告、0 错误。
- 已生成 `1.6/Assemblies/EES.MedievalPatch.dll`；本阶段未修改库存替换表、Workshop 原模组或 `ModsConfig.xml`。

## 12. 阶段 C 实施与静态验收记录

- 新增 `1.6/Patches/MedievalKingdoms_Stockpiles.xml`，处理土耳其、波兰、神罗/法国、拜占庭、伊比利亚、蒙古和幕府七套实际城镇。
- 七城关闭 KCSG 满储物格填充，具体城镇库存倍率覆盖为 `0.75`；每池为 12 项中世纪资源加 2 项对应文化 Tasty 装备。
- 扩展运行时现代物资替换，并为官方高科技物品增加按单件价值换黄金的通用兜底；第三方模组奖励不进入该兜底。
- 13 个 Medieval Kingdoms NPC 派系各自使用对应文化金库池；T1-T4 不追加，后期按层级提升概率和品质，非 T13 最多一件、T13 最多两件。
- 本补丁没有修改任何 PawnKind、人员武器/服装标签、随身装备抽取或人员装备品质。
- 七城 XPath 模拟、库存 Def、文化奖励 Def、本地 XML 解析和 Release 构建均通过；未修改 Workshop 原模组、`ModsConfig.xml`，也未进行进游戏验证。

## 13. 阶段 D 实施与静态验收记录

- 反编译确认 KCSG 文化基地先生成、EES overlay 后叠加；`StrongholdData` 按 Settlement ID 保存并只升不降，最短 2500 tick 重新评估一次。
- 外环不使用不可通行的 `DankPyon_Palisade` 或 `DankPyon_EmbPalisade`，避免堵住文化城门；沙袋、CE 射击孔改为木制低矮路障，铁丝网移除。
- 补给点新增营火与木桶，黑市新增木箱；均为 1x1、低价值、无需电力的中世纪设施，不覆盖 KCSG 文化核心。
- 中世纪 overlay 临时按非 overlay 数量执行 `AddTurrets`，方法正常返回或异常时都会恢复 `isOverlayBase` 保存状态；若 EES 因该地图的空炮塔放置数据抛出 `NullReferenceException`，仅跳过该次炮塔批次并继续其余守军强化。非中世纪派系、非空引用异常和其他 EES overlay 不受影响。
- 默认炮台密度的 T1-T13 数量为 `0 / 2 / 3 / 4 / 6 / 8 / 10 / 13 / 15 / 17 / 19 / 21 / 25`，不再到 35；用户主动修改 EES 炮台密度设置时仍按原设置同比变化。
- 文化奖励期望件数为 `0 / 0.25 / 0.40 / 0.60 / 0.80 / 1.65`；按品质价格倍率折算的期望基础价值倍数为 `0 / 0.234 / 0.375 / 0.668 / 1.060 / 2.846`，无需进一步削弱。
- 27 个 EES Harmony 目标和 `GenStep_Settlement.ScatterAt` 全部存在；`EraFor`、守军口粮、商队口粮三个 Transpiler 模式各唯一命中。
- 13 个 NPC FactionDef 与白名单一致，7 个玩家开局 FactionDef 交集为 0，VFE2 六个敌对派系全部存在；59 个直接引用 Def 在声明依赖中缺失 0。
- 卷宗和逃生腰包的旧无效贴图路径已修为当前 Core 蓝图书和烟幕包 Def 使用的路径。
- Release 构建通过，0 警告、0 错误；未修改 Workshop 原模组或 `ModsConfig.xml`。2026-08-10 使用新游戏进程只读载入 18:24 的 `Autosave-4`：主地图完成载入，Tactical Groups 旧字典错误未复现；EES 空炮塔放置数据触发一次本地降级警告，失败批次被跳过且旧的 `ReinforceGarrison failed` 未复现。该验证仅覆盖这次存档载入和一次恢复分支，不等同于所有文化据点、所有层级的完整游玩验收。
- 扩展 XML 扫描发现 Medieval Overhaul 自带简中 `Races_Hyena.xml` 有一个上游未闭合标签；它不属于本补丁且未被改写，若游戏日志实际报告该项，需要单独修正原模组语言文件或由作者更新。
