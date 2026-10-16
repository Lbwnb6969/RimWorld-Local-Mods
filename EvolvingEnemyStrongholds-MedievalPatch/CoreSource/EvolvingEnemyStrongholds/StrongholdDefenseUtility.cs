using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class StrongholdDefenseUtility
{
	private const int VanillaMaxMatTier = 5;

	private const float MinOwnWallHp = 150f;

	private static readonly HashSet<string> StandardWallStuffs = new HashSet<string> { "WoodLog", "Steel", "Plasteel", "Uranium", "BlocksGranite", "BlocksLimestone", "BlocksMarble", "BlocksSandstone", "BlocksSlate" };

	private static ThingDef _ceWall;

	private static ThingDef _ceEmb;

	private static ThingDef _ceREmb;

	private static ThingDef _ceDoor;

	private static bool _ceResolved;

	private static readonly ThingDef AncientWallDef = DefDatabase<ThingDef>.GetNamedSilentFail("AncientFortifiedWall");

	private static readonly ThingDef OrbitalAncientWallDef = DefDatabase<ThingDef>.GetNamedSilentFail("OrbitalAncientFortifiedWall");

	private static readonly ThingDef AncientBlastDoorDef = DefDatabase<ThingDef>.GetNamedSilentFail("AncientBlastDoor");

	public static ThingDef CeReinforcedDoor
	{
		get
		{
			ResolveCeDefs();
			return _ceDoor;
		}
	}

	public static bool CeStructuresActive
	{
		get
		{
			ResolveCeDefs();
			if (_ceWall != null)
			{
				if (StrongholdMod.Settings != null)
				{
					return StrongholdMod.Settings.enableCEIntegration;
				}
				return true;
			}
			return false;
		}
	}

	public static ThingDef CeReinforcedWall
	{
		get
		{
			ResolveCeDefs();
			return _ceWall;
		}
	}

	public static ThingDef CeEmbrasure
	{
		get
		{
			ResolveCeDefs();
			return _ceEmb;
		}
	}

	public static ThingDef CeReinforcedEmbrasure
	{
		get
		{
			ResolveCeDefs();
			return _ceREmb;
		}
	}

	public static void ReinforceOuterStructures(Map map, StrongholdData data)
	{
		if (map == null || data == null || data.tier <= 3)
		{
			if (data != null)
			{
				EESLog.Message($"Defense: T{data.tier} -> no upgrade.");
			}
			return;
		}
		Faction parentFaction = map.ParentFaction;
		TechLevel techLevel = parentFaction?.def?.techLevel ?? TechLevel.Industrial;
		ModContentPack modContentPack = parentFaction?.def?.modContentPack;
		CellRect settlementRect = GetSettlementRect(map);
		if (settlementRect.Width <= 0)
		{
			EESLog.Warning("Defense: no SettlementRect; skip.");
			return;
		}
		List<Building> list = new List<Building>();
		List<Building> list2 = new List<Building>();
		CollectStructures(map, settlementRect, parentFaction, list, list2);
		if (list.Count == 0 && list2.Count == 0)
		{
			EESLog.Message("Defense: no walls/doors in rect; skip.");
			return;
		}
		bool flag = modContentPack?.IsOfficialMod ?? true;
		bool flag2 = AncientWall(map) != null;
		bool flag3 = data.ContentTier >= 6 && (int)techLevel >= 5 && flag && flag2;
		bool flag4 = CeStructuresActive && data.ContentTier >= 4 && CeReinforcedWall != null;
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		string text = null;
		string text2 = null;
		bool isFactionOwn;
		foreach (Building item in list)
		{
			if (item.Destroyed)
			{
				continue;
			}
			bool flag5 = item.def == ThingDefOf.Wall;
			if (flag3 && flag5)
			{
				if (ReplaceBuilding(map, item, AncientWall(map), null))
				{
					num++;
				}
			}
			else if (flag4 && flag5)
			{
				ThingDef thingDef = ChooseMaterial(ThingDefOf.Wall, parentFaction, modContentPack, techLevel, data.ContentTier, out isFactionOwn, 150f);
				if (thingDef == null || !CanMake(CeReinforcedWall, thingDef))
				{
					if (item.Stuff != null && !IsForeignStuff(item.Stuff, modContentPack) && CanMake(CeReinforcedWall, item.Stuff))
					{
						thingDef = item.Stuff;
					}
					else
					{
						using IEnumerator<ThingDef> enumerator2 = GenStuff.AllowedStuffsFor(CeReinforcedWall, techLevel).GetEnumerator();
						if (enumerator2.MoveNext())
						{
							thingDef = enumerator2.Current;
						}
					}
				}
				if (IsForeignStuff(item.Stuff, modContentPack))
				{
					num5++;
					text2 = item.Stuff?.defName;
				}
				if (ReplaceBuilding(map, item, CeReinforcedWall, thingDef))
				{
					num2++;
					text = thingDef?.defName;
				}
			}
			else
			{
				if (!item.def.MadeFromStuff)
				{
					continue;
				}
				bool isFactionOwn2;
				ThingDef thingDef2 = ChooseMaterial(item.def, parentFaction, modContentPack, techLevel, data.ContentTier, out isFactionOwn2, 150f);
				if (thingDef2 == null || thingDef2 == item.Stuff)
				{
					continue;
				}
				bool flag6 = IsForeignStuff(item.Stuff, modContentPack);
				if ((flag6 || isFactionOwn2 || !(WallHp(item.def, thingDef2) <= WallHp(item.def, item.Stuff))) && ReplaceBuilding(map, item, item.def, thingDef2))
				{
					num3++;
					text = thingDef2.defName;
					if (flag6)
					{
						num5++;
						text2 = item.Stuff?.defName;
					}
				}
			}
		}
		ThingDef thingDef3 = ChooseMaterial(ThingDefOf.Wall, parentFaction, modContentPack, techLevel, data.ContentTier, out isFactionOwn, 150f);
		bool flag7 = CeStructuresActive && data.ContentTier >= 5 && CeReinforcedDoor != null;
		foreach (Building item2 in list2)
		{
			if (item2.Destroyed)
			{
				continue;
			}
			if (flag7 && item2.def == ThingDefOf.Door)
			{
				ThingDef thingDef4 = ((thingDef3 != null && CanMake(CeReinforcedDoor, thingDef3)) ? thingDef3 : null);
				if (thingDef4 == null)
				{
					if (item2.Stuff != null && !IsForeignStuff(item2.Stuff, modContentPack) && CanMake(CeReinforcedDoor, item2.Stuff))
					{
						thingDef4 = item2.Stuff;
					}
					else
					{
						using IEnumerator<ThingDef> enumerator2 = GenStuff.AllowedStuffsFor(CeReinforcedDoor, techLevel).GetEnumerator();
						if (enumerator2.MoveNext())
						{
							thingDef4 = enumerator2.Current;
						}
					}
				}
				if (ReplaceBuilding(map, item2, CeReinforcedDoor, thingDef4))
				{
					num4++;
					text = thingDef4?.defName;
				}
			}
			else
			{
				if (!item2.def.MadeFromStuff)
				{
					continue;
				}
				if (thingDef3 != null && CanMake(item2.def, thingDef3))
				{
					if (thingDef3 != item2.Stuff && ReplaceBuilding(map, item2, item2.def, thingDef3))
					{
						num4++;
					}
					continue;
				}
				bool isFactionOwn3;
				ThingDef thingDef5 = ChooseMaterial(item2.def, parentFaction, modContentPack, techLevel, data.ContentTier, out isFactionOwn3, 150f);
				if (thingDef5 == null || thingDef5 == item2.Stuff)
				{
					continue;
				}
				bool flag8 = IsForeignStuff(item2.Stuff, modContentPack);
				if ((flag8 || isFactionOwn3 || !(WallHp(item2.def, thingDef5) <= WallHp(item2.def, item2.Stuff))) && ReplaceBuilding(map, item2, item2.def, thingDef5))
				{
					num4++;
					if (flag8)
					{
						num5++;
						text2 = item2.Stuff?.defName;
					}
				}
			}
		}
		EESLog.Message(string.Format("Defense: {0}[{1}] tech={2} ", parentFaction?.Name, modContentPack?.Name ?? "?", techLevel) + $"T{data.tier}(CT{data.ContentTier})[{data.tierSource}] role={data.roleDefName} | " + $"ancient(CT>=6={data.ContentTier >= 6},spacer+={(int)techLevel >= 5}," + $"official={flag},defLoaded={flag2})={flag3} | " + string.Format("CE(active={0},ceWall={1},CT>=4={2})={3} | ", CeStructuresActive, (CeReinforcedWall != null) ? "OK" : "null", data.ContentTier >= 4, flag4) + string.Format("result: ancientWalls={0} ceWalls={1} matWalls={2}(stuff~{3}) ", num, num2, num3, text ?? "none") + string.Format("doors={0} foreignStripped={1}(~{2}) scanned(w={3},d={4}).", num4, num5, text2 ?? "none", list.Count, list2.Count));
	}

	public static ThingDef PickCoverStuff(ThingDef coverDef, Faction faction, int tier)
	{
		if (coverDef == null || !coverDef.MadeFromStuff)
		{
			return null;
		}
		TechLevel tech = faction?.def?.techLevel ?? TechLevel.Industrial;
		ModContentPack factionMod = faction?.def?.modContentPack;
		bool isFactionOwn;
		return ChooseMaterial(coverDef, faction, factionMod, tech, tier, out isFactionOwn);
	}

	public static void ResolveWall(Map map, Faction faction, int tier, out ThingDef wallDef, out ThingDef stuff)
	{
		TechLevel techLevel = faction?.def?.techLevel ?? TechLevel.Industrial;
		ModContentPack modContentPack = faction?.def?.modContentPack;
		bool flag = modContentPack?.IsOfficialMod ?? true;
		if (tier >= 6 && (int)techLevel >= 5 && flag && AncientWall(map) != null)
		{
			wallDef = AncientWall(map);
			stuff = null;
			return;
		}
		stuff = ChooseMaterial(ThingDefOf.Wall, faction, modContentPack, techLevel, tier, out var _, 150f);
		if (stuff == null)
		{
			using IEnumerator<ThingDef> enumerator = GenStuff.AllowedStuffsFor(ThingDefOf.Wall, techLevel).GetEnumerator();
			if (enumerator.MoveNext())
			{
				ThingDef current = enumerator.Current;
				stuff = current;
			}
		}
		if (CeStructuresActive && tier >= 4 && CeReinforcedWall != null)
		{
			wallDef = CeReinforcedWall;
		}
		else
		{
			wallDef = ThingDefOf.Wall;
		}
	}

	private static ThingDef ChooseMaterial(ThingDef buildDef, Faction faction, ModContentPack factionMod, TechLevel tech, int tier, out bool isFactionOwn, float minOwnHp = 0f)
	{
		isFactionOwn = false;
		if (buildDef == null || !buildDef.MadeFromStuff)
		{
			return null;
		}
		if (factionMod != null && !factionMod.IsOfficialMod)
		{
			List<ThingDef> list = new List<ThingDef>();
			List<ThingDef> allDefsListForReading = DefDatabase<ThingDef>.AllDefsListForReading;
			for (int i = 0; i < allDefsListForReading.Count; i++)
			{
				ThingDef thingDef = allDefsListForReading[i];
				if (thingDef.IsStuff && thingDef.modContentPack == factionMod && (int)thingDef.techLevel <= (int)tech && CanMake(buildDef, thingDef))
				{
					list.Add(thingDef);
				}
			}
			if (minOwnHp > 0f)
			{
				list.RemoveAll((ThingDef s) => WallHp(buildDef, s) < minOwnHp);
			}
			if (list.Count > 0)
			{
				isFactionOwn = true;
				return PickByTier(buildDef, list, tier, faction, 7);
			}
		}
		List<ThingDef> list2 = new List<ThingDef>();
		foreach (ThingDef item in GenStuff.AllowedStuffsFor(buildDef, tech, checkAllowedInStuffGeneration: true))
		{
			if (item.stuffProps != null && (item.modContentPack == null || item.modContentPack.IsOfficialMod) && IsStandardWallStuff(item))
			{
				list2.Add(item);
			}
		}
		if (list2.Count == 0)
		{
			return null;
		}
		return PickByTier(buildDef, list2, tier, faction, 5);
	}

	private static bool IsStandardWallStuff(ThingDef s)
	{
		if (s != null)
		{
			return StandardWallStuffs.Contains(s.defName);
		}
		return false;
	}

	private static ThingDef PickByTier(ThingDef buildDef, List<ThingDef> pool, int tier, Faction faction, int tierForMax)
	{
		if (pool.Count == 1)
		{
			return pool[0];
		}
		List<ThingDef> list = pool.OrderBy((ThingDef s) => WallHp(buildDef, s)).ToList();
		int count = list.Count;
		int num = Mathf.RoundToInt(Mathf.Clamp01(((float)tier - 2f) / Mathf.Max(1f, (float)tierForMax - 2f)) * (float)(count - 1));
		Rand.PushState(Gen.HashCombineInt(faction?.loadID ?? 0, buildDef.shortHash));
		int num2 = Rand.RangeInclusive(-1, 1);
		Rand.PopState();
		int index = Mathf.Clamp(num + num2, 0, count - 1);
		return list[index];
	}

	private static float WallHp(ThingDef buildDef, ThingDef stuff)
	{
		if (stuff == null)
		{
			return buildDef.GetStatValueAbstract(StatDefOf.MaxHitPoints);
		}
		return buildDef.GetStatValueAbstract(StatDefOf.MaxHitPoints, stuff);
	}

	private static bool IsForeignStuff(ThingDef stuff, ModContentPack factionMod)
	{
		if (stuff == null)
		{
			return false;
		}
		ModContentPack modContentPack = stuff.modContentPack;
		if (modContentPack == null || modContentPack.IsOfficialMod)
		{
			return false;
		}
		return modContentPack != factionMod;
	}

	private static bool CanMake(ThingDef buildDef, ThingDef stuff)
	{
		if (stuff?.stuffProps?.categories == null || buildDef.stuffCategories == null)
		{
			return false;
		}
		foreach (StuffCategoryDef category in stuff.stuffProps.categories)
		{
			if (buildDef.stuffCategories.Contains(category))
			{
				return true;
			}
		}
		return false;
	}

	private static void CollectStructures(Map map, CellRect rect, Faction faction, List<Building> walls, List<Building> doors)
	{
		foreach (IntVec3 item2 in rect)
		{
			if (!item2.InBounds(map))
			{
				continue;
			}
			Building edifice = item2.GetEdifice(map);
			if (edifice != null && !(edifice.Position != item2) && (faction == null || edifice.Faction == null || edifice.Faction == faction))
			{
				if (edifice is Building_Door item)
				{
					doors.Add(item);
				}
				else if (IsWallLike(edifice))
				{
					walls.Add(edifice);
				}
			}
		}
	}

	private static bool IsWallLike(Building b)
	{
		ThingDef def = b.def;
		if (def == ThingDefOf.Wall)
		{
			return true;
		}
		if (def.building != null && def.passability == Traversability.Impassable && def.fillPercent >= 0.99f && !def.IsDoor)
		{
			if (def.building.isNaturalRock || def.mineable)
			{
				return false;
			}
			return true;
		}
		if (def.replaceTags != null && def.replaceTags.Contains("Wall"))
		{
			return true;
		}
		return false;
	}

	private static bool ReplaceBuilding(Map map, Building old, ThingDef newDef, ThingDef newStuff)
	{
		if (newDef == null)
		{
			return false;
		}
		IntVec3 position = old.Position;
		Rot4 rotation = old.Rotation;
		Faction faction = old.Faction;
		try
		{
			old.Destroy();
			Thing thing = ThingMaker.MakeThing(newDef, newDef.MadeFromStuff ? newStuff : null);
			if (faction != null)
			{
				thing.SetFactionDirect(faction);
			}
			GenSpawn.Spawn(thing, position, map, rotation);
			return true;
		}
		catch (Exception ex)
		{
			EESLog.Error($"ReplaceBuilding {newDef?.defName}/{newStuff?.defName} @ {position}: {ex.Message}");
			return false;
		}
	}

	private static CellRect GetSettlementRect(Map map)
	{
		if (!MapGenerator.TryGetVar<CellRect>("SettlementRect", out var var))
		{
			return CellRect.Empty;
		}
		return var;
	}

	private static ThingDef AncientWall(Map map)
	{
		object obj;
		if (map?.Tile.LayerDef?.isSpace != true)
		{
			obj = AncientWallDef;
			if (obj == null)
			{
				return OrbitalAncientWallDef;
			}
		}
		else
		{
			obj = OrbitalAncientWallDef ?? AncientWallDef;
		}
		return (ThingDef)obj;
	}

	private static void ResolveCeDefs()
	{
		if (!_ceResolved)
		{
			_ceWall = DefDatabase<ThingDef>.GetNamedSilentFail("CE_ReinforcedWall");
			_ceEmb = DefDatabase<ThingDef>.GetNamedSilentFail("CE_Embrasure");
			_ceREmb = DefDatabase<ThingDef>.GetNamedSilentFail("CE_ReinforcedEmbrasure");
			_ceDoor = DefDatabase<ThingDef>.GetNamedSilentFail("CE_ReinforcedDoor");
			_ceResolved = true;
			EESLog.Message("[CE구조물] 조회결과: CE_ReinforcedWall=" + ((_ceWall != null) ? "OK" : "없음(CE Armory 미로드?)") + ", CE_Embrasure=" + ((_ceEmb != null) ? "OK" : "없음") + ", CE_ReinforcedEmbrasure=" + ((_ceREmb != null) ? "OK" : "없음") + ", CE_ReinforcedDoor=" + ((_ceDoor != null) ? "OK" : "없음") + " | enableCEIntegration=" + (StrongholdMod.Settings?.enableCEIntegration.ToString() ?? "null"));
		}
	}

	public static ThingDef ResolveDoorDef(bool reinforced)
	{
		if (reinforced && CeStructuresActive && CeReinforcedDoor != null)
		{
			return CeReinforcedDoor;
		}
		return ThingDefOf.Door;
	}

	public static void GetAncientCoreDefs(Map map, out ThingDef ancientWall, out ThingDef blastDoor)
	{
		ancientWall = AncientWall(map);
		blastDoor = AncientBlastDoorDef;
	}
}
