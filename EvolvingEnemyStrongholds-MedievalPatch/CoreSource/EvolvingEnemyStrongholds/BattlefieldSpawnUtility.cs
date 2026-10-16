using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public static class BattlefieldSpawnUtility
{
	private const int NoAssaultDelay = 2000000000;

	public static int SpawnWave(Map map, Faction faction, float points, BattlefieldWave wave, CellRect baseRect, bool nearWall = false)
	{
		if (map == null || faction == null || points <= 0f)
		{
			return 0;
		}
		try
		{
			List<Pawn> list = PawnGroupMakerUtility.GeneratePawns(new PawnGroupMakerParms
			{
				groupKind = PawnGroupKindDefOf.Combat,
				tile = map.Tile,
				points = points,
				faction = faction
			}, warnOnZeroResults: false).ToList();
			if (list.Count == 0)
			{
				EESLog.Warning($"Battlefield wave [{faction.Name} {wave}]: 병력 생성 0(pts={points:0}).");
				return 0;
			}
			IntVec3 intVec = ComputeTarget(map, wave, baseRect, nearWall);
			bool flag = wave == BattlefieldWave.AssaultCenter && !nearWall;
			bool flag2 = !flag && (int)faction.def.techLevel >= 4;
			bool flag3;
			if (flag2)
			{
				List<List<Thing>> thingsGroups = list.Select((Pawn p) => new List<Thing> { p }).ToList();
				DropPodUtility.DropThingGroupsNear(intVec, map, thingsGroups, 110, instaDrop: false, leaveSlag: false, canRoofPunch: true, forbid: false, allowFogged: true, canTransfer: false, faction);
				flag3 = true;
			}
			else
			{
				flag3 = WalkIn(map, list, wave, baseRect, flag);
			}
			if (!flag3)
			{
				EESLog.Warning($"Battlefield wave [{faction.Name} {wave}]: 진입 셀 못 찾음.");
			}
			foreach (Pawn item in list)
			{
				if (item?.mindState != null)
				{
					item.mindState.canFleeIndividual = false;
				}
			}
			AssignLord(map, faction, list, wave, baseRect, intVec);
			EESLog.Message(string.Format("Battlefield wave [{0} {1}]: {2}명 (pts={3:0}, {4}, nearWall={5}) @ {6}.", faction.Name, wave, list.Count, points, flag2 ? "드랍" : "도보", nearWall, intVec));
			return list.Count;
		}
		catch (Exception arg)
		{
			EESLog.Error($"Battlefield SpawnWave [{faction?.Name} {wave}] 실패: {arg}");
			return 0;
		}
	}

	private static IntVec3 ComputeTarget(Map map, BattlefieldWave wave, CellRect baseRect, bool nearWall)
	{
		IntVec3 intVec = ((baseRect.Width > 0) ? baseRect.CenterCell : map.Center);
		switch (wave)
		{
		case BattlefieldWave.ReinforceCenter:
			return CellFinder.RandomClosewalkCellNear(intVec, map, 4);
		case BattlefieldWave.HoldOutside:
			return OutsidePerimeterCell(map, baseRect);
		default:
			if (!nearWall)
			{
				return OppositePlayerEdgeCell(map, intVec);
			}
			return OutsidePerimeterCell(map, baseRect);
		}
	}

	private static IntVec3 PlayerCentroid(Map map)
	{
		List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
		if (list == null || list.Count == 0)
		{
			return map.Center;
		}
		long num = 0L;
		long num2 = 0L;
		for (int i = 0; i < list.Count; i++)
		{
			num += list[i].Position.x;
			num2 += list[i].Position.z;
		}
		return new IntVec3((int)(num / list.Count), 0, (int)(num2 / list.Count));
	}

	private static IntVec3 OppositePlayerEdgeCell(Map map, IntVec3 center)
	{
		IntVec3 intVec = PlayerCentroid(map);
		Vector3 vector = (center - intVec).ToVector3();
		if (vector.sqrMagnitude < 0.01f)
		{
			vector = new Vector3(1f, 0f, 0f);
		}
		vector = vector.normalized;
		IntVec3 intVec2 = center + (vector * 1000f).ToIntVec3();
		IntVec3 intVec3 = new IntVec3(Mathf.Clamp(intVec2.x, 1, map.Size.x - 2), 0, Mathf.Clamp(intVec2.z, 1, map.Size.z - 2));
		if (CellFinder.TryFindRandomCellNear(intVec3, map, 12, (IntVec3 c) => c.Standable(map) && !c.Fogged(map) && map.reachability.CanReachMapEdge(c, TraverseParms.For(TraverseMode.PassDoors)), out var result))
		{
			return result;
		}
		return intVec3;
	}

	private static IntVec3 OutsidePerimeterCell(Map map, CellRect baseRect)
	{
		CellRect cellRect = ((baseRect.Width > 0) ? baseRect : CellRect.CenteredOn(map.Center, 20)).ExpandedBy(5).ClipInsideMap(map);
		List<IntVec3> list = cellRect.EdgeCells.ToList();
		for (int i = 0; i < 60; i++)
		{
			IntVec3 intVec = list[Rand.Range(0, list.Count)];
			if (intVec.InBounds(map) && intVec.Standable(map) && !intVec.Fogged(map) && (baseRect.Width <= 0 || !baseRect.Contains(intVec)))
			{
				return intVec;
			}
		}
		return cellRect.CenterCell;
	}

	private static bool WalkIn(Map map, List<Pawn> pawns, BattlefieldWave wave, CellRect baseRect, bool isInitialC)
	{
		IntVec3 result;
		IntVec3 root = (isInitialC ? FarFromPlayerEdgeCell(map) : (CellFinder.TryFindRandomEdgeCellWith((IntVec3 c) => c.Standable(map) && map.reachability.CanReachMapEdge(c, TraverseParms.For(TraverseMode.PassDoors)), map, CellFinder.EdgeRoadChance_Ignore, out result) ? result : CellFinder.RandomEdgeCell(map)));
		if (!root.IsValid)
		{
			return false;
		}
		foreach (Pawn pawn in pawns)
		{
			IntVec3 loc = CellFinder.RandomClosewalkCellNear(root, map, 8, (IntVec3 c) => c.Standable(map) && !c.Fogged(map));
			GenSpawn.Spawn(pawn, loc, map, Rot4.Random);
		}
		return true;
	}

	private static IntVec3 FarFromPlayerEdgeCell(Map map)
	{
		IntVec3 intVec = PlayerCentroid(map);
		IntVec3 result = IntVec3.Invalid;
		float num = -1f;
		for (int i = 0; i < 200; i++)
		{
			IntVec3 intVec2 = CellFinder.RandomEdgeCell(map);
			if (intVec2.Standable(map) && !intVec2.Fogged(map) && map.reachability.CanReachMapEdge(intVec2, TraverseParms.For(TraverseMode.PassDoors)))
			{
				float num2 = (intVec2 - intVec).LengthHorizontalSquared;
				if (num2 > num)
				{
					num = num2;
					result = intVec2;
				}
			}
		}
		if (!result.IsValid)
		{
			return CellFinder.RandomEdgeCell(map);
		}
		return result;
	}

	private static void AssignLord(Map map, Faction faction, List<Pawn> pawns, BattlefieldWave wave, CellRect baseRect, IntVec3 target)
	{
		List<Pawn> list = pawns.Where((Pawn p) => p != null && !p.Dead).ToList();
		if (list.Count != 0)
		{
			switch (wave)
			{
			case BattlefieldWave.AssaultCenter:
				LordMaker.MakeNewLord(faction, MakeAssaultCenter(map, faction, baseRect), map, list);
				break;
			case BattlefieldWave.HoldOutside:
				LordMaker.MakeNewLord(faction, new LordJob_DefendBase_NoFlee(faction, target, 2000000000), map, list);
				break;
			default:
				StrongholdGarrisonUtility.AssignDispersedDefense(map, faction, baseRect, list, 2000000000, noFlee: true);
				break;
			}
		}
	}

	private static LordJob MakeAssaultCenter(Map map, Faction attacker, CellRect baseRect)
	{
		IntVec3 center = ((baseRect.Width > 0) ? baseRect.CenterCell : map.Center);
		Faction b = map.ParentFaction;
		if (b != null)
		{
			List<Thing> list = (from x in map.listerBuildings.allBuildingsNonColonist
				where x != null && !x.Destroyed && x.Faction == b
				orderby x.Position.DistanceToSquared(center)
				select x).Take(25).Cast<Thing>().ToList();
			if (list.Count > 0)
			{
				return new LordJob_AssaultThings_NoExit(attacker, list);
			}
		}
		return new LordJob_AssaultColony_NoFlee(attacker);
	}
}
