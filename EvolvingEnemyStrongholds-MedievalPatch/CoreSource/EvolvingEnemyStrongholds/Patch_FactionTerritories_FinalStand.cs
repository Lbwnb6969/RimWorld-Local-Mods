using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

// Optional FT bridge: observe completed native decisions, preserving its prompts and ownership rules.
public static class Patch_FactionTerritories_FinalStand
{
    private static Type componentType;
    private static MethodInfo tryGetComponent;
    private static MethodInfo getPending;
    private static FieldInfo letterSettlementId;

    public static void TryApply(Harmony harmony)
    {
        componentType = AccessTools.TypeByName("FactionTerritories.Vassalise.VassaliseComponent");
        Type letterType = AccessTools.TypeByName("FactionTerritories.Vassalise.ChoiceLetter_VassaliseDestroyedSettlement");
        if (componentType == null || letterType == null) return;
        tryGetComponent = AccessTools.Method(componentType, "TryGet");
        getPending = AccessTools.Method(componentType, "GetPendingDecisionForDestroyedSettlement", new[] { typeof(int) });
        letterSettlementId = AccessTools.Field(letterType, "destroyedSettlementWorldObjectId");
        MethodInfo register = AccessTools.Method(componentType, "TryRegisterPendingDecision");
        if (register == null || register.ReturnType != typeof(bool) || register.GetParameters().Length != 9 ||
            register.GetParameters()[0].ParameterType != typeof(DestroyedSettlement) || tryGetComponent == null ||
            getPending == null || letterSettlementId == null)
        {
            EESLog.Warning("FT final stand bridge: unsupported decision API; pending decisions will not count as player conquests.");
            return;
        }
        Apply(harmony, register, nameof(RegisterPendingPostfix));
        Apply(harmony, AccessTools.Method(letterType, "Accept"), nameof(DecisionPostfix));
        Apply(harmony, AccessTools.Method(letterType, "Decline"), nameof(DecisionPostfix));
        Type utility = AccessTools.TypeByName("FactionTerritories.Invasions.Utility");
        MethodInfo resolve = utility == null ? null : AccessTools.Method(utility, "ResolveWithWinner");
        if (resolve != null && resolve.GetParameters().Length == 4)
            Apply(harmony, resolve, nameof(NpcResolutionPostfix), nameof(NpcResolutionPrefix));
    }

    private static void Apply(Harmony harmony, MethodInfo target, string postfix, string prefix = null)
    {
        if (target == null) return;
        var patches = Harmony.GetPatchInfo(target);
        if (patches != null)
            foreach (var patch in patches.Postfixes)
                if (patch.owner == HarmonyInit.HarmonyId && patch.PatchMethod?.DeclaringType == typeof(Patch_FactionTerritories_FinalStand)) return;
        harmony.Patch(target, prefix: prefix == null ? null : new HarmonyMethod(typeof(Patch_FactionTerritories_FinalStand), prefix),
            postfix: new HarmonyMethod(typeof(Patch_FactionTerritories_FinalStand), postfix));
        EESLog.Message("FT final stand outcome hook: " + target.DeclaringType.FullName + "." + target.Name);
    }

    public static void RegisterPendingPostfix(DestroyedSettlement __0, Faction __2, Faction __6, bool __result)
    {
        if (__0 == null || __2 == null) return;
        // FT returns false when this world-object already has a decision. If
        // that existing decision is still unresolved, it is still the native
        // choice for the defeat currently being observed; attach the EES
        // context so CompleteDefeat preserves PendingDecision. A false return
        // without an unresolved decision keeps the ordinary conquest path.
        bool unresolvedExisting = false;
        object[] componentArgs = { null };
        if (tryGetComponent != null && (bool)tryGetComponent.Invoke(null, componentArgs))
        {
            object decision = getPending?.Invoke(componentArgs[0], new object[] { __0.ID });
            object resolved = decision == null ? null : AccessTools.Field(decision.GetType(), "resolved")?.GetValue(decision);
            unresolvedExisting = decision != null && !(resolved is bool isResolved && isResolved);
        }
        if (__result || unresolvedExisting) FinalStandOutcomeUtility.RecordPendingDecision(__0, __2, __6);
    }

    public static void DecisionPostfix(object __instance, MethodBase __originalMethod)
    {
        int id = (int)letterSettlementId.GetValue(__instance);
        var world = WorldComponent_Strongholds.Instance;
        FinalStandOutcome outcome = world?.GetPendingFinalStandOutcome(id);
        if (outcome == null) return;
        object[] arguments = { null };
        if (!(bool)tryGetComponent.Invoke(null, arguments)) return;
        object pending = getPending.Invoke(arguments[0], new object[] { id });
        if (pending == null || !(bool)AccessTools.Field(pending.GetType(), "resolved").GetValue(pending)) return;
        if (__originalMethod.Name == "Decline" && outcome.alliedFactionLoadId < 0 && outcome.playerVictoryConfirmed)
            outcome = outcome.WithKind(FinalStandOutcomeKind.PlayerConquest);
        else if (__originalMethod.Name == "Accept") outcome = outcome.WithKind(FinalStandOutcomeKind.Vassalized);
        else outcome = outcome.WithKind(FinalStandOutcomeKind.Ceded);
        world.ResolveSettlementOutcome(outcome);
        world.RefreshFinalStandTargets(outcome.factionLoadId);
    }

    public static void NpcResolutionPrefix(Settlement __1, out FinalStandOutcome __state)
    {
        __state = FinalStandOutcome.Capture(__1, FinalStandOutcomeKind.NpcLoss);
    }

    public static void NpcResolutionPostfix(Settlement __1, FinalStandOutcome __state)
    {
        if (__state == null || (__1.Spawned && !__1.Destroyed && __1.Faction?.loadID == __state.factionLoadId)) return;
        WorldComponent_Strongholds.Instance?.ResolveSettlementOutcome(__state);
        WorldComponent_Strongholds.Instance?.RefreshFinalStandTargets(__state.factionLoadId);
    }
}
