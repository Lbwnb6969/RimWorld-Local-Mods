using RimWorld;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class LordJob_DefendBase_NoFlee : LordJob_DefendBase
{
	public override bool AddFleeToil => false;

	public LordJob_DefendBase_NoFlee()
	{
	}

	public LordJob_DefendBase_NoFlee(Faction f, IntVec3 baseCenter, int delayBeforeAssault)
		: base(f, baseCenter, delayBeforeAssault)
	{
	}

	public override StateGraph CreateGraph()
	{
		StateGraph stateGraph = base.CreateGraph();
		stateGraph.transitions.RemoveAll((Transition t) => t.target is LordToil_AssaultColony);
		stateGraph.lordToils.RemoveAll((LordToil t) => t is LordToil_AssaultColony);
		return EESDefendDuty.Widen(stateGraph);
	}
}
