using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class StrongholdReachability
{
	private const int MaxRepairPasses = 8;

	private const int MaxWallDepth = 6;

	private const int CoreWallDepthUnlimited = int.MaxValue;

	private const int CoreCarveWidth = 3;

	private const int TinySkipMaxCells = 4;

	private static readonly IntVec3[] Dirs = new IntVec3[4]
	{
		IntVec3.North,
		IntVec3.South,
		IntVec3.East,
		IntVec3.West
	};

	public static bool PassableForConn(Map map, IntVec3 c, Pawn probe = null)
	{
		if (!c.InBounds(map))
		{
			return false;
		}
		Building edifice = c.GetEdifice(map);
		if (edifice == null || edifice.Destroyed)
		{
			return c.Walkable(map);
		}
		if (edifice.def.IsDoor)
		{
			if (probe == null)
			{
				return true;
			}
			if (!(edifice is Building_Door building_Door))
			{
				return true;
			}
			return building_Door.PawnCanOpen(probe);
		}
		return edifice.def.passability != Traversability.Impassable;
	}

	private static Pawn PickDoorProbe(Map map, Faction faction)
	{
		if (map?.mapPawns == null || faction == null)
		{
			return null;
		}
		List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(faction);
		for (int i = 0; i < list.Count; i++)
		{
			Pawn pawn = list[i];
			if (pawn != null && !pawn.Dead && !pawn.IsPrisoner && pawn.RaceProps.Humanlike && pawn.CanOpenDoors)
			{
				return pawn;
			}
		}
		return null;
	}

	private static bool TryFindLockedDoor(Map map, List<IntVec3> comp, List<CellRect> sealedRects, out Building found, out int ownSkipped)
	{
		found = null;
		ownSkipped = 0;
		HashSet<Thing> hashSet = new HashSet<Thing>();
		for (int i = 0; i < comp.Count; i++)
		{
			for (int j = 0; j < 4; j++)
			{
				IntVec3 c = comp[i] + Dirs[j];
				if (!c.InBounds(map))
				{
					continue;
				}
				Building edifice = c.GetEdifice(map);
				if (edifice != null && !edifice.Destroyed && (edifice is Building_HackableDoor || edifice is Building_JammedDoor) && hashSet.Add(edifice))
				{
					if (IsProtected(sealedRects, c))
					{
						ownSkipped++;
					}
					else if (found == null)
					{
						found = edifice;
					}
				}
			}
		}
		return found != null;
	}

	private static bool IsTinyGap(Map map, List<IntVec3> comp)
	{
		if (comp == null || comp.Count > 4)
		{
			return false;
		}
		Describe(map, comp, out var _, out var _, out var doorCells, out var pawns, out var buildings);
		if (pawns == 0 && buildings == 0)
		{
			return doorCells == 0;
		}
		return false;
	}

	public static (int blocked, int doors) EnsureReachable(Map map, StrongholdData data, CellRect area, string tag, bool repair = true)
	{
		if (map == null || area.Width <= 0)
		{
			return (blocked: 0, doors: 0);
		}
		area = area.ClipInsideMap(map);
		Faction parentFaction = map.ParentFaction;
		StrongholdDefenseUtility.ResolveWall(map, parentFaction, data?.ContentTier ?? 1, out var _, out var stuff);
		ThingDef thingDef = StrongholdDefenseUtility.ResolveDoorDef(data != null && data.tier >= 9) ?? ThingDefOf.Door;
		ThingDef doorStuff = null;
		if (thingDef.MadeFromStuff)
		{
			doorStuff = ((stuff != null && stuff.stuffProps != null) ? stuff : ThingDefOf.Steel);
		}
		TerrainDef namedSilentFail = DefDatabase<TerrainDef>.GetNamedSilentFail("Concrete");
		List<CellRect> list = SealedRects(data);
		Pawn pawn = PickDoorProbe(map, parentFaction);
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int num6 = 0;
		int num7 = 0;
		int num8 = 0;
		List<List<IntVec3>> list2 = new List<List<IntVec3>>();
		CellRect coreRect = ((area.Width > 4 && area.Height > 4) ? area.ContractedBy(2) : area);
		int ownSkipped2;
		for (num3 = 1; num3 <= 8; num3++)
		{
			HashSet<IntVec3> hashSet = FloodFromMapEdge(map, pawn);
			num4 = hashSet.Count;
			if (hashSet.Count == 0)
			{
				EESLog.Error("Reachability [" + tag + "]: 맵 가장자리 통행 시드 0개 — 검사 불가 " + $"(맵 {map.Size.x}x{map.Size.z}, 검사 {area.Width}x{area.Height} @{area.CenterCell}).");
				return (blocked: 0, doors: num);
			}
			list2 = FindBlockedComponents(map, area, hashSet, pawn);
			if (list2.Count == 0 || !repair)
			{
				break;
			}
			bool flag = false;
			for (int i = 0; i < list2.Count; i++)
			{
				List<IntVec3> list3 = list2[i];
				if (IsSealedByDesign(list3, list) || !HasArtificialBoundary(map, list3))
				{
					continue;
				}
				if (TryFindLockedDoor(map, list3, list, out var found, out var ownSkipped))
				{
					num5++;
					num6 += ownSkipped;
					Describe(map, list3, out var bounds, out var wallCells, out var doorCells, out var pawns, out var buildings);
					EESLog.Message($"Reachability SKIP-LOCKED [{tag}]: 미도달 {list3.Count}칸 @ {bounds.CenterCell} " + $"bounds ({bounds.minX},{bounds.minZ})~({bounds.maxX},{bounds.maxZ}) 경계벽 {wallCells}칸 · 문 {doorCells}개 · " + $"폰 {pawns} · 건물 {buildings} — 잠긴 문 {found.def.defName} @ {found.Position} 이 " + $"경계라 수리하지 않는다(고대 구조물 보존 · 우리 금고문 제외 {ownSkipped}개).");
					continue;
				}
				num6 += ownSkipped;
				Describe(map, list3, out var bounds2, out var wallCells2, out var doorCells2, out var pawns2, out var buildings2);
				if (IsTinyGap(map, list3))
				{
					num8++;
					EESLog.Message($"Reachability SKIP-TINY [{tag}]: 미도달 {list3.Count}칸 @ {bounds2.CenterCell} " + $"bounds ({bounds2.minX},{bounds2.minZ})~({bounds2.maxX},{bounds2.maxZ}) " + $"경계벽 {wallCells2}칸 · 문 0개 · 폰 0 · 건물 0 — " + $"자투리 틈(임계 {4}칸)이라 문을 달지 않는다.");
					continue;
				}
				bool flag2 = IsCoreComp(list3, coreRect);
				if (flag2)
				{
					num7++;
				}
				if (TryRepair(map, parentFaction, list3, hashSet, list, thingDef, doorStuff, namedSilentFail, flag2 ? int.MaxValue : 6, (!flag2) ? 1 : 3, out var fixCell, out var depth, out var carvedCells, out var placedDoor))
				{
					num += (placedDoor ? 1 : 0);
					num2 += carvedCells;
					flag = true;
					EESLog.Message($"Reachability FIX [{tag}]: 미도달 {list3.Count}칸 @ {bounds2.CenterCell} " + $"bounds ({bounds2.minX},{bounds2.minZ})~({bounds2.maxX},{bounds2.maxZ}) " + $"경계벽 {wallCells2}칸 · 문 {doorCells2}개 · 폰 {pawns2} · 건물 {buildings2} → " + string.Format("{0} 에 {1} ", fixCell, placedDoor ? (thingDef.defName + " 설치") : "자연 암반 관통") + $"(벽 두께 {depth}칸, 실제 파낸 칸 {carvedCells}칸, 통로 폭 {((!flag2) ? 1 : 3)}칸" + (flag2 ? " ·★기지 일대(예산 무제한)" : "") + ").");
					continue;
				}
				string[] obj = new string[6]
				{
					$"Reachability RETRY [{tag}]: 미도달 {list3.Count}칸 @ {bounds2.CenterCell} ",
					$"bounds ({bounds2.minX},{bounds2.minZ})~({bounds2.maxX},{bounds2.maxZ}) ",
					$"경계벽 {wallCells2}칸 · 문 {doorCells2}개 · 폰 {pawns2} · 건물 {buildings2} — ",
					$"이번 pass({num3})엔 ",
					null,
					null
				};
				object obj2;
				if (!flag2)
				{
					ownSkipped2 = 6;
					obj2 = "두께 " + ownSkipped2 + "칸 안에";
				}
				else
				{
					obj2 = "예산 무제한인데도(기지 일대)";
				}
				obj[4] = (string)obj2;
				obj[5] = " 도달 영역이 없다(다음 pass 에서 재시도).";
				EESLog.Message(string.Concat(obj));
			}
			if (!flag)
			{
				break;
			}
		}
		int num9 = 0;
		int num10 = 0;
		int num11 = 0;
		int num12 = 0;
		int num13 = 0;
		for (int j = 0; j < list2.Count; j++)
		{
			if (IsSealedByDesign(list2[j], list))
			{
				num9++;
				continue;
			}
			if (!HasArtificialBoundary(map, list2[j]))
			{
				num10++;
				continue;
			}
			if (TryFindLockedDoor(map, list2[j], list, out var _, out ownSkipped2))
			{
				num12++;
				continue;
			}
			if (IsTinyGap(map, list2[j]))
			{
				num13++;
				continue;
			}
			num11++;
			if (repair)
			{
				Describe(map, list2[j], out var bounds3, out var wallCells3, out var doorCells3, out var pawns3, out var buildings3);
				bool flag3 = IsCoreComp(list2[j], coreRect);
				string[] obj3 = new string[6]
				{
					$"Reachability FAIL [{tag}]: 미도달 {list2[j].Count}칸 @ {bounds3.CenterCell} ",
					$"bounds ({bounds3.minX},{bounds3.minZ})~({bounds3.maxX},{bounds3.maxZ}) ",
					$"경계벽 {wallCells3}칸 · 문 {doorCells3}개 · 폰 {pawns3} · 건물 {buildings3} — ",
					$"pass {num3}/{8} 를 다 돌고도 ",
					null,
					null
				};
				object obj4;
				if (!flag3)
				{
					ownSkipped2 = 6;
					obj4 = "두께 " + ownSkipped2 + "칸 안에";
				}
				else
				{
					obj4 = "★기지 일대인데 예산 무제한으로도";
				}
				obj3[4] = (string)obj4;
				obj3[5] = " 도달 영역이 없어 수리 못 함(그대로 둔다).";
				EESLog.Error(string.Concat(obj3));
			}
		}
		EESLog.Message($"Reachability [{tag}]: 검사 {area.Width}x{area.Height} @{area.CenterCell} · " + $"외부 도달 {num4}칸(맵 가장자리 기준) · 미도달 성분 {list2.Count}개" + $"(봉인 {num9} · 천연 공동 {num10} · 잠긴고대문 {num12} · " + $"자투리 틈 {num13}(≤{4}칸·폰0·건물0·문0) · 미해결 {num11}) · " + $"문 {num}개 설치(자투리라 안 단 것 {num8}건) · " + $"관통 {num2}칸(기지 일대 성분 {num7}건=폭 {3}칸·예산 무제한) · " + $"잠긴문 skip {num5}회" + $"(★우리 금고문이라 제외 {num6}개) · " + $"pass {num3}/{8} · repair={repair} · " + $"봉인등록 {list.Count}개 · " + "문판정=" + ((pawn != null) ? ("PawnCanOpen(" + pawn.LabelShort + ")") : "★대표폰 없음→문=통행 폴백") + ".");
		return (blocked: list2.Count, doors: num);
	}

	public static HashSet<IntVec3> FloodFromMapEdge(Map map, Pawn probe = null)
	{
		HashSet<IntVec3> hashSet = new HashSet<IntVec3>();
		Queue<IntVec3> queue = new Queue<IntVec3>();
		foreach (IntVec3 edgeCell in CellRect.WholeMap(map).EdgeCells)
		{
			if (PassableForConn(map, edgeCell, probe) && hashSet.Add(edgeCell))
			{
				queue.Enqueue(edgeCell);
			}
		}
		while (queue.Count > 0)
		{
			IntVec3 intVec = queue.Dequeue();
			for (int i = 0; i < 4; i++)
			{
				IntVec3 intVec2 = intVec + Dirs[i];
				if (intVec2.InBounds(map) && !hashSet.Contains(intVec2) && PassableForConn(map, intVec2, probe))
				{
					hashSet.Add(intVec2);
					queue.Enqueue(intVec2);
				}
			}
		}
		return hashSet;
	}

	private static List<List<IntVec3>> FindBlockedComponents(Map map, CellRect area, HashSet<IntVec3> reach, Pawn probe)
	{
		List<List<IntVec3>> list = new List<List<IntVec3>>();
		HashSet<IntVec3> hashSet = new HashSet<IntVec3>();
		foreach (IntVec3 item in area)
		{
			if (!PassableForConn(map, item, probe) || reach.Contains(item) || hashSet.Contains(item))
			{
				continue;
			}
			List<IntVec3> list2 = new List<IntVec3>();
			Queue<IntVec3> queue = new Queue<IntVec3>();
			queue.Enqueue(item);
			hashSet.Add(item);
			while (queue.Count > 0)
			{
				IntVec3 intVec = queue.Dequeue();
				list2.Add(intVec);
				for (int i = 0; i < 4; i++)
				{
					IntVec3 intVec2 = intVec + Dirs[i];
					if (intVec2.InBounds(map) && !hashSet.Contains(intVec2) && PassableForConn(map, intVec2, probe))
					{
						hashSet.Add(intVec2);
						queue.Enqueue(intVec2);
					}
				}
			}
			list.Add(list2);
		}
		return list;
	}

	private static List<CellRect> SealedRects(StrongholdData data)
	{
		List<CellRect> list = new List<CellRect>();
		if (data == null)
		{
			return list;
		}
		if (data.sealedRects == null)
		{
			return list;
		}
		for (int i = 0; i < data.sealedRects.Count; i++)
		{
			if (data.sealedRects[i].Width > 0)
			{
				list.Add(data.sealedRects[i]);
			}
		}
		return list;
	}

	private static bool IsCoreComp(List<IntVec3> comp, CellRect coreRect)
	{
		if (coreRect.Width <= 0)
		{
			return false;
		}
		for (int i = 0; i < comp.Count; i++)
		{
			if (coreRect.Contains(comp[i]))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsSealedByDesign(List<IntVec3> comp, List<CellRect> sealedRects)
	{
		if (sealedRects.Count == 0)
		{
			return false;
		}
		for (int i = 0; i < comp.Count; i++)
		{
			bool flag = false;
			for (int j = 0; j < sealedRects.Count; j++)
			{
				if (sealedRects[j].Contains(comp[i]))
				{
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				return false;
			}
		}
		return true;
	}

	private static bool HasArtificialBoundary(Map map, List<IntVec3> comp)
	{
		for (int i = 0; i < comp.Count; i++)
		{
			for (int j = 0; j < 4; j++)
			{
				IntVec3 c = comp[i] + Dirs[j];
				if (c.InBounds(map))
				{
					Building edifice = c.GetEdifice(map);
					if (edifice != null && !edifice.Destroyed && edifice.def.passability == Traversability.Impassable && !IsNaturalRock(edifice))
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	private static bool IsNaturalRock(Building ed)
	{
		if (ed.def.mineable)
		{
			return ed.Faction == null;
		}
		return false;
	}

	private static void Describe(Map map, List<IntVec3> comp, out CellRect bounds, out int wallCells, out int doorCells, out int pawns, out int buildings)
	{
		int num = int.MaxValue;
		int num2 = int.MaxValue;
		int num3 = int.MinValue;
		int num4 = int.MinValue;
		HashSet<IntVec3> hashSet = new HashSet<IntVec3>();
		HashSet<Thing> hashSet2 = new HashSet<Thing>();
		HashSet<Thing> hashSet3 = new HashSet<Thing>();
		pawns = 0;
		for (int i = 0; i < comp.Count; i++)
		{
			IntVec3 intVec = comp[i];
			if (intVec.x < num)
			{
				num = intVec.x;
			}
			if (intVec.z < num2)
			{
				num2 = intVec.z;
			}
			if (intVec.x > num3)
			{
				num3 = intVec.x;
			}
			if (intVec.z > num4)
			{
				num4 = intVec.z;
			}
			List<Thing> thingList = intVec.GetThingList(map);
			for (int j = 0; j < thingList.Count; j++)
			{
				if (thingList[j] is Pawn)
				{
					pawns++;
				}
				else if (thingList[j] is Building building && !building.def.IsDoor)
				{
					hashSet3.Add(building);
				}
			}
			for (int k = 0; k < 4; k++)
			{
				IntVec3 intVec2 = intVec + Dirs[k];
				if (!intVec2.InBounds(map))
				{
					continue;
				}
				Building edifice = intVec2.GetEdifice(map);
				if (edifice != null && !edifice.Destroyed)
				{
					if (edifice.def.IsDoor)
					{
						hashSet2.Add(edifice);
					}
					else if (edifice.def.passability == Traversability.Impassable)
					{
						hashSet.Add(intVec2);
					}
				}
			}
		}
		bounds = CellRect.FromLimits(num, num2, num3, num4);
		wallCells = hashSet.Count;
		doorCells = hashSet2.Count;
		buildings = hashSet3.Count;
	}

	private static bool TryRepair(Map map, Faction faction, List<IntVec3> comp, HashSet<IntVec3> reach, List<CellRect> sealedRects, ThingDef doorDef, ThingDef doorStuff, TerrainDef floor, int maxWallDepth, int carveWidth, out IntVec3 fixCell, out int depth, out int carvedCells, out bool placedDoor)
	{
		fixCell = IntVec3.Invalid;
		depth = 0;
		carvedCells = 0;
		placedDoor = false;
		HashSet<IntVec3> hashSet = new HashSet<IntVec3>(comp);
		Dictionary<IntVec3, IntVec3> dictionary = new Dictionary<IntVec3, IntVec3>();
		Dictionary<IntVec3, int> dictionary2 = new Dictionary<IntVec3, int>();
		LinkedList<IntVec3> linkedList = new LinkedList<IntVec3>();
		for (int i = 0; i < comp.Count; i++)
		{
			for (int j = 0; j < 4; j++)
			{
				IntVec3 intVec = comp[i] + Dirs[j];
				if (intVec.InBounds(map) && !hashSet.Contains(intVec) && !dictionary2.ContainsKey(intVec) && IsCarvableWall(map, intVec) && !IsProtected(sealedRects, intVec))
				{
					dictionary2[intVec] = 1;
					dictionary[intVec] = IntVec3.Invalid;
					linkedList.AddLast(intVec);
				}
			}
		}
		IntVec3 intVec2 = IntVec3.Invalid;
		while (linkedList.Count > 0 && !intVec2.IsValid)
		{
			IntVec3 value = linkedList.First.Value;
			linkedList.RemoveFirst();
			int num = dictionary2[value];
			for (int k = 0; k < 4; k++)
			{
				IntVec3 intVec3 = value + Dirs[k];
				if (!intVec3.InBounds(map) || hashSet.Contains(intVec3))
				{
					continue;
				}
				if (reach.Contains(intVec3))
				{
					intVec2 = value;
					break;
				}
				if (dictionary2.ContainsKey(intVec3) || IsProtected(sealedRects, intVec3))
				{
					continue;
				}
				if (IsCarvableWall(map, intVec3))
				{
					if (num < maxWallDepth)
					{
						dictionary2[intVec3] = num + 1;
						dictionary[intVec3] = value;
						linkedList.AddLast(intVec3);
					}
					continue;
				}
				Building edifice = intVec3.GetEdifice(map);
				if ((edifice == null || edifice.Destroyed || !edifice.def.IsDoor) && PassableForConn(map, intVec3))
				{
					dictionary2[intVec3] = num;
					dictionary[intVec3] = value;
					linkedList.AddFirst(intVec3);
				}
			}
		}
		if (!intVec2.IsValid)
		{
			return false;
		}
		List<IntVec3> list = new List<IntVec3>();
		IntVec3 intVec4 = intVec2;
		while (intVec4.IsValid)
		{
			list.Add(intVec4);
			intVec4 = (dictionary.TryGetValue(intVec4, out var value2) ? value2 : IntVec3.Invalid);
		}
		depth = (dictionary2.TryGetValue(intVec2, out var value3) ? value3 : list.Count);
		fixCell = list[list.Count - 1];
		Building edifice2 = fixCell.GetEdifice(map);
		bool flag = edifice2 != null && IsNaturalRock(edifice2);
		IntVec3 intVec5 = IntVec3.Invalid;
		for (int l = 0; l < 4; l++)
		{
			if (hashSet.Contains(fixCell + Dirs[l]))
			{
				intVec5 = new IntVec3(-Dirs[l].x, 0, -Dirs[l].z);
				break;
			}
		}
		for (int m = 0; m < list.Count; m++)
		{
			if (m == list.Count - 1 && !flag)
			{
				continue;
			}
			Building edifice3 = list[m].GetEdifice(map);
			bool flag2 = edifice3 != null && !edifice3.Destroyed && IsNaturalRock(edifice3);
			if (edifice3 != null && IsCarvableWall(map, list[m]))
			{
				carvedCells++;
				ClearEdificeAt(map, list[m]);
				if (floor != null && list[m].InBounds(map))
				{
					map.terrainGrid.SetTerrain(list[m], floor);
				}
				if (flag2 && carveWidth > 1)
				{
					IntVec3 dir = ((m + 1 < list.Count) ? (list[m + 1] - list[m]) : ((m > 0) ? (list[m] - list[m - 1]) : intVec5));
					carvedCells += WidenAt(map, list[m], dir, carveWidth, sealedRects, floor);
				}
			}
		}
		if (!flag)
		{
			ClearEdificeAt(map, fixCell);
			PlaceDoor(map, faction, doorDef, doorStuff, fixCell);
			placedDoor = true;
		}
		return true;
	}

	private static int WidenAt(Map map, IntVec3 at, IntVec3 dir, int width, List<CellRect> sealedRects, TerrainDef floor)
	{
		if (!dir.IsValid || (dir.x == 0 && dir.z == 0))
		{
			return 0;
		}
		IntVec3 intVec = new IntVec3(-dir.z, 0, dir.x);
		int num = (width - 1) / 2;
		int num2 = 0;
		for (int i = 1; i <= num; i++)
		{
			for (int j = -1; j <= 1; j += 2)
			{
				IntVec3 c = new IntVec3(at.x + intVec.x * i * j, 0, at.z + intVec.z * i * j);
				if (!c.InBounds(map) || IsProtected(sealedRects, c))
				{
					continue;
				}
				Building edifice = c.GetEdifice(map);
				if (edifice != null && !edifice.Destroyed && IsNaturalRock(edifice) && IsCarvableWall(map, c))
				{
					ClearEdificeAt(map, c);
					if (floor != null)
					{
						map.terrainGrid.SetTerrain(c, floor);
					}
					num2++;
				}
			}
		}
		return num2;
	}

	private static bool IsCarvableWall(Map map, IntVec3 c)
	{
		Building edifice = c.GetEdifice(map);
		if (edifice == null || edifice.Destroyed)
		{
			return false;
		}
		if (edifice.def.IsDoor)
		{
			return false;
		}
		if (edifice.def.EverTransmitsPower)
		{
			return false;
		}
		// Connectivity repair may replace our new walls, never a native layout or
		// an unrelated blocking building. Existing maps are diagnostic-only here.
		if (MapGenerator.mapBeingGenerated != map || map?.Parent is not RimWorld.Planet.Settlement settlement)
		{
			return false;
		}
		var data = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
		var plan = data?.generationPlan;
		if (plan == null || plan.legacyMap || !StrongholdCenterPlanner.Eligible(map, data) ||
			settlement.Faction?.HostileTo(Faction.OfPlayer) != true ||
			!edifice.questTags.NullOrEmpty() ||
			plan.nativeDefenseIds == null || plan.nativeDefenseIds.Contains(edifice.thingIDNumber))
		{
			return false;
		}
		// Natural rock has no faction and is not a constructed wall. Keep the
		// existing rock-tunnel repair usable during new hostile generation;
		// resource deposits, explicitly owned rock and tagged objects stay intact.
		if (IsNaturalRock(edifice))
		{
			return edifice.Faction == null && edifice.def.building?.isNaturalRock == true &&
				edifice.def.building.isResourceRock != true &&
				edifice.def.passability == Traversability.Impassable;
		}
		if (edifice.Faction != settlement.Faction || !StrongholdCenterPlanner.StructuralWall(edifice.def))
		{
			return false;
		}
		return edifice.def.passability == Traversability.Impassable;
	}

	private static bool IsProtected(List<CellRect> sealedRects, IntVec3 c)
	{
		for (int i = 0; i < sealedRects.Count; i++)
		{
			if (sealedRects[i].ExpandedBy(1).Contains(c))
			{
				return true;
			}
		}
		return false;
	}

	private static void ClearEdificeAt(Map map, IntVec3 c)
	{
		if (c.InBounds(map))
		{
			Building edifice = c.GetEdifice(map);
			if (edifice != null && IsCarvableWall(map, c))
			{
				edifice.Destroy();
			}
		}
	}

	private static void PlaceDoor(Map map, Faction faction, ThingDef doorDef, ThingDef doorStuff, IntVec3 c)
	{
		if (doorDef == null || !c.InBounds(map))
		{
			return;
		}
		try
		{
			ThingDef stuff = (doorDef.MadeFromStuff ? (doorStuff ?? GenStuff.DefaultStuffFor(doorDef)) : null);
			Thing thing = ThingMaker.MakeThing(doorDef, stuff);
			if (faction != null)
			{
				thing.SetFactionDirect(faction);
			}
			GenSpawn.Spawn(thing, c, map);
			return;
		}
		catch
		{
		}
		try
		{
			Thing thing2 = ThingMaker.MakeThing(doorDef, doorDef.MadeFromStuff ? GenStuff.DefaultStuffFor(doorDef) : null);
			if (faction != null)
			{
				thing2.SetFactionDirect(faction);
			}
			GenSpawn.Spawn(thing2, c, map);
		}
		catch (Exception ex)
		{
			EESLog.Warning($"Reachability: 문 배치 실패 {doorDef.defName} @ {c}: {ex.Message}");
		}
	}
}
