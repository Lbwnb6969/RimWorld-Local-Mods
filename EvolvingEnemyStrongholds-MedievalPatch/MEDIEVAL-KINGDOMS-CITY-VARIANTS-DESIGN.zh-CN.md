# Medieval Kingdoms 据点：当前状态

> 更新：2026-08-12
> 用途：只记录当前有效状态和验收入口。历史方案见 `Deprecated/Design-History`。

## 当前目标

- 范围为法国、神罗、波兰、拜占庭、突厥、摩尔、葡萄牙、西班牙、蒙古九个 NPC 阵营，不包含 Shogunate。
- 每个阵营以 Medieval Kingdoms 原生城镇为 60% 首选，四个本地改进模板各占 10%。
- 本地模板首选尺寸为 `100x100`，地形受限时降为 `88x88`、`76x76`；房屋与功能建筑必须同步增加。
- 原生和本地模板都必须把实际最终城区矩形交给 EES 外墙、外环、驻军、角色建筑和奖励房。
- 已生成地图不迁移；游戏验收只使用未访问的新敌方据点并读取 `Player.log`，不截图。

## 当前实现

- 九阵营候选均为 10 个等概率槽位：原生候选重复六次，四个本地候选各一次。
- 注册表支持共享原生 Def；KCSG 生成后以新建筑实际包围框校准 EES 的城区矩形。
- 36 套本地模板使用 `100x100` 主档和 `88x88`、`76x76` 地形档；每个阵营至少 18 个住宅或帐篷槽位，并具有不同的道路、库存和核心陈设签名。
- 九阵营始终拦截 EES 原生 `RefogVault`。仅内部非建筑格保留迷雾；墙、门、门内外接近格强制解雾；无效奖励房清除 marker。
- `3209927822` 只给原版帝国、部落、外来者和海盗注册候选。其池含电力、炮塔、迫击炮和工业库存，因此保持兼容共存但不复用到九阵营。

## 必须保持的约束

1. 只修改本补丁；数字 Workshop 目录保持只读。
2. `3444347874` 是实际依赖；`3726700923`、`3209927822` 只参考或兼容共存，不能静默新增依赖。
3. 法国 V4 保持在 `Deprecated/France-V4-Rejected`，禁止重新启用。
4. 保留提交 `4fcb47a` 的九阵营 `AddTurrets` 精确 NRE 隔离。
5. 河湖不整片填平；城区内自然山石可替换，城区外不动；EES 使用最终选址矩形。
6. 普通据点不得生成组件、Plasteel、科技蓝图、枪械、现代建筑或 40K 物品。
7. 所有文本、XML 和脚本使用显式严格 UTF-8；重要旧内容移入 `Deprecated`，不直接删除。

## 原生候选

| 阵营 | 原生候选 |
|---|---|
| 法国、神罗 | `BA_HRETown` |
| 波兰 | `BA_PolishTown` |
| 拜占庭 | `BA_ByzantiumTown` |
| 突厥 | `BA_TurkishTown` |
| 摩尔、葡萄牙、西班牙 | `BA_IberiaTown` |
| 蒙古 | `BA_MongolTown` |

## 实施入口

- 当前五轮方案：`MEDIEVAL-KINGDOMS-FACTION-EXPANSION-PLAN.zh-CN.md`
- 核心运行时：`Source/EESMedievalPatch.cs`
- 模板生成器：`Tools/Generate-*.ps1`
- 静态验证：`Tools/Validate-FactionExpansion.ps1`、`Tools/Validate-TerrainProfiles.ps1`
- 游戏日志成功字段：`Calibrated registered town footprint`、`Vault interior refog`、`foggedBoundaryCells=0`、`doorApproachReachable=true`

## 最终验收

- 2026-08-12 使用 `-quicktest -eesmkacceptance` 创建临时、未访问且不保存的敌方据点地图；九阵营各验证一个原生候选和一个本地候选，结果为 `COMPLETE passed=18, failed=0, total=18`。
- 最新 `Player.log` 同时覆盖 `waterCells=0` 的干地、水系地图和超过 28000 个山体格的重山地；本地选址包含主档及 `fallbackIndex=2` 的最小档。
- 7 个具有有效奖励房的样本均记录 `foggedBoundaryCells=0`、`doorApproachReachable=true`；其余样本没有可加雾的有效奖励房。
- `[EES] ReinforceGarrison failed`、验收 `FAIL/ABORT`、`AddTurrets` 异常、缺失 Def、XML、KCSG 和 Harmony 错误均为 0。
- 验收只分析 `C:\Users\Administrator\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log`，没有截图，也没有载入、修改或保存用户存档。

## 历史错误防线

- 候选、Def、C# 注册和验证清单必须集合一致，禁止靠版本后缀猜模板功能。
- 主菜单加载不等于据点生成通过；没有新鲜 `Player.log` 不宣称游戏验收通过。
- 无完整墙环、有门且入口可达的奖励房不得加雾。
- 偏移、缩小、跨河或分区后必须同步 EES 最终矩形，不能只移动 KCSG 核心。
- 不为每类地形复制模板；先整体偏移，再缩小，最后采用开放/分段防御或明确 fallback。
