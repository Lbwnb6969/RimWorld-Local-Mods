using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class StrongholdCenterPlanner
{
    public static bool Eligible(Map map, StrongholdData data)
    {
        bool openSettlement = map?.Parent is Settlement openSettlementObject &&
            (openSettlementObject.Faction?.def?.defName?.StartsWith("VFEM2_Clan", StringComparison.Ordinal) == true ||
             openSettlementObject.Faction?.def?.techLevel == TechLevel.Neolithic || openSettlementObject.Faction?.def?.categoryTag == "Tribal");
        return map?.Parent is Settlement settlement && settlement.Faction?.def?.humanlikeFaction == true &&
            settlement.Faction != Faction.OfPlayer && !EESFactionBlacklist.IsBlocked(settlement.Faction) &&
            !EESCustomBaseDetector.IsForeignSettlement(settlement) &&
            (!EESCustomBaseDetector.IsDedicatedOutpostFaction(settlement.Faction) || openSettlement) &&
            data != null && !FakeOutpostUtility.IsFake(data.roleDefName) && !BattlefieldUtility.IsBattlefield(data.roleDefName) &&
            !ThrumboRanchUtility.IsRanch(data.roleDefName) && !VacuumMapUtility.IsVacuum(map);
    }

    public static bool Prepare(Map map, StrongholdData data, CellRect town)
    {
        if (!Eligible(map, data)) return false;
        var plan = StrongholdGenerationPlan.GetOrCreate(map, data);
        if (plan.legacyMap || plan.centerBuilt) return false;
        if (plan.tier != data.tier) plan.centerReserved = false;
        plan.tier = data.tier;
        plan.roleDefName = data.roleDefName;
        if (plan.centerReserved) return true;
        var wanted = plan.centerKind == "clanHall" ? (data.tier >= 10 ? 35 : 21) :
            (data.tier >= 14 ? 47 + (Math.Min(16, data.tier) - 14) * 4 : data.tier >= 10 ? 41 : data.tier >= 5 ? 29 : 23);
        var integral = BuildBlockedIntegral(map);
        // River-split or densely templated maps can leave no 23-cell square. Keep
        // a compact 17-cell castle fallback so the unified center path still has a
        // vault and command core instead of silently returning an empty stronghold.
        foreach (var size in new[] { wanted, 41, 35, 29, 23, 19, 17, 21 }.Distinct().Where(s => s <= wanted))
        {
            var best = CellRect.Empty;
            var distance = int.MaxValue;
            var center = town.Width > 1 ? town.CenterCell : map.Center;
            // Search the complete legal map before reducing the keep. A coastal
            // town's center can be surrounded by water/native buildings while a
            // full plot remains outside the previous 55-cell search window.
            var search = CellRect.WholeMap(map);
            foreach (var candidate in search)
            {
                var rect = CellRect.CenteredOn(candidate, size / 2);
                if (rect.Width != size || rect.minX < 12 || rect.minZ < 12 || rect.maxX >= map.Size.x - 12 || rect.maxZ >= map.Size.z - 12) continue;
                if (BlockedCount(integral, rect, map.Size.x + 1) != 0) continue;
                var score = (candidate - center).LengthHorizontalSquared;
                if (score >= distance) continue;
                best = rect;
                distance = score;
            }
            if (best.Width <= 0) continue;
            plan.centerRect = best;
            plan.centerReserved = true;
            plan.placementNote = size == wanted ? "full-size" : "terrain-reduced";
            plan.stage = "center-reserved";
            EESLog.Message("Reserved " + plan.centerKind + " T" + data.tier + " " + best + " (" + plan.placementNote + ").");
            return true;
        }
        plan.placementNote = "no-safe-center-plot";
        EESLog.Warning("No safe center footprint for " + map.Parent.LabelCap + "; existing buildings, pawns and items retained.");
        return false;
    }

    private static int[] BuildBlockedIntegral(Map map)
    {
        var stride = map.Size.x + 1;
        var result = new int[stride * (map.Size.z + 1)];
        for (var z = 0; z < map.Size.z; z++)
        {
            var row = 0;
            for (var x = 0; x < map.Size.x; x++)
            {
                var cell = new IntVec3(x, 0, z);
                var terrain = cell.GetTerrain(map);
                var blocked = terrain == null || !terrain.affordances.Contains(TerrainAffordanceDefOf.Heavy) ||
                    cell.GetEdifice(map) != null || cell.GetFirstPawn(map) != null;
                if (blocked) row++;
                result[(z + 1) * stride + x + 1] = result[z * stride + x + 1] + row;
            }
        }
        return result;
    }

    private static int BlockedCount(int[] integral, CellRect rect, int stride)
    {
        return integral[(rect.maxZ + 1) * stride + rect.maxX + 1] - integral[rect.minZ * stride + rect.maxX + 1] -
            integral[(rect.maxZ + 1) * stride + rect.minX] + integral[rect.minZ * stride + rect.minX];
    }

    public static CellRect IncludeCenter(CellRect town, StrongholdGenerationPlan plan)
    {
        if (plan?.centerReserved != true) return town;
        int townMargin = plan.tier >= 14 ? 20 + (plan.tier - 13) * 4 : plan.tier >= 10 ? 20 : plan.tier >= 5 ? 16 : 14;
        var center = plan.centerRect.ExpandedBy(plan.centerKind == "castle" ? townMargin : Math.Min(24, townMargin));
        if (town.Width <= 1) return center;
        return new CellRect(Math.Min(town.minX, center.minX), Math.Min(town.minZ, center.minZ),
            Math.Max(town.maxX, center.maxX) - Math.Min(town.minX, center.minX) + 1,
            Math.Max(town.maxZ, center.maxZ) - Math.Min(town.minZ, center.minZ) + 1);
    }

    public static void ReserveAfterNativeTown(Map map, StrongholdData data, CellRect town)
    {
        if (MapGenerator.mapBeingGenerated != map || !Eligible(map, data) ||
            map.ParentFaction?.HostileTo(Faction.OfPlayer) != true) return;
        var plan = StrongholdGenerationPlan.GetOrCreate(map, data);
        if (plan.legacyMap || plan.centerBuilt || plan.centerReserved ||
            !(StrongholdCultureCenters.PreserveNativeTemplate(map.ParentFaction, plan.nativeTemplate) ||
              StrongholdCultureCenters.IsNativeCastleLayout(map.ParentFaction, plan.nativeTemplate))) return;
        // Reuse a real native castle under precisely the same rules as the builder.
        // Otherwise reserve only after the entire registered town exists.
        if (StrongholdLayoutGenerator.TryFindReusableNativeCenter(map, data, town, plan, out _, out _)) return;
        if (!Prepare(map, data, town)) return;
        var reserved = plan.centerRect.ExpandedBy(1).ClipInsideMap(map);
        var used = MapGenerator.GetOrGenerateVar<List<CellRect>>("UsedRects");
        if (!used.Contains(reserved)) used.Add(reserved);
    }

    public static bool Protecting(Map map)
    {
        if (MapGenerator.mapBeingGenerated != map || map?.Parent is not Settlement settlement ||
            settlement.Faction?.HostileTo(Faction.OfPlayer) != true) return false;
        var data = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
        if (!Eligible(map, data)) return false;
        var siege = map.GetComponent<MapComponent_StrongholdSiege>();
        var plan = data.generationPlan;
        return plan?.centerReserved == true && !plan.centerBuilt && !plan.legacyMap && siege?.centerBuilding != true && siege?.eesGenerated != true;
    }

    public static bool StructuralWall(ThingDef def)
    {
        return def != null && (def.IsDoor || def == ThingDefOf.Wall ||
            (def.graphicData != null && (def.graphicData.linkFlags & LinkFlags.Wall) != 0 && def.fillPercent >= 0.95f &&
             def.building?.isNaturalRock != true && def.building?.IsTurret != true));
    }

    public static bool FindOutsideCell(Map map, ThingDef def, Rot4 rot, IntVec3 near, CellRect reserved, out IntVec3 cell)
    {
        var plan = map.Parent is Settlement settlement ? WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID)?.generationPlan : null;
        foreach (var candidate in GenRadial.RadialCellsAround(near, 65f, true))
        {
            var footprint = GenAdj.OccupiedRect(candidate, rot, def.size);
            if (footprint.Overlaps(reserved.ExpandedBy(2))) continue;
            if (plan?.centerReserved == true && footprint.Overlaps(plan.centerRect.ExpandedBy(1))) continue;
            var clear = true;
            foreach (var point in footprint)
            {
                if (!point.InBounds(map) || !point.Standable(map) || point.GetEdifice(map) != null || point.GetFirstPawn(map) != null ||
                    point.GetFirstItem(map) != null || (def.category == ThingCategory.Building &&
                    def.terrainAffordanceNeeded != null && !point.GetTerrain(map).affordances.Contains(def.terrainAffordanceNeeded)))
                { clear = false; break; }
            }
            if (!clear) continue;
            cell = candidate;
            return true;
        }
        cell = IntVec3.Invalid;
        return false;
    }
}

