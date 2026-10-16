using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public static class ThrumboRanchGen
{
	private static readonly string[] LivestockKinds = new string[5] { "Cow", "Chicken", "Muffalo", "Bison", "Goat" };

	public static void InsertRanchCenter(Map map, StrongholdData data, CellRect footprint)
	{
		Faction faction = map?.ParentFaction;
		if (map == null || faction == null || data == null)
		{
			return;
		}
		try
		{
			ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("Fence");
			ThingDef namedSilentFail2 = DefDatabase<ThingDef>.GetNamedSilentFail("FenceGate");
			IntVec3 centerCell = footprint.CenterCell;
			int radius = Mathf.Clamp(footprint.Width / 4, 5, 9);
			CellRect cellRect = CellRect.CenteredOn(centerCell, radius).ClipInsideMap(map);
			ClearArea(map, cellRect, faction);
			int num = 0;
			int num2 = 0;
			if (namedSilentFail != null)
			{
				IntVec3 intVec = new IntVec3(cellRect.CenterCell.x, 0, cellRect.maxZ);
				IntVec3 intVec2 = new IntVec3(cellRect.CenterCell.x, 0, cellRect.minZ);
				foreach (IntVec3 edgeCell in cellRect.EdgeCells)
				{
					if (!edgeCell.InBounds(map))
					{
						continue;
					}
					bool flag = namedSilentFail2 != null && (edgeCell == intVec || edgeCell == intVec2);
					if (PlaceStructure(map, faction, flag ? namedSilentFail2 : namedSilentFail, edgeCell))
					{
						if (flag)
						{
							num2++;
						}
						else
						{
							num++;
						}
					}
				}
			}
			int num3 = SpawnLivestock(map, faction, cellRect, data);
			data.customLayout = true;
			EESLog.Message($"ThrumboRanch center [{faction.Name} T{data.tier}]: pen {cellRect.Width}x{cellRect.Height} " + $"fences={num} gates={num2} livestock={num3}.");
		}
		catch (Exception ex)
		{
			EESLog.Error("ThrumboRanch InsertRanchCenter failed: " + ex);
		}
	}

	public static void PopulateThrumbos(Map map, StrongholdData data, CellRect footprint)
	{
		Faction faction = map?.ParentFaction;
		if (map == null || faction == null || data == null)
		{
			return;
		}
		try
		{
			PawnKindDef namedSilentFail = DefDatabase<PawnKindDef>.GetNamedSilentFail("Thrumbo");
			if (namedSilentFail == null)
			{
				EESLog.Warning("ThrumboRanch: Thrumbo PawnKindDef 없음 — 트럼보 미스폰.");
				return;
			}
			Rand.PushState(Gen.HashCombineInt(map.uniqueID, 464834));
			int num = Rand.RangeInclusive(3, 6);
			int num2 = 0;
			if (ThrumboRanchUtility.IsAlpha(data.roleDefName))
			{
				num2 = Rand.RangeInclusive(1, 2);
			}
			Rand.PopState();
			PawnKindDef pawnKindDef = (ModsConfig.OdysseyActive ? DefDatabase<PawnKindDef>.GetNamedSilentFail("AlphaThrumbo") : null);
			List<Pawn> list = new List<Pawn>();
			for (int i = 0; i < num; i++)
			{
				Pawn pawn = SpawnFactionAnimal(map, faction, namedSilentFail, footprint);
				if (pawn != null)
				{
					list.Add(pawn);
				}
			}
			int num3 = 0;
			if (pawnKindDef != null)
			{
				for (int j = 0; j < num2; j++)
				{
					Pawn pawn2 = SpawnFactionAnimal(map, faction, pawnKindDef, footprint);
					if (pawn2 != null)
					{
						list.Add(pawn2);
						num3++;
					}
				}
			}
			if (list.Count > 0)
			{
				LordJob_DefendBase lordJob = new LordJob_DefendBase(faction, footprint.CenterCell, 25000);
				LordMaker.MakeNewLord(faction, lordJob, map, list);
			}
			EESLog.Message($"ThrumboRanch herd [{faction.Name} {data.roleDefName}]: 트럼보 {list.Count - num3}마리" + ((num3 > 0) ? $" + 알파 {num3}마리" : "") + " (세력 소속·방어 Lord).");
		}
		catch (Exception ex)
		{
			EESLog.Error("ThrumboRanch PopulateThrumbos failed: " + ex);
		}
	}

	private static int SpawnLivestock(Map map, Faction faction, CellRect pen, StrongholdData data)
	{
		Rand.PushState(Gen.HashCombineInt(map.uniqueID, 464851));
		int num = Rand.RangeInclusive(8, 14);
		Rand.PopState();
		int num2 = 0;
		for (int i = 0; i < num; i++)
		{
			PawnKindDef namedSilentFail = DefDatabase<PawnKindDef>.GetNamedSilentFail(LivestockKinds[i % LivestockKinds.Length]);
			if (namedSilentFail != null && SpawnFactionAnimal(map, faction, namedSilentFail, pen.ContractedBy(1)) != null)
			{
				num2++;
			}
		}
		return num2;
	}

	private static Pawn SpawnFactionAnimal(Map map, Faction faction, PawnKindDef kind, CellRect rect)
	{
		if (!TryFindCell(map, rect, out var cell))
		{
			return null;
		}
		try
		{
			Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer, -1, forceGenerateNewPawn: true));
			GenSpawn.Spawn(pawn, cell, map, Rot4.Random);
			return pawn;
		}
		catch (Exception ex)
		{
			EESLog.Warning("ThrumboRanch animal spawn (" + kind.defName + ") 실패: " + ex.Message);
			return null;
		}
	}

	private static bool TryFindCell(Map map, CellRect rect, out IntVec3 cell)
	{
		for (int i = 0; i < 60; i++)
		{
			IntVec3 randomCell = rect.RandomCell;
			if (randomCell.InBounds(map) && randomCell.Standable(map) && !randomCell.Fogged(map) && randomCell.GetEdifice(map) == null)
			{
				cell = randomCell;
				return true;
			}
		}
		cell = rect.CenterCell;
		if (cell.InBounds(map))
		{
			return cell.Standable(map);
		}
		return false;
	}

	private static bool PlaceStructure(Map map, Faction faction, ThingDef def, IntVec3 c)
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
			ThingDef stuff = (def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
			Thing thing = ThingMaker.MakeThing(def, stuff);
			thing.SetFactionDirect(faction);
			GenSpawn.Spawn(thing, c, map);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void ClearArea(Map map, CellRect rect, Faction faction)
	{
		TerrainDef namedSilentFail = DefDatabase<TerrainDef>.GetNamedSilentFail("Soil");
		foreach (IntVec3 item in rect)
		{
			if (!item.InBounds(map))
			{
				continue;
			}
			// Destroying a spawned thing updates the map's live cell list. Iterate a
			// snapshot so a third-party settlement generator cannot invalidate the
			// index while the ranch clears its pen footprint.
			List<Thing> thingList = item.GetThingList(map).ToList();
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
			if (namedSilentFail != null && map.terrainGrid.TerrainAt(item) != namedSilentFail)
			{
				try
				{
					map.terrainGrid.SetTerrain(item, namedSilentFail);
				}
				catch
				{
				}
			}
			if (map.roofGrid.Roofed(item))
			{
				map.roofGrid.SetRoof(item, null);
			}
		}
	}
}
