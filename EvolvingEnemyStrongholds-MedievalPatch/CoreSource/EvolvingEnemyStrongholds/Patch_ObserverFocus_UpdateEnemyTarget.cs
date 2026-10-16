using HarmonyLib;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(JobGiver_AIFightEnemy), "UpdateEnemyTarget")]
public static class Patch_ObserverFocus_UpdateEnemyTarget
{
	public static void Postfix(Pawn pawn)
	{
		Pawn pawn2 = ObserverFocus.ResolveTargetFor(pawn);
		if (pawn2 != null && pawn.mindState != null && pawn.mindState.enemyTarget != pawn2)
		{
			pawn.mindState.enemyTarget = pawn2;
			ObserverFocus.NotifyForced(pawn, pawn2);
		}
	}
}
