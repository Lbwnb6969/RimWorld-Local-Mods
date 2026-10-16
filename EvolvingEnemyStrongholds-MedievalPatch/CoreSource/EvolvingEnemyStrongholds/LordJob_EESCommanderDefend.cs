using RimWorld;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class LordJob_EESCommanderDefend : LordJob_DefendBase
{
	public LordJob_EESCommanderDefend()
	{
	}

	public LordJob_EESCommanderDefend(Faction faction, IntVec3 baseCenter, int delayBeforeAssault)
		: base(faction, baseCenter, delayBeforeAssault)
	{
	}

	public override StateGraph CreateGraph()
	{
		StateGraph stateGraph = base.CreateGraph();
		stateGraph.transitions.RemoveAll((Transition t) => t.target is LordToil_AssaultColony);
		stateGraph.lordToils.RemoveAll((LordToil lt) => lt is LordToil_AssaultColony);
		return EESDefendDuty.Widen(stateGraph);
	}
}
