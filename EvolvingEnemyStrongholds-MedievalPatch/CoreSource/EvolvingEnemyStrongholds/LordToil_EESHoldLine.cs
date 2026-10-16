using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class LordToil_EESHoldLine : LordToil
{
	private IntVec3 point;

	private List<IntVec3> cells;

	public LordToil_EESHoldLine(IntVec3 point, List<IntVec3> cells)
	{
		this.point = point;
		this.cells = cells;
	}

	public override void UpdateAllDuties()
	{
		Map map = ((lord.ownedPawns.Count > 0) ? lord.ownedPawns[0].MapHeld : null);
		List<IntVec3> list = ((cells != null && cells.Count > 0) ? cells : null);
		List<IntVec3> list2 = null;
		if (list == null && map != null)
		{
			list2 = new List<IntVec3>();
			foreach (IntVec3 item in GenRadial.RadialCellsAround(point, 8f, useCenter: true))
			{
				if (item.InBounds(map) && item.Standable(map))
				{
					list2.Add(item);
					if (list2.Count >= lord.ownedPawns.Count)
					{
						break;
					}
				}
			}
		}
		for (int i = 0; i < lord.ownedPawns.Count; i++)
		{
			Pawn pawn = lord.ownedPawns[i];
			if (pawn?.mindState != null)
			{
				IntVec3 intVec = list?[i % list.Count] ?? ((list2 != null && list2.Count > 0) ? list2[i % list2.Count] : point);
				pawn.mindState.duty = new PawnDuty(DutyDefOf.Defend, intVec, 0f);
			}
		}
	}
}
