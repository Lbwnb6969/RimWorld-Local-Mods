using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

public class QuestPart_EESHVTSpawnTarget : QuestPart
{
	public int targetFactionLoadId = -1;

	public int tier = 7;

	public int commanderType = 1;

	public int variant;

	public bool spawned;

	public bool dirtyJob;

	public override void PreQuestAccept()
	{
		base.PreQuestAccept();
		if (!spawned)
		{
			spawned = true;
			HVTQuestUtility.SpawnAcceptedTarget(quest, this);
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref targetFactionLoadId, "targetFactionLoadId", -1);
		Scribe_Values.Look(ref tier, "tier", 7);
		Scribe_Values.Look(ref commanderType, "commanderType", 1);
		Scribe_Values.Look(ref variant, "variant", 0);
		Scribe_Values.Look(ref spawned, "spawned", defaultValue: false);
		Scribe_Values.Look(ref dirtyJob, "dirtyJob", defaultValue: false);
	}
}
