using HarmonyLib;
using Verse;
using Verse.AI;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(AttackTargetFinder), "BestAttackTarget")]
public static class Patch_ObserverFocus_BestAttackTarget
{
	public static void Postfix(ref IAttackTarget __result, IAttackTargetSearcher searcher)
	{
		if (searcher?.Thing is Pawn p)
		{
			Pawn pawn = ObserverFocus.ResolveTargetFor(p);
			if (pawn != null && __result != pawn)
			{
				__result = pawn;
			}
		}
	}
}
