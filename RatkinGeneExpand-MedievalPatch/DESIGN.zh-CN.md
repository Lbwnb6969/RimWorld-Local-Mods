# [OA]Ratkin Gene Expand 中世纪适配说明

- 目标版本：RimWorld 1.6
- 基因路线：Fantasy Biotech
- 基础鼠族研究：`VR_BasicGenetics -> RatkinGene`
- 金鸢尾兰研究：`RatkinGene + VR_AdvancedGenetics -> OA_RK_RatkinGene`
- 适配建筑：2 座鼠族基因设施和 5 座金鸢尾兰基因设施

建筑成本只使用钢铁和黄金。自定义程序集会直接访问 `PowerComp.PowerOn`，因此补丁保留 `CompPowerTrader`，将建筑设为输出 1 W，并设置 `transmitsPower=true`。每座设施会形成独立电网，不需要电线或外部发电设施也能保持原有工作逻辑。

补丁还允许鼠族基因分析仪连接 `VR_GeneRack`，并允许 `VR_GraftForge` 连接金鸢尾兰基因储存箱和处理器。补丁不修改基因效果、提取成功率、工作时间、建筑类或 Workshop 原文件。

2026-08-07 静态审计：16 个 PatchOperation 全部命中；`RatkinGene` 与 `OA_RK_RatkinGene` 均改为中世纪科技并使用普通研究台；七座设施的成本、供电组件和 Fantasy Biotech 设施连接全部解析成功。按用户要求未进行进游戏验证。
