using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace FactionTerritoriesZh
{
    [StaticConstructorOnStartup]
    public static class HardcodedTextPatcher
    {
        private const string HarmonyId = "shenpc.factionterritories.zh.hardcoded";

        private static readonly Dictionary<string, string> Text = new Dictionary<string, string>
        {
            { "Faction Territories", "派系领土" },
            { "Default map mode: Faction Territories", "默认地图模式：派系领土" },
            { "Faction", "派系" },
            { "Faction: ", "派系：" },
            { "Territory color", "领土颜色" },
            { "Override this faction's territory colour for the Faction Territories map mode.", "覆盖该派系在“派系领土”地图模式中的领土颜色。" },
            { "Use override color", "使用覆盖颜色" },
            { "Clear all faction colour overrides", "清除所有派系颜色覆盖" },
            { "Contested", "争夺中" },
            { "Contested Territory", "争夺领土" },

            { "Caravan incidents", "商队事件" },
            { "Encounter check interval", "遭遇检查间隔" },
            { "How often caravan incident checks run. The slider uses a sensible working range, but manual input can exceed it.", "商队事件检查的频率。滑块使用较合理的范围，但手动输入可以超出该范围。" },
            { "CaravanMeeting", "商队会面" },
            { "Ambush", "伏击" },
            { "Attack", "攻击" },
            { "Join fight", "加入战斗" },
            { "Your caravan has entered the battle at ", "你的商队已进入战斗地点：" },
            { "You have left the map at ", "你已离开地图：" },
            { "You helped ", "你帮助了" },
            { " forces have arrived to support ", "的部队已抵达支援" },
            { " forces have launched a territory incursion on ", "的部队对以下地点发动了领土侵扰：" },
            { "Caravan entered warzone", "商队进入战区" },
            { "Base invasion resolved", "基地入侵已解决" },
            { "\n\nThe battle will resolve in ", "\n\n战斗将在以下时间后解决：" },
            { " unless you intervene.", "，除非你介入。" },
            { "This battle is no longer active.", "这场战斗已不再进行。" },

            { "Territory generation", "领土生成" },
            { "Base radius used when generating faction territory around settlements.", "围绕定居点生成派系领土时使用的基础半径。" },
            { "Radius", "半径" },
            { "Radius (tile steps)", "半径（地块步数）" },
            { "Variation (± tile steps, 0 = perfect circle)", "变化幅度（±地块步数，0 = 完美圆形）" },
            { "How much generated territory edges can vary from the base radius.", "生成的领土边缘相对基础半径可产生的变化量。" },
            { "Seed override (0 = world seed)", "种子覆盖（0 = 世界种子）" },
            { "Lets you force a consistent alternate seed for territory generation.", "允许为领土生成强制使用一个固定的替代种子。" },
            { "Movement cost shaping", "移动成本塑形" },
            { "Terrain difficulty impact", "地形难度影响" },
            { "How strongly tile movement difficulty affects territory spread.", "地块移动难度影响领土扩张形状的强度。" },
            { "Extra movement difficulty applied to impassable hill tiles when shaping territory.", "塑造领土时施加给不可通行丘陵地块的额外移动难度。" },
            { " movement difficulty for impassable hill tiles", "不可通行丘陵地块移动难度" },
            { "Rim War integration", "Rim War 集成" },
            { "Add to territory radius based on Rim War SettlementScanRange: ", "根据 Rim War 的 SettlementScanRange 增加领土半径：" },
            { "Adds extra territory radius based on Rim War settlement scan range.", "根据 Rim War 定居点扫描范围增加额外领土半径。" },
            { "Effective extra radius per settlement is: round(SettlementScanRange × percent).", "每个定居点的有效额外半径为：round(SettlementScanRange × 百分比)。" },
            { "Road movement multiplier", "道路移动倍率" },
            { "Include water tiles", "包含水域地块" },
            { "When enabled, water tiles can be included in rendered territory.", "启用后，渲染领土时可以包含水域地块。" },
            { "Default map mode", "默认地图模式" },
            { "When enabled, the game will switch to the Faction Territories map mode the first time the World map is opened.", "启用后，首次打开世界地图时会自动切换到“派系领土”地图模式。" },
            { "Hide MapModeFramework toolbar", "隐藏 MapModeFramework 工具栏" },
            { "When enabled, hides the MapModeFramework toolbar UI on the world map. Hotkeys may still work.", "启用后，在世界地图隐藏 MapModeFramework 工具栏界面。快捷键可能仍然有效。" },
            { "Draw borders", "绘制边界" },
            { "Draws border outlines around territory regions.", "在领土区域周围绘制边界轮廓。" },
            { "Use solid averaged colour for contested territory", "争夺领土使用平均纯色" },
            { "When enabled, contested territory uses a flat colour made from the unweighted average of all claiming faction territory colours instead of the contested Voronoi pattern.", "启用后，争夺领土会使用所有宣称派系领土颜色的未加权平均纯色，而不是争夺 Voronoi 图案。" },
            { "Fill alpha", "填充透明度" },
            { "Opacity of the territory fill colour.", "领土填充颜色的不透明度。" },
            { "Border alpha", "边界透明度" },
            { "Opacity of territory borders.", "领土边界的不透明度。" },
            { "Border width", "边界宽度" },
            { "Thickness of rendered territory borders.", "渲染出的领土边界厚度。" },
            { "Territory label opacity", "领土标签透明度" },
            { "Opacity of world-map territory labels.", "世界地图领土标签的不透明度。" },
            { "Minimum connected tiles for a label (0 = always)", "显示标签所需的最小连通地块数（0 = 始终显示）" },
            { "Minimum region size required before a territory label is shown.", "显示领土标签前所需的最小区域规模。" },
            { "Show contested labels", "显示争夺标签" },
            { "When enabled, contested regions will show a \"Contested\" label. Region colouring and tooltips remain regardless.", "启用后，争夺区域会显示“争夺中”标签。区域着色和提示不受影响。" },
            { "Settlements", "定居点" },
            { "Uncontested range around settlements (0 = disabled)", "定居点周围非争夺范围（0 = 禁用）" },
            { "Tiles around settlements that should stay uncontested.", "定居点周围应保持非争夺状态的地块数。" },

            { "Map incursions", "地图侵扰" },
            { "Temporary hostile territory incursions", "临时地图敌对领土侵扰" },
            { "Temporary friendly territory incursions", "临时地图友好领土侵扰" },
            { "Persistent hostile territory incursions", "持久地图敌对领土侵扰" },
            { "Persistent friendly territory incursions", "持久地图友好领土侵扰" },
            { "Allows hostile faction incursions on temporary maps created inside their territory.", "允许敌对派系在其领土内生成的临时地图上发动侵扰。" },
            { "Allows non-hostile faction incursions on temporary maps created inside their territory.", "允许非敌对派系在其领土内生成的临时地图上发动侵扰。" },
            { "Allows hostile faction incursions on persistent maps inside their territory.", "允许敌对派系在其领土内的持久地图上发动侵扰。" },
            { "Allows non-hostile faction incursions on persistent maps inside their territory.", "允许非敌对派系在其领土内的持久地图上发动侵扰。" },
            { "Incursion chance on map entry", "进入地图时侵扰概率" },
            { "Chance for an incursion check when entering a map inside faction territory.", "进入派系领土内地图时进行侵扰检查的概率。" },
            { "Base interval between faction map incursions", "派系地图侵扰基础间隔" },
            { "Base time between automatic faction territory incursions. The slider uses a sensible working range, but manual input can exceed it.", "自动派系领土侵扰之间的基础时间。滑块使用较合理的范围，但手动输入可以超出该范围。" },
            { "Adds variance to the incursion interval so checks do not happen on a perfectly fixed cadence.", "为侵扰间隔增加随机浮动，避免检查完全固定节奏发生。" },

            { "Enable Vassalisation", "启用附庸化" },
            { "Enable defeated settlement vassalisation prompt", "启用击败定居点后的附庸化提示" },
            { "When disabled, defeated settlements will not show the vassalisation choice window. The settlement will simply remain destroyed.", "禁用后，被击败的定居点不会显示附庸化选择窗口，该定居点会直接保持被摧毁状态。" },
            { "Settlement purchase goodwill cost: ", "购买定居点所需好感：" },
            { "Vassalage points", "附庸点数" },
            { "Vassalage points per day per vassal", "每个附庸每日提供的附庸点数" },
            { "Vassalage point multiplier per tech level", "按科技等级调整的附庸点数倍率" },
            { "Colonist cost", "殖民者花费" },
            { "Colonist cost multiplier: x", "殖民者花费倍率：x" },
            { "Cost = PawnKind combat power (threat points) × multiplier.", "花费 = 角色种类战斗力（威胁点数）× 倍率。" },
            { "After purchase, choose whether they join as colonist or slave in the offer letter.", "购买后，可在提议信中选择让其作为殖民者或奴隶加入。" },
            { "- Points are stored per faction; factions with at least one vassal outpost show a Vassalage button in the faction tab.", "- 点数按派系分别储存；至少拥有一个附庸前哨站的派系会在派系标签页显示“附庸关系”按钮。" },
            { "- Purchasing items is restricted to what the faction can sell via caravan trader kinds.", "- 可购买物品受该派系商队商人类型可出售内容限制。" },
            { "Vassalage", "附庸关系" },
            { "Open vassalage screen (purchase with points).", "打开附庸关系界面（使用点数购买）。" },
            { "Open vassalage status and purchase options for this outpost's original faction.", "打开该前哨站原所属派系的附庸状态与购买选项。" },
            { "Vassalage points: ", "附庸点数：" },
            { "No vassalage points available.", "没有可用的附庸点数。" },
            { "Not enough vassalage points.", "附庸点数不足。" },
            { "Missing vassalage points component.", "缺少附庸点数组件。" },
            { "No vassal outposts exist.", "没有附庸前哨站。" },
            { "No valid vassal factions found.", "没有找到有效的附庸派系。" },
            { "Missing vassal components.", "缺少附庸组件。" },

            { "Base invasions", "基地入侵" },
            { "Enable base invasions", "启用基地入侵" },
            { "When disabled, no new base invasions will be created automatically. Existing invasions can still resolve normally.", "禁用后，不会自动生成新的基地入侵。已存在的入侵仍会正常解决。" },
            { "Base interval between new invasions", "新入侵基础间隔" },
            { "Base time between automatic invasion attempts. The slider uses a sensible working range, but manual input can exceed it.", "自动入侵尝试之间的基础时间。滑块使用较合理的范围，但手动输入可以超出该范围。" },
            { "Adds variance to the invasion interval so invasions do not happen on a perfectly fixed cadence.", "为入侵间隔增加随机浮动，避免入侵完全固定节奏发生。" },
            { "Time before an invasion resolves off-map", "入侵离线解决前的时间" },
            { "How long an unattended invasion can persist on the world map before it resolves without a player visit.", "无人介入的入侵在未被玩家访问时，可在世界地图持续多久后自动解决。" },
            { "Only one new invasion will be added at a time.", "每次只会添加一个新的入侵。" },
            { "Prevents several fresh invasions from being created in the same pass.", "防止同一轮中生成多个新的入侵。" },
            { "Base invasion", "基地入侵" },
            { "Base invasion: ", "基地入侵：" },
            { "Attacker: ", "进攻方：" },
            { "Defender: ", "防守方：" },
            { "Winner: ", "胜利方：" },
            { "Expires in: ", "剩余时间：" },
            { "Force a base invasion event on this settlement.", "在该定居点强制触发一次基地入侵事件。" },
            { "Force a base invasion event on this vassal outpost.", "在该附庸前哨站强制触发一次基地入侵事件。" },
            { "DEV: Force invasion", "开发：强制入侵" },
            { "Could not force invasion.", "无法强制入侵。" },
            { "Could not choose an attacker.", "无法选择进攻方。" },
            { "Failed creating invasion world object.", "创建入侵世界物体失败。" },
            { "That settlement already has an active invasion.", "该定居点已有进行中的入侵。" },
            { "That vassal already has an active invasion.", "该附庸已有进行中的入侵。" },
            { "No eligible hostile attacker claims this settlement's tile.", "没有符合条件的敌对进攻方宣称该定居点地块。" },
            { "No eligible hostile attacker claims this vassal's tile.", "没有符合条件的敌对进攻方宣称该附庸地块。" },
            { "Could not create a battle map for this vassal.", "无法为该附庸创建战斗地图。" },

            { "Settlement construction", "定居点建设" },
            { "Enable settlement construction", "启用定居点建设" },
            { "When enabled, eligible factions can create settlement construction sites near the edge of their territory.", "启用后，符合条件的派系可在其领土边缘附近创建定居点建设工地。" },
            { "Per-faction interval between new construction sites", "各派系新建设工地间隔" },
            { "Base time between settlement construction attempts for each eligible faction separately. The slider uses a sensible working range, but manual input can exceed it.", "每个符合条件派系各自尝试建设定居点之间的基础时间。滑块使用较合理的范围，但手动输入可以超出该范围。" },
            { "Adds variance to each faction's construction interval so factions do not all expand in lockstep.", "为各派系建设间隔增加随机浮动，避免所有派系同步扩张。" },
            { "Construction time before becoming a settlement", "变为定居点前的建设时间" },
            { "How long a construction site remains on the world map before it finishes and turns into a settlement if not defeated.", "建设工地未被击败时，会在世界地图上保持多久后完成并变为定居点。" },
            { "Construction sites do not claim territory until they finish.", "建设工地完成前不会宣称领土。" },
            { "Territory is only added once the construction site completes and becomes a real settlement.", "领土只会在建设工地完成并成为真正定居点后添加。" },
            { "Settlement completed: ", "定居点建设完成：" },
            { "Settlement construction: ", "定居点建设：" },
            { "Attacking construction", "攻击建设工地" },
            { " has begun constructing a new settlement near the edge of its territory.", "已开始在其领土边缘附近建设新的定居点。" },
            { " has established a new settlement.", "已建立新的定居点。" },
            { "Defender threat multiplier: x", "防守方威胁倍率：x" },
            { "Multiplier applied to defender strength for generated construction site defence maps.", "应用于生成建设工地防御地图时防守方强度的倍率。" },
            { "Completes in: ", "完成还需：" },
            { "Current tile: ", "当前地块：" },
            { "Tiles remaining (incl. current): ", "剩余地块（含当前）：" },
            { "Tiles: ", "地块：" },

            { "Vassalisation opportunity", "附庸化机会" },
            { "Vassalise", "纳为附庸" },
            { "Vassalise instead", "改为纳为附庸" },
            { "Do not vassalise", "不附庸化" },
            { "Leave it to ", "留给" },
            { "Jump to tile", "跳转到地块" },
            { "an allied faction", "盟友派系" },
            { "an enemy settlement", "敌方定居点" },
            { "its current owner", "其当前所有者" },
            { "capture this settlement.\n\nYou can leave the base to them", "占领该定居点。\n\n你可以把基地留给他们" },
            { ", or vassalise it for yourself", "，或将其纳为自己的附庸" },
            { ".\n\nDo you want to convert it into a vassal?\n\nThis will remove the destroyed settlement marker and create a vassal outpost on the same tile.", "。\n\n是否要将其转化为附庸？\n\n这会移除被摧毁的定居点标记，并在同一地块创建一个附庸前哨站。" },
            { "Do you want to convert this defeated settlement into a vassal outpost?\n\nThis will remove the destroyed settlement marker and create a vassal outpost on the same tile.", "是否要将这个被击败的定居点转化为附庸前哨站？\n\n这会移除被摧毁的定居点标记，并在同一地块创建一个附庸前哨站。" },
            { "Vassalage is disabled in settings.", "设置中已禁用附庸关系。" },
            { "Invalid settlement.", "无效定居点。" },
            { "Invalid settlement tile.", "无效定居点地块。" },
            { "Invalid settlement faction.", "无效定居点派系。" },
            { "Could not vassalise this settlement.", "无法将该定居点纳为附庸。" },
            { "Could not leave this settlement to your ally.", "无法将该定居点留给你的盟友。" },
            { "Could not buy this settlement as a vassal.", "无法将该定居点购买为附庸。" },
            { "Could not spend the required goodwill.", "无法消耗所需好感。" },
            { "Cannot spend goodwill with this faction.", "无法与该派系消耗好感。" },
            { "Could not convert this settlement into a vassal outpost.", "无法将该定居点转化为附庸前哨站。" },
            { "Buy as vassal", "购买为附庸" },
            { "Buy settlement as vassal", "购买定居点作为附庸" },
            { "Spend goodwill to convert this settlement into a vassal outpost.", "消耗好感，将该定居点转化为附庸前哨站。" },
            { "Cannot buy this settlement as a vassal.", "不能将该定居点购买为附庸。" },
            { "Settlement: ", "定居点：" },
            { "Current goodwill: ", "当前好感：" },
            { "This will spend goodwill, remove the settlement, and replace it with a vassal outpost.\nTerritory regions and vassalage points will update immediately.", "这会消耗好感，移除该定居点，并将其替换为附庸前哨站。\n领土区域和附庸点数会立即更新。" },
            { "DEV: Vassalise settlement", "开发：附庸化定居点" },
            { "Convert this settlement into a vassal outpost (Faction Territories).", "将该定居点转化为附庸前哨站（派系领土）。" },
            { "Vassal Outpost", "附庸前哨站" },
            { "Vassal Outpost: ", "附庸前哨站：" },
            { "Vassal outpost has no valid original faction.", "附庸前哨站没有有效的原派系。" },
            { "VassalOutpost renamed", "附庸前哨站已重命名" },
            { "Rename vassal outpost", "重命名附庸前哨站" },
            { "Rename the vassal outpost label.", "重命名附庸前哨站标签。" },
            { "\nOriginal settlement: ", "\n原定居点：" },
            { "Original faction: ", "原派系：" },

            { "Vassal Colonist Offer", "附庸殖民者提议" },
            { "The vassal's offer has expired.", "该附庸的提议已过期。" },
            { "Accept (colonist)", "接受（殖民者）" },
            { "Accept (slave)", "接受（奴隶）" },
            { " is ready to join your colony.\n\nChoose whether they join as a free colonist or as a slave.\n\nYou have 24 hours to decide.", "已准备加入你的殖民地。\n\n请选择让其作为自由殖民者还是奴隶加入。\n\n你有 24 小时做出决定。" },
            { " has joined your colony.", "已加入你的殖民地。" },

            { "Items", "物品" },
            { "Weapons", "武器" },
            { "Apparel", "服装" },
            { "Buildings", "建筑" },
            { "Resources", "资源" },
            { "Food", "食物" },
            { "Medicine", "医疗用品" },
            { "Pawns", "角色" },
            { "Colonists", "殖民者" },
            { "Search", "搜索" },
            { "Category", "类别" },
            { "Buy", "购买" },
            { "Buy vassal", "购买附庸" },
            { "Buy x", "购买 x" },
            { "Buy x1 (cost: ", "购买 x1（花费：" },
            { "Buy max you can afford: x", "购买可负担最大数量：x" },
            { "Cost (x1)", "花费（x1）" },
            { "Cost: ", "花费：" },
            { "Cannot afford any", "无法负担任何数量" },
            { "No sellable items found for this faction (with current filters).", "当前筛选条件下未找到该派系可出售的物品。" },
            { "Items are restricted to what this faction would sell at its settlements. Purchased items arrive by drop pods.", "物品范围限定为该派系在其定居点会出售的内容。购买的物品会通过空投舱送达。" },
            { "Could not create the requested purchase.", "无法创建请求的购买项目。" },
            { "Delivery failed.", "送达失败。" },
            { "Delivery failed: ", "送达失败：" },
            { "Delivery succeeded but points could not be deducted (state changed).", "送达成功，但点数无法扣除（状态已变化）。" },
            { "Delivery item unexpectedly spawned before caravan transfer.", "物品在商队转移前意外生成。" },
            { "Could not find a safe drop spot (no unroofed/standable cell).", "找不到安全投放点（没有露天且可站立的格子）。" },
            { "No player home map and no player caravan available.", "没有可用的玩家基地地图或玩家商队。" },
            { "No player home map available.", "没有可用的玩家基地地图。" },
            { "Caravan inventory unavailable.", "商队库存不可用。" },
            { "Caravan inventory full or cannot accept items.", "商队库存已满或无法接收物品。" },
            { "That building cannot be delivered in minified form.", "该建筑无法以打包形式送达。" },
            { "Nothing to deliver.", "没有可送达的内容。" },
            { "Purchased ", "已购买" },
            { "Received ", "已收到" },
            { " Refunded ", "已退款" },
            { " silver tribute.", "白银贡品。" },

            { "Roads", "道路" },
            { "Road construction", "道路建设" },
            { "Road", "道路" },
            { "Active project: ", "当前项目：" },
            { "No active road project.", "没有当前道路项目。" },
            { "No road is currently being built.", "当前没有正在建设的道路。" },
            { "Missing road progress component.", "缺少道路进度组件。" },
            { "Invest points", "投入点数" },
            { "Invest ", "投入" },
            { "Invest all available points (", "投入所有可用点数（" },
            { "Invested ", "已投入" },
            { " points into road progress.", "点道路进度。" },
            { " points into roads. Completed ", "点到道路。已完成" },
            { "Current: ", "当前：" },
            { "ProgressPerTile", "每地块进度" },
            { "RoadProgressPerTile", "每地块道路进度" },

            { "All", "全部" },
            { "OK", "确定" },
            { "Cancel", "取消" },
            { "Dismiss", "关闭" },
            { "Rename", "重命名" },
            { "Rebuild", "重建" },
            { "Recalculate", "重新计算" },
            { "Recompute", "重新计算" },
            { "Regenerate", "重新生成" },
            { "Clear", "清除" },
            { "Claim", "宣称" },
            { "Reject", "拒绝" },
            { "Recruit", "招募" },
            { "Rendering", "渲染" },
            { "Labels", "标签" },
            { "Expansion", "扩张" },
            { "Verbose logging", "详细日志" },
            { "Accepts values like 12h, 1d, 1d 12h, or a plain number of hours. Manual entry is not capped by the slider range.", "接受 12h、1d、1d 12h 或纯小时数等值。手动输入不受滑块范围限制。" },
            { "Interval randomisation: ±", "间隔随机浮动：±" },
            { "Variation", "变化幅度" },
            { "Variation (", "变化幅度（" },
            { "Cost = combat power × ", "花费 = 战斗力 × " },
            { " + average equipment cost x ", " + 平均装备花费 x " },
            { "This is the applied formula.", "这是实际使用的公式。" },
            { "No valid player home map to deliver tribute.", "没有可用于送达贡品的有效玩家基地地图。" },
            { "Unknown", "未知" },
            { "(unknown faction)", "（未知派系）" },
            { "your ally", "你的盟友" },
            { "your vassal", "你的附庸" },
            { "battle", "战斗" },
            { "this map", "这张地图" },
            { "tile(s).", "个地块。" },
            { " points.", "点数。" },
            { " points).", "点）。" },
            { " points to ", "点给" },
            { " vassal factions.", "个附庸派系。" },
            { " goodwill", "好感" },
            { " goodwill)", "好感）" },
            { " goodwill with ", "好感，与" },
            { ". Current goodwill: ", "。当前好感：" },
            // Preserve the 13 mappings present in the deployed integrated DLL.
            { " (cost: ", "（花费：" },
            { " from ", "，原属" },
            { " has secured ", "已成功夺取" },
            { " is attempting to seize ", "正在试图夺取" },
            { "Adds +", "增加+" },
            { "Attacking ", "正在攻击" },
            { "Invalid faction.", "无效派系。" },
            { "Invalid selling faction.", "无效出售派系。" },
            { "Invalid vassal outpost.", "无效附庸前哨站。" },
            { "Requires ", "需要" },
            { "Settlement has no valid non-player defender.", "定居点没有有效的非玩家防守方。" },
            { "\nTiles: ", "\n地块：" },
            { "tile ", "地块" }
        };

        static HardcodedTextPatcher()
        {
            try
            {
                new Harmony(HarmonyId).PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (Exception ex)
            {
                Log.Error("[FactionTerritoriesZh] Failed to apply hardcoded text patches: " + ex);
            }
        }

        public static bool TryTranslate(string source, out string result)
        {
            if (source != null && Text.TryGetValue(source, out result))
            {
                return true;
            }
            result = source;
            return false;
        }

        public static bool HasSourceText(string source)
        {
            return source != null && Text.ContainsKey(source);
        }

        public static string TranslateString(string source)
        {
            if (LanguageDatabase.activeLanguage?.folderName?.StartsWith("ChineseSimplified",
                    StringComparison.Ordinal) == true && TryTranslate(source, out string result))
                return result;
            return source;
        }
    }

    [HarmonyPatch]
    public static class FactionTerritoriesStringTranspiler
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            Assembly asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "FactionTerritories");
            if (asm == null)
            {
                yield break;
            }

            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray();
            }

            foreach (Type type in types)
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters)
                    {
                        continue;
                    }
                    if (HasTranslatableString(method))
                    {
                        yield return method;
                    }
                }

                foreach (ConstructorInfo ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (HasTranslatableString(ctor))
                    {
                        yield return ctor;
                    }
                }
            }
        }

        private static bool HasTranslatableString(MethodBase method)
        {
            MethodBody body = method.GetMethodBody();
            if (body == null)
            {
                return false;
            }

            byte[] il = body.GetILAsByteArray();
            if (il == null)
            {
                return false;
            }

            for (int i = 0; i < il.Length;)
            {
                OpCode opcode;
                byte op = il[i++];
                if (op == 0xfe)
                {
                    if (i >= il.Length)
                    {
                        return false;
                    }
                    opcode = MultiByteOpCodes[il[i++]];
                }
                else
                {
                    opcode = SingleByteOpCodes[op];
                }

                if (opcode == OpCodes.Ldstr)
                {
                    if (i + 4 > il.Length)
                    {
                        return false;
                    }

                    int token = BitConverter.ToInt32(il, i);
                    try
                    {
                        if (HardcodedTextPatcher.HasSourceText(method.Module.ResolveString(token)))
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }
                    i += 4;
                    continue;
                }

                if (opcode.OperandType == OperandType.InlineSwitch)
                {
                    if (i + 4 > il.Length)
                    {
                        return false;
                    }
                    int count = BitConverter.ToInt32(il, i);
                    i += 4 + count * 4;
                    continue;
                }

                i += OperandSize(opcode);
            }

            return false;
        }

        private static int OperandSize(OpCode opcode)
        {
            switch (opcode.OperandType)
            {
                case OperandType.InlineNone:
                    return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    return 1;
                case OperandType.InlineVar:
                    return 2;
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR:
                    return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    return 8;
                default:
                    return 0;
            }
        }

        private static readonly OpCode[] SingleByteOpCodes = BuildOpCodeMap(false);
        private static readonly OpCode[] MultiByteOpCodes = BuildOpCodeMap(true);

        private static OpCode[] BuildOpCodeMap(bool multiByte)
        {
            OpCode[] result = new OpCode[256];
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                object value = field.GetValue(null);
                if (!(value is OpCode))
                {
                    continue;
                }

                OpCode opcode = (OpCode)value;
                if (multiByte == (opcode.Size == 2))
                {
                    result[opcode.Value & 0xff] = opcode;
                }
            }
            return result;
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                bool translatedLiteral = instruction.opcode == OpCodes.Ldstr && instruction.operand is string source &&
                    HardcodedTextPatcher.HasSourceText(source);
                yield return instruction;
                if (translatedLiteral)
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(HardcodedTextPatcher), nameof(HardcodedTextPatcher.TranslateString)));
            }
        }
    }
}
