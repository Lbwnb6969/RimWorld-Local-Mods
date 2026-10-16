# 本地补丁整合与 EES 中世纪覆盖验收

## 结论

当前工作区适合保留为少量职责清晰的本地 Mod，而不是把所有内容压成一个包：

| 组 | 目录 | 处理建议 |
| --- | --- | --- |
| EES 中世纪适配 | `EvolvingEnemyStrongholds-MedievalPatch` | 保持独立；依赖 EES、Medieval Overhaul、VFE Medieval 2，覆盖 9 个已安装的 Medieval Kingdoms 贵族阵营。 |
| 城墙兼容 | `CastleWallsExpanded-Reborn-CompatibilityPatch` | 保持独立并放在两个 Castle Walls 模组之后；包含平衡 XML 和程序集。 |
| 简体中文 | 各 `*-ChinesePatch`、`MedievalKingdoms-Core-Research-ChineseSupplement` | 可在发布时合并成一个汉化合集，但各对象仍保留独立依赖，避免缺少原模组时整包加载失败。 |
| 功能兼容 | `VFE-Medieval2-Security-CompatibilityPatch`、`RatkinGeneExpand-MedievalPatch` 等 | 不与汉化合并；这些补丁修改研究、建筑或程序集行为，独立更易定位错误。 |

## EES 覆盖状态

- 法国、神圣罗马帝国、波兰、拜占庭、突厥、摩尔、葡萄牙、西班牙、蒙古各有 4 套文化城镇模板（共 36 套），使用原阵营核心布局，再叠加 EES 中世纪外围防御。
- 现代炮台、迫击炮、弹药库和通讯设施已过滤；高阶外围可使用 Medieval Overhaul/VFE Medieval 2 的中世纪弩炮、弩塔和城墙资源。
- T1-T13 研究门槛、驻军总槽位、后期第二精英队和奖励分级已由程序集实现。
- `BA_Takeda_NobleHouse`、`BA_Tokugawa_NobleHouse`、`BA_Uesugi_NobleHouse`、`BA_Mori_NobleHouse` 目前只有代码白名单，工作区未找到对应源 Def，不能宣称日本阵营模板已完成。安装 Shogunate 内容后应再增加 4 套日本模板并加入验收清单。

## 验收命令

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\EvolvingEnemyStrongholds-MedievalPatch\Tools\Validate-EESMedievalCoverage.ps1
```

脚本使用显式 UTF-8 读取 XML；任何解析失败、阵营模板少于 4 套或现代设施 Def 泄漏都会以非零状态退出。

## 保留与边界

不要删除 `Deprecated` 中的历史设计、旧补丁和验收日志；它们是回滚和审计依据。不要把二进制程序集、Workshop 原模组文件或 `ModsConfig.xml` 复制进汉化合集。每次修改前先建立 Git 备份引用，并在修改后重新运行静态验收。
