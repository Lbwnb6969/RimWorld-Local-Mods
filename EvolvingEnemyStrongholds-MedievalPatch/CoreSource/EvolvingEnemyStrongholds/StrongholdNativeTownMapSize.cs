using System;
using System.Collections;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

// Whole native towns cannot share a 150-cell map with a standard high-tier keep.
// Size the new map before terrain/content generation; never resize a loaded map.
[HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
public static class StrongholdNativeTownMapSize
{
    public static int RequiredSize(MapParent parent, StrongholdData data, bool isPocketMap)
    {
        if (isPocketMap || parent is not Settlement settlement || settlement.HasMap ||
            !settlement.Tile.Valid || settlement.Tile.LayerDef?.isSpace == true ||
            settlement.Faction?.def?.humanlikeFaction != true || settlement.Faction == Faction.OfPlayer ||
            !settlement.Faction.HostileTo(Faction.OfPlayer) || EESFactionBlacklist.IsBlocked(settlement.Faction) ||
            EESCustomBaseDetector.IsForeignSettlement(settlement) || EESCustomBaseDetector.IsDedicatedOutpostFaction(settlement.Faction) ||
            data == null || data.tier < 10 || data.generationPlan != null ||
            FakeOutpostUtility.IsFake(data.roleDefName) || BattlefieldUtility.IsBattlefield(data.roleDefName) ||
            ThrumboRanchUtility.IsRanch(data.roleDefName) || !HasWholeTown(settlement.Faction)) return 0;
        // Largest installed whole-city source is 141x136. A T16 keep is 55 wide,
        // with 32-cell town margins and 12-cell map borders: 141+55+64+24=284.
        return 300;
    }

    private static bool HasWholeTown(Faction faction)
    {
        foreach (var extension in faction?.def?.modExtensions ?? new System.Collections.Generic.List<DefModExtension>())
        {
            if (extension?.GetType().FullName != "KCSG.CustomGenOption") continue;
            foreach (string field in new[] { "chooseFromSettlements", "chooseFromlayouts" })
            {
                if (AccessTools.Field(extension.GetType(), field)?.GetValue(extension) is not IEnumerable choices) continue;
                foreach (var choice in choices)
                {
                    string name = choice is Def def ? def.defName : choice as string;
                    if (field == "chooseFromSettlements" ? StrongholdCultureCenters.PreserveNativeTemplate(faction, name) :
                        StrongholdCultureCenters.IsNativeCastleLayout(faction, name)) return true;
                }
            }
        }
        return false;
    }

    [HarmonyPriority(Priority.Last)]
    public static void Prefix(ref IntVec3 mapSize, MapParent parent, bool isPocketMap)
    {
        if (isPocketMap || parent is not Settlement settlement || settlement.HasMap ||
            !settlement.Tile.Valid || settlement.Tile.LayerDef?.isSpace == true ||
            settlement.Faction?.def?.humanlikeFaction != true || settlement.Faction == Faction.OfPlayer ||
            !settlement.Faction.HostileTo(Faction.OfPlayer) || EESFactionBlacklist.IsBlocked(settlement.Faction) ||
            EESCustomBaseDetector.IsForeignSettlement(settlement) || EESCustomBaseDetector.IsDedicatedOutpostFaction(settlement.Faction) ||
            !HasWholeTown(settlement.Faction)) return;
        var world = WorldComponent_Strongholds.Instance;
        var data = world?.GetExisting(settlement.ID) ?? world?.GetOrCreate(settlement);
        int minimum = RequiredSize(parent, data, isPocketMap);
        if (minimum == 0 || mapSize.x <= 0 || mapSize.z <= 0 || mapSize.x >= minimum && mapSize.z >= minimum) return;
        var original = mapSize;
        mapSize = new IntVec3(Math.Max(mapSize.x, minimum), mapSize.y, Math.Max(mapSize.z, minimum));
        EESLog.Message("Whole native-town map capacity: " + settlement.LabelCap + " T" + data.tier +
            " " + original + " -> " + mapSize + "; original town and standard keep retain full footprints.");
    }
}