[HarmonyPatch]
public static class Patch_KCSG_ReserveStrongholdCenter
{
    private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("KCSG.SettlementGenUtils"), "Generate");
    private static bool Prepare() => TargetMethod() != null;

    [HarmonyPriority(Priority.Last)]
    private static void Prefix(RimWorld.BaseGen.ResolveParams __0, Map __1, object __2)
    {
        if (__1?.Parent is not Settlement settlement) return;
        var data = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
        if (StrongholdCenterPlanner.Eligible(__1, data) && StrongholdCultureCenters.PreserveNativeTemplate(settlement.Faction, (__2 as Def)?.defName))
        {
            var plan = StrongholdGenerationPlan.GetOrCreate(__1, data);
            if (!plan.centerBuilt && !plan.legacyMap)
            {
                plan.nativeTemplate = ((__2 as Def).defName);
                plan.stage = "native-center-pending";
            }
            return;
        }
        StrongholdCenterPlanner.Prepare(__1, data, __0.rect);
    }
}

[HarmonyPatch]
public static class Patch_KCSG_ReservedCenterWalls
{
    private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("KCSG.SymbolUtils"), "GenerateBuildingAt");
    private static bool Prepare() => TargetMethod() != null;

    private static bool Prefix(Map map, IntVec3 cell, object symbol)
    {
        if (!StrongholdCenterPlanner.Protecting(map)) return true;
        var def = AccessTools.Field(symbol.GetType(), "thingDef")?.GetValue(symbol) as ThingDef;
        if (!StrongholdCenterPlanner.StructuralWall(def)) return true;
        var data = WorldComponent_Strongholds.Instance.GetExisting(((Settlement)map.Parent).ID);
        return !data.generationPlan.centerRect.ExpandedBy(1).Contains(cell);
    }
}

