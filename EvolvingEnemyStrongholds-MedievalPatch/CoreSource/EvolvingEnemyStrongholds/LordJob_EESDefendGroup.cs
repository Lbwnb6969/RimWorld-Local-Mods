using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class LordJob_EESDefendGroup : LordJob_DefendBase
{
	public int groupType;

	public float fleeThreshold;

	public int initialCount;

	public int assaultTick = -1;

	public int assaultDelayTicks;

	public bool fledTriggered;

	public bool fleeSuppressLogged;

	public IntVec3 holdPoint = IntVec3.Invalid;

	public float holdRadius;

	public List<IntVec3> guardCells;

	public bool huntInvaders;

	public bool releasedToHunt;

	public bool reformSkipLogged;

	public override bool AddFleeToil => false;

	public LordJob_EESDefendGroup()
	{
	}

	public LordJob_EESDefendGroup(Faction f, IntVec3 center, GarrisonGroupType gtype, float threshold, int initCount, int delayBeforeAssault = 2000000000)
		: base(f, center, delayBeforeAssault)
	{
		groupType = (int)gtype;
		fleeThreshold = threshold;
		initialCount = initCount;
	}

	public override StateGraph CreateGraph()
	{
		if (huntInvaders)
		{
			return new StateGraph
			{
				StartingToil = new LordToil_EESHuntInvaders(holdPoint.IsValid ? holdPoint : IntVec3.Invalid)
			};
		}
		if (holdRadius > 0f && holdPoint.IsValid)
		{
			return new StateGraph
			{
				StartingToil = new LordToil_EESHoldLine(holdPoint, guardCells)
			};
		}
		StateGraph stateGraph = base.CreateGraph();
		stateGraph.transitions.RemoveAll((Transition t) => t.target is LordToil_AssaultColony);
		stateGraph.lordToils.RemoveAll((LordToil lt) => lt is LordToil_AssaultColony);
		return EESDefendDuty.Widen(stateGraph);
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref groupType, "eesGType", 0);
		Scribe_Values.Look(ref fleeThreshold, "eesGThresh", 0f);
		Scribe_Values.Look(ref initialCount, "eesGInit", 0);
		Scribe_Values.Look(ref assaultTick, "eesGAssault", -1);
		Scribe_Values.Look(ref assaultDelayTicks, "eesGAssaultDelay", 0);
		Scribe_Values.Look(ref fledTriggered, "eesGFled", defaultValue: false);
		Scribe_Values.Look(ref holdPoint, "eesGHoldPt", IntVec3.Invalid);
		Scribe_Values.Look(ref holdRadius, "eesGHoldR", 0f);
		Scribe_Collections.Look(ref guardCells, "eesGGuardCells", LookMode.Value);
		Scribe_Values.Look(ref huntInvaders, "eesGHunt", defaultValue: false);
		Scribe_Values.Look(ref releasedToHunt, "eesGReleased", defaultValue: false);
	}
}
