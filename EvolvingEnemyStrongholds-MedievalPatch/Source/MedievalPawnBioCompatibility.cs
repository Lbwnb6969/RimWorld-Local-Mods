using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace EESMedievalPatch
{
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    internal static class MedievalPawnBioScope
    {
        [ThreadStatic]
        internal static PawnGenerationRequest? CurrentRequest;

        private static void Prefix(PawnGenerationRequest request, out PawnGenerationRequest? __state)
        {
            __state = CurrentRequest;
            CurrentRequest = request;
        }

        private static Exception Finalizer(Exception __exception, PawnGenerationRequest? __state)
        {
            CurrentRequest = __state;
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class MedievalRequiredWorkBioPatch
    {
        private static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.Method(typeof(PawnBioAndNameGenerator), "TryGetRandomUnusedSolidBioFor");

        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("rimworld.erdelf.alien_race.main")]
        private static void Postfix(PawnKindDef kind, ref PawnBio result, ref bool __result)
        {
            if (!__result || result == null || MedievalPawnBioScope.CurrentRequest is not PawnGenerationRequest request ||
                request.KindDef != kind || request.Faction == null || request.Faction.IsPlayer ||
                !MedievalUtility.IsMedieval(request.Faction) || kind.requiredWorkTags == WorkTags.None) return;

            // HAR's replacement solid-bio selector omits vanilla's requiredWorkTags
            // check. A pacifist pirate-king bio then makes every retry of a melee
            // leader/parent impossible. Restore the declared eligibility check;
            // native generation can continue with that culture's shuffled bio.
            WorkTags disabled = (result.childhood?.workDisables ?? WorkTags.None) |
                (result.adulthood?.workDisables ?? WorkTags.None);
            if ((disabled & kind.requiredWorkTags) == WorkTags.None) return;
            result = null;
            __result = false;
            // HAR also caches the chosen bio for forced gear/hediffs. A rejected
            // choice must not leak those additions into the shuffled replacement.
            var cache = AccessTools.Field(AccessTools.TypeByName("AlienRace.HarmonyPatches"), "bioReference");
            cache?.SetValue(null, null);
        }
    }
}
