using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class AssaultOp : IExposable, IThingHolder
{
	public int phase;

	public Faction faction;

	public int settlementId = -1;

	public int sourceTile = -1;

	public bool isEmpire;

	public int startTick;

	public List<Pawn> squad = new List<Pawn>();

	public Thing shuttle;

	public bool shuttleResolved;

	public bool abstractMode;

	public int dropMode = -1;

	public int waitDeadlineTick = -1;

	public bool fleeSuppressLogged;

	public int forcedMode = -1;

	public int dropTick = -1;

	public int dropCount;

	public Lord lord;

	public bool fled;

	public bool landed;

	public int peakAlive;

	private ThingOwner innerContainerInternal;

	public ThingOwner innerContainer => innerContainerInternal ?? (innerContainerInternal = new ThingOwner<Pawn>(this, oneStackOnly: false));

	public IThingHolder ParentHolder => null;

	public AssaultOp()
	{
		innerContainerInternal = new ThingOwner<Pawn>(this, oneStackOnly: false);
	}

	public ThingOwner GetDirectlyHeldThings()
	{
		return innerContainer;
	}

	public void GetChildHolders(List<IThingHolder> outChildren)
	{
		ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
	}

	public void ExposeData()
	{
		Scribe_Values.Look(ref phase, "phase", 0);
		Scribe_References.Look(ref faction, "faction");
		Scribe_Values.Look(ref settlementId, "settlementId", -1);
		Scribe_Values.Look(ref sourceTile, "sourceTile", -1);
		Scribe_Values.Look(ref isEmpire, "isEmpire", defaultValue: false);
		Scribe_Values.Look(ref startTick, "startTick", 0);
		Scribe_Collections.Look(ref squad, "squad", LookMode.Reference);
		Scribe_References.Look(ref shuttle, "shuttle");
		Scribe_Values.Look(ref shuttleResolved, "shuttleResolved", defaultValue: false);
		Scribe_Values.Look(ref abstractMode, "abstractMode", defaultValue: false);
		Scribe_Values.Look(ref dropMode, "dropMode", -1);
		Scribe_Values.Look(ref forcedMode, "forcedMode", -1);
		Scribe_Values.Look(ref dropTick, "dropTick", -1);
		Scribe_Values.Look(ref waitDeadlineTick, "waitDeadlineTick", -1);
		Scribe_Values.Look(ref dropCount, "dropCount", 0);
		Scribe_References.Look(ref lord, "lord");
		Scribe_Values.Look(ref fled, "fled", defaultValue: false);
		Scribe_Values.Look(ref landed, "landed", defaultValue: false);
		Scribe_Values.Look(ref peakAlive, "peakAlive", 0);
		Scribe_Deep.Look(ref innerContainerInternal, "innerContainer", this);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (squad == null)
			{
				squad = new List<Pawn>();
			}
			if (innerContainerInternal == null)
			{
				innerContainerInternal = new ThingOwner<Pawn>(this, oneStackOnly: false);
			}
		}
	}
}