[HarmonyPatch]
public static class Patch_KCSG_ReserveNativeCastleLayout
{
    private static System.Reflection.MethodBase TargetMethod() => AccessTools.TypeByName("KCSG.LayoutUtils")?.GetMethods()
        .SingleOrDefault(method => method.Name == "Generate" && method.GetParameters().Length == 7 &&
            method.GetParameters()[0].ParameterType.FullName == "KCSG.StructureLayoutDef");
    private static bool Prepare() => TargetMethod() != null;

    private static void Prefix(object __0, CellRect __1, Map __2)
    {
        var name = (__0 as Def)?.defName;
        if (MapGenerator.mapBeingGenerated != __2 || __2?.Parent is not Settlement settlement ||
            settlement.Faction?.HostileTo(Faction.OfPlayer) != true ||
            !(StrongholdCultureCenters.IsNativeCastleLayout(settlement.Faction, name) || name == "DankPyon_StartingCastle")) return;
        var data = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
        if (!StrongholdCenterPlanner.Eligible(__2, data)) return;
        var plan = StrongholdGenerationPlan.GetOrCreate(__2, data);
        EESLog.Message("Native layout candidate " + name + ": reserved=" + plan.centerReserved + ", built=" + plan.centerBuilt + ", kind=" + plan.centerKind + ".");
        if (plan.centerReserved || plan.centerBuilt || plan.legacyMap || plan.centerKind == "clanHall") return;
        plan.nativeTemplate = name;
        plan.nativeBounds = __1;
        plan.stage = "native-center-pending";
    }

