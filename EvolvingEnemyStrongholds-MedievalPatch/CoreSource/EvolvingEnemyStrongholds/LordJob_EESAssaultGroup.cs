using RimWorld;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class LordJob_EESAssaultGroup : LordJob_AssaultColony
{
	public int groupType;

	public float fleeThreshold;

	public int initialCount;

	public bool fledTriggered;

	public bool fleeSuppressLogged;

	public override bool AddFleeToil => false;

	public LordJob_EESAssaultGroup()
	{
	}

	public LordJob_EESAssaultGroup(Faction f, GarrisonGroupType gtype, float threshold, int initCount)
		: base(f, canKidnap: false, canTimeoutOrFlee: false, sappers: true, useAvoidGridSmart: false, canSteal: false)
	{
		groupType = (int)gtype;
		fleeThreshold = threshold;
		initialCount = initCount;
	}

	public override StateGraph CreateGraph()
	{
		StateGraph stateGraph = base.CreateGraph();
		stateGraph.transitions.RemoveAll((Transition t) => t.target is LordToil_ExitMap || t.target is LordToil_ExitMapAndDefendSelf);
		stateGraph.lordToils.RemoveAll((LordToil lt) => lt is LordToil_ExitMap || lt is LordToil_ExitMapAndDefendSelf);
		return stateGraph;
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref groupType, "eesGType", 0);
		Scribe_Values.Look(ref fleeThreshold, "eesGThresh", 0f);
		Scribe_Values.Look(ref initialCount, "eesGInit", 0);
		Scribe_Values.Look(ref fledTriggered, "eesGFled", defaultValue: false);
	}
}
