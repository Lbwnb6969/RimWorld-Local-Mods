using RimWorld;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class LordJob_AssaultColony_NoFlee : LordJob_AssaultColony
{
	public override bool AddFleeToil => false;

	public LordJob_AssaultColony_NoFlee()
	{
	}

	public LordJob_AssaultColony_NoFlee(Faction f, bool canKidnap = false, bool canTimeoutOrFlee = false, bool sappers = true)
		: base(f, canKidnap, canTimeoutOrFlee, sappers, useAvoidGridSmart: false, canSteal: false)
	{
	}

	public override StateGraph CreateGraph()
	{
		StateGraph stateGraph = base.CreateGraph();
		stateGraph.transitions.RemoveAll((Transition t) => t.target is LordToil_ExitMap || t.target is LordToil_ExitMapAndDefendSelf);
		stateGraph.lordToils.RemoveAll((LordToil t) => t is LordToil_ExitMap || t is LordToil_ExitMapAndDefendSelf);
		return stateGraph;
	}
}
