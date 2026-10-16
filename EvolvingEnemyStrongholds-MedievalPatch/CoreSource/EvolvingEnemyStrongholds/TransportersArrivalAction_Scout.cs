using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public class TransportersArrivalAction_Scout : TransportersArrivalAction_FormCaravan
{
	private Settlement settlement;

	public TransportersArrivalAction_Scout()
	{
	}

	public TransportersArrivalAction_Scout(Settlement settlement, string arrivalMessageKey)
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
		return CanScout(pods, settlement);
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
				ScoutUtility.TryScout(settlement, strongholdData, caravan);
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

	public static FloatMenuAcceptanceReport CanScout(IEnumerable<IThingHolder> pods, Settlement settlement)
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
		if (!ScoutUtility.CanScoutNow(strongholdData, out var reason, settlement))
		{
			return FloatMenuAcceptanceReport.WithFailMessage(reason);
		}
		return true;
	}

	public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(Action<PlanetTile, TransportersArrivalAction> launchAction, IEnumerable<IThingHolder> pods, Settlement settlement, bool isShuttle)
	{
		if (StrongholdMod.Settings != null && !StrongholdMod.Settings.enableScouting)
		{
			yield break;
		}
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
		string label = ((strongholdData == null) ? EESText.T("EES.Action.ScoutLabel", settlement.Label) : EESText.T("EES.Action.ScoutLabelChance", settlement.Label, ScoutUtility.RollChanceInPods(strongholdData, pods).ToStringPercent()));
		foreach (FloatMenuOption floatMenuOption in TransportersArrivalActionUtility.GetFloatMenuOptions(() => CanScout(pods, settlement), () => new TransportersArrivalAction_Scout(settlement, isShuttle ? "MessageShuttleArrived" : "MessageTransportPodsArrived"), label, launchAction, settlement.Tile))
		{
			if (floatMenuOption.action != null)
			{
				StrongholdData pd = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
				Action inner = floatMenuOption.action;
				floatMenuOption.action = delegate
				{
					ScoutConfirmUtility.RunWithConfirmsPods(settlement, pd, inner);
				};
			}
			yield return floatMenuOption;
		}
	}
}
