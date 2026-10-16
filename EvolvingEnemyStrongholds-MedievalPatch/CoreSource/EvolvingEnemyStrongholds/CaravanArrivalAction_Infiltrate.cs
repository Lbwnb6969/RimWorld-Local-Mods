using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public class CaravanArrivalAction_Infiltrate : CaravanArrivalAction
{
	private Settlement settlement;

	public override string Label => EESText.T("EES.Action.InfiltrateLabel", settlement.Label);

	public override string ReportString => EESText.T("EES.Action.InfiltrateReport", settlement.Label);

	public CaravanArrivalAction_Infiltrate()
	{
	}

	public CaravanArrivalAction_Infiltrate(Settlement settlement)
	{
		this.settlement = settlement;
	}

	public override FloatMenuAcceptanceReport StillValid(Caravan caravan, PlanetTile destinationTile)
	{
		FloatMenuAcceptanceReport floatMenuAcceptanceReport = base.StillValid(caravan, destinationTile);
		if (!floatMenuAcceptanceReport)
		{
			return floatMenuAcceptanceReport;
		}
		if (settlement != null && settlement.Tile != destinationTile)
		{
			return false;
		}
		return CanInfiltrate(caravan, settlement);
	}

	public override void Arrived(Caravan caravan)
	{
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
		if (strongholdData != null)
		{
			ScoutUtility.TryInfiltrate(settlement, strongholdData, caravan);
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_References.Look(ref settlement, "settlement");
	}

	public static FloatMenuAcceptanceReport CanInfiltrate(Caravan caravan, Settlement settlement)
	{
		if (settlement == null || !settlement.Spawned)
		{
			return false;
		}
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
		if (strongholdData == null)
		{
			return false;
		}
		if (!ScoutUtility.CanInfiltrateNow(strongholdData, out var reason, settlement))
		{
			return FloatMenuAcceptanceReport.WithFailMessage(reason);
		}
		int count = ScoutUtility.InfiltratorsOf(caravan).Count;
		if (count == 0)
		{
			return FloatMenuAcceptanceReport.WithFailMessage(EESText.T("EES.Infil.NoAgent"));
		}
		if (count > 3)
		{
			return FloatMenuAcceptanceReport.WithFailMessage(EESText.T("EES.Infil.TooMany", 3));
		}
		return true;
	}

	public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan, Settlement settlement)
	{
		if (!ScoutUtility.Enabled)
		{
			yield break;
		}
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
		string label = ((strongholdData == null || !ScoutUtility.IsScouted(strongholdData)) ? EESText.T("EES.Action.InfiltrateLabel", settlement.Label) : EESText.T("EES.Action.InfiltrateLabelChance", settlement.Label, ScoutUtility.InfiltrateSuccessChance(ScoutUtility.InfiltratorsOf(caravan), strongholdData).ToStringPercent()));
		foreach (FloatMenuOption floatMenuOption in CaravanArrivalActionUtility.GetFloatMenuOptions(() => CanInfiltrate(caravan, settlement), () => new CaravanArrivalAction_Infiltrate(settlement), label, caravan, settlement.Tile, settlement))
		{
			yield return floatMenuOption;
		}
	}

	public static Gizmo Gizmo(Caravan caravan, Settlement settlement)
	{
		StrongholdData d = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
		if (d == null)
		{
			return null;
		}
		string reason;
		bool flag = ScoutUtility.CanInfiltrateNow(d, out reason, settlement);
		List<Pawn> list = ScoutUtility.InfiltratorsOf(caravan);
		bool flag2 = ScoutUtility.IsScouted(d);
		float f = ScoutUtility.InfiltrateSuccessChance(list, d);
		Command_Action command_Action = new Command_Action();
		command_Action.defaultLabel = EESText.T("EES.Gizmo.InfiltrateLabel");
		command_Action.defaultDesc = (flag2 ? EESText.T("EES.Gizmo.InfiltrateDesc", f.ToStringPercent(), list.Count) : EESText.T("EES.Gizmo.InfiltrateDescLocked", list.Count));
		command_Action.icon = EESTextures.InfiltrateIcon;
		command_Action.action = delegate
		{
			ScoutUtility.TryInfiltrate(settlement, d, caravan);
		};
		Command_Action command_Action2 = command_Action;
		if (!flag)
		{
			command_Action2.Disable(reason);
		}
		else if (list.Count == 0)
		{
			command_Action2.Disable(EESText.T("EES.Infil.NoAgent"));
		}
		else if (list.Count > 3)
		{
			command_Action2.Disable(EESText.T("EES.Infil.TooMany", 3));
		}
		return command_Action2;
	}
}
