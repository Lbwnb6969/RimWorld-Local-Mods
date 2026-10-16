using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class LordToil_EESHuntInvaders : LordToil
{
	private IntVec3 anchor;

	public LordToil_EESHuntInvaders(IntVec3 anchor)
	{
		this.anchor = anchor;
	}

	public override void UpdateAllDuties()
	{
		DutyDef def = DefDatabase<DutyDef>.GetNamedSilentFail("EES_HuntInvaders") ?? DutyDefOf.Defend;
		for (int i = 0; i < lord.ownedPawns.Count; i++)
		{
			Pawn pawn = lord.ownedPawns[i];
			if (pawn?.mindState != null)
			{
				pawn.mindState.duty = new PawnDuty(def, anchor, 0f);
			}
		}
	}
}
