# RimWorld 本地模组合集

个人整合包中的25个本地模组、兼容补丁及中文翻译，当前包含1.6与1.7分支。快照日期：2026-10-16。

## 安装

按需将本仓库中的单个模组文件夹放入 RimWorld/Mods，在游戏中启用并满足各 About/About.xml 声明的依赖与加载顺序。不要直接同时启用全部模组：独立汉化与整合汉化存在内容重叠，按自己的整合方式选择。EES独立核心与作者工坊版按其LoadFolders条件选择，Masters移植版不要与原版同时启用。

工坊依赖、原版游戏、存档、个人设置、测试日志、缓存与废弃备份不包含在本仓库。已退役的LocalPerformance系列也不包含。

## 1.7 构建

已编译的运行DLL随模组包含。需自行构建时使用 .NET 10 SDK，例如：

```powershell
dotnet build Build17/DubsApparelTweaks.ColonistsOnly/Build.csproj -p:RimWorldDir="D:/SteamLibrary/steamapps/common/RimWorld" -p:WorkshopDir="D:/SteamLibrary/steamapps/workshop/content/294100"
```

构建依赖安装在你自己的游戏/工坊目录，部分项目需先构建EES核心。Build17使用现有本地构建项目并将路径参数化；本次上传未重新验证全部项目构建。

## 内容

| 文件夹 | packageId | 声明版本 |
|---|---|---|
| AlphaVehicles-AgeOfSail-ChinesePatch | Local.AlphaVehicles.AgeOfSail.ChineseSimplified | 1.6, 1.7 |
| AnimalApparel-BasicArmor-ChinesePatch | Local.AnimalApparel.BasicArmor.ChineseSimplified | 1.6, 1.7 |
| CaptureExpansion-ChinesePatch | Local.CaptureExpansion.ChineseSimplified | 1.6, 1.7 |
| CastleWallsExpanded-ChinesePatch | Local.CastleWallsExpanded.ChineseSimplified | 1.6, 1.7 |
| CastleWallsExpanded-Reborn-CompatibilityPatch | Local.CastleWallsExpanded.RebornCompatibility | 1.6, 1.7 |
| CastleWallsReborn-ChinesePatch | Local.CastleWallsReborn.ChineseSimplified | 1.6, 1.7 |
| DubsApparelTweaks-ColonistsOnly | local.dubsappareltweaks.colonistsonly | 1.6, 1.7 |
| EvolvingEnemyStrongholds-MedievalPatch | Local.EvolvingEnemyStrongholds.MedievalPatch | 1.6, 1.7 |
| HospitalityCasino-MedievalPatch | Local.HospitalityCasino.MedievalPatch | 1.6, 1.7 |
| LocalChineseIntegrated | Local.Chinese.Integrated | 1.6, 1.7 |
| MedievalGeneAssembler-CompatibilityPatch | Local.DDJY.MedievalGeneAssembler.Compatibility | 1.6, 1.7 |
| MedievalThings-Geysers-ChinesePatch | Local.MThings.Geysers.ChineseSimplified | 1.6, 1.7 |
| MedievalThings-Medical-ChinesePatch | Local.MThings.Medical.ChineseSimplified | 1.6, 1.7 |
| Progression-Agriculture-ChinesePatch | Local.Progression.Agriculture.ChineseSimplified | 1.6, 1.7 |
| Progression-Production-ChinesePatch | Local.Progression.Production.ChineseSimplified | 1.6, 1.7 |
| Progression-Warfare-ChinesePatch | Local.Progression.Warfare.ChineseSimplified | 1.6, 1.7 |
| RatkinGeneExpand-MedievalPatch | Local.OARK.RatkinGeneExpand.MedievalPatch | 1.6, 1.7 |
| RimDark40K-LostArmory-Vanilla | Local.RimDark40K.LostArmory.Vanilla | 1.6, 1.7 |
| RimDark40k-MankindsFinest-MedievalPatch | Local.RimDark.MankindsFinest.MedievalPatch | 1.6, 1.7 |
| RimDark40k-MastersOfMankind-Port | Local.RimDark.MastersOfMankind | 1.6, 1.7 |
| StartupLog-CompatibilityPatch | Local.StartupLog.CompatibilityFixes | 1.6, 1.7 |
| VFE-Medieval2-Security-CompatibilityPatch | Local.VFE.Medieval2.SecurityCompatibility | 1.6, 1.7 |
| WeaponStats-ChinesePatch | Local.WeaponStats.ChineseSimplified | 1.6, 1.7 |
| WorkshopChineseIntegrated | Local.Workshop.ChineseIntegrated | 1.6, 1.7 |
| WorldTileMutatorEditor | Local.WorldTileMutatorEditor | 1.6, 1.7 |

## 来源与权利

保留各模组About中的作者、依赖、工坊链接以及已有来源说明/许可文件。合集包含原作者内容的本地移植与汉化，并非全部原创。此仓库不为上游素材重新授予许可；原作者代码、纹理、音效和翻译的权利仍属于各自权利人，使用与再分发需遵守其原有许可。

## 验证范围

本次上传验证文件完整性、XML可解析性与敏感字符串；沿用此前本机Steam1.7存档副本的针对性验证结论。没有重跑全部玩法或测量长期TPS，也不将supportedVersions声明视为全部功能保证。
