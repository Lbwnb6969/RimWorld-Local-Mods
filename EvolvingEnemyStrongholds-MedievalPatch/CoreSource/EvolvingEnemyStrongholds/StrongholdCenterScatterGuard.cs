using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

internal static class StrongholdCenterScatterGuard
{
    internal static bool Overlaps(Map map, CellRect footprint)
    {
        if (!StrongholdCenterPlanner.Protecting(map)) return false;
        var plan = WorldComponent_Strongholds.Instance.GetExisting(((Settlement)map.Parent).ID).generationPlan;
        return footprint.Overlaps(plan.centerRect.ExpandedBy(1));
    }

    internal static bool BlocksRotation(GenStep_ScatterThings step, IntVec3 loc, Rot4 rot, Map map)
    {
        if (!StrongholdCenterPlanner.Protecting(map)) return false;
        var def = step.thingDef;
        if (def != null && def.IsEdifice() && !(step.minify && def.Minifiable) &&
            Overlaps(map, GenAdj.OccupiedRect(loc, rot, def.size))) return true;
        // IrregularLump starts with these exact radial cells and only removes
        // cells. Check its full possible destructive area without consuming RNG.
        if (step.clearSpaceSize > 0)
            foreach (var offset in GenRadial.RadialPattern.Take(step.clearSpaceSize * 2))
                if ((loc + offset).InBounds(map) && Overlaps(map, CellRect.SingleCell(loc + offset))) return true;
        return false;
    }
}

[HarmonyPatch(typeof(GenStep_ScatterThings), "IsRotationValid")]
internal static class Patch_ScatterThings_ReservedCenter
{
    private static void Postfix(GenStep_ScatterThings __instance, IntVec3 loc, Rot4 rot, Map map, ref bool __result)
    {
        if (__result && StrongholdCenterScatterGuard.BlocksRotation(__instance, loc, rot, map)) __result = false;
    }
}

[HarmonyPatch(typeof(GenStep_ScatterGroup), "CalculateScatterInformation")]
internal static class Patch_ScatterGroup_ReservedCenter
{
    private static void Postfix(Map map, CellRect rect, object outThingSpawns, ref bool __result)
    {
        if (!__result || !StrongholdCenterScatterGuard.Overlaps(map, rect) || outThingSpawns is not IList spawns) return;
        // Reject the complete selected building group, preserving native retry
        // logic. Pure item/plant/filth groups keep their original result.
        if (!spawns.Cast<object>().Any(spawn =>
            (AccessTools.Field(spawn.GetType(), "def")?.GetValue(spawn) as ThingDef)?.IsEdifice() == true)) return;
        spawns.Clear();
        __result = false;
    }
}

[HarmonyPatch]
internal static class Patch_ReGrowthBoulder_ReservedCenter
{
    private static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.TypeByName("ReGrowthCore.GenStep_RockChunks_GrowLowRockFormationFrom_Patch"), "TrySpawnThingAt");
    private static bool Prepare() => TargetMethod() != null;

    private static bool Prefix(IntVec3 centerPos, ThingDef thingDef, Map map, ref bool __result)
    {
        // ReGrowth clears the entire footprint before calling GenSpawn. Stop
        // that destructive path at validation, before any existing thing moves.
        if (thingDef?.IsEdifice() != true || !StrongholdCenterScatterGuard.Overlaps(map,
            GenAdj.OccupiedRect(centerPos, thingDef.defaultPlacingRot, thingDef.size))) return true;
        __result = false;
        return false;
    }
}
