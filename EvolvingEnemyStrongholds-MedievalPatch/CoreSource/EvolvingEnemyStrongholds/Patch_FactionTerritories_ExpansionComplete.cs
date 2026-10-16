using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch]
public static class Patch_FactionTerritories_ExpansionComplete
{
    public static void TryApply(Harmony harmony)
    {
        MethodBase target = TargetMethod();
        if (target == null || harmony == null)
        {
            EESLog.Message("[FT兼容] 扩张完成目标当前不可用，延后下一次 Active 检查。");
            return;
        }
        HarmonyLib.Patches patches = Harmony.GetPatchInfo(target);
        if (patches != null && patches.Postfixes.Any(patch =>
            patch.owner == HarmonyInit.HarmonyId && patch.PatchMethod?.DeclaringType == typeof(Patch_FactionTerritories_ExpansionComplete)))
        {
            return;
        }
        harmony.Patch(target, postfix: new HarmonyMethod(typeof(Patch_FactionTerritories_ExpansionComplete), nameof(Postfix)));
        EESLog.Message("[FT兼容] 扩张完成 EES Postfix 已安装: " + target.DeclaringType.FullName + "." + target.Name + ".");
    }

    private static MethodBase TargetMethod()
    {
        Type utility = AccessTools.TypeByName("FactionTerritories.Expansion.Utility");
        return utility == null ? null : AccessTools.Method(utility, "TryComplete");
    }

    private static bool Prepare() => TargetMethod() != null;

    internal static void Postfix(object __0, bool __result)
    {
        if (!__result || __0 == null || Find.WorldObjects == null)
        {
            return;
        }
        try
        {
            Type type = __0.GetType();
            Faction faction = AccessTools.Property(type, "OwnerFaction")?.GetValue(__0) as Faction;
            PlanetTile tile = AccessTools.Property(type, "Tile")?.GetValue(__0) is PlanetTile value ? value : PlanetTile.Invalid;
            if (faction == null || !tile.Valid)
            {
                return;
            }
            Settlement settlement = Find.WorldObjects.Settlements.FirstOrDefault(candidate =>
                candidate.Tile == tile && candidate.Faction?.loadID == faction.loadID);
            if (settlement == null || EESFactionBlacklist.IsBlocked(settlement.Faction) ||
                EESCustomBaseDetector.IsForeignSettlement(settlement))
            {
                return;
            }
            StrongholdData data = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
            if (data != null)
            {
                EESLog.Message($"[FT兼容] 扩张据点完成并登记 EES：{settlement.LabelCap} [{faction.Name}] tile={tile}, role={data.roleDefName}, tier=T{data.tier}.");
            }
        }
        catch (Exception ex)
        {
            EESLog.Warning("[FT兼容] 扩张完成后登记 EES 失败（不阻断 FT 完成）: " + ex.Message);
        }
    }
}
