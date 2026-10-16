using RimWorld;
using Verse;
using Verse.AI;

namespace EvolvingEnemyStrongholds;

public class LordToil_EESDefendBase : LordToil_DefendBase
{
	public LordToil_EESDefendBase(IntVec3 baseCenter)
		: base(baseCenter)
	{
	}

	public override void UpdateAllDuties()
	{
		DutyDef duty = EESDefendDuty.Duty;
		for (int i = 0; i < lord.ownedPawns.Count; i++)
		{
			Pawn pawn = lord.ownedPawns[i];
			if (pawn?.mindState != null)
			{
				pawn.mindState.duty = new PawnDuty(duty, baseCenter);
			}
		}
	}
}