    private static void Postfix(object __0, CellRect __1, Map __2)
    {
        if (__2?.Parent is not Settlement settlement ||
            !StrongholdCultureCenters.IsNativeCastleLayout(settlement.Faction, (__0 as Def)?.defName)) return;
        StrongholdCenterPlanner.ReserveAfterNativeTown(__2,
            WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID), __1);
    }
}

[HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.Spawn), new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool) })]
public static class Patch_GenSpawn_ReservedStrongholdCenter
{
    private static bool Prefix(Thing newThing, ref IntVec3 loc, Map map, Rot4 rot, out bool __state)
    {
        __state = StrongholdCenterSpawnGuard.TryMove(newThing, ref loc, map, rot);
        return StrongholdCenterSpawnGuard.PreserveItems(newThing, loc, map, rot);
    }

    private static void Postfix(Thing __result, Map map, bool __state)
    {
        StrongholdCenterSpawnGuard.Record(__result, map, __state);
    }
}

internal static class StrongholdCenterSpawnGuard
{
    internal static bool PreserveItems(Thing newThing, IntVec3 loc, Map map, Rot4 rot)
    {
        if (newThing?.def.category != ThingCategory.Building || MapGenerator.mapBeingGenerated != map ||
            map?.Parent is not Settlement settlement || newThing.Faction != settlement.Faction ||
            map.GetComponent<MapComponent_StrongholdSiege>()?.eesGenerated != true) return true;
        var data = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
        if (!StrongholdCenterPlanner.Eligible(map, data)) return true;
        var footprint = GenAdj.OccupiedRect(loc, rot, newThing.def.size);
        var items = footprint.Where(c => c.InBounds(map)).SelectMany(c => c.GetThingList(map))
            .Where(t => t.def.category == ThingCategory.Item && t is not Plant && t.def.plant == null).Distinct().ToList();
        foreach (var item in items)
        {
            if (!StrongholdCenterPlanner.FindOutsideCell(map, item.def, item.Rotation, item.Position, footprint, out var safe))
            {
                EESLog.Warning("Skipped " + newThing.def.defName + " at " + loc + ": no safe relocation cell for " + item.def.defName + ".");
                return false;
            }
            var original = item.Position;
            item.DeSpawn();
            try { GenSpawn.Spawn(item, safe, map, item.Rotation); }
            catch (Exception exception)
            {
                if (!item.Spawned) GenSpawn.Spawn(item, original, map, item.Rotation);
                EESLog.Warning("Skipped " + newThing.def.defName + ": item relocation failed: " + exception.Message);
                return false;
            }
        }
        return true;
    }

    internal static bool TryMove(Thing newThing, ref IntVec3 loc, Map map, Rot4 rot)
    {
        if (newThing?.def == null || newThing is Plant || newThing.def.plant != null || newThing.def.category == ThingCategory.Filth) return false;
        if (!StrongholdCenterPlanner.Protecting(map) || map.Parent is not Settlement settlement) return false;
        var plan = WorldComponent_Strongholds.Instance.GetExisting(settlement.ID)?.generationPlan;
        if (plan == null || newThing?.def == null) return false;
        if (!GenAdj.OccupiedRect(loc, rot, newThing.def.size).Overlaps(plan.centerRect.ExpandedBy(1))) return false;
        if (!StrongholdCenterPlanner.FindOutsideCell(map, newThing.def, rot, loc, plan.centerRect, out var moved)) return false;
        loc = moved;
        return newThing.def.category == ThingCategory.Item && newThing is not Corpse &&
            !newThing.def.IsWithinCategory(ThingCategoryDefOf.Chunks);
    }

    internal static void Record(Thing result, Map map, bool captured)
    {
        if (!captured || result?.Spawned != true || map?.Parent is not Settlement settlement) return;
        var plan = WorldComponent_Strongholds.Instance.GetExisting(settlement.ID)?.generationPlan;
        if (plan != null && plan.nativeLootIds.Count < 64 && !plan.nativeLootIds.Contains(result.thingIDNumber)) plan.nativeLootIds.Add(result.thingIDNumber);
    }
}
