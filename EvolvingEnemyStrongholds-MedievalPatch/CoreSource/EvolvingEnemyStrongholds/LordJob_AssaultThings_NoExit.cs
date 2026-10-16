using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class LordJob_AssaultThings_NoExit : LordJob_AssaultThings
{
	public override bool AddFleeToil => false;

	public LordJob_AssaultThings_NoExit()
	{
	}

	public LordJob_AssaultThings_NoExit(Faction attacker, List<Thing> things)
		: base(attacker, things)
	{
	}

	public override StateGraph CreateGraph()
	{
		StateGraph stateGraph = base.CreateGraph();
		stateGraph.transitions.RemoveAll((Transition t) => t.target is LordToil_ExitMapAndDefendSelf);
		stateGraph.lordToils.RemoveAll((LordToil t) => t is LordToil_ExitMapAndDefendSelf);
		return stateGraph;
	}
}
