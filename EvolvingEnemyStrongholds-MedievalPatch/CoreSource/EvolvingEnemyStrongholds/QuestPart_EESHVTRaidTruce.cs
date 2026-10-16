using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

public class QuestPart_EESHVTRaidTruce : QuestPart
{
	public string inSignal;

	public int issuerFactionLoadId = -1;

	public override void Notify_QuestSignalReceived(Signal signal)
	{
		base.Notify_QuestSignalReceived(signal);
		if (!(signal.tag != inSignal))
		{
			Faction fac = CounterScoutUtility.FactionByLoadId(issuerFactionLoadId);
			WorldComponent_EESHVT.Instance?.GrantTruce(fac);
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref inSignal, "inSignal");
		Scribe_Values.Look(ref issuerFactionLoadId, "issuerFactionLoadId", -1);
	}
}
