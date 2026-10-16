using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class MechRelayUtility
{
	public const int MinDisplayTier = 11;

	private const float HeavyPowerMin = 250f;

	private const float BossPowerMin = 600f;

	private static readonly HashSet<string> BossKindNames = new HashSet<string> { "Mech_Diabolus", "Mech_Warqueen", "Mech_Apocriton" };

	private const int PlanSeed = 65818138;

	public static List<PawnKindDef> PlanMechs(Faction faction, int tier, int settlementId)
	{
		List<PawnKindDef> list = new List<PawnKindDef>();
		if (faction?.def == null || tier < 11)
		{
			return list;
		}
		BuildBands(faction, out var light, out var heavy, out var boss);
		if (light.Count == 0 && heavy.Count == 0 && boss.Count == 0)
		{
			return list;
		}
		List<PawnKindDef> list2 = ((light.Count > 0) ? light : heavy);
		List<PawnKindDef> list3 = ((heavy.Count > 0) ? heavy : light);
		List<PawnKindDef> list4 = boss;
		if (list4.Count == 0)
		{
			PawnKindDef pawnKindDef = StrongestOf(heavy) ?? StrongestOf(light);
			if (pawnKindDef != null)
			{
				list4 = new List<PawnKindDef> { pawnKindDef };
			}
		}
		Rand.PushState(Gen.HashCombineInt(settlementId, 65818138));
		try
		{
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			if (tier >= 13)
			{
				num2 = Rand.RangeInclusive(4, 6);
				num3 = 2;
			}
			else if (tier >= 12)
			{
				num = Rand.RangeInclusive(3, 5);
				num2 = Rand.RangeInclusive(2, 3);
				num3 = 1;
			}
			else
			{
				num = Rand.RangeInclusive(3, 5);
			}
			for (int i = 0; i < num; i++)
			{
				if (list2.Count <= 0)
				{
					break;
				}
				list.Add(list2[Rand.Range(0, list2.Count)]);
			}
			for (int j = 0; j < num2; j++)
			{
				if (list3.Count <= 0)
				{
					break;
				}
				list.Add(list3[Rand.Range(0, list3.Count)]);
			}
			if (num3 > 0 && list4.Count > 0)
			{
				if (tier >= 13)
				{
					list.Add(list4[list4.Count - 1]);
					for (int k = 1; k < num3; k++)
					{
						list.Add(list4[Rand.Range(0, list4.Count)]);
					}
				}
				else
				{
					int maxExclusive = Mathf.Max(1, (list4.Count + 1) / 2);
					for (int l = 0; l < num3; l++)
					{
						list.Add(list4[Rand.Range(0, maxExclusive)]);
					}
				}
			}
		}
		finally
		{
			Rand.PopState();
		}
		return list;
	}

	public static int EstimateMechCount(Settlement s, StrongholdData d)
	{
		if (s?.Faction?.def == null || d == null)
		{
			return 0;
		}
		if (!s.Faction.def.humanlikeFaction)
		{
			return 0;
		}
		if (StrongholdScoreUtility.DisplayRole(d, s) != "MechanoidRelay")
		{
			return 0;
		}
		return PlanMechs(s.Faction, d.tier, d.settlementId).Count;
	}

	public static int SpawnRelayMechs(Map map, Faction faction, StrongholdData data, CellRect rect)
	{
		if (map == null || faction?.def == null || data == null)
		{
			return 0;
		}
		if (data.roleDefName != "MechanoidRelay")
		{
			return 0;
		}
		if (!faction.def.humanlikeFaction)
		{
			return 0;
		}
		List<PawnKindDef> list = PlanMechs(faction, data.tier, data.settlementId);
		if (list.Count == 0)
		{
			EESLog.Message($"MechRelay [{faction.Name} T{data.tier}]: 편성 계획 0기(풀 비어있음 또는 티어 미달) — 스킵.");
			return 0;
		}
		List<Pawn> list2 = new List<Pawn>();
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < list.Count; i++)
		{
			if (!StrongholdGarrisonUtility.TryConsumePopulationSlot(special: true))
			{
				EESLog.Warning($"MechRelay [{faction.Name} T{data.tier}]: population budget exhausted at {i}/{list.Count}; remaining mechs skipped.");
				break;
			}
			if (stringBuilder.Length > 0)
			{
				stringBuilder.Append(",");
			}
			stringBuilder.Append(list[i].defName);
			Pawn pawn;
			try
			{
				pawn = PawnGenerator.GeneratePawn(list[i], faction);
			}
			catch (Exception ex)
			{
				StrongholdGarrisonUtility.ReleasePopulationSlot();
				EESLog.Warning("MechRelay: GeneratePawn 실패 [" + list[i].defName + "]: " + ex.Message);
				continue;
			}
			if (!StrongholdGarrisonUtility.TryFindSpawnCell(map, rect, out var cell))
			{
				StrongholdGarrisonUtility.ReleasePopulationSlot();
				StrongholdGarrisonUtility.DiscardUnspawnedPawn(pawn);
				continue;
			}
			GenSpawn.Spawn(pawn, cell, map);
			list2.Add(pawn);
		}
		if (list2.Count > 0)
		{
			StrongholdGarrisonUtility.AssignDispersedDefense(map, faction, rect, list2);
		}
		EESLog.Message($"MechRelay [{faction.Name} T{data.tier}]: 계획 {list.Count}기[{stringBuilder}] → 스폰 {list2.Count}기 (세력 소속·분산 방어 합류).");
		return list2.Count;
	}

	private static void BuildBands(Faction faction, out List<PawnKindDef> light, out List<PawnKindDef> heavy, out List<PawnKindDef> boss)
	{
		light = new List<PawnKindDef>();
		heavy = new List<PawnKindDef>();
		boss = new List<PawnKindDef>();
		HashSet<ModContentPack> identityMods = StrongholdGarrisonUtility.GetIdentityMods();
		HashSet<ModContentPack> hashSet = StrongholdGarrisonUtility.FactionModSetForDef(faction.def);
		List<PawnKindDef> allDefsListForReading = DefDatabase<PawnKindDef>.AllDefsListForReading;
		for (int i = 0; i < allDefsListForReading.Count; i++)
		{
			PawnKindDef pawnKindDef = allDefsListForReading[i];
			RaceProperties raceProperties = pawnKindDef.race?.race;
			if (raceProperties == null || !raceProperties.IsMechanoid || !pawnKindDef.allowInMechClusters || raceProperties.IsWorkMech || pawnKindDef.combatPower <= 0f)
			{
				continue;
			}
			ModContentPack modContentPack = pawnKindDef.modContentPack;
			ModContentPack modContentPack2 = pawnKindDef.race.modContentPack;
			if (((modContentPack == null || !identityMods.Contains(modContentPack)) && (modContentPack2 == null || !identityMods.Contains(modContentPack2))) || (modContentPack != null && hashSet.Contains(modContentPack)) || (modContentPack2 != null && hashSet.Contains(modContentPack2)))
			{
				if (BossKindNames.Contains(pawnKindDef.defName) || pawnKindDef.combatPower >= 600f)
				{
					boss.Add(pawnKindDef);
				}
				else if (pawnKindDef.combatPower >= 250f)
				{
					heavy.Add(pawnKindDef);
				}
				else
				{
					light.Add(pawnKindDef);
				}
			}
		}
		SortByPower(light);
		SortByPower(heavy);
		SortByPower(boss);
	}

	private static void SortByPower(List<PawnKindDef> list)
	{
		list.Sort((PawnKindDef a, PawnKindDef b) => a.combatPower.CompareTo(b.combatPower));
	}

	private static PawnKindDef StrongestOf(List<PawnKindDef> list)
	{
		if (list == null || list.Count <= 0)
		{
			return null;
		}
		return list[list.Count - 1];
	}
}
