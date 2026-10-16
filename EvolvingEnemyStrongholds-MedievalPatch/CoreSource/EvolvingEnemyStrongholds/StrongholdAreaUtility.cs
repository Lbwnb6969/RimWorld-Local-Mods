using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.BaseGen;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class StrongholdAreaUtility
{
	private const int MapEdgeMargin = 14;

	private static readonly int[] ExpandPerTier = new int[14]
	{
		0, 0, 0, 1, 1, 2, 3, 5, 6, 9,
		12, 15, 18, 26
	};

	private const int GateHalfWidth = 2;

	public const int OuterRingOffset = 4;

	public const int OuterRingOffsetMax = 8;

	public static int OuterRingOffsetFor(StrongholdData data)
	{
		int num = ((data != null && data.maxTurretFootprint > 0) ? data.maxTurretFootprint : 2);
		return Mathf.Clamp(Mathf.Max(4, num + 1), 4, 8);
	}

	public static CellRect ExpandArea(Map map, StrongholdData data, out CellRect original)
	{
		CellRect result = (original = GetRect(map));
		if (result.Width <= 0 || data == null || data.tier <= 1)
		{
			return result;
		}
		int num = ExpandPerTier[Mathf.Clamp(data.tier, 1, 13)] + FinalStandUtility.AreaBonus(data);
		if (num <= 0)
		{
			return result;
		}
		CellRect cellRect = result.ExpandedBy(num);
		CellRect cellRect2 = map.BoundsRect(14);
		int num2 = Mathf.Max(cellRect.minX, cellRect2.minX);
		int num3 = Mathf.Max(cellRect.minZ, cellRect2.minZ);
		int num4 = Mathf.Min(cellRect.maxX, cellRect2.maxX);
		int num5 = Mathf.Min(cellRect.maxZ, cellRect2.maxZ);
		if (num4 <= num2 || num5 <= num3)
		{
			return result;
		}
		cellRect = CellRect.FromLimits(num2, num3, num4, num5);
		MapGenerator.SetVar("SettlementRect", cellRect);
		EESLog.Message($"Area: {map.ParentFaction?.Name} T{data.tier} expand +{num}/side " + $"{result.Width}x{result.Height} -> {cellRect.Width}x{cellRect.Height} " + $"(mapClamp bounds {cellRect2.Width}x{cellRect2.Height}).");
		return cellRect;
	}

	public static int BuildPerimeter(Map map, StrongholdData data, CellRect rect)
	{
		Faction parentFaction = map.ParentFaction;
		if (parentFaction == null || rect.Width < 5 || rect.Height < 5)
		{
			return 0;
		}
		StrongholdDefenseUtility.ResolveWall(map, parentFaction, data.ContentTier, out var wallDef, out var stuff);
		if (wallDef == null)
		{
			return 0;
		}
		bool flag = data.tier <= 7;
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		foreach (IntVec3 edgeCell in rect.EdgeCells)
		{
			if ((flag && IsGate(edgeCell, rect)) || !edgeCell.InBounds(map) || !VacuumMapUtility.IsBuildableGround(map, edgeCell))
			{
				continue;
			}
			Building edifice = edgeCell.GetEdifice(map);
			if (edifice != null)
			{
				bool flag2 = edifice.def.passability == Traversability.Impassable && !edifice.def.IsDoor;
				if (flag || flag2)
				{
					num3++;
					continue;
				}
				edifice.Destroy();
				num5++;
			}
			if (!edgeCell.Standable(map))
			{
				if (flag)
				{
					num4++;
					continue;
				}
				num2++;
			}
			try
			{
				EvictPawnsForWall(map, edgeCell);
				Thing thing = ThingMaker.MakeThing(wallDef, wallDef.MadeFromStuff ? stuff : null);
				thing.SetFactionDirect(parentFaction);
				GenSpawn.Spawn(thing, edgeCell, map);
				num++;
			}
			catch (Exception ex)
			{
				EESLog.Error($"Area perimeter {wallDef.defName} @ {edgeCell}: {ex.Message}");
			}
		}
		int num6 = 0;
		if (!flag)
		{
			num6 = CarveGateDoors(map, parentFaction, data.ContentTier, rect, stuff);
		}
		EESLog.Message($"Area perimeter [{parentFaction.Name} T{data.tier}]: {wallDef.defName}" + string.Format("{0} built={1}(sealedWater={2},sealedEdifice={3}) gateDoors={4} ", (stuff != null) ? ("/" + stuff.defName) : "", num, num2, num5, num6) + string.Format("skip(edifice={0},water={1}) (gates={2}).", num3, num4, flag ? "open-nodoor(T7-)" : "walled+door(T8+)"));
		return num;
	}

	public static int ResealPerimeter(Map map, StrongholdData data, CellRect rect)
	{
		Faction faction = map?.ParentFaction;
		if (faction == null || data == null || data.tier < 8 || rect.Width < 5)
		{
			return 0;
		}
		StrongholdDefenseUtility.ResolveWall(map, faction, data.ContentTier, out var wallDef, out var stuff);
		if (wallDef == null)
		{
			return 0;
		}
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		StringBuilder stringBuilder = new StringBuilder();
		foreach (IntVec3 edgeCell in rect.EdgeCells)
		{
			if (!edgeCell.InBounds(map) || !VacuumMapUtility.IsBuildableGround(map, edgeCell))
			{
				continue;
			}
			Building edifice = edgeCell.GetEdifice(map);
			if (edifice != null)
			{
				if (edifice.def.passability == Traversability.Impassable && !edifice.def.IsDoor)
				{
					continue;
				}
				if (edifice.def.IsDoor)
				{
					num3++;
					continue;
				}
				edifice.Destroy();
			}
			TerrainDef terrain = edgeCell.GetTerrain(map);
			if (terrain != null && terrain.IsSubstructure)
			{
				num2++;
				if (num2 <= 6)
				{
					stringBuilder.Append(edgeCell).Append(' ');
				}
				continue;
			}
			try
			{
				EvictPawnsForWall(map, edgeCell);
				Thing thing = ThingMaker.MakeThing(wallDef, wallDef.MadeFromStuff ? stuff : null);
				thing.SetFactionDirect(faction);
				GenSpawn.Spawn(thing, edgeCell, map);
				num++;
			}
			catch (Exception ex)
			{
				EESLog.Error($"ResealPerimeter {wallDef.defName} @ {edgeCell}: {ex.Message}");
			}
		}
		int num4 = CarveGateDoors(map, faction, data.ContentTier, rect, stuff);
		EESLog.Message($"Perimeter reseal [{faction.Name} T{data.tier}]: +{num} wall(s) " + $"(생성 마지막 재봉인 — 중력부양선 무관). 보존: 문={num3} / ★미봉인(부양선 하부구조)={num2}" + string.Format("{0} | ★게이트 재보장: 신규문 {1}.", (num2 > 0) ? (" @ " + stringBuilder.ToString().Trim()) : "", num4));
		return num;
	}

	private static int CarveGateDoors(Map map, Faction faction, int tier, CellRect rect, ThingDef wallStuff)
	{
		ThingDef thingDef = StrongholdDefenseUtility.ResolveDoorDef(tier >= 5);
		if (thingDef == null)
		{
			return 0;
		}
		ThingDef stuff = ((!thingDef.MadeFromStuff) ? null : ((wallStuff != null && wallStuff.stuffProps != null) ? wallStuff : (StrongholdDefenseUtility.PickCoverStuff(thingDef, faction, tier) ?? GenStuff.DefaultStuffFor(thingDef))));
		CellRect mapBounds = map.BoundsRect(2);
		int num = rect.minX + rect.Width / 2;
		int num2 = rect.minZ + rect.Height / 2;
		int num3 = 0;
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < 4; i++)
		{
			bool flag = i < 2;
			int num4 = i switch
			{
				2 => rect.maxX,
				1 => rect.minZ,
				0 => rect.maxZ,
				_ => rect.minX,
			};
			int num5 = (flag ? num : num2);
			int num6 = (flag ? rect.Width : rect.Height) / 2;
			bool flag2 = false;
			for (int j = 0; j <= num6; j++)
			{
				if (flag2)
				{
					break;
				}
				int[] array = ((j == 0) ? new int[1] : new int[2]
				{
					-j,
					j
				});
				foreach (int num7 in array)
				{
					int num8 = num5 + num7;
					IntVec3 cell = (flag ? new IntVec3(num8, 0, num4) : new IntVec3(num4, 0, num8));
					if (TryMakeGateDoor(map, faction, thingDef, stuff, cell, mapBounds, out var existedDoor))
					{
						if (!existedDoor)
						{
							num3++;
						}
						stringBuilder.Append(string.Format("{0}@{1},{2}{3} ", SideName(i), cell.x, cell.z, existedDoor ? "(기존)" : ""));
						flag2 = true;
						break;
					}
				}
			}
			if (!flag2)
			{
				stringBuilder.Append(SideName(i) + "=실패 ");
			}
		}
		if (num3 == 0)
		{
			bool flag3 = false;
			foreach (IntVec3 edgeCell in rect.EdgeCells)
			{
				if (TryMakeGateDoor(map, faction, thingDef, stuff, edgeCell, mapBounds, out var existedDoor2))
				{
					if (existedDoor2)
					{
						stringBuilder.Append("FALLBACK=기존문있음 ");
					}
					else
					{
						num3++;
						stringBuilder.Append($"FALLBACK@{edgeCell.x},{edgeCell.z} ");
					}
					flag3 = true;
					break;
				}
			}
			if (!flag3)
			{
				stringBuilder.Append("FALLBACK=불가(둘레 전부 막힘) ");
			}
		}
		EESLog.Message($"Area gates [{faction.Name} T{tier}]: made={num3} | {stringBuilder.ToString().TrimEnd(Array.Empty<char>())}");
		return num3;
	}

	private static bool TryMakeGateDoor(Map map, Faction faction, ThingDef doorDef, ThingDef stuff, IntVec3 cell, CellRect mapBounds, out bool existedDoor)
	{
		existedDoor = false;
		if (!cell.InBounds(map) || !mapBounds.Contains(cell))
		{
			return false;
		}
		Building edifice = cell.GetEdifice(map);
		if (edifice is Building_Door)
		{
			existedDoor = true;
			return true;
		}
		if (edifice != null && !edifice.Destroyed)
		{
			edifice.Destroy();
		}
		if (!cell.Standable(map))
		{
			return false;
		}
		try
		{
			Thing thing = ThingMaker.MakeThing(doorDef, doorDef.MadeFromStuff ? stuff : null);
			thing.SetFactionDirect(faction);
			GenSpawn.Spawn(thing, cell, map);
			return true;
		}
		catch (Exception ex)
		{
			EESLog.Error($"Area gate door @ {cell}: {ex.Message}");
			return false;
		}
	}

	private static void EvictPawnsForWall(Map map, IntVec3 cell)
	{
		List<Thing> list = null;
		List<Thing> thingList = cell.GetThingList(map);
		for (int i = 0; i < thingList.Count; i++)
		{
			if (thingList[i] is Pawn)
			{
				(list ?? (list = new List<Thing>())).Add(thingList[i]);
			}
		}
		if (list == null)
		{
			return;
		}
		foreach (Thing item in list)
		{
			if (item is Pawn { Spawned: not false } pawn && CellFinder.TryFindRandomCellNear(cell, map, 8, (IntVec3 c) => c != cell && c.InBounds(map) && c.Standable(map) && c.GetEdifice(map) == null, out var result))
			{
				pawn.Position = result;
				pawn.Notify_Teleported(endCurrentJob: false, resetTweenedPos: false);
			}
		}
	}

	private static string SideName(int side)
	{
		return side switch
		{
			0 => "N",
			1 => "S",
			2 => "E",
			_ => "W",
		};
	}

	public static int AddInteriorBuildings(Map map, CellRect original, CellRect expanded, StrongholdData data)
	{
		Faction parentFaction = map.ParentFaction;
		if (parentFaction == null || original.Width <= 0 || expanded.Area <= original.Area)
		{
			return 0;
		}
		CellRect inner = expanded.ContractedBy(2);
		List<CellRect> obj = new List<CellRect>
		{
			CellRect.FromLimits(expanded.minX, original.maxZ + 1, expanded.maxX, expanded.maxZ),
			CellRect.FromLimits(expanded.minX, expanded.minZ, expanded.maxX, original.minZ - 1),
			CellRect.FromLimits(expanded.minX, original.minZ, original.minX - 1, original.maxZ),
			CellRect.FromLimits(original.maxX + 1, original.minZ, expanded.maxX, original.maxZ)
		};
		int num = 0;
		BaseGen.globalSettings.map = map;
		BaseGen.globalSettings.minEmptyNodes = 0;
		foreach (CellRect item in obj)
		{
			CellRect rect = ClipToInner(item, inner);
			if (rect.Width >= 4 && rect.Height >= 4 && rect.Area >= 60 && !rect.Overlaps(original))
			{
				ResolveParams resolveParams = new ResolveParams
				{
					rect = rect,
					faction = parentFaction,
					floorOnlyIfTerrainSupports = true
				};
				BaseGen.symbolStack.Push("basePart_outdoors", resolveParams);
				num++;
			}
		}
		if (num == 0)
		{
			return 0;
		}
		Patch_BaseGenPostSpawnGuard.Begin();
		try
		{
			BaseGen.Generate();
		}
		catch (Exception ex)
		{
			EESLog.Error("AddInteriorBuildings BaseGen.Generate failed: " + ex);
		}
		finally
		{
			Patch_BaseGenPostSpawnGuard.End();
		}
		EESLog.Message($"Area interior [{parentFaction.Name} T{data.tier}]: filled {num} strip(s) via BaseGen.");
		return num;
	}

	private static CellRect ClipToInner(CellRect raw, CellRect inner)
	{
		int num = Mathf.Max(raw.minX, inner.minX);
		int num2 = Mathf.Max(raw.minZ, inner.minZ);
		int num3 = Mathf.Min(raw.maxX, inner.maxX);
		int num4 = Mathf.Min(raw.maxZ, inner.maxZ);
		if (num3 < num || num4 < num2)
		{
			return CellRect.Empty;
		}
		return CellRect.FromLimits(num, num2, num3, num4);
	}

	public static void ClearTreesAroundWalls(Map map, StrongholdData data, CellRect settlementRect, int radius)
	{
		if (map == null || data == null || VacuumMapUtility.IsVacuum(map))
		{
			return;
		}
		CellRect cellRect = settlementRect.ExpandedBy(radius).ClipInsideMap(map);
		CellRect cellRect2 = settlementRect.ContractedBy(2);
		int num = 0;
		foreach (IntVec3 item in cellRect)
		{
			if (!item.InBounds(map) || cellRect2.Contains(item))
			{
				continue;
			}
			List<Thing> thingList = item.GetThingList(map);
			for (int num2 = thingList.Count - 1; num2 >= 0; num2--)
			{
				Thing thing = thingList[num2];
				if (thing?.def?.plant != null && thing.def.plant.IsTree && !thing.Destroyed)
				{
					thing.Destroy();
					num++;
				}
			}
		}
		EESLog.Message($"Area treeClear [{map.ParentFaction?.Name} T{data.tier}]: removed {num} trees " + $"in band (inside2 ~ outside{radius}) around walls.");
	}

	public static void BuildOuterRing(Map map, StrongholdData data, CellRect settlementRect)
	{
		Faction parentFaction = map.ParentFaction;
		if (parentFaction == null || settlementRect.Width < 5)
		{
			return;
		}
		int contentTier = data.ContentTier;
		int num = OuterRingOffsetFor(data);
		CellRect rect = settlementRect.ExpandedBy(num);
		CellRect cellRect = map.BoundsRect(2);
		ThingDef sandbags = ThingDefOf.Sandbags;
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("Barricade");
		ThingDef namedSilentFail2 = DefDatabase<ThingDef>.GetNamedSilentFail("BarbedWire");
		ThingDef thingDef = ((namedSilentFail != null) ? StrongholdDefenseUtility.PickCoverStuff(namedSilentFail, parentFaction, contentTier) : null);
		Rand.PushState(Gen.HashCombineInt(data.settlementId, 12648430));
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		try
		{
			bool ceStructuresActive = StrongholdDefenseUtility.CeStructuresActive;
			ThingDef ceEmbrasure = StrongholdDefenseUtility.CeEmbrasure;
			ThingDef ceReinforcedEmbrasure = StrongholdDefenseUtility.CeReinforcedEmbrasure;
			ThingDef thingDef2 = null;
			ThingDef stuff = null;
			if (ceStructuresActive)
			{
				thingDef2 = ((data.tier <= 4) ? sandbags : ((data.tier <= 8) ? namedSilentFail : ((data.tier > 10) ? (ceReinforcedEmbrasure ?? ceEmbrasure ?? namedSilentFail) : (ceEmbrasure ?? namedSilentFail))));
				if (thingDef2 != null && thingDef2 != sandbags && thingDef2.MadeFromStuff)
				{
					stuff = StrongholdDefenseUtility.PickCoverStuff(thingDef2, parentFaction, contentTier);
				}
			}
			foreach (IntVec3 edgeCell in rect.EdgeCells)
			{
				if (IsGate(edgeCell, rect) || !edgeCell.InBounds(map) || !cellRect.Contains(edgeCell) || edgeCell.GetEdifice(map) != null || !edgeCell.Standable(map))
				{
					continue;
				}
				if (ceStructuresActive)
				{
					if (thingDef2 != null && TrySpawn(map, parentFaction, thingDef2, edgeCell, stuff))
					{
						num3++;
					}
				}
				else if ((data.tier >= 8) ? (namedSilentFail != null) : (data.tier >= 6 && namedSilentFail != null && Rand.Bool))
				{
					if (TrySpawn(map, parentFaction, namedSilentFail, edgeCell, thingDef))
					{
						num3++;
					}
				}
				else if (sandbags != null && TrySpawn(map, parentFaction, sandbags, edgeCell, null))
				{
					num2++;
				}
			}
		}
		finally
		{
			Rand.PopState();
		}
		if (namedSilentFail2 != null)
		{
			CellRect rect2 = rect.ExpandedBy(1);
			foreach (IntVec3 edgeCell2 in rect2.EdgeCells)
			{
				if (!IsGate(edgeCell2, rect2) && edgeCell2.InBounds(map) && cellRect.Contains(edgeCell2) && edgeCell2.GetEdifice(map) == null && edgeCell2.Standable(map) && TrySpawn(map, parentFaction, namedSilentFail2, edgeCell2, null))
				{
					num4++;
				}
			}
		}
		EESLog.Message($"Area outerRing [{parentFaction.Name} T{contentTier}]: sandbags={num2} barricades={num3}" + string.Format("{0} barbedWire={1} (offset {2}, maxTurret={3}).", (thingDef != null) ? ("/" + thingDef.defName) : "", num4, num, data.maxTurretFootprint));
	}

	private static bool TrySpawn(Map map, Faction faction, ThingDef def, IntVec3 cell, ThingDef stuff)
	{
		try
		{
			ThingDef stuff2 = (def.MadeFromStuff ? (stuff ?? GenStuff.DefaultStuffFor(def)) : null);
			Thing thing = ThingMaker.MakeThing(def, stuff2);
			thing.SetFactionDirect(faction);
			GenSpawn.Spawn(thing, cell, map);
			return true;
		}
		catch (Exception ex)
		{
			EESLog.Error($"Area ring spawn {def.defName} @ {cell}: {ex.Message}");
			return false;
		}
	}

	private static bool IsGate(IntVec3 c, CellRect rect)
	{
		int num = rect.minX + rect.Width / 2;
		int num2 = rect.minZ + rect.Height / 2;
		bool num3 = c.x == rect.minX || c.x == rect.maxX;
		bool flag = c.z == rect.minZ || c.z == rect.maxZ;
		if (num3 && Mathf.Abs(c.z - num2) <= 2)
		{
			return true;
		}
		if (flag && Mathf.Abs(c.x - num) <= 2)
		{
			return true;
		}
		return false;
	}

	private static CellRect GetRect(Map map)
	{
		if (!MapGenerator.TryGetVar<CellRect>("SettlementRect", out var var))
		{
			return CellRect.Empty;
		}
		return var;
	}
}
