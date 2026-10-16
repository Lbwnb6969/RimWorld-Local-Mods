using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public class TransportersArrivalAction_Infiltrate : TransportersArrivalAction_FormCaravan
{
	private Settlement settlement;

	public TransportersArrivalAction_Infiltrate()
	{
	}

	public TransportersArrivalAction_Infiltrate(Settlement settlement, string arrivalMessageKey)
		: base(arrivalMessageKey)
	{
		this.settlement = settlement;
	}

	public override FloatMenuAcceptanceReport StillValid(IEnumerable<IThingHolder> pods, PlanetTile destinationTile)
	{
		FloatMenuAcceptanceReport floatMenuAcceptanceReport = base.StillValid(pods, destinationTile);
		if (!floatMenuAcceptanceReport)
		{
			return floatMenuAcceptanceReport;
		}
		if (settlement != null && settlement.Tile != destinationTile)
		{
			return false;
		}
		return CanInfiltrate(pods, settlement);
	}

	public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
	{
		base.Arrived(transporters, tile);
		if (settlement != null && settlement.Faction != null && settlement.Faction != Faction.OfPlayer)
		{
			StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
			if (strongholdData != null)
			{
				Caravan caravan = FindPlayerCaravanAt(settlement.Tile);
				ScoutUtility.TryInfiltrate(settlement, strongholdData, caravan);
			}
		}
	}

	private static Caravan FindPlayerCaravanAt(PlanetTile tile)
	{
		List<Caravan> caravans = Find.WorldObjects.Caravans;
		for (int i = 0; i < caravans.Count; i++)
		{
			Caravan caravan = caravans[i];
			if (caravan != null && caravan.Faction == Faction.OfPlayer && caravan.Tile == tile)
			{
				return caravan;
			}
		}
		return null;
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_References.Look(ref settlement, "settlement");
	}

	public static FloatMenuAcceptanceReport CanInfiltrate(IEnumerable<IThingHolder> pods, Settlement settlement)
	{
		if (settlement == null || !settlement.Spawned)
		{
			return false;
		}
		if (!TransportersArrivalAction_FormCaravan.CanFormCaravanAt(pods, settlement.Tile))
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
		int num = ScoutUtility.InfiltratorsInPods(pods);
		if (num == 0)
		{
			return FloatMenuAcceptanceReport.WithFailMessage(EESText.T("EES.Infil.NoAgent"));
		}
		if (num > 3)
		{
			return FloatMenuAcceptanceReport.WithFailMessage(EESText.T("EES.Infil.TooMany", 3));
		}
		return true;
	}

	public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(Action<PlanetTile, TransportersArrivalAction> launchAction, IEnumerable<IThingHolder> pods, Settlement settlement, bool isShuttle)
	{
		if (!ScoutUtility.Enabled)
		{
			yield break;
		}
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
		string label = ((strongholdData == null || !ScoutUtility.IsScouted(strongholdData)) ? EESText.T("EES.Action.InfiltrateLabel", settlement.Label) : EESText.T("EES.Action.InfiltrateLabelChance", settlement.Label, ScoutUtility.InfiltrateSuccessChance(ScoutUtility.InfiltratorsListInPods(pods), strongholdData).ToStringPercent()));
		foreach (FloatMenuOption floatMenuOption in TransportersArrivalActionUtility.GetFloatMenuOptions(() => CanInfiltrate(pods, settlement), () => new TransportersArrivalAction_Infiltrate(settlement, isShuttle ? "MessageShuttleArrived" : "MessageTransportPodsArrived"), label, launchAction, settlement.Tile))
		{
			yield return floatMenuOption;
		}
	}
}
