using System.Reflection;
using EvolvingEnemyStrongholds;
using HarmonyLib;
using Verse;

namespace EESMedievalPatch;

internal static partial class MedievalUtility
{
    internal static bool AllowsCastleGateTurretFootprint(Map map, IntVec3 center, IntVec2 size, Rot4 rotation) =>
        map == null || !ConstructionContext(map) || !IsSupportedHostileSettlement(map) ||
        !IntersectsCastleGateEntrance(map, GenAdj.OccupiedRect(center, rotation, size));
}

// Only the EES candidate predicates change. Preserve a native rejection and
// compare the exact rotation/occupied rectangle; nearby safe positions remain
// candidates. GenSpawn, player construction and loaded-map turrets are untouched.
[HarmonyPatch]
internal static class CastleGateTurretPlacementPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(StrongholdGarrisonUtility), "TurretFootprintFree");
    private static void Postfix(Map map, IntVec3 center, IntVec2 size, ref bool __result)
    {
        if (__result) __result = MedievalUtility.AllowsCastleGateTurretFootprint(map, center, size, Rot4.North);
    }
}

[HarmonyPatch]
internal static class CastleGateRotatedTurretPlacementPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(StrongholdGarrisonUtility), "TurretFootprintFreeRot");
    private static void Postfix(Map map, IntVec3 center, IntVec2 size, Rot4 rot, ref bool __result)
    {
        if (__result) __result = MedievalUtility.AllowsCastleGateTurretFootprint(map, center, size, rot);
    }
}
