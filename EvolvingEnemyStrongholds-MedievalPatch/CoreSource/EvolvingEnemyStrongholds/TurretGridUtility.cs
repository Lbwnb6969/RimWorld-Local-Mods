using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class TurretGridUtility
{
	public static void RewireTurretsToGenerators(Map map, Faction faction, MapComponent_StrongholdSiege siege)
	{
		if (map == null || faction == null || siege == null || siege.turretsGridPowered || siege.isPolluted)
		{
			return;
		}
		Settlement settlement = map.Parent as Settlement;
		StrongholdData strongholdData = ((settlement == null) ? null : WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID));
		if (strongholdData != null && FakeOutpostUtility.IsFake(strongholdData.roleDefName))
		{
			return;
		}
		if (!MapHasPowerGenStep(map))
		{
			EESLog.Message("TurretGrid [" + faction.Name + "] " + ((settlement != null) ? settlement.LabelCap.ToString() : "?") + ": " + string.Format("생성기={0} genSteps={1} ", map.generatorDef?.defName ?? "null", (map.generatorDef?.genSteps?.Count).GetValueOrDefault()) + "GenStep_Power=★없음 → 자가발전 유지(전선 연결 주체 없음 · 발전기 교체 안 함). " + $"세력 포탑 {CountFactionTurrets(map, faction)}기 부하 {TotalTurretConsumption(map, faction):0}W · " + $"세력 발전기 {CountFactionGenerators(map, faction)}개(그대로 둠).");
			return;
		}
		try
		{
			int num = SwapGeneratorsToChemfuel(map, faction);
			if (num == 0)
			{
				EESLog.Message("TurretGrid [" + faction.Name + "]: 세력 발전기 0개 → 자가발전 유지(포탑 무방비 방지).");
				return;
			}
			map.powerNetManager.UpdatePowerNetsAndConnections_First();
			float num2 = RefreshGeneratorOutputs(map, faction);
			float num3 = TotalTurretConsumption(map, faction);
			siege.turretsGridPowered = true;
			EESLog.Message("TurretGrid [" + faction.Name + "]: 생성기=" + (map.generatorDef?.defName ?? "null") + " GenStep_Power=있음 | " + $"발전기 {num}개 화학연료화(연료 가득) + 자가발전 비활성 → " + $"발전기 파괴 시 포탑 OFF. ★발전 {num2:0}W / 포탑부하 {num3:0}W (여유 {num2 - num3:0}W).");
		}
		catch (Exception arg)
		{
			EESLog.Error($"TurretGrid RewireTurretsToGenerators [{faction?.Name}] 실패 → 자가발전 폴백 유지: {arg}");
		}
	}

	private static bool MapHasPowerGenStep(Map map)
	{
		List<GenStepDef> list = map?.generatorDef?.genSteps;
		if (list == null)
		{
			return true;
		}
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i]?.genStep is GenStep_Power)
			{
				return true;
			}
		}
		return false;
	}

	private static int CountFactionTurrets(Map map, Faction faction)
	{
		int num = 0;
		List<Building> allBuildingsNonColonist = map.listerBuildings.allBuildingsNonColonist;
		for (int i = 0; i < allBuildingsNonColonist.Count; i++)
		{
			Building building = allBuildingsNonColonist[i];
			if (building != null && !building.Destroyed && building.Faction == faction && building is Building_Turret)
			{
				num++;
			}
		}
		return num;
	}

	private static int CountFactionGenerators(Map map, Faction faction)
	{
		int num = 0;
		List<Building> allBuildingsNonColonist = map.listerBuildings.allBuildingsNonColonist;
		for (int i = 0; i < allBuildingsNonColonist.Count; i++)
		{
			Building building = allBuildingsNonColonist[i];
			if (building != null && !building.Destroyed && building.Faction == faction)
			{
				ThingWithComps thingWithComps = building;
				if (thingWithComps != null && thingWithComps.GetComp<CompPowerPlant>() != null)
				{
					num++;
				}
			}
		}
		return num;
	}

	private static float TotalTurretConsumption(Map map, Faction faction)
	{
		float num = 0f;
		List<Building> allBuildingsNonColonist = map.listerBuildings.allBuildingsNonColonist;
		for (int i = 0; i < allBuildingsNonColonist.Count; i++)
		{
			Building building = allBuildingsNonColonist[i];
			if (building != null && !building.Destroyed && building.Faction == faction && building is Building_Turret)
			{
				CompPowerTrader compPowerTrader = building.TryGetComp<CompPowerTrader>();
				if (compPowerTrader != null && compPowerTrader.Props != null)
				{
					num += Mathf.Max(0f, compPowerTrader.Props.PowerConsumption);
				}
			}
		}
		return num;
	}

	private static float RefreshGeneratorOutputs(Map map, Faction faction)
	{
		float num = 0f;
		List<Building> allBuildingsNonColonist = map.listerBuildings.allBuildingsNonColonist;
		for (int i = 0; i < allBuildingsNonColonist.Count; i++)
		{
			Building building = allBuildingsNonColonist[i];
			if (building == null || building.Destroyed || building.Faction != faction)
			{
				continue;
			}
			ThingWithComps thingWithComps = building;
			if (thingWithComps != null)
			{
				CompPowerPlant comp = thingWithComps.GetComp<CompPowerPlant>();
				if (comp != null)
				{
					comp.UpdateDesiredPowerOutput();
					num += comp.PowerOutput;
				}
			}
		}
		return num;
	}

	private static int SwapGeneratorsToChemfuel(Map map, Faction faction)
	{
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("ChemfuelPoweredGenerator");
		if (namedSilentFail == null)
		{
			return 0;
		}
		List<Building> list = new List<Building>();
		List<Building> allBuildingsNonColonist = map.listerBuildings.allBuildingsNonColonist;
		for (int i = 0; i < allBuildingsNonColonist.Count; i++)
		{
			Building building = allBuildingsNonColonist[i];
			if (building != null && !building.Destroyed && building.Faction == faction && building.def != namedSilentFail)
			{
				ThingWithComps thingWithComps = building;
				if (thingWithComps != null && thingWithComps.GetComp<CompPowerPlant>() != null)
				{
					list.Add(building);
				}
			}
		}
		int num = 0;
		int num2 = 0;
		foreach (Building item in list)
		{
			if (item.Destroyed)
			{
				continue;
			}
			CellRect cellRect = item.OccupiedRect();
			if (cellRect.Width < 2 || cellRect.Height < 2)
			{
				num2++;
				EESLog.Message($"TurretGrid [{faction.Name}]: {item.def.defName} {cellRect.Width}x{cellRect.Height} @{cellRect.CenterCell} — " + "화학연료 발전기(2x2)가 안 들어가 ★교체 보류(원본 유지). 부수고 빈자리로 두지 않는다.");
				continue;
			}
			try
			{
				item.Destroy();
			}
			catch
			{
			}
			int num3 = 0;
			for (int j = cellRect.minX; j + 1 <= cellRect.maxX; j += 2)
			{
				for (int k = cellRect.minZ; k + 1 <= cellRect.maxZ; k += 2)
				{
					if (PlaceChemfuel(map, faction, namedSilentFail, new IntVec3(j, 0, k)))
					{
						num3++;
					}
				}
			}
			num += num3;
			if (num3 == 0)
			{
				EESLog.Error($"TurretGrid [{faction.Name}]: {item.def.defName} {cellRect.Width}x{cellRect.Height} @{cellRect.CenterCell} 를 " + "부족으나 화학연료 발전기를 ★0개 배치 — 그 자리 전력이 사라졌다(맵 경계·InBounds 실패 의심).");
			}
		}
		if (num2 > 0)
		{
			EESLog.Message($"TurretGrid [{faction.Name}]: 2x2 미만이라 교체 보류한 발전기 {num2}개 " + $"(교체 성공 {num}개). 보류분은 원본 그대로 전력을 낸다.");
		}
		return num;
	}

	private static bool PlaceChemfuel(Map map, Faction faction, ThingDef chemfuel, IntVec3 pos)
	{
		foreach (IntVec3 item in new CellRect(pos.x, pos.z, 2, 2))
		{
			if (!item.InBounds(map))
			{
				return false;
			}
			Building edifice = item.GetEdifice(map);
			if (edifice != null && !edifice.Destroyed)
			{
				try
				{
					edifice.Destroy();
				}
				catch
				{
				}
			}
		}
		try
		{
			Thing thing = ThingMaker.MakeThing(chemfuel);
			thing.SetFactionDirect(faction);
			GenSpawn.Spawn(thing, pos, map, Rot4.North);
			CompRefuelable compRefuelable = thing.TryGetComp<CompRefuelable>();
			compRefuelable?.Refuel(compRefuelable.Props.fuelCapacity);
			return true;
		}
		catch (Exception ex)
		{
			EESLog.Warning("TurretGrid 화학연료 발전기 배치 실패: " + ex.Message);
			return false;
		}
	}
}
