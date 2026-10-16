using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(SettlementDefeatUtility), nameof(SettlementDefeatUtility.CheckDefeated))]
public static class Patch_FinalStandDefeat
{
    [HarmonyPriority(int.MaxValue)]
    public static void Prefix(Settlement factionBase, out FinalStandOutcomeUtility.DefeatContext __state)
    {
        __state = FinalStandOutcomeUtility.BeginDefeat(factionBase);
    }

    public static void Postfix(FinalStandOutcomeUtility.DefeatContext __state) => FinalStandOutcomeUtility.CompleteDefeat(__state);

    public static Exception Finalizer(Exception __exception, FinalStandOutcomeUtility.DefeatContext __state)
    {
        FinalStandOutcomeUtility.EndDefeat(__state);
        return __exception;
    }
}

[HarmonyPatch(typeof(Settlement), nameof(Settlement.PostRemove))]
public static class Patch_FinalStandRemoved
{
    public static void Postfix(Settlement __instance) => FinalStandOutcomeUtility.NotifyRemoval(__instance);
}

[HarmonyPatch(typeof(WorldObject), nameof(WorldObject.SetFaction))]
public static class Patch_FinalStandTransferred
{
    public static void Prefix(WorldObject __instance, out FinalStandOutcome __state)
    {
        __state = __instance is Settlement settlement && settlement.Spawned && Scribe.mode == LoadSaveMode.Inactive
            ? FinalStandOutcome.Capture(settlement, FinalStandOutcomeKind.Ceded) : null;
    }

    public static void Postfix(WorldObject __instance, FinalStandOutcome __state)
    {
        if (__instance is Settlement settlement) FinalStandOutcomeUtility.NotifyOwnershipChange(__state, settlement);
    }
}

[HarmonyPatch(typeof(Faction), nameof(Faction.TryAffectGoodwillWith))]
public static class Patch_FinalStandGoodwill
{
    public static void Postfix(Faction __instance, Faction other, bool __result)
    {
        if (!__result || Scribe.mode != LoadSaveMode.Inactive) return;
        if (__instance == Faction.OfPlayer) WorldComponent_Strongholds.Instance?.RefreshFinalStandTargets(other?.loadID ?? -1);
        else if (other == Faction.OfPlayer) WorldComponent_Strongholds.Instance?.RefreshFinalStandTargets(__instance.loadID);
    }
}

[HarmonyPatch(typeof(Faction), nameof(Faction.SetRelation))]
public static class Patch_FinalStandRelation
{
    public static void Postfix(Faction __instance, FactionRelation __0)
    {
        if (Scribe.mode != LoadSaveMode.Inactive) return;
        if (__instance == Faction.OfPlayer) WorldComponent_Strongholds.Instance?.RefreshFinalStandTargets(__0?.other?.loadID ?? -1);
        else if (__0?.other == Faction.OfPlayer) WorldComponent_Strongholds.Instance?.RefreshFinalStandTargets(__instance.loadID);
    }
}

[HarmonyPatch(typeof(MapParent), nameof(MapParent.FinalizeLoading))]
public static class Patch_FinalStandMapLoaded
{
    public static void Postfix(MapParent __instance)
    {
        if (__instance is Settlement settlement && WorldComponent_Strongholds.Instance?.HasPendingExistingMapFinalStand(settlement.ID) == true)
            WorldComponent_Strongholds.Instance.TryApplyExistingMapFinalStandReinforcement(settlement.Map);
    }
}
