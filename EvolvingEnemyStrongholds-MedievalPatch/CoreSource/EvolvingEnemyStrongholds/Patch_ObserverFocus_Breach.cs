using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(JobGiver_AIFightEnemy), "TryGiveJob")]
public static class Patch_ObserverFocus_Breach
{
	public static void Postfix(Pawn pawn, ref Job __result)
	{
		if (__result != null)
		{
			return;
		}
		Pawn pawn2 = ObserverFocus.ResolveTargetFor(pawn);
		if (pawn2 == null)
		{
			return;
		}
		Map map = pawn.Map;
		if (map == null)
		{
			return;
		}
		if (pawn.CanReach(pawn2, PathEndMode.Touch, Danger.Deadly))
		{
			Job job = JobMaker.MakeJob(JobDefOf.Goto, new LocalTargetInfo(pawn2.Position));
			job.expiryInterval = 120;
			job.checkOverrideOnExpire = true;
			job.collideWithPawns = false;
			__result = job;
			ObserverFocus.NotifyPursue(pawn, pawn2);
			return;
		}
		using PawnPath pawnPath = map.pathFinder.FindPathNow(pawn.Position, pawn2.Position, TraverseParms.For(pawn, Danger.Deadly, TraverseMode.PassAllDestroyableThings));
		if (pawnPath != null && pawnPath.Found)
		{
			IntVec3 cellBefore;
			Thing thing = pawnPath.FirstBlockingBuilding(out cellBefore, pawn);
			if (thing != null)
			{
				Job job2 = JobMaker.MakeJob(JobDefOf.AttackStatic, thing);
				job2.expiryInterval = 300;
				job2.checkOverrideOnExpire = true;
				job2.ignoreDesignations = true;
				__result = job2;
				ObserverFocus.NotifyBreach(pawn, thing, pawn2);
			}
		}
	}
}
