using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

public class Alert_StrongholdReinforcement : Alert
{
	public Alert_StrongholdReinforcement()
	{
		defaultPriority = AlertPriority.High;
	}

	private static MapComponent_StrongholdSiege Comp()
	{
		return Find.CurrentMap?.GetComponent<MapComponent_StrongholdSiege>();
	}

	public override string GetLabel()
	{
		MapComponent_StrongholdSiege mapComponent_StrongholdSiege = Comp();
		if (mapComponent_StrongholdSiege == null || !mapComponent_StrongholdSiege.ReinforcementActive)
		{
			return EESText.T("EES.Reinf.Countdown", "?");
		}
		return EESText.T("EES.Reinf.Countdown", mapComponent_StrongholdSiege.ReinforcementTicksRemaining.ToStringTicksToPeriod());
	}

	public override TaggedString GetExplanation()
	{
		return EESText.T("EES.Reinf.CountdownDesc");
	}

	public override AlertReport GetReport()
	{
		return Comp()?.ReinforcementActive ?? false;
	}
}
