using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class LordToil_EESBoardShuttle : LordToil
{
	private Thing shuttle;

	private static DutyDef boardDuty;

	private const float ArrivedDist = 5f;

	private static DutyDef BoardDuty
	{
		get
		{
			object obj = boardDuty;
			if (obj == null)
			{
				obj = DefDatabase<DutyDef>.GetNamedSilentFail("EnterTransporter") ?? DutyDefOf.EnterTransporterAndDefendSelf;
				boardDuty = (DutyDef)obj;
			}
			return (DutyDef)obj;
		}
	}

	public override bool AllowSatisfyLongNeeds => false;

	public LordToil_EESBoardShuttle()
	{
	}

	public LordToil_EESBoardShuttle(Thing shuttle)
	{
		this.shuttle = shuttle;
	}

	public override void UpdateAllDuties()
	{
		if (shuttle == null)
		{
			return;
		}
		DutyDef dutyDef = BoardDuty;
		for (int i = 0; i < lord.ownedPawns.Count; i++)
		{
			Pawn pawn = lord.ownedPawns[i];
			if (pawn?.mindState != null && (pawn.mindState.duty == null || pawn.mindState.duty.def != dutyDef))
			{
				pawn.mindState.duty = new PawnDuty(dutyDef, shuttle);
				pawn.mindState.duty.locomotion = LocomotionUrgency.Jog;
			}
		}
	}

	public override void LordToilTick()
	{
		if (shuttle == null || !shuttle.Spawned)
		{
			return;
		}
		Map map = shuttle.Map;
		if (map == null)
		{
			return;
		}
		IntVec3 position = shuttle.Position;
		for (int i = 0; i < lord.ownedPawns.Count; i++)
		{
			Pawn pawn = lord.ownedPawns[i];
			if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed || pawn.jobs == null)
			{
				continue;
			}
			if ((pawn.Position - position).LengthHorizontal <= 5f)
			{
				if (pawn.CurJobDef != JobDefOf.Wait && pawn.CurJobDef != JobDefOf.Wait_MaintainPosture)
				{
					pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait, 120), JobCondition.InterruptForced);
				}
			}
			else if (pawn.CanReach(shuttle, PathEndMode.Touch, Danger.Deadly) && (pawn.CurJobDef != JobDefOf.Goto || pawn.pather == null || !pawn.pather.Moving || !((pawn.pather.Destination.Cell - position).LengthHorizontal <= 6f)))
			{
				Job job = JobMaker.MakeJob(JobDefOf.Goto, BoardGotoCell(pawn, map));
				job.locomotionUrgency = LocomotionUrgency.Jog;
				pawn.jobs.StartJob(job, JobCondition.InterruptForced);
			}
		}
	}

	private IntVec3 BoardGotoCell(Pawn p, Map map)
	{
		foreach (IntVec3 item in GenRadial.RadialCellsAround(shuttle.Position, 4f, useCenter: true))
		{
			if (item.InBounds(map) && item.Standable(map) && p.CanReach(item, PathEndMode.OnCell, Danger.Deadly))
			{
				return item;
			}
		}
		return shuttle.Position;
	}
}
