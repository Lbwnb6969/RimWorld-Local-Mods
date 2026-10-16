using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public class TransportersArrivalAction_EESIntercept : TransportersArrivalAction
{
	private WorldObject_EESSupplyCaravan supply;

	public WorldObject_EESSupplyCaravan Supply => supply;

	public override bool GeneratesMap => true;

	public TransportersArrivalAction_EESIntercept()
	{
	}

	public TransportersArrivalAction_EESIntercept(WorldObject_EESSupplyCaravan supply)
	{
		this.supply = supply;
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_References.Look(ref supply, "supply");
	}

	public override FloatMenuAcceptanceReport StillValid(IEnumerable<IThingHolder> pods, PlanetTile destinationTile)
	{
		FloatMenuAcceptanceReport floatMenuAcceptanceReport = base.StillValid(pods, destinationTile);
		if (!floatMenuAcceptanceReport)
		{
			return floatMenuAcceptanceReport;
		}
		if (supply != null && RimPactsCompat.WorldWarBlocked(supply.Faction))
		{
			EESLog.Message("Supply: 공중 요격 도착 무효 [" + supply.Faction?.Name + "] — " + RimPactsCompat.WorldWarBlockDebug(supply.Faction) + " (바닐라 폴백 착륙).");
			return false;
		}
		return supply != null && !supply.Destroyed && supply.Spawned && supply.Tile == destinationTile && !Find.WorldObjects.AnyMapParentAt(destinationTile);
	}

	public override bool ShouldUseLongEvent(List<ActiveTransporterInfo> pods, PlanetTile tile)
	{
		return true;
	}

	public override void Arrived(List<ActiveTransporterInfo> transporters, PlanetTile tile)
	{
		Faction faction = supply?.Faction;
		int num = supply?.destSettlementId ?? (-1);
		int tier = supply?.ConvoyTier() ?? 3;
		Map orGenerateMap;
		try
		{
			orGenerateMap = GetOrGenerateMapUtility.GetOrGenerateMap(tile, new IntVec3(100, 1, 100), SupplyCaravanUtility.EncounterDef);
		}
		catch (Exception ex)
		{
			EESLog.Error("Supply pod intercept: 맵 생성 실패 — 재throw. " + ex);
			throw;
		}
		IntVec3 center = orGenerateMap.Center;
		List<Pawn> list = null;
		try
		{
			orGenerateMap.Parent?.SetFaction(faction);
			list = SupplyCaravanUtility.GenerateEscort(faction, tier, num);
			for (int i = 0; i < list.Count; i++)
			{
				IntVec3 loc = CellFinder.RandomSpawnCellForPawnNear(center, orGenerateMap);
				GenSpawn.Spawn(list[i], loc, orGenerateMap, Rot4.Random);
			}
			SupplyCaravanUtility.FinalizeBattleMap(orGenerateMap, faction, list, center, num, tier, supply);
		}
		catch (Exception ex2)
		{
			EESLog.Error("Supply pod intercept: 호위/화물 연출 실패(하차는 계속 진행): " + ex2);
		}
		bool flag = false;
		try
		{
			PawnsArrivalModeDefOf.EdgeDrop.Worker.TravellingTransportersArrived(transporters, orGenerateMap);
			flag = true;
		}
		catch (Exception ex3)
		{
			EESLog.Error("Supply pod intercept: EdgeDrop 하차 실패 → 수동 하차 폴백: " + ex3);
		}
		if (!flag)
		{
			int num2 = 0;
			for (int j = 0; j < transporters.Count; j++)
			{
				try
				{
					ThingOwner thingOwner = transporters[j]?.innerContainer;
					if (thingOwner != null)
					{
						IntVec3 dropLoc = CellFinder.RandomSpawnCellForPawnNear(center, orGenerateMap);
						num2 += thingOwner.Count;
						thingOwner.TryDropAll(dropLoc, orGenerateMap, ThingPlaceMode.Near);
					}
				}
				catch (Exception ex4)
				{
					EESLog.Error($"Supply pod intercept: 수동 하차도 실패(transporter {j}): " + ex4);
				}
			}
			Messages.Message(EESText.T("EES.Supply.PodDropFallback"), new TargetInfo(center, orGenerateMap), MessageTypeDefOf.CautionInput, historical: false);
			EESLog.Message($"Supply: 수동 하차 폴백 완료 — {num2}개 내용물 직접 스폰.");
		}
		try
		{
			EESLog.Message($"Supply: 포드 교전 성립 @ {orGenerateMap.Tile} [{faction?.Name}] " + $"호위 {list?.Count ?? 0} (적대={faction?.HostileTo(Faction.OfPlayer)}, dropped={flag}).");
			Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_SupplyEngaged"), EESText.T("EES.Supply.Engaged.Text"), EESLetterDefOf.EES_SupplyEngaged, new GlobalTargetInfo(center, orGenerateMap));
		}
		catch (Exception ex5)
		{
			EESLog.Error("Supply pod intercept: 레터 실패(무해): " + ex5);
		}
		if (supply != null && !supply.Destroyed)
		{
			supply.Notify_BattleBegun(orGenerateMap.Tile);
		}
	}
}
