using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class FakeOutpostGen
{
	public static void BuildFake(Settlement settlement, Map map, StrongholdData data, MapComponent_StrongholdSiege siege)
	{
		Faction faction = settlement.Faction;
		int tier = data.tier;
		int num = Mathf.Clamp(12 + tier, 16, 28);
		IntVec3 intVec = FindBaseCenter(map);
		IntVec3 center = (intVec.IsValid ? intVec : map.Center);
		int radius = num;
		CellRect cellRect = CellRect.CenteredOn(center, radius).ClipInsideMap(map);
		FakeVariant fakeVariant = FakeOutpostUtility.VariantOf(data.roleDefName);
		int num2 = ClearAreaAndPawns(map, cellRect, faction);
		float lootMul = ((fakeVariant == FakeVariant.Insect) ? 1f : 1.5f);
		CellRect rewardRoom = StrongholdLayoutGenerator.BuildFakeRewardRoom(map, faction, data, cellRect, lootMul);
		FillVariantFloor(map, fakeVariant, cellRect, rewardRoom);
		int num3 = BuildSealedWall(map, faction, data, cellRect);
		int num4 = 0;
		try
		{
			StrongholdAreaUtility.BuildOuterRing(map, data, cellRect);
			num4 = 1;
		}
		catch (Exception ex)
		{
			EESLog.Warning("FakeOutpost BuildOuterRing failed: " + ex.Message);
		}
		int num5 = 0;
		try
		{
			num5 = StrongholdGarrisonUtility.AddTurrets(map, faction, data, cellRect, inert: true);
		}
		catch (Exception ex2)
		{
			EESLog.Warning("FakeOutpost AddTurrets failed: " + ex2.Message);
		}
		int num6 = 0;
		try
		{
			num6 = FakeMonsterUtility.SpawnMonsters(map, data, cellRect, rewardRoom);
		}
		catch (Exception ex3)
		{
			EESLog.Error("FakeOutpost monster spawn failed: " + ex3);
		}
		try
		{
			foreach (IntVec3 item in cellRect)
			{
				if (item.InBounds(map))
				{
					map.roofGrid.SetRoof(item, null);
				}
			}
			foreach (IntVec3 item2 in rewardRoom)
			{
				if (item2.InBounds(map))
				{
					map.roofGrid.SetRoof(item2, RoofDefOf.RoofConstructed);
				}
			}
		}
		catch (Exception ex4)
		{
			EESLog.Warning("FakeOutpost roof failed: " + ex4.Message);
		}
		data.customLayout = true;
		data.hasCommsRoom = false;
		data.hasAmmoDepot = false;
		data.hasMortarBattery = false;
		data.hasPrison = false;
		data.hasCommander = false;
		data.commanderType = CommanderType.None;
		data.commanderKindDefName = null;
		if (siege != null)
		{
			siege.eesBaseRect = cellRect;
			siege.isFake = true;
		}
		EESLog.Message($"FakeOutpost [{faction.Name} T{tier} {data.roleDefName}] @ {cellRect.CenterCell}: " + $"rect {cellRect.Width}x{cellRect.Height} | clearedPawns={num2} outerWalls={num3} " + string.Format("inertTurrets={0} barricades={1} ", num5, (num4 > 0) ? "ON" : "off") + $"monsters={num6} | 보상방 {rewardRoom.Width}x{rewardRoom.Height}.");
	}

	private static IntVec3 FindBaseCenter(Map map)
	{
		if (map?.listerBuildings == null)
		{
			return IntVec3.Invalid;
		}
		long num = 0L;
		long num2 = 0L;
		int num3 = 0;
		List<Building> allBuildingsNonColonist = map.listerBuildings.allBuildingsNonColonist;
		for (int i = 0; i < allBuildingsNonColonist.Count; i++)
		{
			Building building = allBuildingsNonColonist[i];
			if (building != null && !building.Destroyed && building.def != null && !building.def.mineable && (building.def.building == null || !building.def.building.isNaturalRock) && building.def.category == ThingCategory.Building)
			{
				num += building.Position.x;
				num2 += building.Position.z;
				num3++;
			}
		}
		if (num3 == 0)
		{
			return IntVec3.Invalid;
		}
		return new IntVec3((int)(num / num3), 0, (int)(num2 / num3));
	}

	private static void FillVariantFloor(Map map, FakeVariant variant, CellRect baseRect, CellRect rewardRoom)
	{
		TerrainDef terrainDef = null;
		switch (variant)
		{
		case FakeVariant.Insect:
			terrainDef = DefDatabase<TerrainDef>.GetNamedSilentFail("InsectSludge") ?? DefDatabase<TerrainDef>.GetNamedSilentFail("Mud");
			break;
		case FakeVariant.Entity:
			terrainDef = DefDatabase<TerrainDef>.GetNamedSilentFail("MossyTerrain");
			break;
		case FakeVariant.Mech:
			terrainDef = DefDatabase<TerrainDef>.GetNamedSilentFail("MetalTile");
			break;
		}
		if (terrainDef == null)
		{
			return;
		}
		CellRect cellRect = rewardRoom.ExpandedBy(1);
		int num = 0;
		foreach (IntVec3 item in baseRect)
		{
			if (item.InBounds(map) && !cellRect.Contains(item) && item.GetEdifice(map) == null)
			{
				map.terrainGrid.SetTerrain(item, terrainDef);
				num++;
			}
		}
		EESLog.Message($"FakeOutpost floor [{variant}]: {terrainDef.defName} ×{num}칸(기지 내부 나무바닥·물 포함).");
	}

	private static int ClearAreaAndPawns(Map map, CellRect rect, Faction faction)
	{
		foreach (IntVec3 item in rect)
		{
			if (!item.InBounds(map))
			{
				continue;
			}
			List<Thing> thingList = item.GetThingList(map);
			for (int num = thingList.Count - 1; num >= 0; num--)
			{
				Thing thing = thingList[num];
				if (thing != null && !thing.Destroyed && !(thing is Pawn))
				{
					ThingCategory category = thing.def.category;
					if (category == ThingCategory.Building || category == ThingCategory.Item || category == ThingCategory.Plant || category == ThingCategory.Filth || thing == item.GetEdifice(map))
					{
						try
						{
							thing.Destroy();
						}
						catch
						{
						}
					}
				}
			}
		}
		int num2 = 0;
		List<Thing> list = new List<Thing>();
		List<Building> allBuildingsNonColonist = map.listerBuildings.allBuildingsNonColonist;
		for (int i = 0; i < allBuildingsNonColonist.Count; i++)
		{
			Building building = allBuildingsNonColonist[i];
			if (building != null && !building.Destroyed)
			{
				ThingWithComps thingWithComps = building;
				if (thingWithComps != null && (thingWithComps.GetComp<CompPowerPlant>() != null || thingWithComps.GetComp<CompPowerBattery>() != null))
				{
					list.Add(building);
				}
			}
		}
		for (int j = 0; j < list.Count; j++)
		{
			try
			{
				if (!list[j].Destroyed)
				{
					list[j].Destroy();
					num2++;
				}
			}
			catch
			{
			}
		}
		int num3 = 0;
		List<Thing> list2 = new List<Thing>();
		List<Building> allBuildingsNonColonist2 = map.listerBuildings.allBuildingsNonColonist;
		for (int k = 0; k < allBuildingsNonColonist2.Count; k++)
		{
			Building building2 = allBuildingsNonColonist2[k];
			if (building2 != null && !building2.Destroyed && building2.Faction == faction)
			{
				list2.Add(building2);
			}
		}
		for (int l = 0; l < list2.Count; l++)
		{
			try
			{
				if (!list2[l].Destroyed)
				{
					list2[l].Destroy();
					num3++;
				}
			}
			catch
			{
			}
		}
		int num4 = 0;
		List<Pawn> list3 = new List<Pawn>();
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int m = 0; m < allPawnsSpawned.Count; m++)
		{
			Pawn pawn = allPawnsSpawned[m];
			if (pawn != null && !pawn.Destroyed && pawn.Faction == faction)
			{
				list3.Add(pawn);
			}
		}
		foreach (Pawn item2 in list3)
		{
			try
			{
				if (item2.IsWorldPawn())
				{
					item2.DeSpawn();
				}
				else
				{
					item2.Destroy();
				}
				num4++;
			}
			catch
			{
			}
		}
		EESLog.Message($"FakeOutpost clear [{faction.Name}]: 세력건물 {num3}개 + 발전기/배터리 {num2}개 + 폰 {num4}명 제거(맵 전역).");
		return num4;
	}

	private static int BuildSealedWall(Map map, Faction faction, StrongholdData data, CellRect rect)
	{
		StrongholdDefenseUtility.ResolveWall(map, faction, Mathf.Min(data.ContentTier, 5), out var wallDef, out var stuff);
		if (wallDef == null)
		{
			wallDef = ThingDefOf.Wall;
		}
		int num = 0;
		foreach (IntVec3 edgeCell in rect.EdgeCells)
		{
			ThingDef stuff2 = (wallDef.MadeFromStuff ? (stuff ?? GenStuff.DefaultStuffFor(wallDef)) : null);
			if (PlaceWall(map, faction, wallDef, stuff2, edgeCell))
			{
				num++;
			}
		}
		return num;
	}

	private static bool PlaceWall(Map map, Faction faction, ThingDef def, ThingDef stuff, IntVec3 c)
	{
		if (def == null || !c.InBounds(map))
		{
			return false;
		}
		Building edifice = c.GetEdifice(map);
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
		try
		{
			ThingDef stuff2 = (def.MadeFromStuff ? (stuff ?? GenStuff.DefaultStuffFor(def)) : null);
			Thing thing = ThingMaker.MakeThing(def, stuff2);
			thing.SetFactionDirect(faction);
			GenSpawn.Spawn(thing, c, map);
			return true;
		}
		catch
		{
			return false;
		}
	}
}
