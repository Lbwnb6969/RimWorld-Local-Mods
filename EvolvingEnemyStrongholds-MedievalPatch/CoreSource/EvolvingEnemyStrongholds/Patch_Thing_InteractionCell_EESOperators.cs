using HarmonyLib;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Thing), nameof(Thing.InteractionCell), MethodType.Getter)]
public static class Patch_Thing_InteractionCell_EESOperators
{
    private static void Postfix(Thing __instance, ref IntVec3 __result)
    {
        if (__instance is not Building_Turret || __instance.Map == null || __instance.Faction == Faction.OfPlayer) return;
        var siege = __instance.Map.GetComponent<MapComponent_StrongholdSiege>();
        if (siege != null && siege.TryGetTurretManningCell(__instance, out var cell)) __result = cell;
    }
}
