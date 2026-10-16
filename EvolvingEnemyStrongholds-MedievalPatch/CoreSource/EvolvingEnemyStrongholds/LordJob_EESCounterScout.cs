using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class LordJob_EESCounterScout : LordJob
{
	public const string MemoLeave = "EESScoutLeave";

	public const string MemoFight = "EESScoutFight";

	public const int MinMissionTicks = 60000;

	private IntVec3 lurkSpot = IntVec3.Invalid;

	private int startTick = -1;

	public int StartTick => startTick;

	public int ElapsedTicks
	{
		get
		{
			if (startTick >= 0)
			{
				return Find.TickManager.TicksGame - startTick;
			}
			return int.MaxValue;
		}
	}

	public override bool AddFleeToil => false;

	public LordJob_EESCounterScout()
	{
	}

	public LordJob_EESCounterScout(IntVec3 lurkSpot)
	{
		this.lurkSpot = lurkSpot;
		startTick = Find.TickManager.TicksGame;
	}

	public override StateGraph CreateGraph()
	{
		StateGraph stateGraph = new StateGraph();
		LordToil_EESScoutLurk lordToil_EESScoutLurk = new LordToil_EESScoutLurk(lurkSpot);
		stateGraph.AddToil(lordToil_EESScoutLurk);
		stateGraph.StartingToil = lordToil_EESScoutLurk;
		LordToil_EESScoutDefend lordToil_EESScoutDefend = new LordToil_EESScoutDefend(lurkSpot);
		stateGraph.AddToil(lordToil_EESScoutDefend);
		LordToil_ExitMap lordToil_ExitMap = new LordToil_ExitMap(LocomotionUrgency.Sprint, canDig: false, interruptCurrentJob: true);
		stateGraph.AddToil(lordToil_ExitMap);
		Transition transition = new Transition(lordToil_EESScoutLurk, lordToil_EESScoutDefend);
		transition.AddTrigger(new Trigger_Memo("EESScoutFight"));
		transition.AddTrigger(new Trigger_PawnHarmed());
		stateGraph.AddTransition(transition);
		Transition transition2 = new Transition(lordToil_EESScoutLurk, lordToil_ExitMap);
		transition2.AddSource(lordToil_EESScoutDefend);
		transition2.AddTrigger(new Trigger_Memo("EESScoutLeave"));
		stateGraph.AddTransition(transition2);
		return stateGraph;
	}

	public override void ExposeData()
	{
		Scribe_Values.Look(ref lurkSpot, "lurkSpot", IntVec3.Invalid);
		Scribe_Values.Look(ref startTick, "startTick", -1);
	}
}
