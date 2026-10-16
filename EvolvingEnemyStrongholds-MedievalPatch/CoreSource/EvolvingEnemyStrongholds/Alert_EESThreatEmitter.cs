using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public class Alert_EESThreatEmitter : Alert
{
	private readonly List<Settlement> emitters = new List<Settlement>();

	private readonly List<GlobalTargetInfo> targets = new List<GlobalTargetInfo>();

	public Alert_EESThreatEmitter()
	{
		defaultPriority = AlertPriority.High;
	}

	private void Rebuild()
	{
		ThreatEmitterUtility.CollectEmitters(emitters);
		targets.Clear();
		for (int i = 0; i < emitters.Count; i++)
		{
			targets.Add(new GlobalTargetInfo(emitters[i]));
		}
	}

	public override string GetLabel()
	{
		return EESText.T("EES.Emitter.AlertLabel", emitters.Count);
	}

	public override TaggedString GetExplanation()
	{
		StringBuilder stringBuilder = new StringBuilder();
		WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
		for (int i = 0; i < emitters.Count; i++)
		{
			Settlement settlement = emitters[i];
			string text = (instance?.GetExisting(settlement.ID))?.roleDefName;
			if (text != null)
			{
				stringBuilder.AppendLine(EESText.T("EES.Emitter.AlertEntry", settlement.LabelCap, EESText.T("EES.Role." + text), settlement.Faction?.Name ?? "?"));
			}
		}
		stringBuilder.AppendLine();
		stringBuilder.Append(EESText.T("EES.Emitter.AlertDesc"));
		return stringBuilder.ToString();
	}

	public override AlertReport GetReport()
	{
		Rebuild();
		if (emitters.Count == 0)
		{
			return false;
		}
		return AlertReport.CulpritsAre(targets);
	}
}
