using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static partial class StrongholdLayoutGenerator
{
	private struct SpinePlan
	{
		public CellRect plan;

		public CellRect vault;

		public CellRect corridor;

		public IntVec3 gate;

		public List<CellRect> slots;

		public int rows;

		public int roomH;

		public bool tight;

		public bool stacked;
	}

	private const int PlanWidth = 21;

	private const int SlotWidth = 9;

	private const int CorridorMinX = 9;

	private const int CorridorWidth = 3;

	private const int RightSlotMinX = 12;

	private const int MaxPlanRows = 3;

	private const int RoomHeightFull = 9;

	private const int RoomHeightTight = 7;

	private const int StackedCorridorHeight = 3;

	private const int MinWallStuffVotes = 8;

	private const int GateApronDepth = 2;

	private const int GateCarveMax = 16;

	public static void InsertCustomCenter(Map map, StrongholdData data, CellRect footprint)
	{
		Faction faction = map?.ParentFaction;
		if (map == null || faction == null || data == null)
		{
			return;
		}
		try
		{
			int votes;
			int scanned;
			bool matched;
			TerrainDef terrainDef = SampleBaseFloor(map, footprint, out votes, out scanned, out matched) ?? DefDatabase<TerrainDef>.GetNamedSilentFail("Concrete");
			StrongholdDefenseUtility.ResolveWall(map, faction, data.ContentTier, out var wallDef, out var stuff);
			if (wallDef == null)
			{
				wallDef = ThingDefOf.Wall;
				stuff = ThingDefOf.BlocksGranite;
			}
			int votes2;
			int scanned2;
			ThingDef thingDef = SampleBaseWallStuff(map, faction, footprint, out votes2, out scanned2);
			bool flag = false;
			if (thingDef != null && thingDef.stuffProps != null && wallDef.MadeFromStuff)
			{
				stuff = thingDef;
				flag = true;
			}
			ThingDef thingDef2 = StrongholdDefenseUtility.ResolveDoorDef(data.tier >= 9);
			ThingDef thingDef3 = StuffOrDefault(thingDef2, stuff);
			if (stuff == null && thingDef2.MadeFromStuff)
			{
				thingDef3 = ThingDefOf.Steel;
			}
			bool elec = faction.def != null && (int)faction.def.techLevel >= 4;
			string role = data.roleDefName;
			ClearChunks(map, footprint);
			data.ClearSealedRects();
			List<(string, Action<CellRect>, bool, bool)> list = new List<(string, Action<CellRect>, bool, bool)>();
			if (data.hasCommander)
			{
				list.Add(("지휘실", delegate(CellRect r)
				{
					FurnishCommand(map, faction, data, r);
				}, true, false));
			}
			bool flag2 = RoleHas(role, "prison") || HasLivePrisoner(data);
			if (flag2)
			{
				list.Add(("포로 수용소", delegate(CellRect r)
				{
					FurnishPrison(map, faction, data, r);
				}, true, true));
			}
			Action<CellRect> action = null;
			string item = "역할방";
			if (RoleHas(role, "mine"))
			{
				item = "탐색기실";
				action = delegate(CellRect r)
				{
					FurnishScanner(map, faction, data, r, footprint);
				};
			}
			else if (RoleHas(role, "noble") || RoleHas(role, "estate"))
			{
				item = "귀족방";
				action = delegate(CellRect r)
				{
					FurnishNoble(map, faction, data, r);
				};
			}
			else if (RoleHas(role, "research"))
			{
				item = "연구실";
				action = delegate(CellRect r)
				{
					FurnishLab(map, faction, data, r);
				};
			}
			else if (RoleHas(role, "blackmarket") || RoleHas(role, "market"))
			{
				item = "휴게실";
				action = delegate(CellRect r)
				{
					FurnishRec(map, faction, data, r);
				};
			}
			else if (RoleHas(role, "supply"))
			{
				item = "보급창고";
				action = delegate(CellRect r)
				{
					FurnishStoreroom(map, faction, data, r, role);
				};
			}
			else if (RoleHas(role, "cryo"))
			{
				item = "동면 병영";
				action = delegate(CellRect r)
				{
					CryoBarracksUtility.FurnishCryoBarracks(map, faction, data, r);
				};
			}
			else if (RoleHas(role, "shrine"))
			{
				item = "주술 제단";
				action = delegate(CellRect r)
				{
					ThreatEmitterUtility.FurnishShrine(map, faction, data, r);
				};
			}
			if (action != null)
			{
				list.Add((item, action, true, false));
			}
			if (data.hasAmmoDepot)
			{
				list.Add(("탄약 보급고", delegate(CellRect r)
				{
					FurnishStoreroom(map, faction, data, r, "AmmoDepot");
				}, true, false));
			}
			if (data.hasMortarBattery)
			{
				list.Add(("박격포탄실", delegate(CellRect r)
				{
					FurnishMortarShellRoom(map, faction, data, r);
				}, true, false));
			}
			int num = Mathf.Clamp(4 + data.ContentTier / 5, 4, 5);
			SpinePlan plan = BuildSpinePlan(map, footprint, list.Count, num * 2 + 1);
			if (plan.slots.Count < list.Count)
			{
				EESLog.Error($"Layout [{faction.Name}]: 슬롯 부족 — 요구 {list.Count}개, 평면도 {plan.slots.Count}개 " + $"(rows={plan.rows}). 초과 방 미배치.");
			}
			HashSet<IntVec3> hashSet = new HashSet<IntVec3>();
			HashSet<IntVec3> hashSet2 = new HashSet<IntVec3>();
			HashSet<IntVec3> hashSet3 = new HashSet<IntVec3>();
			RecordRingWalls(map, plan.vault, hashSet, hashSet2);
			RecordEdgeDoors(map, plan.vault, hashSet3);
			for (int num2 = 0; num2 < plan.slots.Count; num2++)
			{
				RecordRingWalls(map, plan.slots[num2], hashSet, hashSet2);
				RecordEdgeDoors(map, plan.slots[num2], hashSet3);
			}
			List<IntVec3> list2 = new List<IntVec3>();
			List<(IntVec3, CellRect)> list3 = new List<(IntVec3, CellRect)>();
			if (plan.rows > 0)
			{
				List<CellRect> list4 = new List<CellRect> { plan.vault };
				for (int num3 = 0; num3 < plan.slots.Count; num3++)
				{
					list4.Add(plan.slots[num3]);
				}
				RecordRingWalls(map, plan.corridor, hashSet, hashSet2, list4, list2);
			}
			int count = hashSet.Count;
			int count2 = hashSet2.Count;
			int count3 = hashSet3.Count;
			DemolishBuildings(map, plan.vault.ExpandedBy(1), keepConduits: true);
			for (int num4 = 0; num4 < plan.slots.Count; num4++)
			{
				DemolishBuildings(map, plan.slots[num4].ExpandedBy(1), keepConduits: true);
			}
			if (plan.rows > 0)
			{
				DemolishBuildings(map, plan.corridor.ExpandedBy(1), keepConduits: true);
			}
			int num5 = LayFloorCounted(map, plan.vault.ExpandedBy(1), terrainDef);
			for (int num6 = 0; num6 < plan.slots.Count; num6++)
			{
				num5 += LayFloorCounted(map, plan.slots[num6].ExpandedBy(1), terrainDef);
			}
			if (plan.rows > 0)
			{
				num5 += LayFloorCounted(map, plan.corridor.ExpandedBy(1), terrainDef);
			}
			for (int num7 = 0; num7 < plan.slots.Count && num7 < list.Count; num7++)
			{
				CellRect cellRect = plan.slots[num7];
				(string, Action<CellRect>, bool, bool) tuple = list[num7];
				IntVec3 intVec = (tuple.Item4 ? IntVec3.Invalid : CorridorDoorCell(plan, cellRect));
				if (intVec.IsValid)
				{
					list3.Add((intVec, cellRect));
				}
				BuildEnclosedRoom(map, faction, cellRect, intVec, wallDef, stuff, thingDef2, thingDef3, terrainDef, tuple.Item3, tuple.Item4 ? null : hashSet3);
				if (tuple.Item4)
				{
					data.RegisterSealedRect(cellRect, tuple.Item1 ?? "역할방");
				}
				tuple.Item2(cellRect.ContractedBy(1));
				if (tuple.Item3)
				{
					LightRoom(map, faction, cellRect, elec);
				}
				int num8 = 0;
				foreach (IntVec3 edgeCell in cellRect.EdgeCells)
				{
					if (edgeCell.InBounds(map) && edgeCell.GetEdifice(map) == null)
					{
						num8++;
					}
				}
				EESLog.Message($"Custom room built [{faction.Name}]: {tuple.Item1} rect {cellRect.Width}x{cellRect.Height} @ {cellRect.CenterCell} " + string.Format("door={0} roof={1} openEdges={2}.", intVec.IsValid ? intVec.ToString() : "없음(봉인)", tuple.Item3, num8));
			}
			IntVec3 intVec2 = new IntVec3(plan.vault.CenterCell.x, 0, plan.vault.minZ);
			list3.Add((intVec2, plan.vault));
			BuildEnclosedRoom(map, faction, plan.vault, intVec2, wallDef, stuff, thingDef2, thingDef3, terrainDef, roof: true);
			ThickRoofVault(map, plan.vault);
			FurnishVault(map, faction, data, plan.vault.ContractedBy(1), elec);
			SecureVault(map, faction, data, plan.vault.ContractedBy(1));
			LightRoom(map, faction, plan.vault, elec);
			if (plan.rows > 0 && !plan.stacked)
			{
				for (int num9 = plan.corridor.minX; num9 <= plan.corridor.maxX; num9++)
				{
					IntVec3 intVec3 = new IntVec3(num9, 0, plan.corridor.minZ);
					if (intVec3.InBounds(map))
					{
						if (intVec3 == plan.gate)
						{
							Place(map, faction, thingDef2, thingDef3, intVec3);
						}
						else
						{
							Place(map, faction, wallDef, stuff, intVec3);
						}
					}
				}
			}
			else if (plan.stacked)
			{
				for (int num10 = plan.corridor.minZ; num10 <= plan.corridor.maxZ; num10++)
				{
					for (int num11 = 0; num11 < 2; num11++)
					{
						IntVec3 intVec4 = new IntVec3((num11 == 0) ? plan.corridor.minX : plan.corridor.maxX, 0, num10);
						if (intVec4.InBounds(map) && intVec4.GetEdifice(map) == null)
						{
							if (intVec4 == plan.gate)
							{
								Place(map, faction, thingDef2, thingDef3, intVec4);
							}
							else
							{
								Place(map, faction, wallDef, stuff, intVec4);
							}
						}
					}
				}
			}
			List<IntVec3> list5 = new List<IntVec3>();
			string skipDetail;
			int doorsRestored;
			string doorSkipDetail;
			int num12 = SealCutWalls(map, faction, plan, hashSet, hashSet2, wallDef, stuff, thingDef2, thingDef3, out skipDetail, out doorsRestored, out doorSkipDetail, list5);
			int num13 = 0;
			if (plan.gate.IsValid)
			{
				IntVec3 intVec5 = (plan.stacked ? new IntVec3(-1, 0, 0) : new IntVec3(0, 0, -1));
				for (int num14 = 1; num14 <= 16; num14++)
				{
					IntVec3 c = plan.gate + new IntVec3(intVec5.x * num14, 0, intVec5.z * num14);
					if (!c.InBounds(map) || (c.Walkable(map) && c.GetEdifice(map) == null))
					{
						break;
					}
					ClearEdifice(map, c);
					num13++;
				}
			}
			int num15 = 0;
			foreach (IntVec3 item2 in hashSet3)
			{
				if (item2.InBounds(map) && !(item2.GetEdifice(map) is Building_Door))
				{
					num15++;
				}
			}
			int num16 = 0;
			int num17 = 0;
			List<string> list6 = new List<string>();
			foreach (var (intVec6, cellRect2) in list3)
			{
				if (!intVec6.InBounds(map))
				{
					continue;
				}
				IntVec3 c2 = IntVec3.Invalid;
				IntVec3 c3 = IntVec3.Invalid;
				for (int num18 = 0; num18 < 4; num18++)
				{
					IntVec3 intVec7 = intVec6 + num18 switch
					{
						2 => IntVec3.East,
						1 => IntVec3.South,
						0 => IntVec3.North,
						_ => IntVec3.West,
					};
					if (intVec7.InBounds(map))
					{
						if (cellRect2.ContractedBy(1).Contains(intVec7))
						{
							c2 = intVec7;
						}
						else if (!cellRect2.Contains(intVec7))
						{
							c3 = intVec7;
						}
					}
				}
				bool flag3 = c2.IsValid && c2.GetEdifice(map) != null;
				bool flag4 = c3.IsValid && c3.GetEdifice(map) != null;
				if (flag3)
				{
					num16++;
				}
				if (flag4)
				{
					num17++;
				}
				if (flag3 || flag4)
				{
					list6.Add(string.Format("({0},{1}){2}{3}", intVec6.x, intVec6.z, flag3 ? "안" : "", flag4 ? "밖" : ""));
				}
			}
			int num19 = plan.rows * 2 - plan.slots.Count;
			int num20 = plan.vault.minX;
			int num21 = plan.vault.maxX;
			if (plan.rows > 0)
			{
				num20 = Mathf.Min(num20, plan.corridor.minX);
				num21 = Mathf.Max(num21, plan.corridor.maxX);
			}
			for (int num22 = 0; num22 < plan.slots.Count; num22++)
			{
				num20 = Mathf.Min(num20, plan.slots[num22].minX);
				num21 = Mathf.Max(num21, plan.slots[num22].maxX);
			}
			bool flag5 = true;
			if (plan.rows > 0)
			{
				flag5 = plan.vault.CenterCell.x >= plan.corridor.minX && plan.vault.CenterCell.x <= plan.corridor.maxX;
				for (int num23 = 0; num23 < plan.slots.Count && num23 < list.Count; num23++)
				{
					if (list[num23].Item4)
					{
						continue;
					}
					IntVec3 intVec8 = CorridorDoorCell(plan, plan.slots[num23]);
					bool num24;
					if (!plan.stacked)
					{
						if (intVec8.z >= plan.corridor.minZ)
						{
							num24 = intVec8.z <= plan.corridor.maxZ;
							goto IL_1100;
						}
					}
					else if (intVec8.x >= plan.corridor.minX)
					{
						num24 = intVec8.x <= plan.corridor.maxX;
						goto IL_1100;
					}
					goto IL_1102;
					IL_1102:
					flag5 = false;
					continue;
					IL_1100:
					if (num24)
					{
						continue;
					}
					goto IL_1102;
				}
			}
			EESLog.Message($"Layout spine [{faction.Name} T{data.tier} role={role}]: " + string.Format("구성={0} 평면도 {1}x{2} ", plan.stacked ? "세로1열(슬롯1)" : "2열", plan.plan.Width, plan.plan.Height) + $"@ {plan.plan.CenterCell} 점유x({num20},{num21}) 폭={num21 - num20 + 1} " + $"rows={plan.rows} 슬롯={plan.slots.Count}/{plan.rows * 2}(미사용 {num19}) " + $"방깊이={plan.roomH} 금고 {plan.vault.Width}x{plan.vault.Height}@{plan.vault.CenterCell} " + "복도 " + ((plan.rows > 0) ? (plan.corridor.Width + "x" + plan.corridor.Height) : "없음") + " 게이트=" + ((plan.rows > 0) ? plan.gate.ToString() : "없음") + " " + $"문정렬OK={flag5} tight={plan.tight} " + "| 벽=" + wallDef.defName + "/" + ((stuff != null) ? stuff.defName : "비-stuff") + "(" + (flag ? $"기지 실측 {votes2}/{scanned2}" : $"기지 미채택 표본{scanned2}—종전값") + ") " + string.Format("바닥={0}({1}) {2}칸 ", (terrainDef != null) ? terrainDef.defName : "없음", matched ? $"기지 실측 {votes}/{scanned}" : $"기지 미채택 표본{scanned}—Concrete", num5) + $"벽선이음 {num12}/{count}칸[{skipDetail}]" + ((list5.Count > 0) ? (" 세운곳@" + string.Join(",", list5.ConvertAll((IntVec3 intVec9) => $"({intVec9.x},{intVec9.z})").ToArray())) : "") + " " + $"복도링 방안쪽 제외 {list2.Count}칸" + ((list2.Count > 0) ? ("@" + string.Join(",", list2.ConvertAll((IntVec3 intVec9) => $"({intVec9.x},{intVec9.z})").ToArray())) : "") + " " + $"문 {list3.Count}개(안막힘 {num16} 밖막힘 {num17}" + ((list6.Count > 0) ? (" @" + string.Join(",", list6.ToArray())) : "") + ") " + $"게이트앞 파냄 {num13}칸(앞마당 {2}칸 보호) " + $"문복원 링 {doorsRestored}/{count2}[{doorSkipDetail}] 방벽 {count3 - num15}/{count3}(봉인방 위 유실 {num15}) " + $"(cmd={data.hasCommander},prison={flag2},ammo={data.hasAmmoDepot},mortar={data.hasMortarBattery}).");
		}
		catch (Exception ex)
		{
			EESLog.Error("InsertCustomCenter failed: " + ex);
		}
	}

	private static IntVec3 CorridorDoorCell(SpinePlan plan, CellRect slot)
	{
		if (plan.stacked)
		{
			return new IntVec3(slot.CenterCell.x, 0, slot.maxZ);
		}
		return new IntVec3((slot.minX < plan.corridor.minX) ? slot.maxX : slot.minX, 0, slot.CenterCell.z);
	}

	private static SpinePlan BuildSpinePlan(Map map, CellRect footprint, int slotCount, int vSize)
	{
		int num = Mathf.Clamp(Mathf.CeilToInt((float)slotCount / 2f), 0, 3);
		int[] array = new int[2] { 9, 7 };
		SpinePlan result = default(SpinePlan);
		if (slotCount == 1)
		{
			for (int i = 0; i < array.Length; i++)
			{
				SpinePlan result2 = ComposeStacked(footprint, array[i], vSize);
				CellRect cellRect = result2.plan.ExpandedBy(1);
				if (cellRect.FullyContainedWithin(footprint.ContractedBy(2)) && cellRect.FullyContainedWithin(CellRect.WholeMap(map).ContractedBy(2)))
				{
					return result2;
				}
			}
		}
		for (int j = 0; j < array.Length; j++)
		{
			SpinePlan spinePlan = ComposeSpine(footprint, slotCount, num, array[j], vSize);
			result = spinePlan;
			CellRect cellRect2 = spinePlan.plan.ExpandedBy(1);
			if (cellRect2.FullyContainedWithin(footprint.ContractedBy(2)) && cellRect2.FullyContainedWithin(CellRect.WholeMap(map).ContractedBy(2)))
			{
				return spinePlan;
			}
		}
		result.tight = true;
		EESLog.Warning($"Layout spine: 부지 부족 — 평면도 {result.plan.Width}x{result.plan.Height} 가 " + $"기지 {footprint.Width}x{footprint.Height} 에 여유를 두고 들어가지 않는다(축소 모듈로 강행). " + $"slots={slotCount} rows={num}.");
		return result;
	}

	private static SpinePlan ComposeSpine(CellRect footprint, int slotCount, int rows, int roomH, int vSize)
	{
		int num = roomH - 1;
		int num2 = rows * num + vSize;
		int num3 = footprint.CenterCell.x - 10;
		int num4 = footprint.CenterCell.z - num2 / 2;
		SpinePlan result = new SpinePlan
		{
			rows = rows,
			roomH = roomH,
			plan = new CellRect(num3, num4, 21, num2),
			vault = new CellRect(num3 + 10 - vSize / 2, num4 + rows * num, vSize, vSize),
			corridor = new CellRect(num3 + 9, num4, 3, rows * num + 1),
			gate = new IntVec3(num3 + 10, 0, num4),
			slots = new List<CellRect>()
		};
		int num5 = Mathf.Min(slotCount, rows * 2);
		for (int i = 0; i < num5; i++)
		{
			int num6 = rows - 1 - i / 2;
			int num7 = i % 2;
			result.slots.Add(new CellRect(num3 + ((num7 != 0) ? 12 : 0), num4 + num6 * num, 9, roomH));
		}
		return result;
	}

	private static SpinePlan ComposeStacked(CellRect footprint, int roomH, int vSize)
	{
		int num = Mathf.Max(9, vSize);
		int num2 = vSize + 2 + (roomH - 1);
		int x = footprint.CenterCell.x;
		int z = footprint.CenterCell.z;
		int num3 = x - num / 2;
		int num4 = z - num2 / 2;
		return new SpinePlan
		{
			stacked = true,
			rows = 1,
			roomH = roomH,
			plan = new CellRect(num3, num4, num, num2),
			slots = new List<CellRect>
			{
				new CellRect(x - 4, num4, 9, roomH)
			},
			corridor = new CellRect(num3, num4 + roomH - 1, num, 3),
			vault = new CellRect(x - vSize / 2, num4 + roomH - 1 + 3 - 1, vSize, vSize),
			gate = new IntVec3(num3, 0, num4 + roomH - 1 + 1)
		};
	}

	private static void ClearChunks(Map map, CellRect rect)
	{
		List<Thing> list = new List<Thing>();
		foreach (IntVec3 item in rect)
		{
			if (!item.InBounds(map))
			{
				continue;
			}
			List<Thing> thingList = item.GetThingList(map);
			for (int i = 0; i < thingList.Count; i++)
			{
				Thing thing = thingList[i];
				if (IsChunk(thing.def) || (thing is Building building && building.def.mineable))
				{
					list.Add(thing);
				}
			}
		}
		foreach (Thing item2 in list)
		{
			if (item2 != null && !item2.Destroyed)
			{
				item2.Destroy();
			}
		}
	}

	private static void LightRoom(Map map, Faction faction, CellRect room, bool elec)
	{
		if (elec)
		{
			ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("WallLamp");
			if (namedSilentFail != null && TryPlaceWallLamp(map, faction, room, namedSilentFail))
			{
				return;
			}
		}
		CellRect cellRect = room.ContractedBy(1);
		IntVec3 c = IntVec3.Invalid;
		foreach (IntVec3 cell in cellRect.Cells)
		{
			if (cell.InBounds(map) && cell.Standable(map) && cell.GetFirstBuilding(map) == null)
			{
				c = cell;
				break;
			}
		}
		if (c.IsValid)
		{
			ThingDef namedSilentFail2 = DefDatabase<ThingDef>.GetNamedSilentFail("TorchLamp");
			if (namedSilentFail2 != null)
			{
				Place(map, faction, namedSilentFail2, null, c);
			}
		}
	}

	private static bool TryPlaceWallLamp(Map map, Faction faction, CellRect room, ThingDef lamp)
	{
		IntVec3 centerCell = room.CenterCell;
		foreach (IntVec3 edgeCell in room.EdgeCells)
		{
			if (!edgeCell.InBounds(map))
			{
				continue;
			}
			Building edifice = edgeCell.GetEdifice(map);
			if (edifice == null || edifice.def == null || !edifice.def.IsWall)
			{
				continue;
			}
			IntVec3 intVec = centerCell - edgeCell;
			Rot4 rot = ((Mathf.Abs(intVec.x) < Mathf.Abs(intVec.z)) ? ((intVec.z >= 0) ? Rot4.North : Rot4.South) : ((intVec.x >= 0) ? Rot4.East : Rot4.West));
			IntVec3 c = edgeCell + rot.FacingCell;
			if (!c.InBounds(map) || !c.Standable(map) || !FindNearestPoweredCell(map, edgeCell, 2.9f).IsValid)
			{
				continue;
			}
			try
			{
				Thing thing = ThingMaker.MakeThing(lamp, lamp.MadeFromStuff ? GenStuff.DefaultStuffFor(lamp) : null);
				thing.SetFactionDirect(faction);
				GenSpawn.Spawn(thing, edgeCell, map, rot);
				CompPowerTrader compPowerTrader = thing.TryGetComp<CompPowerTrader>();
				if (compPowerTrader != null)
				{
					compPowerTrader.PowerOn = true;
				}
				return true;
			}
			catch
			{
			}
		}
		return false;
	}

	private static IntVec3 FindNearestPoweredCell(Map map, IntVec3 from, float r)
	{
		foreach (IntVec3 item in GenRadial.RadialCellsAround(from, r, useCenter: false))
		{
			if (!item.InBounds(map))
			{
				continue;
			}
			List<Thing> thingList = item.GetThingList(map);
			for (int i = 0; i < thingList.Count; i++)
			{
				if (thingList[i].def.EverTransmitsPower)
				{
					return item;
				}
			}
		}
		return IntVec3.Invalid;
	}

	private static int DemolishBuildings(Map map, CellRect rect, bool keepConduits = false)
	{
		HashSet<Thing> hashSet = new HashSet<Thing>();
		foreach (IntVec3 item in rect)
		{
			if (!item.InBounds(map))
			{
				continue;
			}
			List<Thing> thingList = item.GetThingList(map);
			for (int i = 0; i < thingList.Count; i++)
			{
				Thing thing = thingList[i];
				if (thing is Building building)
				{
					if (!building.def.EverTransmitsPower && (building.Faction != Faction.OfPlayer || building.def.mineable))
					{
						hashSet.Add(building);
					}
				}
				else if (IsChunk(thing.def))
				{
					hashSet.Add(thing);
				}
			}
		}
		int num = 0;
		foreach (Thing item2 in hashSet)
		{
			if (item2 != null && !item2.Destroyed)
			{
				item2.Destroy();
				num++;
			}
		}
		return num;
	}

	private static bool IsChunk(ThingDef def)
	{
		if (def == null)
		{
			return false;
		}
		if (def.thingCategories != null)
		{
			for (int i = 0; i < def.thingCategories.Count; i++)
			{
				string defName = def.thingCategories[i].defName;
				if (defName == "Chunks" || defName == "StoneChunks")
				{
					return true;
				}
			}
		}
		if (def.defName != null)
		{
			return def.defName.StartsWith("Chunk");
		}
		return false;
	}

	private static ThingDef StuffOrDefault(ThingDef def, ThingDef preferred)
	{
		if (def == null || !def.MadeFromStuff)
		{
			return null;
		}
		if (preferred != null && preferred.stuffProps != null)
		{
			return preferred;
		}
		return GenStuff.DefaultStuffFor(def);
	}

	private static ThingDef SampleBaseWallStuff(Map map, Faction faction, CellRect rect, out int votes, out int scanned)
	{
		votes = 0;
		scanned = 0;
		if (map == null || faction == null)
		{
			return null;
		}
		Dictionary<ThingDef, int> dictionary = new Dictionary<ThingDef, int>();
		foreach (IntVec3 item in rect)
		{
			if (item.InBounds(map))
			{
				Building edifice = item.GetEdifice(map);
				if (edifice != null && edifice.def != null && edifice.Faction != Faction.OfPlayer && !edifice.def.mineable && edifice.def.building != null && !edifice.def.building.isNaturalRock && edifice.def.passability == Traversability.Impassable && edifice.Stuff != null && edifice.Stuff.stuffProps != null)
				{
					scanned++;
					dictionary.TryGetValue(edifice.Stuff, out var value);
					dictionary[edifice.Stuff] = value + 1;
				}
			}
		}
		ThingDef thingDef = null;
		foreach (KeyValuePair<ThingDef, int> item2 in dictionary)
		{
			if (item2.Value > votes || (item2.Value == votes && thingDef != null && string.CompareOrdinal(item2.Key.defName, thingDef.defName) < 0))
			{
				thingDef = item2.Key;
				votes = item2.Value;
			}
		}
		if (votes < 8)
		{
			votes = 0;
			return null;
		}
		return thingDef;
	}

	private static void RecordRingWalls(Map map, CellRect rect, HashSet<IntVec3> walls, HashSet<IntVec3> doors, List<CellRect> skipInteriors = null, List<IntVec3> skippedInside = null)
	{
		if (map == null || walls == null)
		{
			return;
		}
		foreach (IntVec3 edgeCell in rect.ExpandedBy(1).EdgeCells)
		{
			if (skipInteriors != null)
			{
				bool flag = false;
				for (int i = 0; i < skipInteriors.Count; i++)
				{
					if (skipInteriors[i].Width > 2 && skipInteriors[i].Height > 2 && skipInteriors[i].ContractedBy(1).Contains(edgeCell))
					{
						flag = true;
						break;
					}
				}
				if (flag)
				{
					skippedInside?.Add(edgeCell);
					continue;
				}
			}
			if (!edgeCell.InBounds(map))
			{
				continue;
			}
			Building edifice = edgeCell.GetEdifice(map);
			if (edifice != null && edifice.def != null && edifice.def.building != null && !edifice.def.mineable && !edifice.def.building.isNaturalRock && !edifice.def.EverTransmitsPower)
			{
				if (edifice is Building_Door || edifice.def.IsDoor)
				{
					doors?.Add(edgeCell);
				}
				else if (edifice.def.passability == Traversability.Impassable)
				{
					walls.Add(edgeCell);
				}
			}
		}
	}

	private static void RecordEdgeDoors(Map map, CellRect rect, HashSet<IntVec3> into)
	{
		if (map == null || into == null)
		{
			return;
		}
		foreach (IntVec3 edgeCell in rect.EdgeCells)
		{
			if (edgeCell.InBounds(map))
			{
				Building edifice = edgeCell.GetEdifice(map);
				if (edifice != null && edifice.def != null && (edifice is Building_Door || edifice.def.IsDoor))
				{
					into.Add(edgeCell);
				}
			}
		}
	}

	private static TerrainDef SampleBaseFloor(Map map, CellRect rect, out int votes, out int scanned, out bool matched)
	{
		votes = 0;
		scanned = 0;
		matched = false;
		if (map == null)
		{
			return null;
		}
		Dictionary<TerrainDef, int> dictionary = new Dictionary<TerrainDef, int>();
		foreach (IntVec3 item in rect)
		{
			if (item.InBounds(map))
			{
				TerrainDef terrainDef = map.terrainGrid.TerrainAt(item);
				if (terrainDef != null && !terrainDef.natural && terrainDef.IsFloor && !terrainDef.isFoundation)
				{
					scanned++;
					dictionary.TryGetValue(terrainDef, out var value);
					dictionary[terrainDef] = value + 1;
				}
			}
		}
		TerrainDef terrainDef2 = null;
		foreach (KeyValuePair<TerrainDef, int> item2 in dictionary)
		{
			if (item2.Value > votes || (item2.Value == votes && terrainDef2 != null && string.CompareOrdinal(item2.Key.defName, terrainDef2.defName) < 0))
			{
				terrainDef2 = item2.Key;
				votes = item2.Value;
			}
		}
		if (votes < 8)
		{
			votes = 0;
			return null;
		}
		matched = true;
		return terrainDef2;
	}

	private static int SealCutWalls(Map map, Faction faction, SpinePlan plan, HashSet<IntVec3> cut, HashSet<IntVec3> cutDoors, ThingDef wallDef, ThingDef wallStuff, ThingDef doorDef, ThingDef doorStuff, out string skipDetail, out int doorsRestored, out string doorSkipDetail, List<IntVec3> sealedCells = null)
	{
		skipDetail = "";
		doorSkipDetail = "";
		doorsRestored = 0;
		if (map == null || cut == null || wallDef == null)
		{
			return 0;
		}
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int num6 = 0;
		List<IntVec3> list = new List<IntVec3>();
		HashSet<IntVec3> hashSet = GateApronCells(plan);
		foreach (IntVec3 item in cut)
		{
			if (!item.InBounds(map))
			{
				num5++;
			}
			else if (item.GetEdifice(map) != null)
			{
				num2++;
			}
			else if (plan.rows > 0 && plan.corridor.Contains(item))
			{
				num3++;
				list.Add(item);
			}
			else if (plan.gate.IsValid && item == plan.gate)
			{
				num4++;
			}
			else if (hashSet.Contains(item))
			{
				num6++;
				list.Add(item);
			}
			else
			{
				Place(map, faction, wallDef, wallStuff, item);
				sealedCells?.Add(item);
				num++;
			}
		}
		int num7 = 0;
		int num8 = 0;
		int num9 = 0;
		int num10 = 0;
		if (cutDoors != null && doorDef != null)
		{
			foreach (IntVec3 cutDoor in cutDoors)
			{
				if (!cutDoor.InBounds(map))
				{
					num10++;
					continue;
				}
				if (cutDoor.GetEdifice(map) != null)
				{
					num7++;
					continue;
				}
				if (plan.rows > 0 && plan.corridor.Contains(cutDoor))
				{
					num8++;
					continue;
				}
				if (plan.gate.IsValid && cutDoor == plan.gate)
				{
					num9++;
					continue;
				}
				Place(map, faction, doorDef, doorStuff, cutDoor);
				doorsRestored++;
			}
		}
		skipDetail = $"이미벽 {num2} 복도 {num3} 게이트 {num4} 앞마당 {num6} 맵밖 {num5}";
		if (list.Count > 0)
		{
			skipDetail = skipDetail + " 통로유지@" + string.Join(",", list.ConvertAll((IntVec3 c) => $"({c.x},{c.z})").ToArray());
		}
		doorSkipDetail = $"이미벽 {num7} 복도 {num8} 게이트 {num9} 맵밖 {num10}";
		return num;
	}

	private static HashSet<IntVec3> GateApronCells(SpinePlan plan)
	{
		HashSet<IntVec3> hashSet = new HashSet<IntVec3>();
		if (!plan.gate.IsValid)
		{
			return hashSet;
		}
		IntVec3 intVec = (plan.stacked ? new IntVec3(-1, 0, 0) : new IntVec3(0, 0, -1));
		for (int i = 1; i <= 2; i++)
		{
			hashSet.Add(plan.gate + new IntVec3(intVec.x * i, 0, intVec.z * i));
		}
		return hashSet;
	}

	private static void CarveDoorOutward(Map map, CellRect leaf, IntVec3 doorCell, TerrainDef floor, HashSet<IntVec3> protectedCells = null)
	{
		IntVec3 intVec = ((doorCell.z == leaf.maxZ) ? new IntVec3(0, 0, 1) : ((doorCell.z == leaf.minZ) ? new IntVec3(0, 0, -1) : ((doorCell.x == leaf.maxX) ? new IntVec3(1, 0, 0) : new IntVec3(-1, 0, 0))));
		for (int i = 1; i <= 16; i++)
		{
			IntVec3 intVec2 = doorCell + new IntVec3(intVec.x * i, 0, intVec.z * i);
			if (intVec2.InBounds(map) && (protectedCells == null || !protectedCells.Contains(intVec2)) && (!intVec2.Walkable(map) || intVec2.GetEdifice(map) != null))
			{
				ClearEdifice(map, intVec2);
				if (floor != null)
				{
					map.terrainGrid.SetTerrain(intVec2, floor);
				}
				continue;
			}
			break;
		}
	}

	private static void BuildEnclosedRoom(Map map, Faction faction, CellRect rect, IntVec3 doorCell, ThingDef wallDef, ThingDef wallStuff, ThingDef doorDef, ThingDef doorStuff, TerrainDef floor, bool roof, HashSet<IntVec3> extraDoors = null)
	{
		foreach (IntVec3 edgeCell in rect.EdgeCells)
		{
			bool flag = (edgeCell.x == rect.minX || edgeCell.x == rect.maxX) && (edgeCell.z == rect.minZ || edgeCell.z == rect.maxZ);
			if (edgeCell == doorCell || (extraDoors != null && !flag && extraDoors.Contains(edgeCell)))
			{
				Place(map, faction, doorDef, doorStuff, edgeCell);
			}
			else
			{
				Place(map, faction, wallDef, wallStuff, edgeCell);
			}
		}
		LayFloor(map, rect.ContractedBy(1), floor);
		if (roof && rect.Width <= 13 && rect.Height <= 13)
		{
			RoofRect(map, rect);
		}
	}

	private static void RoofRect(Map map, CellRect rect)
	{
		RoofDef roofConstructed = RoofDefOf.RoofConstructed;
		foreach (IntVec3 item in rect)
		{
			if (item.InBounds(map))
			{
				map.roofGrid.SetRoof(item, roofConstructed);
			}
		}
	}

	private static void ThickRoofVault(Map map, CellRect rect)
	{
		RoofDef roofRockThick = RoofDefOf.RoofRockThick;
		foreach (IntVec3 item in rect)
		{
			if (item.InBounds(map))
			{
				map.roofGrid.SetRoof(item, roofRockThick);
			}
		}
	}

	public static void InsertVaultOverlay(Map map, StrongholdData data, CellRect baseRect)
	{
		Faction faction = map?.ParentFaction;
		if (map == null || faction == null || data == null || baseRect.Width <= 0)
		{
			return;
		}
		try
		{
			data.ClearSealedRectsOfKind("금고", "가짜금고");
			bool elec = faction.def != null && (int)faction.def.techLevel >= 4;
			StrongholdDefenseUtility.ResolveWall(map, faction, data.ContentTier, out var wallDef, out var stuff);
			if (wallDef == null)
			{
				wallDef = ThingDefOf.Wall;
				stuff = ThingDefOf.BlocksGranite;
			}
			ThingDef thingDef = StrongholdDefenseUtility.ResolveDoorDef(data.tier >= 9);
			ThingDef doorStuff = StuffOrDefault(thingDef, stuff);
			TerrainDef namedSilentFail = DefDatabase<TerrainDef>.GetNamedSilentFail("Concrete");
			int vh = Mathf.Clamp(4 + data.ContentTier / 5, 4, 5);
			CellRect cellRect = FindVaultSpotOverlay(map, baseRect, vh);
			if (cellRect.Width <= 0)
			{
				EESLog.Warning("VaultOverlay [" + faction.Name + "]: 빈 자리 못 찾음 → 금고 생략.");
				return;
			}
			ClearChunks(map, cellRect.ExpandedBy(1));
			DemolishBuildings(map, cellRect.ExpandedBy(1));
			IntVec3 doorCell = new IntVec3(cellRect.CenterCell.x, 0, cellRect.minZ);
			BuildEnclosedRoom(map, faction, cellRect, doorCell, wallDef, stuff, thingDef, doorStuff, namedSilentFail, roof: true);
			ThickRoofVault(map, cellRect);
			FurnishVault(map, faction, data, cellRect.ContractedBy(1), elec);
			CarveDoorOutward(map, cellRect, doorCell, namedSilentFail);
			SecureVault(map, faction, data, cellRect);
			LightRoom(map, faction, cellRect, elec);
			MapComponent_StrongholdSiege component = map.GetComponent<MapComponent_StrongholdSiege>();
			if (component != null)
			{
				component.eesVaultRect = cellRect;
			}
			EESLog.Message($"VaultOverlay [{faction.Name} T{data.tier}]: 컴팩트 금고 @ {cellRect.CenterCell} " + $"({cellRect.Width}x{cellRect.Height}) 커스텀 레이아웃 기지에 삽입.");
		}
		catch (Exception ex)
		{
			EESLog.Error("InsertVaultOverlay failed: " + ex);
		}
	}

	private static CellRect FindVaultSpotOverlay(Map map, CellRect baseRect, int vh)
	{
		IntVec3 centerCell = baseRect.CenterCell;
		float radius = (float)Mathf.Max(baseRect.Width, baseRect.Height) / 2f + 2f;
		IntVec3 center = IntVec3.Invalid;
		int num = int.MaxValue;
		foreach (IntVec3 item in GenRadial.RadialCellsAround(centerCell, radius, useCenter: true))
		{
			if (!baseRect.Contains(item))
			{
				continue;
			}
			CellRect cellRect = CellRect.CenteredOn(item, vh + 1).ClipInsideMap(map);
			int num2 = 0;
			foreach (IntVec3 item2 in cellRect)
			{
				if (!item2.InBounds(map))
				{
					num2 += 100;
				}
				else
				{
					Building edifice = item2.GetEdifice(map);
					if (edifice != null && !edifice.Destroyed)
					{
						num2++;
					}
				}
				if (num2 > 4)
				{
					break;
				}
			}
			if (num2 <= 2)
			{
				return CellRect.CenteredOn(item, vh).ClipInsideMap(map);
			}
			if (num2 < num)
			{
				num = num2;
				center = item;
			}
		}
		if (!center.IsValid)
		{
			center = centerCell;
		}
		return CellRect.CenteredOn(center, vh).ClipInsideMap(map);
	}

	public static void RemoveOrphanWallAttachments(Map map, CellRect area)
	{
		// Only clean attachments created by this actual new hostile EES map.
		// Loaded/legacy maps and original town contents retain their objects.
		if (map?.Parent is not Settlement settlement || MapGenerator.mapBeingGenerated != map ||
			settlement.Faction?.HostileTo(Faction.OfPlayer) != true || EESFactionBlacklist.IsBlocked(settlement.Faction) ||
			EESCustomBaseDetector.IsForeignSettlement(settlement)) return;
		var plan = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID)?.generationPlan;
		if (plan == null || plan.legacyMap || plan.nativeDefenseIds == null) return;
		var original = new HashSet<int>(plan.nativeDefenseIds);
		List<Thing> list = new List<Thing>();
		foreach (IntVec3 item in area.ClipInsideMap(map))
		{
			if (!item.InBounds(map))
			{
				continue;
			}
			List<Thing> thingList = item.GetThingList(map);
			for (int i = 0; i < thingList.Count; i++)
			{
				if (thingList[i] is Building { Destroyed: false } building && building.def?.building != null && building.def.building.isAttachment && building.Faction == settlement.Faction &&
					!original.Contains(building.thingIDNumber) && GenConstruct.GetWallAttachedTo(building) == null)
				{
					list.Add(building);
				}
			}
		}
		for (int j = 0; j < list.Count; j++)
		{
			if (!list[j].Destroyed)
			{
				list[j].Destroy();
			}
		}
		if (list.Count > 0)
		{
			EESLog.Message($"Removed {list.Count} orphan wall-attachment(s) (벽 없는 부착물 = 전원망 NRE 원인 제거).");
		}
	}

	private static bool RoleHas(string role, string token)
	{
		if (!role.NullOrEmpty())
		{
			return role.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
		}
		return false;
	}

	private static bool HasLivePrisoner(StrongholdData data)
	{
		if (data.capturedPrisoners == null)
		{
			return false;
		}
		for (int i = 0; i < data.capturedPrisoners.Count; i++)
		{
			Pawn pawn = data.capturedPrisoners[i];
			if (pawn != null && !pawn.Dead && !pawn.Destroyed && pawn.Faction != Faction.OfPlayer)
			{
				return true;
			}
		}
		return false;
	}

	private static void FurnishVault(Map map, Faction faction, StrongholdData data, CellRect room, bool elec)
	{
		if (RimPactsCompat.NotAPlayerAssault(map?.Parent as Settlement))
		{
			data.vaultValue = 0f;
			data.vaultValueAnnounced = true;
			EESLog.Message("Vault loot SKIP [" + faction?.Name + "]: 직공 아님(우호 방문·관전 전투) — 금고 비움.");
			return;
		}
		int num = StrongholdZoneUtility.FillVault(map, faction, data, room);
		float num2 = 0f;
		int num3 = 0;
		foreach (IntVec3 item in room)
		{
			if (!item.InBounds(map))
			{
				continue;
			}
			List<Thing> thingList = item.GetThingList(map);
			for (int i = 0; i < thingList.Count; i++)
			{
				Thing thing = thingList[i];
				if (thing != null && thing.def != null && thing.def.category == ThingCategory.Item)
				{
					num2 += thing.MarketValue * (float)thing.stackCount;
					num3++;
				}
			}
		}
		data.vaultValue = num2;
		data.vaultValueAnnounced = false;
		EESLog.Message($"Vault loot [{faction.Name} T{data.tier}]: {num} kinds, {num3} items, value≈{num2:0}은 (legendary={data.tier >= 12}).");
	}

	public static CellRect BuildFakeRewardRoom(Map map, Faction faction, StrongholdData data, CellRect baseRect, float lootMul = 1.5f)
	{
		int contentTier = data.ContentTier;
		int radius = Mathf.Clamp(6 + contentTier, 7, 11) / 2;
		CellRect cellRect = CellRect.CenteredOn(baseRect.CenterCell, radius).ClipInsideMap(map);
		foreach (IntVec3 item in cellRect)
		{
			if (item.InBounds(map))
			{
				ClearEdifice(map, item);
			}
		}
		TerrainDef namedSilentFail = DefDatabase<TerrainDef>.GetNamedSilentFail("Concrete");
		if (namedSilentFail != null)
		{
			LayFloor(map, cellRect, namedSilentFail);
		}
		StrongholdDefenseUtility.GetAncientCoreDefs(map, out var ancientWall, out var blastDoor);
		ThingDef thingDef = ancientWall ?? ThingDefOf.Wall;
		ThingDef thingDef2 = blastDoor ?? ThingDefOf.Door;
		IntVec3 intVec = new IntVec3(Mathf.Clamp(cellRect.CenterCell.x, cellRect.minX + 1, cellRect.maxX - 1), 0, cellRect.minZ);
		foreach (IntVec3 edgeCell in cellRect.EdgeCells)
		{
			if (edgeCell.InBounds(map))
			{
				if (edgeCell == intVec)
				{
					Place(map, faction, thingDef2, thingDef2.MadeFromStuff ? GenStuff.DefaultStuffFor(thingDef2) : null, edgeCell);
				}
				else
				{
					Place(map, faction, thingDef, thingDef.MadeFromStuff ? GenStuff.DefaultStuffFor(thingDef) : null, edgeCell);
				}
			}
		}
		CellRect vaultInterior = cellRect.ContractedBy(1);
		int num = StrongholdZoneUtility.FillVault(map, faction, data, vaultInterior, lootMul);
		float num2 = 0f;
		int num3 = 0;
		foreach (IntVec3 item2 in cellRect)
		{
			if (!item2.InBounds(map))
			{
				continue;
			}
			List<Thing> thingList = item2.GetThingList(map);
			for (int i = 0; i < thingList.Count; i++)
			{
				Thing thing = thingList[i];
				if (thing != null && thing.def != null && thing.def.category == ThingCategory.Item)
				{
					num2 += thing.MarketValue * (float)thing.stackCount;
					num3++;
				}
			}
		}
		data.vaultValue = num2;
		data.vaultValueAnnounced = false;
		data.RegisterSealedRect(cellRect, "가짜금고");
		EESLog.Message($"FakeReward [{faction.Name} T{data.tier}]: room {cellRect.Width}x{cellRect.Height} " + $"wall={thingDef.defName} door={thingDef2.defName} | kinds={num} items={num3} value≈{num2:0}은(×1.5).");
		return cellRect;
	}

	private static void SecureVault(Map map, Faction faction, StrongholdData data, CellRect vaultRoom)
	{
		if (data.tier < 10 && !VacuumMapUtility.IsVacuum(map))
		{
			return;
		}
		StrongholdDefenseUtility.GetAncientCoreDefs(map, out var ancientWall, out var blastDoor);
		if (ancientWall == null)
		{
			return;
		}
		data.RegisterSealedRect(vaultRoom, "금고");
		CellRect leaf = vaultRoom.ExpandedBy(1);
		IntVec3 intVec = new IntVec3(Mathf.Clamp(vaultRoom.CenterCell.x, leaf.minX + 1, leaf.maxX - 1), 0, leaf.minZ);
		ThingDef thingDef = blastDoor ?? ThingDefOf.Door;
		foreach (IntVec3 edgeCell in leaf.EdgeCells)
		{
			if (edgeCell.InBounds(map))
			{
				if (edgeCell == intVec)
				{
					Place(map, faction, thingDef, thingDef.MadeFromStuff ? GenStuff.DefaultStuffFor(thingDef) : null, edgeCell);
				}
				else
				{
					Place(map, faction, ancientWall, ancientWall.MadeFromStuff ? GenStuff.DefaultStuffFor(ancientWall) : null, edgeCell);
				}
			}
		}
		CarveDoorOutward(map, leaf, intVec, DefDatabase<TerrainDef>.GetNamedSilentFail("Concrete"));
		EESLog.Message($"Layout vault secured [{faction.Name} T{data.tier}]: ancient walls + {thingDef.defName} entry @ {intVec}.");
	}

	private static void FurnishCommand(Map map, Faction faction, StrongholdData data, CellRect room)
	{
		CellRect @int = room.ContractedBy(1);
		data.commandRoomCenter = room.CenterCell;
		var plan = StrongholdGenerationPlan.GetOrCreate(map, data);
		if (plan != null) plan.commandCell = data.commandRoomCenter;
		if (data.commanderType == CommanderType.Comms)
		{
			if (PlaceCommsConsole(map, faction, @int))
			{
				data.hasCommsRoom = true;
			}
			else
			{
				EESLog.Warning("FurnishCommand [" + faction.Name + "]: CommsConsole 배치 실패(조작칸 개방 위치 없음).");
			}
		}
	}

	private static bool PlaceCommsConsole(Map map, Faction faction, CellRect int2)
	{
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("CommsConsole");
		if (namedSilentFail == null)
		{
			return false;
		}
		int num = ((!namedSilentFail.rotatable) ? 1 : 4);
		foreach (IntVec3 cell in int2.Cells)
		{
			if (!cell.InBounds(map) || !cell.Standable(map) || cell.GetFirstBuilding(map) != null)
			{
				continue;
			}
			for (int i = 0; i < num; i++)
			{
				Rot4 rot = new Rot4(i);
				IntVec3 intVec = cell + namedSilentFail.interactionCellOffset.RotatedBy(rot);
				if (intVec.InBounds(map) && intVec.Standable(map) && intVec.GetEdifice(map) == null)
				{
					try
					{
						Thing thing = ThingMaker.MakeThing(namedSilentFail, namedSilentFail.MadeFromStuff ? GenStuff.DefaultStuffFor(namedSilentFail) : null);
						thing.SetFactionDirect(faction);
						GenSpawn.Spawn(thing, cell, map, rot);
						EESLog.Message($"Comms console placed @ {cell} rot={rot} interactCell={intVec} (in-room, open).");
						return true;
					}
					catch (Exception ex)
					{
						EESLog.Warning("Comms console place: " + ex.Message);
					}
				}
			}
		}
		return false;
	}

	private static void FurnishPrison(Map map, Faction faction, StrongholdData data, CellRect room)
	{
		StrongholdDefenseUtility.ResolveWall(map, faction, data.ContentTier, out var wallDef, out var stuff);
		if (wallDef == null)
		{
			wallDef = ThingDefOf.Wall;
			stuff = ThingDefOf.BlocksGranite;
		}
		foreach (IntVec3 edgeCell in room.ExpandedBy(1).EdgeCells)
		{
			if (edgeCell.InBounds(map))
			{
				Building edifice = edgeCell.GetEdifice(map);
				if (edifice != null && !edifice.Destroyed && edifice.def.IsDoor)
				{
					Place(map, faction, wallDef, StuffOrDefault(wallDef, stuff), edgeCell);
				}
			}
		}
		StrongholdZoneUtility.PopulatePrisonForLayout(map, faction, data, room);
	}

	private static void FurnishLab(Map map, Faction faction, StrongholdData data, CellRect room)
	{
		CellRect interior = room.ContractedBy(1);
		HashSet<IntVec3> occ = new HashSet<IntVec3>();
		bool num = faction.def != null && (int)faction.def.techLevel >= 4;
		int contentTier = data.ContentTier;
		if (num)
		{
			PlaceNamed(map, faction, (contentTier >= 8) ? "HiTechResearchBench" : "SimpleResearchBench", interior, occ);
			PlaceNamed(map, faction, "SimpleResearchBench", interior, occ);
			if (contentTier >= 10)
			{
				PlaceNamed(map, faction, "MultiAnalyzer", interior, occ);
			}
		}
		else
		{
			PlaceNamed(map, faction, "SimpleResearchBench", interior, occ);
			PlaceNamed(map, faction, "SimpleResearchBench", interior, occ);
		}
		PlaceNamed(map, faction, "Shelf", interior, occ);
		PlaceNamed(map, faction, "Shelf", interior, occ);
	}

	private static void FurnishNoble(Map map, Faction faction, StrongholdData data, CellRect room)
	{
		HashSet<IntVec3> occ = new HashSet<IntVec3>();
		TerrainDef terrainDef = DefDatabase<TerrainDef>.GetNamedSilentFail("CarpetFine") ?? DefDatabase<TerrainDef>.GetNamedSilentFail("CarpetRoyal") ?? DefDatabase<TerrainDef>.GetNamedSilentFail("Carpet") ?? DefDatabase<TerrainDef>.GetNamedSilentFail("GoldTile");
		if (terrainDef != null)
		{
			foreach (IntVec3 item in room)
			{
				if (item.InBounds(map))
				{
					map.terrainGrid.SetTerrain(item, terrainDef);
				}
			}
		}
		int x = room.CenterCell.x;
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("Throne");
		ThingDef namedSilentFail2 = DefDatabase<ThingDef>.GetNamedSilentFail("Brazier");
		ThingDef namedSilentFail3 = DefDatabase<ThingDef>.GetNamedSilentFail("Drape");
		IntVec3 cell = new IntVec3(x, 0, room.maxZ);
		if (namedSilentFail != null && PlaceBuildingRot(map, faction, namedSilentFail, cell, Rot4.South) != null)
		{
			MarkOccupied(occ, cell, namedSilentFail.size);
		}
		if (namedSilentFail2 != null)
		{
			PlaceBuildingRot(map, faction, namedSilentFail2, new IntVec3(x - 1, 0, room.maxZ), Rot4.South);
			PlaceBuildingRot(map, faction, namedSilentFail2, new IntVec3(x + 1, 0, room.maxZ), Rot4.South);
		}
		if (namedSilentFail3 != null)
		{
			int num = 0;
			for (int i = room.minX; i + 1 <= room.maxX; i += 3)
			{
				if (num >= 2)
				{
					break;
				}
				IntVec3 intVec = new IntVec3(i, 0, room.minZ);
				IntVec3 c = new IntVec3(i + 1, 0, room.minZ);
				if (intVec.InBounds(map) && c.InBounds(map) && intVec.GetEdifice(map) == null && c.GetEdifice(map) == null && PlaceBuildingRot(map, faction, namedSilentFail3, intVec, Rot4.North) != null)
				{
					num++;
				}
			}
		}
		RefuelRoom(map, room);
	}

	private static void RefuelRoom(Map map, CellRect room)
	{
		HashSet<Thing> hashSet = new HashSet<Thing>();
		foreach (IntVec3 item in room)
		{
			if (!item.InBounds(map))
			{
				continue;
			}
			List<Thing> thingList = item.GetThingList(map);
			for (int i = 0; i < thingList.Count; i++)
			{
				Thing thing = thingList[i];
				if (hashSet.Add(thing))
				{
					CompRefuelable compRefuelable = thing.TryGetComp<CompRefuelable>();
					compRefuelable?.Refuel(compRefuelable.Props.fuelCapacity);
				}
			}
		}
	}

	private static void FurnishScanner(Map map, Faction faction, StrongholdData data, CellRect room, CellRect footprint)
	{
		CellRect interior = room.ContractedBy(1);
		HashSet<IntVec3> occ = new HashSet<IntVec3>();
		PlaceNamed(map, faction, "GroundPenetratingScanner", interior, occ);
		try
		{
			StrongholdZoneUtility.PlaceMineDrills(map, faction, footprint);
		}
		catch
		{
		}
	}

	private static void FurnishRec(Map map, Faction faction, StrongholdData data, CellRect room)
	{
		CellRect cellRect = room.ContractedBy(1);
		HashSet<IntVec3> occ = new HashSet<IntVec3>();
		if (faction.def != null && (int)faction.def.techLevel >= 4)
		{
			PlaceNamed(map, faction, "BilliardsTable", cellRect, occ);
		}
		PlaceNamed(map, faction, "PokerTable", cellRect, occ);
		PlaceNamed(map, faction, "ChessTable", cellRect, occ);
		for (int i = 0; i < 3; i++)
		{
			PlaceNamed(map, faction, "DiningChair", cellRect, occ);
		}
		if (RoleHas(data.roleDefName, "black") || RoleHas(data.roleDefName, "market"))
		{
			PlaceShelvesWithItems(map, faction, cellRect, occ, 2, new List<(string, int)>
			{
				("Silver", 200),
				("Gold", 40)
			});
		}
	}

	private static void FurnishStoreroom(Map map, Faction faction, StrongholdData data, CellRect room, string role)
	{
		CellRect cellRect = room.ContractedBy(1);
		HashSet<IntVec3> occ = new HashSet<IntVec3>();
		int contentTier = data.ContentTier;
		bool flag = faction.def != null && (int)faction.def.techLevel >= 4;
		List<(string, int)> list = new List<(string, int)>();
		if (RoleHas(role, "supply"))
		{
			if (flag)
			{
				PlaceNamed(map, faction, "NutrientPasteDispenser", cellRect, occ);
				PlaceNamed(map, faction, "Hopper", cellRect, occ);
				PlaceNamed(map, faction, "Hopper", cellRect, occ);
			}
			list.Add(("MealSimple", 12 + contentTier * 4));
			list.Add(("Pemmican", 60 + contentTier * 15));
			list.Add(("MealSurvivalPack", 6 + contentTier));
			list.Add(("RawPotatoes", 40 + contentTier * 10));
		}
		else if (RoleHas(role, "ammo"))
		{
			data.hasAmmoDepot = true;
			list.Add(("Shell_HighExplosive", 10 + contentTier * 2));
			list.Add(("Shell_Incendiary", 6));
			list.Add(("Chemfuel", 100 + contentTier * 20));
			if (CECompat.Active)
			{
				foreach (ThingDef item in CECompat.PickLootAmmo(faction, 3))
				{
					list.Add((item.defName, 120));
				}
			}
		}
		else
		{
			list.Add(("Cloth", 80 + contentTier * 20));
			list.Add(("Steel", 60 + contentTier * 20));
			list.Add(("WoodLog", 80));
			list.Add(("MedicineHerbal", 8 + contentTier));
			list.Add(("MealSimple", 8 + contentTier * 2));
		}
		PlaceShelvesWithItems(map, faction, cellRect, occ, Mathf.Clamp(cellRect.Area / 7, 3, 10), list);
	}

	private static void FurnishMortarShellRoom(Map map, Faction faction, StrongholdData data, CellRect room)
	{
		CellRect @int = room.ContractedBy(1);
		HashSet<IntVec3> occ = new HashSet<IntVec3>();
		int contentTier = data.ContentTier;
		List<(string, int)> items = new List<(string, int)>
		{
			("Shell_HighExplosive", 20 + contentTier * 4),
			("Shell_Incendiary", 10 + contentTier),
			("Shell_EMP", 6 + contentTier)
		};
		PlaceShelvesWithItems(map, faction, @int, occ, 2, items);
	}

	private static void PlaceShelvesWithItems(Map map, Faction faction, CellRect int2, HashSet<IntVec3> occ, int shelfCount, List<(string def, int amt)> items)
	{
		ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail("Shelf") ?? DefDatabase<ThingDef>.GetNamedSilentFail("ShelfSmall");
		List<IntVec3> list = new List<IntVec3>();
		if (thingDef != null)
		{
			for (int i = 0; i < shelfCount; i++)
			{
				IntVec3 intVec = FindFootprintCell(map, int2, thingDef.size, occ);
				if (!intVec.IsValid || PlaceBuilding(map, faction, thingDef, intVec) == null)
				{
					break;
				}
				foreach (IntVec3 item3 in GenAdj.OccupiedRect(intVec, Rot4.North, thingDef.size))
				{
					list.Add(item3);
				}
				MarkOccupied(occ, intVec, thingDef.size);
			}
		}
		if (list.Count == 0)
		{
			return;
		}
		int num = 0;
		foreach (var item4 in items)
		{
			string item = item4.def;
			int item2 = item4.amt;
			ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail(item);
			if (namedSilentFail == null || item2 <= 0 || !StrongholdLootFilter.IsDroppableLoot(namedSilentFail, "shelf"))
			{
				continue;
			}
			int num2 = ((namedSilentFail.stackLimit > 0) ? namedSilentFail.stackLimit : item2);
			int num3 = item2;
			while (num3 > 0 && num < list.Count)
			{
				try
				{
					Thing thing = ThingMaker.MakeThing(namedSilentFail, namedSilentFail.MadeFromStuff ? GenStuff.DefaultStuffFor(namedSilentFail) : null);
					thing.stackCount = Mathf.Min(num3, num2);
					thing.SetForbidden(value: false, warnOnFail: false);
					GenSpawn.Spawn(thing, list[num], map);
				}
				catch
				{
				}
				num3 -= num2;
				num++;
			}
		}
	}

	private static void ClearEdifice(Map map, IntVec3 c)
	{
		if (c.InBounds(map))
		{
			Building edifice = c.GetEdifice(map);
			if (edifice != null && !edifice.Destroyed)
			{
				edifice.Destroy();
			}
		}
	}

	private static void Place(Map map, Faction faction, ThingDef def, ThingDef stuff, IntVec3 c)
	{
		if (def == null || !c.InBounds(map))
		{
			return;
		}
		ClearEdifice(map, c);
		ThingDef stuff2 = (def.MadeFromStuff ? StuffOrDefault(def, stuff) : null);
		try
		{
			Thing thing = ThingMaker.MakeThing(def, stuff2);
			thing.SetFactionDirect(faction);
			GenSpawn.Spawn(thing, c, map);
			return;
		}
		catch
		{
		}
		try
		{
			Thing thing2 = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
			thing2.SetFactionDirect(faction);
			GenSpawn.Spawn(thing2, c, map);
		}
		catch (Exception ex)
		{
			EESLog.Warning($"Place fail {def.defName} @ {c}: {ex.Message}");
		}
	}

	private static Thing PlaceBuilding(Map map, Faction faction, ThingDef def, IntVec3 cell)
	{
		try
		{
			Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
			thing.SetFactionDirect(faction);
			GenSpawn.Spawn(thing, cell, map);
			return thing;
		}
		catch
		{
			return null;
		}
	}

	private static Thing PlaceBuildingRot(Map map, Faction faction, ThingDef def, IntVec3 cell, Rot4 rot)
	{
		if (def == null || !cell.InBounds(map))
		{
			return null;
		}
		try
		{
			Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
			thing.SetFactionDirect(faction);
			GenSpawn.Spawn(thing, cell, map, rot);
			return thing;
		}
		catch
		{
			return null;
		}
	}

	private static bool PlaceNamed(Map map, Faction faction, string defName, CellRect interior, HashSet<IntVec3> occ)
	{
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
		if (namedSilentFail == null)
		{
			return false;
		}
		IntVec3 cell = FindFootprintCell(map, interior, namedSilentFail.size, occ);
		if (!cell.IsValid)
		{
			return false;
		}
		if (PlaceBuilding(map, faction, namedSilentFail, cell) == null)
		{
			return false;
		}
		MarkOccupied(occ, cell, namedSilentFail.size);
		return true;
	}

	private static IntVec3 FindFootprintCell(Map map, CellRect interior, IntVec2 size, HashSet<IntVec3> occ)
	{
		foreach (IntVec3 item in interior)
		{
			if (!item.InBounds(map) || occ.Contains(item))
			{
				continue;
			}
			bool flag = true;
			foreach (IntVec3 item2 in GenAdj.OccupiedRect(item, Rot4.North, size))
			{
				if (!interior.Contains(item2) || !item2.InBounds(map) || occ.Contains(item2) || !item2.Standable(map) ||
					item2.GetEdifice(map) != null || item2.GetFirstItem(map) != null || item2.GetFirstPawn(map) != null)
				{
					flag = false;
					break;
				}
			}
			if (flag)
			{
				return item;
			}
		}
		return IntVec3.Invalid;
	}

	private static void MarkOccupied(HashSet<IntVec3> occ, IntVec3 cell, IntVec2 size)
	{
		foreach (IntVec3 item in GenAdj.OccupiedRect(cell, Rot4.North, size))
		{
			occ.Add(item);
		}
	}

	private static void LayFloor(Map map, CellRect rect, TerrainDef floor)
	{
		if (floor == null)
		{
			return;
		}
		foreach (IntVec3 item in rect)
		{
			if (item.InBounds(map) && item.GetEdifice(map) == null)
			{
				map.terrainGrid.SetTerrain(item, floor);
			}
		}
	}

	private static int LayFloorCounted(Map map, CellRect rect, TerrainDef floor)
	{
		if (floor == null)
		{
			return 0;
		}
		int num = 0;
		foreach (IntVec3 item in rect)
		{
			if (item.InBounds(map) && item.GetEdifice(map) == null && map.terrainGrid.TerrainAt(item) != floor)
			{
				map.terrainGrid.SetTerrain(item, floor);
				if (map.terrainGrid.TerrainAt(item) == floor)
				{
					num++;
				}
			}
		}
		return num;
	}
}
