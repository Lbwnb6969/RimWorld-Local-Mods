using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public class CaravanArrivalAction_Scout : CaravanArrivalAction
{
	private Settlement settlement;

	public override string Label => EESText.T("EES.Action.ScoutLabel", settlement.Label);

	public override string ReportString => EESText.T("EES.Action.ScoutReport", settlement.Label);

	public CaravanArrivalAction_Scout()
	{
	}

	public CaravanArrivalAction_Scout(Settlement settlement)
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
		return CanScout(caravan, settlement);
	}

	public override void Arrived(Caravan caravan)
	{
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
		if (strongholdData != null)
		{
			ScoutUtility.TryScout(settlement, strongholdData, caravan);
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_References.Look(ref settlement, "settlement");
	}

	public static FloatMenuAcceptanceReport CanScout(Caravan caravan, Settlement settlement)
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
		if (!ScoutUtility.CanScoutNow(strongholdData, out var reason, settlement))
		{
			return FloatMenuAcceptanceReport.WithFailMessage(reason);
		}
		return true;
	}

	public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan, Settlement settlement)
	{
		if (StrongholdMod.Settings != null && !StrongholdMod.Settings.enableScouting)
		{
			yield break;
		}
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
		string label = ((strongholdData == null) ? EESText.T("EES.Action.ScoutLabel", settlement.Label) : EESText.T("EES.Action.ScoutLabelChance", settlement.Label, ScoutUtility.RollChance(strongholdData, caravan).ToStringPercent()));
		foreach (FloatMenuOption floatMenuOption in CaravanArrivalActionUtility.GetFloatMenuOptions(() => CanScout(caravan, settlement), () => new CaravanArrivalAction_Scout(settlement), label, caravan, settlement.Tile, settlement))
		{
			if (floatMenuOption.action != null)
			{
				StrongholdData wd = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
				Action inner = floatMenuOption.action;
				floatMenuOption.action = delegate
				{
					ScoutConfirmUtility.RunWithConfirms(settlement, wd, caravan, inner, "월드맵 메뉴");
				};
			}
			yield return floatMenuOption;
		}
	}
}
