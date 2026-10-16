using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public class QuestPart_EESHVTTarget : QuestPart
{
	public Settlement settlement;

	public int settlementId = -1;

	public int factionLoadId = -1;

	public string outSignalKilled;

	public string outSignalInvalid;

	public override IEnumerable<GlobalTargetInfo> QuestLookTargets
	{
		get
		{
			foreach (GlobalTargetInfo questLookTarget in base.QuestLookTargets)
			{
				yield return questLookTarget;
			}
			if (settlement != null && !settlement.Destroyed)
			{
				yield return settlement;
			}
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_References.Look(ref settlement, "settlement");
		Scribe_Values.Look(ref settlementId, "settlementId", -1);
		Scribe_Values.Look(ref factionLoadId, "factionLoadId", -1);
		Scribe_Values.Look(ref outSignalKilled, "outSignalKilled");
		Scribe_Values.Look(ref outSignalInvalid, "outSignalInvalid");
	}
}
