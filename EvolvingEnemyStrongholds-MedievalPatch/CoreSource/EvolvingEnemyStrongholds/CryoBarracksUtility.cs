using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class CryoBarracksUtility
{
	public const int MinDisplayTier = 8;

	public const float VisibleFraction = 0.65f;

	public const float ReserveFraction = 0.35f;

	private const int MaxCaskets = 12;

	private const int ReserveSeed = 792327;

	private const float GarrisonLossWakeFrac = 0.6f;

	private static ThingDef CasketDef => DefDatabase<ThingDef>.GetNamedSilentFail("CryptosleepCasket");

	public static bool IsCryo(string role)
	{
		return role == "CryoBarracks";
	}

	public static void FurnishCryoBarracks(Map map, Faction faction, StrongholdData data, CellRect room)
	{
		ThingDef casketDef = CasketDef;
		if (casketDef == null)
		{
			EESLog.Warning("CryoBarracks: CryptosleepCasket def 없음 — 캐스킷 미배치(예비대는 가시 폴백).");
			return;
		}
		MapComponent_StrongholdSiege component = map.GetComponent<MapComponent_StrongholdSiege>();
		if (component != null)
		{
			CellRect cellRect = room.ExpandedBy(1).ClipInsideMap(map);
			component.cryoRoomMinX = cellRect.minX;
			component.cryoRoomMinZ = cellRect.minZ;
			component.cryoRoomW = cellRect.Width;
			component.cryoRoomH = cellRect.Height;
		}
		int num = 0;
		for (int i = room.minZ; i + 1 <= room.maxZ; i += 3)
		{
			if (num >= 12)
			{
				break;
			}
			for (int j = room.minX; j <= room.maxX; j += 2)
			{
				if (num >= 12)
				{
					break;
				}
				IntVec3 intVec = new IntVec3(j, 0, i);
				if (CasketSpotFree(map, room, intVec))
				{
					try
					{
						Thing thing = ThingMaker.MakeThing(casketDef);
						thing.SetFactionDirect(faction);
						GenSpawn.Spawn(thing, intVec, map, Rot4.South);
						num++;
					}
					catch (Exception ex)
					{
						EESLog.Warning($"CryoBarracks: 캐스킷 배치 실패 @ {intVec}: {ex.Message}");
					}
				}
			}
		}
		EESLog.Message($"CryoBarracks furnish [{faction?.Name}]: 캐스킷 {num}기 배치 (room {room.Width}x{room.Height} @ {room.CenterCell}).");
	}

	private static bool CasketSpotFree(Map map, CellRect room, IntVec3 cell)
	{
		foreach (IntVec3 item in GenAdj.OccupiedRect(cell, Rot4.South, new IntVec2(1, 2)))
		{
			if (!item.InBounds(map) || !room.Contains(item))
			{
				return false;
			}
			if (!item.Standable(map) || item.GetEdifice(map) != null || item.GetFirstBuilding(map) != null)
			{
				return false;
			}
		}
		return true;
	}

	public static int FillReserve(Map map, Faction faction, StrongholdData data, CellRect rect)
	{
		if (map == null || faction?.def == null || data == null)
		{
			return 0;
		}
		if (!IsCryo(data.roleDefName) || !faction.def.humanlikeFaction || data.militaryPower <= 0)
		{
			return 0;
		}
		MapComponent_StrongholdSiege component = map.GetComponent<MapComponent_StrongholdSiege>();
		if (component == null)
		{
			return 0;
		}
		float num = (SupplyCaravanUtility.SupplyCutCombatActive(data) ? 0.75f : 1f);
		float num2 = (float)data.militaryPower * 0.35f * num;
		if (num2 < 100f)
		{
			return 0;
		}
		PawnGroupMakerParms parms = new PawnGroupMakerParms
		{
			tile = map.Tile,
			faction = faction,
			groupKind = PawnGroupKindDefOf.Combat,
			points = num2,
			inhabitants = true,
			seed = Gen.HashCombineInt(data.settlementId, 792327)
		};
		List<Pawn> list;
		try
		{
			list = new List<Pawn>(PawnGroupMakerUtility.GeneratePawns(parms, warnOnZeroResults: false));
		}
		catch (Exception ex)
		{
			EESLog.Error("CryoBarracks GeneratePawns failed: " + ex.Message);
			return 0;
		}
		if (list.Count == 0)
		{
			return 0;
		}
		List<Building_CryptosleepCasket> list2 = CollectEmptyCaskets(map, faction);
		int num3 = 0;
		List<Pawn> list3 = new List<Pawn>();
		for (int i = 0; i < list.Count; i++)
		{
			Pawn pawn = list[i];
			if (!StrongholdGarrisonUtility.TryConsumePopulationSlot(special: true))
			{
				StrongholdGarrisonUtility.DiscardUnspawnedPawn(pawn);
				continue;
			}
			StrongholdGarrisonUtility.GiveSiegeRations(pawn);
			if (num3 < list2.Count && list2[num3].TryAcceptThing(pawn, allowSpecialEffects: false))
			{
				component.cryoCaskets.Add(list2[num3]);
				num3++;
			}
			else
			{
				list3.Add(pawn);
			}
		}
		int num4 = 0;
		List<Pawn> list4 = new List<Pawn>();
		for (int j = 0; j < list3.Count; j++)
		{
			if (StrongholdGarrisonUtility.TryFindSpawnCell(map, rect, out var cell))
			{
				GenSpawn.Spawn(list3[j], cell, map);
				list4.Add(list3[j]);
				num4++;
			}
			else
			{
				StrongholdGarrisonUtility.ReleasePopulationSlot();
				StrongholdGarrisonUtility.DiscardUnspawnedPawn(list3[j]);
			}
		}
		if (list4.Count > 0)
		{
			StrongholdGarrisonUtility.AssignDispersedDefense(map, faction, rect, list4);
		}
		if (num3 > 0)
		{
			component.cryoArmed = true;
			component.cryoWoken = false;
			component.cryoEdgeBaseline = CountRoomEdges(component, map);
			component.cryoInitialGarrison = CountFactionHumanlike(map, faction);
		}
		EESLog.Message($"CryoBarracks reserve [{faction.Name} T{data.tier}]: 예산 {num2:F0} → {list.Count}명 생성, " + $"캐스킷 수용 {num3}/{list2.Count}기, 가시 폴백 {num4}명. armed={component.cryoArmed} " + $"(edgeBaseline={component.cryoEdgeBaseline}, initGarrison={component.cryoInitialGarrison}).");
		return num3;
	}

	private static List<Building_CryptosleepCasket> CollectEmptyCaskets(Map map, Faction faction)
	{
		List<Building_CryptosleepCasket> list = new List<Building_CryptosleepCasket>();
		ThingDef casketDef = CasketDef;
		if (casketDef == null)
		{
			return list;
		}
		List<Thing> list2 = map.listerThings.ThingsOfDef(casketDef);
		for (int i = 0; i < list2.Count; i++)
		{
			if (list2[i] is Building_CryptosleepCasket building_CryptosleepCasket && building_CryptosleepCasket.Faction == faction && !building_CryptosleepCasket.HasAnyContents)
			{
				list.Add(building_CryptosleepCasket);
			}
		}
		return list;
	}

	public static void TickCryo(MapComponent_StrongholdSiege siege, Map map)
	{
		if (siege == null || map == null || !siege.cryoArmed || siege.cryoWoken || !(map.Parent is Settlement { Faction: var faction }) || !EESHostility.SystemsActive(map, faction))
		{
			return;
		}
		string text = null;
		for (int i = 0; i < siege.cryoCaskets.Count; i++)
		{
			Thing thing = siege.cryoCaskets[i];
			if (thing == null || thing.Destroyed)
			{
				text = "캐스킷 파괴";
				break;
			}
			if (thing.HitPoints < thing.MaxHitPoints)
			{
				text = "캐스킷 피격";
				break;
			}
			if (thing is Building_CryptosleepCasket { HasAnyContents: false })
			{
				text = "캐스킷 개방";
				break;
			}
		}
		if (text == null && siege.cryoEdgeBaseline > 0)
		{
			int num = CountRoomEdges(siege, map);
			if (num >= 0 && num < siege.cryoEdgeBaseline)
			{
				text = "병영 벽 파괴";
			}
		}
		if (text == null && siege.cryoInitialGarrison > 0 && (map.mapPawns.FreeColonistsSpawnedCount > 0 || EESHostility.AnyEnemyOf(map, faction)) && CountFactionHumanlike(map, faction) < Mathf.CeilToInt((float)siege.cryoInitialGarrison * 0.6f))
		{
			text = "수비대 손실";
		}
		if (text != null)
		{
			WakeReserve(siege, map, faction, text);
		}
	}

	public static void WakeReserve(MapComponent_StrongholdSiege siege, Map map, Faction faction, string reason)
	{
		siege.cryoWoken = true;
		List<Pawn> list = new List<Pawn>();
		for (int i = 0; i < siege.cryoCaskets.Count; i++)
		{
			if (!(siege.cryoCaskets[i] is Building_CryptosleepCasket { Destroyed: false } building_CryptosleepCasket))
			{
				continue;
			}
			try
			{
				ThingOwner searchableContents = building_CryptosleepCasket.SearchableContents;
				List<Pawn> list2 = new List<Pawn>();
				for (int j = 0; j < searchableContents.Count; j++)
				{
					if (searchableContents[j] is Pawn item)
					{
						list2.Add(item);
					}
				}
				building_CryptosleepCasket.EjectContents();
				for (int k = 0; k < list2.Count; k++)
				{
					if (list2[k].Spawned && !list2[k].Dead)
					{
						list.Add(list2[k]);
					}
				}
			}
			catch (Exception ex)
			{
				EESLog.Warning($"CryoBarracks: 캐스킷 개방 실패 @ {siege.cryoCaskets[i].Position}: {ex.Message}");
			}
		}
		if (list.Count > 0)
		{
			CellRect rect = ((siege.eesBaseRect.Width > 0) ? siege.eesBaseRect : CellRect.CenteredOn(map.Center, 20));
			StrongholdGarrisonUtility.AssignDispersedDefense(map, faction, rect, list);
			CECompat.EnsureGarrisonAmmo(map, faction);
			Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_CryoWake"), EESText.T("EES.Cryo.WakeText", faction.Name), LetterDefOf.ThreatBig, new TargetInfo(list[0].Position, map));
		}
		EESLog.Message($"CryoBarracks WAKE [{faction.Name}]: 사유={reason}, 각성 {list.Count}명 (캐스킷 {siege.cryoCaskets.Count}기).");
	}

	private static int CountRoomEdges(MapComponent_StrongholdSiege siege, Map map)
	{
		if (siege.cryoRoomW <= 0 || siege.cryoRoomH <= 0)
		{
			return -1;
		}
		CellRect cellRect = new CellRect(siege.cryoRoomMinX, siege.cryoRoomMinZ, siege.cryoRoomW, siege.cryoRoomH);
		int num = 0;
		foreach (IntVec3 edgeCell in cellRect.EdgeCells)
		{
			if (edgeCell.InBounds(map) && edgeCell.GetEdifice(map) != null)
			{
				num++;
			}
		}
		return num;
	}

	private static int CountFactionHumanlike(Map map, Faction faction)
	{
		int num = 0;
		List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(faction);
		for (int i = 0; i < list.Count; i++)
		{
			Pawn pawn = list[i];
			if (pawn != null && !pawn.Dead && pawn.RaceProps != null && pawn.RaceProps.Humanlike)
			{
				num++;
			}
		}
		return num;
	}
}
