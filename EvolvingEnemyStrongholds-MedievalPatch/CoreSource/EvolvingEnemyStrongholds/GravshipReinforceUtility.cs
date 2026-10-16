using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public static class GravshipReinforceUtility
{
	public const float MortarChance = 0.3f;

	public const int GravshipVisionRadius = 6;

	public static void HideBaseInterior(Map map, CellRect rect)
	{
		if (map == null || rect.Width < 5)
		{
			return;
		}
		HashSet<IntVec3> hashSet = new HashSet<IntVec3>();
		foreach (Pawn item in map.mapPawns.AllPawnsSpawned)
		{
			if (item == null || item.Faction != Faction.OfPlayer)
			{
				continue;
			}
			foreach (IntVec3 item2 in CellRect.CenteredOn(item.Position, 6))
			{
				hashSet.Add(item2);
			}
		}
		CellRect rect2 = rect.ContractedBy(1);
		map.fogGrid.Refog(rect2);
		int num = 0;
		foreach (IntVec3 item3 in rect2)
		{
			if (item3.InBounds(map) && map.fogGrid.IsFogged(item3))
			{
				TerrainDef terrain = item3.GetTerrain(map);
				if ((terrain != null && terrain.IsSubstructure) || hashSet.Contains(item3))
				{
					map.fogGrid.Unfog(item3);
					num++;
				}
			}
		}
		int num2 = 0;
		foreach (IntVec3 edgeCell in rect.EdgeCells)
		{
			if (edgeCell.InBounds(map) && map.fogGrid.IsFogged(edgeCell))
			{
				map.fogGrid.Unfog(edgeCell);
				num2++;
			}
		}
		EESLog.Message($"Gravship hide-interior [{map.Parent?.LabelCap}]: 내부 {rect2.Width}x{rect2.Height} 재안개, " + $"외벽노출 {num2}칸, 함선+폰시야 {num}칸 노출 (외벽은 보이고 내부만 은폐).");
	}

	public static int RevealWholeMap(Map map)
	{
		if (map == null)
		{
			return 0;
		}
		int num = 0;
		foreach (IntVec3 allCell in map.AllCells)
		{
			if (map.fogGrid.IsFogged(allCell))
			{
				num++;
			}
		}
		map.fogGrid.ClearAllFog();
		return num;
	}

	public static bool IsGravshipAttack(Map map)
	{
		if (!ModsConfig.OdysseyActive || map == null)
		{
			return false;
		}
		try
		{
			return GravshipUtility.GetPlayerGravEngine_NewTemp(map) != null;
		}
		catch (Exception ex)
		{
			EESLog.Warning("IsGravshipAttack failed: " + ex.Message);
			return false;
		}
	}

	public static void SpawnResponseWave(Map map, Settlement settlement, Faction faction, StrongholdData data)
	{
		try
		{
			float num = StrongholdMod.Settings?.reinforceSizeFraction ?? 0.25f;
			int num2 = StrongholdMod.Settings?.reinforceMaxPoints ?? 2000;
			float num3 = Mathf.Clamp((float)data.militaryPower * num, 150f, num2);
			EESLog.Message($"Gravship reinforcement {settlement?.LabelCap}: 스폰 시작 (points={num3:0} mil={data.militaryPower:0} frac={num:0.00}).");
			IncidentParms parms = new IncidentParms
			{
				target = map,
				faction = faction,
				points = num3
			};
			List<Pawn> list = FinalStandUtility.LimitReinforcementPawns(map, faction, data,
				PawnGroupMakerUtility.GeneratePawns(IncidentParmsUtility.GetDefaultPawnGroupMakerParms(PawnGroupKindDefOf.Combat, parms, ensureCanGenerateAtLeastOnePawn: true)));
			if (list.Count == 0)
			{
				EESLog.Warning($"Gravship reinforcement {settlement?.LabelCap}: GeneratePawns=0 → 증원 없음(points={num3:0}).");
				return;
			}
			StrongholdFeatureEffects.ApplyToPawns(map, faction, data, list);
			TechLevel techLevel = faction.def?.techLevel ?? TechLevel.Industrial;
			bool flag = (int)techLevel >= 4;
			bool flag2 = flag && Rand.Chance(0.3f);
			EESLog.Message($"Gravship reinforcement {settlement?.LabelCap}: pawns={list.Count} tech={techLevel} " + string.Format("canDrop={0} mortar={1} → 경로={2}.", flag, flag2, flag ? "DROP" : "EDGE"));
			if (flag)
			{
				float gdist;
				IntVec3 intVec = FindDistantDropCenter(map, out gdist);
				if (flag2)
				{
					LordMaker.MakeNewLord(faction, new LordJob_Siege(faction, intVec, num3 * 0.4f), map, list);
				}
				else
				{
					LordMaker.MakeNewLord(faction, new LordJob_AssaultColony_NoFlee(faction, canKidnap: false, canTimeoutOrFlee: false, sappers: false), map, list);
				}
				DropPodUtility.DropThingsNear(intVec, map, list.Cast<Thing>(), 110, canInstaDropDuringInit: false, leaveSlag: false, canRoofPunch: true, forbid: false);
				Messages.Message(EESText.T("EES.Reinf.Wave", list.Count), new TargetInfo(intVec, map), MessageTypeDefOf.ThreatBig, historical: false);
				EESLog.Message($"Gravship reinforcement {settlement.LabelCap}: {list.Count} pawns " + string.Format("via DROP{0} @ {1} (중력부양선과 {2:0}칸, points={3:0}).", flag2 ? "+MORTAR-siege" : "", intVec, gdist, num3));
				return;
			}
			IntVec3 intVec2 = IntVec3.Invalid;
			List<Pawn> list2 = new List<Pawn>();
			foreach (Pawn item in list)
			{
				if (!RCellFinder.TryFindRandomPawnEntryCell(out var result, map, CellFinder.EdgeRoadChance_Hostile))
				{
					StrongholdGarrisonUtility.DiscardUnspawnedPawn(item);
					continue;
				}
				GenSpawn.Spawn(item, result, map, Rot4.Random);
				list2.Add(item);
				if (!intVec2.IsValid)
				{
					intVec2 = result;
				}
			}
			if (list2.Count == 0)
			{
				EESLog.Warning("Gravship reinforcement " + settlement?.LabelCap + ": 엣지 진입셀 0 → 증원 없음.");
				return;
			}
			LordMaker.MakeNewLord(faction, new LordJob_AssaultColony_NoFlee(faction, canKidnap: false, canTimeoutOrFlee: false, sappers: false), map, list2);
			Messages.Message(EESText.T("EES.Reinf.Wave", list2.Count), new TargetInfo(intVec2, map), MessageTypeDefOf.ThreatBig, historical: false);
			EESLog.Message($"Gravship reinforcement {settlement.LabelCap}: {list2.Count} pawns via edge @ {intVec2} (points={num3:0}).");
		}
		catch (Exception arg)
		{
			EESLog.Error($"Gravship reinforcement {settlement?.LabelCap}: {arg}");
		}
	}

	private static IntVec3 GravEnginePos(Map map)
	{
		try
		{
			return GravshipUtility.GetPlayerGravEngine_NewTemp(map)?.Position ?? IntVec3.Invalid;
		}
		catch
		{
			return IntVec3.Invalid;
		}
	}

	private static IntVec3 FindDistantDropCenter(Map map, out float gdist)
	{
		IntVec3 intVec = GravEnginePos(map);
		IntVec3 intVec2 = DropCellFinder.FindRaidDropCenterDistant(map);
		if (!intVec.IsValid)
		{
			gdist = -1f;
			return intVec2;
		}
		float num = (intVec2 - intVec).LengthHorizontalSquared;
		for (int i = 0; i < 25; i++)
		{
			if (!(num < 2025f))
			{
				break;
			}
			IntVec3 intVec3 = DropCellFinder.FindRaidDropCenterDistant(map);
			float num2 = (intVec3 - intVec).LengthHorizontalSquared;
			if (num2 > num)
			{
				intVec2 = intVec3;
				num = num2;
			}
		}
		gdist = Mathf.Sqrt(num);
		return intVec2;
	}

	public static void PatchPerimeter(Map map, StrongholdData data, CellRect baseRect)
	{
		Faction faction = map?.ParentFaction;
		if (faction == null || data == null || data.tier < 8 || baseRect.Width < 5)
		{
			return;
		}
		StrongholdDefenseUtility.ResolveWall(map, faction, data.ContentTier, out var wallDef, out var stuff);
		if (wallDef == null)
		{
			return;
		}
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		foreach (IntVec3 edgeCell in baseRect.EdgeCells)
		{
			if (!edgeCell.InBounds(map))
			{
				continue;
			}
			if (edgeCell.GetEdifice(map) != null)
			{
				num3++;
				continue;
			}
			if (edgeCell.GetFirstPawn(map) != null)
			{
				num4++;
				continue;
			}
			TerrainDef terrain = edgeCell.GetTerrain(map);
			if (terrain != null && terrain.IsSubstructure)
			{
				num2++;
				continue;
			}
			try
			{
				Thing thing = ThingMaker.MakeThing(wallDef, wallDef.MadeFromStuff ? stuff : null);
				thing.SetFactionDirect(faction);
				GenSpawn.Spawn(thing, edgeCell, map);
				num++;
			}
			catch
			{
			}
		}
		int num5 = ((num2 > 0) ? BendWallAroundSubstructure(map, faction, baseRect, wallDef, stuff) : 0);
		EESLog.Message($"Gravship perimeter patch [{faction.Name} T{data.tier}]: ring +{num} wall(s) + 함선우회 +{num5} wall(s). " + $"보존 skip(edifice={num3}, pawn={num4}, 하부구조={num2}=함선 착륙구간).");
	}

	private static int BendWallAroundSubstructure(Map map, Faction faction, CellRect baseRect, ThingDef wallDef, ThingDef stuff)
	{
		int num = 0;
		foreach (IntVec3 item in baseRect)
		{
			if (!item.InBounds(map))
			{
				continue;
			}
			TerrainDef terrain = item.GetTerrain(map);
			if ((terrain != null && terrain.IsSubstructure) || item.GetEdifice(map) != null || item.GetFirstPawn(map) != null)
			{
				continue;
			}
			bool flag = false;
			for (int i = 0; i < 4; i++)
			{
				IntVec3 c = item + GenAdj.CardinalDirections[i];
				if (c.InBounds(map))
				{
					TerrainDef terrain2 = c.GetTerrain(map);
					if (terrain2 != null && terrain2.IsSubstructure)
					{
						flag = true;
						break;
					}
				}
			}
			if (flag)
			{
				try
				{
					Thing thing = ThingMaker.MakeThing(wallDef, wallDef.MadeFromStuff ? stuff : null);
					thing.SetFactionDirect(faction);
					GenSpawn.Spawn(thing, item, map);
					num++;
				}
				catch
				{
				}
			}
		}
		return num;
	}
}
