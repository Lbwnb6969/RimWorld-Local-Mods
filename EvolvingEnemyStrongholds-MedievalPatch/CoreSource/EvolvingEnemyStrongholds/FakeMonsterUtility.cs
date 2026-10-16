using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public static class FakeMonsterUtility
{
	private const float MonsterMul = 1f;

	private const int MaxMonsters = 35;

	private static readonly string[] MechPool = new string[7] { "Mech_Scyther", "Mech_Lancer", "Mech_Pikeman", "Mech_Militor", "Mech_Scorcher", "Mech_CentipedeBlaster", "Mech_CentipedeGunner" };

	private static readonly string[] InsectPool = new string[4] { "Megaspider", "Spelopede", "Megascarab", "Locust" };

	private static readonly string[] EntityPool = new string[5] { "Toughspike", "Trispike", "Bulbfreak", "Gorehulk", "Chimera" };

	public static int SpawnMonsters(Map map, StrongholdData data, CellRect baseRect, CellRect rewardRoom)
	{
		FakeVariant fakeVariant = FakeOutpostUtility.VariantOf(data.roleDefName);
		Faction faction = MonsterFaction(fakeVariant);
		if (faction == null)
		{
			EESLog.Warning($"FakeMonster: variant {fakeVariant} 세력 없음(DLC 미설치?) → 몬스터 스폰 skip.");
			return 0;
		}
		List<PawnKindDef> list = ResolveKinds(PoolFor(fakeVariant));
		List<IntVec3> list2 = FreeInteriorCells(map, baseRect, rewardRoom);
		if (list.Count == 0 || list2.Count == 0)
		{
			EESLog.Warning($"FakeMonster [{faction.Name}]: kinds={list.Count} cells={list2.Count} → skip.");
			return 0;
		}
		Rand.PushState(Gen.HashCombineInt(data.settlementId, 61453));
		list2.Shuffle();
		int ci = 0;
		int num = 0;
		float num2 = (float)Mathf.Max(data.militaryPower, 500) * 1f;
		List<Pawn> list3 = new List<Pawn>();
		List<int> list4 = new List<int>();
		if (fakeVariant == FakeVariant.Insect && ModsConfig.OdysseyActive)
		{
			PawnKindDef namedSilentFail = DefDatabase<PawnKindDef>.GetNamedSilentFail("HiveQueen");
			if (namedSilentFail != null && ci < list2.Count)
			{
				Pawn pawn = SpawnSleeper(map, namedSilentFail, faction, list2[ci++]);
				if (pawn != null)
				{
					list3.Add(pawn);
					list4.Add(pawn.thingIDNumber);
					num2 -= namedSilentFail.combatPower;
				}
			}
		}
		if (fakeVariant == FakeVariant.Mech)
		{
			int tier = data.tier;
			SpawnBoss("Mech_Diabolus", (tier >= 10) ? 2 : ((tier >= 9) ? 1 : 0), map, faction, list2, ref ci, list3, list4);
			SpawnBoss("Mech_Warqueen", (tier >= 11) ? 1 : 0, map, faction, list2, ref ci, list3, list4);
			SpawnBoss("Mech_Apocriton", (tier >= 12) ? 1 : 0, map, faction, list2, ref ci, list3, list4);
		}
		while (num2 > 0f && num < 35 && ci < list2.Count)
		{
			PawnKindDef pawnKindDef = list[Rand.Range(0, list.Count)];
			Pawn pawn2 = SpawnSleeper(map, pawnKindDef, faction, list2[ci++]);
			if (pawn2 != null)
			{
				list3.Add(pawn2);
				list4.Add(pawn2.thingIDNumber);
				num2 -= Mathf.Max(50f, pawnKindDef.combatPower);
				num++;
			}
		}
		Rand.PopState();
		List<int> list5 = new List<int>();
		List<Pawn> nocioPawns = new List<Pawn>();
		List<int> list6 = new List<int>();
		int num3 = 0;
		int num4 = 0;
		switch (fakeVariant)
		{
		case FakeVariant.Insect:
			num3 += PlaceHives(map, faction, data, baseRect, rewardRoom, list2, ci, list6);
			break;
		case FakeVariant.Mech:
			num3 += PlaceMechClusterBuildings(map, data, baseRect, list6, list4);
			break;
		case FakeVariant.Entity:
			num3 += PlaceNociospheres(map, faction, rewardRoom, list5, nocioPawns);
			num4 = Mathf.Clamp((baseRect.Width - 4) * (baseRect.Height - 4) / 6, 30, 130);
			break;
		}
		float wanderRadius = 11f;
		if (list3.Count > 0)
		{
			try
			{
				LordMaker.MakeNewLord(faction, new LordJob_StructureThreatCluster(faction, baseRect.CenterCell, wanderRadius), map, list3);
			}
			catch (Exception ex)
			{
				EESLog.Warning("FakeMonster lord failed: " + ex.Message);
			}
		}
		map.GetComponent<MapComponent_FakeOutpostWake>()?.Arm(baseRect, rewardRoom, list4, list5, list6, num4);
		EESLog.Message($"FakeMonster [{faction.Name} {fakeVariant} T{data.tier}]: 몬스터 {list3.Count}체(예산 {data.militaryPower}×{1f}) " + $"+ hive {list6.Count} + 고통구체 {list5.Count} + 전조나무(예약) {num4} (전부 휴면).");
		return list3.Count;
	}

	private static void SpawnBoss(string defName, int n, Map map, Faction faction, List<IntVec3> cells, ref int ci, List<Pawn> monsters, List<int> ids)
	{
		if (n <= 0)
		{
			return;
		}
		PawnKindDef namedSilentFail = DefDatabase<PawnKindDef>.GetNamedSilentFail(defName);
		if (namedSilentFail == null)
		{
			EESLog.Warning("FakeMech boss " + defName + " 없음(DLC?) → skip.");
			return;
		}
		for (int i = 0; i < n; i++)
		{
			if (ci >= cells.Count)
			{
				break;
			}
			Pawn pawn = SpawnSleeper(map, namedSilentFail, faction, cells[ci++]);
			if (pawn != null)
			{
				monsters.Add(pawn);
				ids.Add(pawn.thingIDNumber);
			}
		}
	}

	private static int PlaceMechClusterBuildings(Map map, StrongholdData data, CellRect baseRect, List<int> buildingIds, List<int> monsterIds)
	{
		_ = Faction.OfMechanoids;
		float num = Mathf.Max(data.militaryPower, 500f) * 1f;
		List<Thing> list = null;
		try
		{
			MechClusterSketch sketch = MechClusterGenerator.GenerateClusterSketch(num, map, startDormant: true, forceNoConditionCauser: true);
			list = MechClusterUtility.SpawnCluster(baseRect.CenterCell, map, sketch, dropInPods: false);
		}
		catch (Exception ex)
		{
			EESLog.Error("FakeMechBuildings failed: " + ex);
			return 0;
		}
		if (list == null)
		{
			return 0;
		}
		CellRect rect = baseRect.ContractedBy(2);
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		for (int i = 0; i < list.Count; i++)
		{
			Thing thing = list[i];
			if (thing == null || thing.Destroyed)
			{
				continue;
			}
			if (!rect.Contains(thing.Position))
			{
				if (!RelocateInside(thing, map, rect))
				{
					try
					{
						thing.Destroy();
						num3++;
					}
					catch
					{
					}
					continue;
				}
				num5++;
			}
			if (thing is Pawn { Dead: false } pawn)
			{
				pawn.GetComp<CompCanBeDormant>()?.ToSleep();
				StripWakeUpComp(pawn);
				monsterIds.Add(pawn.thingIDNumber);
				num4++;
			}
			else
			{
				if (!(thing is ThingWithComps thingWithComps))
				{
					continue;
				}
				string defName = thingWithComps.def.defName;
				if ((defName != null && defName.StartsWith("Activator")) || thingWithComps.GetComp<CompProjectileInterceptor>() != null || thingWithComps.GetComp<CompPowerPlant>() != null || thingWithComps.GetComp<CompPowerBattery>() != null)
				{
					try
					{
						thing.Destroy();
						num3++;
					}
					catch
					{
					}
				}
				else
				{
					thingWithComps.GetComp<CompCanBeDormant>()?.ToSleep();
					StripWakeUpComp(thingWithComps);
					buildingIds.Add(thing.thingIDNumber);
					num2++;
				}
			}
		}
		EESLog.Message($"FakeMechBuildings [T{data.tier}]: points={num:0} 건물 {num2}(휴면등록) + 클러스터메카 {num4} " + $"+ 제거(활성기/보호막/발전기/자리없음) {num3} + 벽밖→안 재배치 {num5}.");
		return num2;
	}

	private static bool RelocateInside(Thing t, Map map, CellRect rect)
	{
		IntVec2 size = t.def.size;
		for (int i = 0; i < 80; i++)
		{
			IntVec3 randomCell = rect.RandomCell;
			bool flag = true;
			foreach (IntVec3 item in GenAdj.OccupiedRect(randomCell, Rot4.North, size))
			{
				if (!item.InBounds(map) || !item.Standable(map) || item.GetEdifice(map) != null)
				{
					flag = false;
					break;
				}
			}
			if (!flag)
			{
				continue;
			}
			try
			{
				if (t.Spawned)
				{
					t.DeSpawn();
				}
				GenSpawn.Spawn(t, randomCell, map);
				return true;
			}
			catch
			{
				return false;
			}
		}
		return false;
	}

	private static void StripWakeUpComp(Thing t)
	{
		if (!(t is ThingWithComps { AllComps: not null } thingWithComps))
		{
			return;
		}
		for (int num = thingWithComps.AllComps.Count - 1; num >= 0; num--)
		{
			if (thingWithComps.AllComps[num] is CompWakeUpDormant)
			{
				thingWithComps.AllComps.RemoveAt(num);
			}
		}
	}

	private static int PlaceHives(Map map, Faction faction, StrongholdData data, CellRect baseRect, CellRect rewardRoom, List<IntVec3> cells, int startIdx, List<int> hiveIds)
	{
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("Hive");
		if (namedSilentFail == null)
		{
			return 0;
		}
		int num = Mathf.Clamp(data.tier * 2 - 2, 6, 26);
		int num2 = 0;
		for (int i = startIdx; i < cells.Count; i++)
		{
			if (num2 >= num)
			{
				break;
			}
			IntVec3 intVec = cells[i];
			if (intVec.GetEdifice(map) == null && intVec.GetFirstPawn(map) == null)
			{
				try
				{
					Thing thing = ThingMaker.MakeThing(namedSilentFail);
					thing.SetFactionDirect(faction);
					GenSpawn.Spawn(thing, intVec, map);
					(thing as ThingWithComps)?.GetComp<CompCanBeDormant>()?.ToSleep();
					StripWakeUpComp(thing);
					hiveIds.Add(thing.thingIDNumber);
					num2++;
				}
				catch
				{
				}
			}
		}
		return num2;
	}

	private static int PlaceNociospheres(Map map, Faction faction, CellRect rewardRoom, List<int> nocioIds, List<Pawn> nocioPawns)
	{
		PawnKindDef namedSilentFail = DefDatabase<PawnKindDef>.GetNamedSilentFail("Nociosphere");
		if (namedSilentFail == null)
		{
			return 0;
		}
		CellRect cellRect = rewardRoom.ContractedBy(1);
		int num = 0;
		foreach (IntVec3 item in cellRect)
		{
			if (num >= 2)
			{
				break;
			}
			if (item.InBounds(map) && item.Standable(map) && item.GetFirstPawn(map) == null)
			{
				Pawn pawn = SpawnSleeper(map, namedSilentFail, faction, item);
				if (pawn != null)
				{
					nocioIds.Add(pawn.thingIDNumber);
					nocioPawns.Add(pawn);
					num++;
				}
			}
		}
		return num;
	}

	private static Faction MonsterFaction(FakeVariant v)
	{
		return v switch
		{
			FakeVariant.Mech => Faction.OfMechanoids,
			FakeVariant.Insect => Faction.OfInsects,
			FakeVariant.Entity => Faction.OfEntities,
			_ => null,
		};
	}

	private static string[] PoolFor(FakeVariant v)
	{
		return v switch
		{
			FakeVariant.Mech => MechPool,
			FakeVariant.Insect => InsectPool,
			FakeVariant.Entity => EntityPool,
			_ => new string[0],
		};
	}

	private static List<PawnKindDef> ResolveKinds(string[] names)
	{
		List<PawnKindDef> list = new List<PawnKindDef>();
		for (int i = 0; i < names.Length; i++)
		{
			PawnKindDef namedSilentFail = DefDatabase<PawnKindDef>.GetNamedSilentFail(names[i]);
			if (namedSilentFail != null)
			{
				list.Add(namedSilentFail);
			}
		}
		return list;
	}

	private static Pawn SpawnSleeper(Map map, PawnKindDef kind, Faction faction, IntVec3 cell)
	{
		try
		{
			Pawn pawn = PawnGenerator.GeneratePawn(kind, faction);
			GenSpawn.Spawn(pawn, cell, map);
			pawn.GetComp<CompCanBeDormant>()?.ToSleep();
			StripWakeUpComp(pawn);
			return pawn;
		}
		catch (Exception ex)
		{
			EESLog.Warning("FakeMonster spawn " + kind?.defName + " failed: " + ex.Message);
			return null;
		}
	}

	private static List<IntVec3> FreeInteriorCells(Map map, CellRect baseRect, CellRect rewardRoom)
	{
		List<IntVec3> list = new List<IntVec3>();
		CellRect cellRect = baseRect.ContractedBy(1);
		CellRect cellRect2 = rewardRoom.ExpandedBy(1);
		foreach (IntVec3 item in cellRect)
		{
			if (item.InBounds(map) && !cellRect2.Contains(item) && item.Standable(map) && item.GetEdifice(map) == null && item.GetFirstPawn(map) == null)
			{
				list.Add(item);
			}
		}
		return list;
	}
}
