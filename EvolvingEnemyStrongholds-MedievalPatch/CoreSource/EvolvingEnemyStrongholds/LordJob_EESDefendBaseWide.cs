using RimWorld;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class LordJob_EESDefendBaseWide : LordJob_DefendBase
{
	public LordJob_EESDefendBaseWide()
	{
	}

	public LordJob_EESDefendBaseWide(Faction faction, IntVec3 baseCenter, int delayBeforeAssault)
		: base(faction, baseCenter, delayBeforeAssault)
	{
	}

	public override StateGraph CreateGraph()
	{
		return EESDefendDuty.Widen(base.CreateGraph());
	}
}
