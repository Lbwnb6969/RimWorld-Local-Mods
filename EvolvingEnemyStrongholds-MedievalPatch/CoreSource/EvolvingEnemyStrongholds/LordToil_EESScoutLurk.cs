using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class LordToil_EESScoutLurk : LordToil
{
	private const float ArrivedDist = 2f;

	private const int SlotReachTries = 8;

	private IntVec3 spot;

	private readonly Dictionary<Pawn, IntVec3> slots = new Dictionary<Pawn, IntVec3>();

	public override bool AllowSatisfyLongNeeds => false;

	public LordToil_EESScoutLurk(IntVec3 spot)
	{
		this.spot = spot;
	}

	public override void UpdateAllDuties()
	{
		for (int i = 0; i < lord.ownedPawns.Count; i++)
		{
			Pawn pawn = lord.ownedPawns[i];
			if (pawn?.mindState != null)
			{
				pawn.mindState.duty = new PawnDuty(DutyDefOf.TravelOrWait, spot.IsValid ? spot : pawn.Position);
				pawn.mindState.duty.locomotion = LocomotionUrgency.Walk;
			}
		}
	}

	public override void LordToilTick()
	{
		if (!spot.IsValid)
		{
			lord.ReceiveMemo("EESScoutLeave");
			return;
		}
		if (((lord?.LordJob as LordJob_EESCounterScout)?.ElapsedTicks ?? int.MaxValue) >= 60000)
		{
			lord.ReceiveMemo("EESScoutLeave");
			return;
		}
		for (int i = 0; i < lord.ownedPawns.Count; i++)
		{
			Pawn pawn = lord.ownedPawns[i];
			if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed || pawn.jobs == null)
			{
				continue;
			}
			IntVec3 intVec = SlotFor(pawn);
			if ((pawn.Position - intVec).LengthHorizontal <= 2f)
			{
				if (pawn.CurJobDef != JobDefOf.Wait && pawn.CurJobDef != JobDefOf.Wait_MaintainPosture)
				{
					pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 300), JobCondition.InterruptForced);
				}
			}
			else if (!pawn.CanReach(intVec, PathEndMode.OnCell, Danger.Some))
			{
				if (intVec != spot && pawn.CanReach(spot, PathEndMode.OnCell, Danger.Some))
				{
					slots[pawn] = spot;
				}
				else if (pawn.Map?.reachability != null && pawn.Map.reachability.CanReachColony(pawn.Position))
				{
					slots[pawn] = pawn.Position;
				}
			}
			else if (pawn.CurJobDef != JobDefOf.Goto || pawn.pather == null || !pawn.pather.Moving || !((pawn.pather.Destination.Cell - intVec).LengthHorizontal <= 2f))
			{
				Job job = JobMaker.MakeJob(JobDefOf.Goto, intVec);
				job.locomotionUrgency = LocomotionUrgency.Walk;
				pawn.jobs.StartJob(job, JobCondition.InterruptForced);
			}
		}
	}

	private IntVec3 SlotFor(Pawn p)
	{
		if (slots.TryGetValue(p, out var value) && value.IsValid)
		{
			return value;
		}
		if (slots.Count == 0)
		{
			slots[p] = spot;
			return spot;
		}
		IntVec3 intVec = spot;
		for (int i = 0; i < 8; i++)
		{
			IntVec3 intVec2 = CellFinder.RandomClosewalkCellNear(spot, p.Map, 3);
			if (intVec2.IsValid && p.Map?.reachability != null && p.Map.reachability.CanReachColony(intVec2))
			{
				intVec = intVec2;
				break;
			}
		}
		slots[p] = intVec;
		return intVec;
	}
}
