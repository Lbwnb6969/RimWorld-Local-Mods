using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace EvolvingEnemyStrongholds;

public class JobDriver_InterceptComms : JobDriver
{
	public Settlement target;

	public const int DurationTicks = 10000;

	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_References.Look(ref target, "eesInterceptTarget");
	}

	protected override IEnumerable<Toil> MakeNewToils()
	{
		bool strike = job.def != null && job.def.defName == "EES_PrecisionStrike";
		this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
		this.FailOnBurningImmobile(TargetIndex.A);
		yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
		Toil toil = Toils_General.Wait(10000, TargetIndex.A);
		toil.FailOnDespawnedNullOrForbidden(TargetIndex.A);
		toil.FailOn(() => !(job.targetA.Thing is Building_CommsConsole building_CommsConsole) || !building_CommsConsole.CanUseCommsNow);
		toil.WithProgressBarToilDelay(TargetIndex.A);
		toil.activeSkill = () => (!strike) ? SkillDefOf.Intellectual : SkillDefOf.Shooting;
		yield return toil;
		Toil toil2 = ToilMaker.MakeToil("MakeNewToils");
		toil2.initAction = delegate
		{
			if (strike)
			{
				StrikeUtility.Resolve(pawn, target);
			}
			else
			{
				InterceptUtility.Resolve(pawn, target);
			}
		};
		toil2.defaultCompleteMode = ToilCompleteMode.Instant;
		yield return toil2;
	}
}
