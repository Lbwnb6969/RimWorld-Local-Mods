using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class LordJob_EESBoardShuttle : LordJob
{
	private Thing shuttle;

	public override bool AddFleeToil => false;

	public LordJob_EESBoardShuttle()
	{
	}

	public LordJob_EESBoardShuttle(Thing shuttle)
	{
		this.shuttle = shuttle;
	}

	public override StateGraph CreateGraph()
	{
		StateGraph stateGraph = new StateGraph();
		LordToil_EESBoardShuttle lordToil_EESBoardShuttle = new LordToil_EESBoardShuttle(shuttle);
		stateGraph.AddToil(lordToil_EESBoardShuttle);
		stateGraph.StartingToil = lordToil_EESBoardShuttle;
		return stateGraph;
	}

	public override void ExposeData()
	{
		Scribe_References.Look(ref shuttle, "shuttle");
	}
}
