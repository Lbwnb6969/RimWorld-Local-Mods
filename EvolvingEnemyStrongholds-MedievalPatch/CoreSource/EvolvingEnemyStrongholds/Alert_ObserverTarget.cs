using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

public class Alert_ObserverTarget : Alert
{
	public Alert_ObserverTarget()
	{
		defaultPriority = AlertPriority.Medium;
	}

	private static MapComponent_StrongholdSiege Comp()
	{
		return Find.CurrentMap?.GetComponent<MapComponent_StrongholdSiege>();
	}

	public override string GetLabel()
	{
		int num = Comp()?.ObserverTicksRemaining ?? (-1);
		return EESText.T("EES.Observer.Countdown", (num >= 0) ? num.ToStringTicksToPeriod() : "?");
	}

	public override TaggedString GetExplanation()
	{
		Pawn pawn = Comp()?.ObserverTarget;
		return (pawn != null) ? EESText.T("EES.Observer.CountdownDescTarget", pawn.LabelShortCap) : EESText.T("EES.Observer.CountdownDesc");
	}

	public override AlertReport GetReport()
	{
		return Comp()?.ObserverActive ?? false;
	}
}
