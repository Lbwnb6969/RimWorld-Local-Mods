using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

// Compressed candidates are real reserve people, deeply saved by their owner.
// The optional mod remains responsible for their native death/ejection behavior.
public static class StrongholdPawnOwnership
{
    public static List<Pawn> Group(Pawn root)
    {
        var result = new List<Pawn>();
        var seen = new HashSet<Pawn>();
        if (root != null) { result.Add(root); seen.Add(root); }
        for (int i = 0; i < result.Count; i++)
        {
            foreach (var hediff in result[i].health.hediffSet.hediffs.OfType<HediffWithComps>())
            foreach (var comp in hediff.comps ?? new List<HediffComp>())
            {
                if (comp.GetType().FullName != "MultiRaiders.Hediff.HediffComp_MirrorImage") continue;
                if (AccessTools.Field(comp.GetType(), "fakePawns")?.GetValue(comp) is IEnumerable<Pawn> members)
                    foreach (var member in members)
                        if (member != null && seen.Add(member)) result.Add(member);
            }
        }
        return result;
    }

    public static int GroupSize(Pawn root) => Group(root).Count(p => !p.Dead && !p.Destroyed && !p.Discarded);

    public static List<Pawn> Population(Map map, Faction faction, bool foreignHostiles = false)
    {
        var roots = new HashSet<Pawn>();
        if (map == null) return roots.ToList();
        foreach (var pawn in map.mapPawns.AllPawnsSpawned) roots.Add(pawn);
        foreach (var holder in map.listerThings.AllThings.OfType<IThingHolder>())
            foreach (var pawn in ThingOwnerUtility.GetAllThingsRecursively(holder, allowUnreal: false).OfType<Pawn>()) roots.Add(pawn);
        var result = new HashSet<Pawn>();
        foreach (var root in roots)
        foreach (var pawn in Group(root))
            if (!pawn.Dead && !pawn.Destroyed && !pawn.Discarded &&
                (faction != null && pawn.Faction == faction || foreignHostiles && pawn.Faction?.HostileTo(Faction.OfPlayer) == true ||
                 faction == null && !foreignHostiles)) result.Add(pawn);
        return result.ToList();
    }

    public static HashSet<Pawn> DeepOwned(IEnumerable<Pawn> owners)
    {
        var result = new HashSet<Pawn>();
        foreach (var owner in owners)
            foreach (var pawn in Group(owner)) if (pawn != owner) result.Add(pawn);
        return result;
    }

    // Detach only surplus members of this newly allocated group. They remain
    // ordinary world people with their data/relations, rather than being killed.
    public static int FitGroup(Pawn root, int available)
    {
        if (available <= 0) return 0;
        int count = GroupSize(root);
        if (count <= available) return count;
        foreach (var hediff in root.health.hediffSet.hediffs.OfType<HediffWithComps>())
        foreach (var comp in hediff.comps ?? new List<HediffComp>())
        {
            if (comp.GetType().FullName != "MultiRaiders.Hediff.HediffComp_MirrorImage" ||
                AccessTools.Field(comp.GetType(), "fakePawns")?.GetValue(comp) is not List<Pawn> members) continue;
            for (int i = members.Count - 1; i >= 0 && count > available; i--)
            {
                var member = members[i];
                int size = GroupSize(member);
                if (size == 0) continue;
                members.RemoveAt(i);
                ReturnUnused(member);
                count -= size;
            }
            AccessTools.Method(hediff.GetType(), "UpdateSeverity")?.Invoke(hediff, null);
        }
        return count;
    }

    public static void ReturnUnused(Pawn pawn)
    {
        if (pawn == null || pawn.Spawned || pawn.Destroyed || pawn.Discarded || pawn.ParentHolder != null ||
            pawn.GetLord() != null || Find.WorldPawns.Contains(pawn)) return;
        Find.WorldPawns.PassToWorld(pawn, PawnDiscardDecideMode.Decide);
    }
}

[HarmonyPatch]
public static class Patch_PawnGenerator_ValidStrongholdRecruits
{
    private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(typeof(PawnGenerator), "IsValidCandidateToRedress");

    internal static bool Scoped(PawnGenerationRequest request)
    {
        var map = MapGenerator.mapBeingGenerated;
        var data = map?.Parent is Settlement site ? WorldComponent_Strongholds.Instance?.GetExisting(site.ID) : null;
        return map != null && map.ParentFaction == request.Faction && request.Faction?.HostileTo(Faction.OfPlayer) == true &&
            data?.generationPlan?.legacyMap != true && StrongholdCenterPlanner.Eligible(map, data);
    }

    private static void Postfix(Pawn pawn, PawnGenerationRequest request, ref bool __result)
    {
        if (__result && Scoped(request) && StrongholdPawnOwnership.Group(pawn).Skip(1).Any(p => p.Destroyed || p.Discarded))
            __result = false;
    }
}
