using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(JobGiver_ReactToCloseMeleeThreat), "TryGiveJob")]
public static class Patch_ObserverFocus_NoMeleeReact
{
	public static bool Prefix(Pawn pawn, ref Job __result)
	{
		if (ObserverFocus.ResolveTargetFor(pawn) == null)
		{
			return true;
		}
		if (pawn.mindState != null)
		{
			pawn.mindState.meleeThreat = null;
		}
		__result = null;
		return false;
	}
}
