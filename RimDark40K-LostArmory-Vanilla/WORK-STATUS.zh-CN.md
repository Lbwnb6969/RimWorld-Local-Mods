# Lost Armory 本地任务链

状态：已完成 EES 线索、三阶安全回收、失败补缺防重复和新旧任务兼容的深度验证；最新全库隔离回归 `RuntimeAudit-20261002-231108` 为 `failures=0`、退出码 0，覆盖三阶、失败、运输、过滤器和保存/重载。

全库审计后续改动：回收检查每次只建立一份目标 ID 快照，复用容器列表并跳过已回收槽位，不跨 Tick 缓存物品位置。线索信件使用世界层/地图格坐标，避免持有随后移除的据点引用；两条信件读档断言通过。三阶、失败、运输、过滤器及多次重载继续通过。当前 DLL SHA-256 为 `66AD1A4C6BFD51CB17571253F2C6B0CB2074308E3AECB87CC1A7F10E5F62B82A`；完整结果见工作区 `StartupLog-CompatibilityPatch/Docs/LOCAL_LIBRARY_AUDIT_20261002.zh-CN.md`，下文保留此前深度验证记录。

2026-10-02 最终轮次：

- 回收槽位持久保存阶位、装备 Def、实际 Thing ID 和已回收标志。放弃/超时后重试只补缺件；已安全回收装备以后售出或损毁不会重发；仍在地图、商队、容器或运输中的装备不复制。
- 生成前异常与任务已登记后的异常均能回滚锁、站点和新槽位；部分回收、全毁奖励、缺失 Site 和非胜利消失不会推进阶位。在途运输不提前结算，玩家地图落地容器或商队库存可以结算。
- 线索按实际世界层 ID、tile 和阵营 loadID 持久去重；同地点同阵营重建不能换对象 ID 重抽。
- 新任务 `completionVersion=1` 必须通过回收部件；仅旧 `completionVersion=0` 无回收部件活动任务保留原成功契约。进行中部分回收任务和旧活动任务均通过读档验证。
- 修复攻陷触发器：当前 RimWorld 的 `Map.components` 是公开字段，原来的仅非公开反射查找会拒绝实际 EES 地图。现在读取公开组件集合，继续要求真实 `eesGenerated` 标记；中心建筑拆毁后可以产生线索，普通地图不触发。

此前深度验证日志位于工作区根目录 `Deprecated/Backups/RuntimeAudit-20261002-125918/Player.runtime-audit.log`，结构化结果为同目录 `RuntimeAudit.result.json`，753 条 PASS 断言日志，退出码 0，`failures=0`。该轮 DLL SHA-256：`8B94DA7DA852F4846E513464057C7ADA6800727EE13D573952A696B8F40A79DC`。详细测试范围及失败记录见 `EvolvingEnemyStrongholds-MedievalPatch/Docs/EES_DEEP_VALIDATION_20261002.zh-CN.md`。

任务根 `Local_RimDark40K_LostChapterArmory`、`Local_RimDark40K_PrimarisSealedVault` 和 `Local_RimDark40K_GoldenArmory` 保留原研究、奖励质量、站点城堡和顺序门槛，但随机根权重设为 0；它们由本地程序集在 EES 据点判定摧毁后按 `1 -> 2 -> 3` 生成。

攻陷据点时先保存线索状态，发送“军械库线索”信件，约 600 ticks 后再生成任务。线索、已触发据点、当前任务阶位和已完成阶位都会保存；载入时发现孤立任务引用会清除，不会重复触发同一据点。触发仅检查 EES `MapComponent_StrongholdSiege.eesGenerated`，普通 Settlement、独立 Site 和非 EES 地图不触发。

站点生成后，任务部件登记每件奖励的 Thing ID，并保存站点、回收期限、回收信号和完成标志。地图生成后开始回收期限；站点消失只代表战斗段结束，只有全部登记奖励进入玩家殖民地或玩家商队才发送成功信号，奖励缺失或期限到达发送失败信号。旧的 `QuestNode_NoWorldObject` 成功分支已移除。

隔离运行审计：`Deprecated/Backups/RuntimeAudit-20261002-055950`，`LocalPatchAudit` 通过，三阶任务、站点城堡、物品质量、占位保护、三阶奖励实际转移、信号结算和保存重载均为 0 失败；最终标记为 `[LocalPatchAudit] DONE: three sites and save/reload checked; failures=0`。地图销毁本身的测试夹具使用脱离地图的已销毁 WorldObject，避免把整合包已有的 `MapDrawer.Dispose` NRE 当成任务逻辑结果。
