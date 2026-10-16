using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

// RSC normally ejects after vanilla MakeDowned has removed the root's Lord.
// Run its own handoff at the actual loss notification, while that Lord exists.
// Its later native postfix sees an empty group and cannot eject twice.
[HarmonyPatch(typeof(Lord), nameof(Lord.Notify_PawnLost))]
internal static class Patch_StrongholdCompressedDownedLord
{
    internal static bool Scoped(Pawn pawn, Lord lord)
    {
        var map = pawn?.Map;
        var data = map?.Parent is Settlement site ? WorldComponent_Strongholds.Instance?.GetExisting(site.ID) : null;
        return map != null && lord != null && pawn.GetLord() == lord && pawn.Faction == map.ParentFaction &&
            pawn.Faction?.HostileTo(Faction.OfPlayer) == true && data?.generationPlan?.legacyMap != true &&
            StrongholdCenterPlanner.Eligible(map, data) &&
            (map.GetComponent<MapComponent_StrongholdSiege>().eesGenerated || FinalStandUtility.Active(data));
    }

    private static void Prefix(Lord __instance, Pawn pawn, PawnLostCondition cond)
    {
        if (cond != PawnLostCondition.Incapped || !pawn.Downed || !Scoped(pawn, __instance)) return;
        var group = pawn.health.hediffSet.hediffs.FirstOrDefault(h =>
            h.GetType().FullName == "MultiRaiders.Hediff.HediffMirrorImage");
        if (group != null && StrongholdPawnOwnership.Group(pawn).Count > 1)
        {
            AccessTools.Method(group.GetType(), "SpawnNextInLine").Invoke(group, null);
            // MakeDowned already cleared the root's duty. RSC then defaults to
            // AssaultColony; let the existing Lord assign its actual defense,
            // hold-line, turret or assault duties to the newly joined candidate.
            __instance.CurLordToil.UpdateAllDuties();
        }
    }
}
