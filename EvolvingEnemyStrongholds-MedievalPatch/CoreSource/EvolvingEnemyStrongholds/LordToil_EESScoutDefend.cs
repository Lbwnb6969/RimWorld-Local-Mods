using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class LordToil_EESScoutDefend : LordToil_DefendPoint
{
	public LordToil_EESScoutDefend(IntVec3 point)
		: base(point, 12f, 3f)
	{
	}

	public override void LordToilTick()
	{
		base.LordToilTick();
		if (((lord?.LordJob as LordJob_EESCounterScout)?.ElapsedTicks ?? int.MaxValue) >= 60000)
		{
			lord.ReceiveMemo("EESScoutLeave");
		}
	}
}
