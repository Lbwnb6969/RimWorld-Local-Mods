using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public static class GarrisonGroupUtility
{
	public const float EliteChance = 0.33f;

	public const float ZoneChance = 0.4f;

	public const float HeraldChance = 0.1f;

	public const float PursuerAppear = 0.75f;

	public const float DisruptorAppear = 0.35f;

	public const int EliteMin = 3;

	public const int EliteMax = 5;

	public const int PursuerMin = 6;

	public const int PursuerMax = 12;

	public const int HeraldCount = 3;

	public const int DisruptorMin = 4;

	public const int DisruptorMax = 7;

	public const int DefenderMin = 5;

	public const int DefenderMax = 15;

	public const float ZoneDivisor = 4.5f;

	public const int AssaultMin = 5;

	public const int AssaultMax = 8;

	public const float AssaultSpacerChance = 0.1f;

	public const float EliteThreshold = 0f;

	public const float ZoneThreshold = 0.08f;

	public const float PursuerThreshold = 0.12f;

	public const float HeraldThreshold = 0.7f;

	public const float DisruptorThreshold = 0.15f;

	public const float DefenderThresholdMin = 0.1f;

	public const float DefenderThresholdMax = 0.25f;

	public const float AssaultThreshold = 0.2f;

	private static readonly string[] DisruptorEMP = new string[2] { "Weapon_GrenadeEMP", "Gun_EMPLauncher" };

	private static readonly string[] DisruptorGeneral = new string[8] { "Gun_SmokeLauncher", "Gun_SmokeLauncher", "Gun_SmokeLauncher", "Gun_ToxbombLauncher", "Weapon_GrenadeTox", "Weapon_GrenadeEMP", "Gun_NerveSpiker", "NerveSpiker" };

	private static float AlertAdjusted(float baseChance, int alertLevel)
	{
		return Mathf.Clamp01(baseChance + 0.1f * (float)Mathf.Clamp(alertLevel, 0, 5));
	}

	public static void PlanConsumption(StrongholdData d, int total, out int min, out int max)
	{
		min = 0;
		max = 0;
		if (d != null && d.groupPlanDecided)
		{
			if (d.hasElite)
			{
				min += 3;
				max += 5;
			}
			if (d.hasZoneGuard)
			{
				int num = Mathf.Max(1, Mathf.RoundToInt((float)total / 4.5f));
				min += num;
				max += num;
			}
			if (d.hasHerald)
			{
				min += 3;
				max += 3;
			}
			min += d.pursuerGroups * 6;
			max += d.pursuerGroups * 12;
			if (d.hasAssault)
			{
				min += 5;
				max += 8;
			}
			min += d.disruptorGroups * 4;
			max += d.disruptorGroups * 7;
			if (total > 0)
			{
				min = Mathf.Min(min, total);
				max = Mathf.Min(max, total);
			}
		}
	}

	public static bool IsApplicable(Faction f, StrongholdData d)
	{
		if (f == null || d == null)
		{
			return false;
		}
		if (!f.def.humanlikeFaction)
		{
			return false;
		}
		if (EESFactionKind.IsInsect(f))
		{
			return false;
		}
		if (BattlefieldUtility.IsBattlefield(d.roleDefName))
		{
			return false;
		}
		if (FakeOutpostUtility.IsFake(d.roleDefName))
		{
			return false;
		}
		if (EESCustomBaseDetector.IsDedicatedOutpostFaction(f))
		{
			return false;
		}
		return true;
	}

	private static bool EliteTierEligible(Faction f, int tier)
	{
		switch (f.def.techLevel)
		{
		case TechLevel.Animal:
		case TechLevel.Neolithic:
			return tier >= 3;
		case TechLevel.Medieval:
			return tier >= 5;
		case TechLevel.Industrial:
			if (tier >= 9)
			{
				return tier <= 10;
			}
			return false;
		case TechLevel.Spacer:
			if (tier >= 8)
			{
				return tier <= 11;
			}
			return false;
		case TechLevel.Ultra:
		case TechLevel.Archotech:
			return true;
		default:
			return tier >= 8;
		}
	}

	public static void DecidePlan(StrongholdData d, Settlement s)
	{
		if (d == null || s == null || d.groupPlanForced)
		{
			return;
		}
		d.hasElite = false;
		d.eliteCount = 0;
		d.hasZoneGuard = false;
		d.zoneGuardDir = -1;
		d.pursuerGroups = 0;
		d.hasHerald = false;
		d.disruptorGroups = 0;
		d.hasAssault = false;
		d.forcedAssaultMode = -1;
		d.groupPlanDecided = true;
		if (EESPuppetUtility.IsPuppetOutpost(s))
		{
			return;
		}
		Faction faction = s.Faction;
		if (IsApplicable(faction, d))
		{
			int tier = d.tier;
			int alertLevel = d.alertLevel;
			if (EliteTierEligible(faction, tier) && Roll(s.ID, 225, AlertAdjusted(0.33f, alertLevel)))
			{
				d.hasElite = true;
				d.eliteCount = RandRange(s.ID, 226, 3, 5);
			}
			if (tier >= 6 && Roll(s.ID, 32, AlertAdjusted(0.4f, alertLevel)))
			{
				d.hasZoneGuard = true;
				d.zoneGuardDir = RandRange(s.ID, 33, 0, 3);
			}
			d.pursuerGroups = RollGroupCount(s.ID, 48, AlertAdjusted(0.75f, alertLevel), 0.75f, new float[3] { 0.4f, 0.25f, 0.1f });
			if (tier >= 3 && Roll(s.ID, 64, AlertAdjusted(0.1f, alertLevel)))
			{
				d.hasHerald = true;
			}
			if ((int)faction.def.techLevel >= 4)
			{
				d.disruptorGroups = RollGroupCount(s.ID, 80, AlertAdjusted(0.35f, alertLevel), 0.35f, new float[2] { 0.2f, 0.15f });
			}
			if (Faction.OfEmpire != null && faction == Faction.OfEmpire)
			{
				d.hasAssault = true;
			}
			else if ((int)faction.def.techLevel >= 5 && Roll(s.ID, 96, 0.1f))
			{
				d.hasAssault = true;
			}
			ApplyRoleProfile(d, faction, s.ID);
			EESLog.Message(string.Format("GarrisonGroups plan [{0} T{1} alert{2}]: elite={3} ", faction.Name, tier, alertLevel, d.hasElite ? d.eliteCount.ToString() : "-") + string.Format("zone={0} pursuer={1} herald={2} disruptor={3} assault={4}.", d.hasZoneGuard ? DirName(d.zoneGuardDir) : "-", d.pursuerGroups, d.hasHerald, d.disruptorGroups, d.hasAssault));
		}
	}

	private static void ApplyRoleProfile(StrongholdData d, Faction faction, int settlementId)
	{
		if (d == null || faction == null || d.roleDefName.NullOrEmpty())
		{
			return;
		}
		string role = d.roleDefName;
		bool tierSix = d.tier >= 6;
		bool tierThree = d.tier >= 3;
		if (role == StrongholdRoles.TrainingCamp || role == StrongholdRoles.CommandFortress || role == StrongholdRoles.NobleEstate)
		{
			if (!d.hasElite && EliteTierEligible(faction, d.tier))
			{
				d.hasElite = true;
				d.eliteCount = RandRange(settlementId, 0x2A1, 4, 5);
			}
		}
		if (role == StrongholdRoles.SupplyDepot)
		{
			d.pursuerGroups = Mathf.Min(3, d.pursuerGroups + 1);
		}
		else if (role == StrongholdRoles.AmmoDepot || role == StrongholdRoles.MortarBattery || role == StrongholdRoles.ArtilleryBase)
		{
			if (tierSix && !d.hasZoneGuard)
			{
				d.hasZoneGuard = true;
				d.zoneGuardDir = RandRange(settlementId, 0x2A2, 0, 3);
			}
			if ((int)faction.def.techLevel >= 4)
			{
				d.disruptorGroups = Mathf.Min(2, d.disruptorGroups + 1);
			}
		}
		else if (role == StrongholdRoles.PrisonCamp)
		{
			if (tierThree)
			{
				d.hasHerald = true;
			}
			if (tierSix && !d.hasZoneGuard)
			{
				d.hasZoneGuard = true;
				d.zoneGuardDir = RandRange(settlementId, 0x2A3, 0, 3);
			}
		}
		else if (role == StrongholdRoles.ResearchOutpost || role == StrongholdRoles.PsychicShrine || role == StrongholdRoles.JammingStation)
		{
			if (tierThree)
			{
				d.hasHerald = true;
			}
		}
		else if (role == StrongholdRoles.Mine)
		{
			if (tierThree && !d.hasZoneGuard)
			{
				d.hasZoneGuard = true;
				d.zoneGuardDir = RandRange(settlementId, 0x2A4, 0, 3);
			}
			d.pursuerGroups = Mathf.Min(3, d.pursuerGroups + 1);
		}
	}

	private static bool Roll(int id, int seed, float chance)
	{
		Rand.PushState(Gen.HashCombineInt(id, 0x6A22000 ^ seed));
		bool result = Rand.Value < chance;
		Rand.PopState();
		return result;
	}

	private static int RandRange(int id, int seed, int min, int max)
	{
		Rand.PushState(Gen.HashCombineInt(id, 0x6A22000 ^ seed));
		int result = Rand.RangeInclusive(min, max);
		Rand.PopState();
		return result;
	}

	private static int RollGroupCount(int id, int seed, float appearAdj, float baseAppear, float[] absProbs)
	{
		Rand.PushState(Gen.HashCombineInt(id, 0x6A22000 ^ seed));
		float value = Rand.Value;
		int num = 0;
		if (value < appearAdj)
		{
			float num2 = Rand.Value * baseAppear;
			float num3 = 0f;
			for (int i = 0; i < absProbs.Length; i++)
			{
				num3 += absProbs[i];
				if (num2 < num3)
				{
					num = i + 1;
					break;
				}
			}
			if (num == 0)
			{
				num = absProbs.Length;
			}
		}
		Rand.PopState();
		return num;
	}

	public static string DirName(int dir)
	{
		return dir switch
		{
			0 => "북",
			1 => "동",
			2 => "남",
			3 => "서",
			_ => "?",
		};
	}

	public static Color GroupColor(GarrisonGroupType t)
	{
		return t switch
		{
			GarrisonGroupType.Elite => new Color(0.45f, 0.75f, 1f),
			GarrisonGroupType.Herald => new Color(1f, 0.55f, 0.1f),
			_ => Color.white,
		};
	}

	public static void DyePawn(Pawn p, GarrisonGroupType t)
	{
		if (p?.apparel == null || (t != GarrisonGroupType.Elite && t != GarrisonGroupType.Herald))
		{
			return;
		}
		Color color = GroupColor(t);
		foreach (Apparel item in p.apparel.WornApparel)
		{
			item.TryGetComp<CompColorable>()?.SetColor(color);
		}
	}

	public static void ApplyGroups(Map map, Faction faction, StrongholdData d, CellRect rect, List<Pawn> bPawns)
	{
		if (map == null || faction == null || d == null || bPawns == null || bPawns.Count == 0)
		{
			return;
		}
		IntVec3 center = ((rect.Width > 0) ? rect.CenterCell : map.Center);
		int count = bPawns.Count;
		int settlementId = d.settlementId;
		List<Pawn> list = new List<Pawn>(bPawns);
		StringBuilder stringBuilder = new StringBuilder();
		if (d.hasElite)
		{
			List<Pawn> list2 = Take(list, d.eliteCount);
			if (list2.Count > 0)
			{
				List<Pawn> list3 = MakeElite(map, faction, list2, settlementId);
				foreach (Pawn item in list3)
				{
					Finish(item, GarrisonGroupType.Elite);
				}
				LordMaker.MakeNewLord(faction, new LordJob_EESDefendGroup(faction, center, GarrisonGroupType.Elite, 0f, list3.Count), map, list3);
				stringBuilder.Append($" 정예{list3.Count}");
			}
		}
		if (d.hasZoneGuard && list.Count > 0)
		{
			int n = Mathf.Max(1, Mathf.RoundToInt((float)count / 4.5f));
			List<Pawn> list4 = Take(list, n);
			if (list4.Count > 0)
			{
				IntVec3 intVec = EdgeGuardPoint(rect, d.zoneGuardDir);
				foreach (Pawn item2 in list4)
				{
					GiveRangedWeapon(item2, faction);
					Finish(item2, GarrisonGroupType.ZoneGuard);
				}
				LordJob_EESDefendGroup lordJob_EESDefendGroup = new LordJob_EESDefendGroup(faction, intVec, GarrisonGroupType.ZoneGuard, 0.08f, list4.Count);
				lordJob_EESDefendGroup.guardCells = FindBarricadeLineCells(map, faction, rect, d.zoneGuardDir, list4.Count);
				lordJob_EESDefendGroup.holdPoint = ((lordJob_EESDefendGroup.guardCells.Count > 0) ? lordJob_EESDefendGroup.guardCells[lordJob_EESDefendGroup.guardCells.Count / 2] : intVec);
				lordJob_EESDefendGroup.holdRadius = Mathf.Clamp((float)rect.Width / 5f, 4f, 8f);
				LordMaker.MakeNewLord(faction, lordJob_EESDefendGroup, map, list4);
				stringBuilder.Append($" 경비{list4.Count}({DirName(d.zoneGuardDir)}/바리케이드{lordJob_EESDefendGroup.guardCells.Count})");
			}
		}
		if (d.hasHerald && list.Count > 0)
		{
			List<Pawn> list5 = Take(list, 3);
			if (list5.Count > 0)
			{
				foreach (Pawn item3 in list5)
				{
					Disarm(item3);
					Finish(item3, GarrisonGroupType.Herald);
				}
				LordMaker.MakeNewLord(faction, new LordJob_EESDefendGroup(faction, center, GarrisonGroupType.Herald, 0.7f, list5.Count), map, list5);
				stringBuilder.Append($" 전령{list5.Count}");
			}
		}
		for (int i = 0; i < d.pursuerGroups; i++)
		{
			if (list.Count <= 0)
			{
				break;
			}
			int n2 = TierWeightedCount(6, 12, d.tier, settlementId, 768 + i);
			List<Pawn> list6 = Take(list, n2);
			if (list6.Count == 0)
			{
				break;
			}
			foreach (Pawn item4 in list6)
			{
				Finish(item4, GarrisonGroupType.Pursuer);
			}
			LordJob_EESDefendGroup lordJob_EESDefendGroup2 = new LordJob_EESDefendGroup(faction, center, GarrisonGroupType.Pursuer, 0.12f, list6.Count);
			lordJob_EESDefendGroup2.assaultDelayTicks = (i + 1) * 2500;
			bool flag = EESHostility.AnyEnemyOf(map, faction);
			if (flag)
			{
				lordJob_EESDefendGroup2.assaultTick = Find.TickManager.TicksGame + lordJob_EESDefendGroup2.assaultDelayTicks;
			}
			LordMaker.MakeNewLord(faction, lordJob_EESDefendGroup2, map, list6);
			stringBuilder.Append(string.Format(" 추격{0}={1}({2})", i + 1, list6.Count, flag ? $"무장+{lordJob_EESDefendGroup2.assaultDelayTicks}t" : "적 대기"));
		}
		if (d.hasAssault && list.Count > 0)
		{
			int n3 = TierWeightedCount(5, 8, d.tier, settlementId, 1536);
			List<Pawn> list7 = Take(list, n3);
			if (list7.Count > 0)
			{
				foreach (Pawn item5 in list7)
				{
					Finish(item5, GarrisonGroupType.Assault);
				}
				AssaultGroupUtility.BeginBoarding(map, faction, d, list7);
				stringBuilder.Append($" 강습{list7.Count}");
			}
		}
		bool flag2 = EESHostility.AnyEnemyMech(map, faction) || AnyPlayerMech(map);
		for (int j = 0; j < d.disruptorGroups; j++)
		{
			if (list.Count <= 0)
			{
				break;
			}
			int n4 = TierWeightedCount(4, 7, d.tier, settlementId, 1280 + j);
			List<Pawn> list8 = Take(list, n4);
			if (list8.Count == 0)
			{
				break;
			}
			foreach (Pawn item6 in list8)
			{
				GiveDisruptorWeapon(item6, faction, flag2, settlementId);
				Finish(item6, GarrisonGroupType.Disruptor);
			}
			LordMaker.MakeNewLord(faction, new LordJob_EESDefendGroup(faction, center, GarrisonGroupType.Disruptor, 0.15f, list8.Count), map, list8);
			stringBuilder.Append(string.Format(" 교란{0}{1}", list8.Count, flag2 ? "[EMP]" : ""));
		}
		int num = 0;
		while (list.Count > 0)
		{
			int n5 = TierWeightedCount(5, 15, d.tier, settlementId, 1792 + num);
			List<Pawn> list9 = Take(list, n5);
			if (list9.Count == 0)
			{
				break;
			}
			float num2 = 0.1f + 0.15f * Frac(settlementId, 1808 + num);
			IntVec3 center2 = DispersePoint(rect, num);
			foreach (Pawn item7 in list9)
			{
				Finish(item7, GarrisonGroupType.Defender);
			}
			LordMaker.MakeNewLord(faction, new LordJob_EESDefendGroup(faction, center2, GarrisonGroupType.Defender, num2, list9.Count), map, list9);
			stringBuilder.Append($" 도주{list9.Count}@{num2:P0}");
			num++;
		}
		EESLog.Message($"GarrisonGroups apply [{faction.Name} T{d.tier}] B={count}:{stringBuilder}.");
	}

	private static void Finish(Pawn p, GarrisonGroupType t)
	{
		DyePawn(p, t);
		if (p?.mindState != null)
		{
			p.mindState.canFleeIndividual = false;
		}
	}

	private static List<Pawn> MakeElite(Map map, Faction faction, List<Pawn> carved, int id)
	{
		PawnKindDef pawnKindDef = BestCombatKind(faction);
		List<Pawn> list = new List<Pawn>();
		int num = 0;
		foreach (Pawn item in carved)
		{
			IntVec3 position = item.Position;
			if (pawnKindDef == null)
			{
				UpgradeEliteGear(item, id);
				list.Add(item);
				continue;
			}
			try
			{
				Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(pawnKindDef, faction, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true, allowDead: false, allowDowned: false, canGeneratePawnRelations: true, mustBeCapableOfViolence: true, 1f, forceAddFreeWarmLayerIfNeeded: false, allowGay: true, allowPregnant: false, allowFood: true, allowAddictions: true, inhabitant: true));
				GenSpawn.Spawn(pawn, position, map);
				UpgradeEliteGear(pawn, id);
				if (StrongholdGarrisonUtility.GiveSiegeRations(pawn) > 0)
				{
					num++;
				}
				list.Add(pawn);
				item.Destroy();
			}
			catch (Exception ex)
			{
				EESLog.Warning("MakeElite " + pawnKindDef?.defName + ": " + ex.Message);
				UpgradeEliteGear(item, id);
				list.Add(item);
			}
		}
		if (num > 0)
		{
			EESLog.Message($"Elite rations [{faction?.Name}]: 재생성 정예병 {num}명에 비상식량 지급.");
		}
		return list;
	}

	private static PawnKindDef BestCombatKind(Faction faction)
	{
		PawnKindDef pawnKindDef = null;
		float num = -1f;
		List<PawnGroupMaker> list = faction.def?.pawnGroupMakers;
		if (list != null)
		{
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i]?.kindDef != PawnGroupKindDefOf.Combat)
				{
					continue;
				}
				List<PawnGenOption> options = list[i].options;
				if (options == null)
				{
					continue;
				}
				for (int j = 0; j < options.Count; j++)
				{
					PawnKindDef pawnKindDef2 = options[j]?.kind;
					if (pawnKindDef2 != null && pawnKindDef2.combatPower > num)
					{
						num = pawnKindDef2.combatPower;
						pawnKindDef = pawnKindDef2;
					}
				}
			}
		}
		return pawnKindDef ?? faction.RandomPawnKind();
	}

	private static IntVec3 EdgeGuardPoint(CellRect r, int dir)
	{
		if (r.Width <= 0)
		{
			return r.CenterCell;
		}
		IntVec3 centerCell = r.CenterCell;
		int num = Mathf.Max(3, r.Width / 2 - 3);
		int num2 = Mathf.Max(3, r.Height / 2 - 3);
		return dir switch
		{
			0 => centerCell + new IntVec3(0, 0, num2),
			1 => centerCell + new IntVec3(num, 0, 0),
			2 => centerCell + new IntVec3(0, 0, -num2),
			3 => centerCell + new IntVec3(-num, 0, 0),
			_ => centerCell,
		};
	}

	private static List<Pawn> Take(List<Pawn> pool, int n)
	{
		List<Pawn> list = new List<Pawn>();
		int num = Mathf.Min(Mathf.Max(0, n), pool.Count);
		for (int i = 0; i < num; i++)
		{
			list.Add(pool[i]);
		}
		pool.RemoveRange(0, num);
		return list;
	}

	private static int TierWeightedCount(int min, int max, int tier, int id, int seed)
	{
		float t = Mathf.Clamp01((float)(tier - 1) / 12f);
		float num = Mathf.Lerp(min, max, t);
		float num2 = (float)(max - min) * (Frac(id, seed) - 0.5f);
		return Mathf.Clamp(Mathf.RoundToInt(num + num2), min, max);
	}

	private static float Frac(int id, int seed)
	{
		Rand.PushState(Gen.HashCombineInt(id, 0x6A22000 ^ seed));
		float value = Rand.Value;
		Rand.PopState();
		return value;
	}

	private static IntVec3 QuadrantPoint(CellRect r, int dir)
	{
		if (r.Width <= 0)
		{
			return r.CenterCell;
		}
		IntVec3 centerCell = r.CenterCell;
		int num = Mathf.Max(2, r.Width / 3);
		int num2 = Mathf.Max(2, r.Height / 3);
		return dir switch
		{
			0 => centerCell + new IntVec3(0, 0, num2),
			1 => centerCell + new IntVec3(num, 0, 0),
			2 => centerCell + new IntVec3(0, 0, -num2),
			3 => centerCell + new IntVec3(-num, 0, 0),
			_ => centerCell,
		};
	}

	private static IntVec3 DispersePoint(CellRect r, int idx)
	{
		if (r.Width <= 0)
		{
			return r.CenterCell;
		}
		int[] array = new int[5] { -1, 0, 1, 2, 3 };
		int num = array[idx % array.Length];
		if (num >= 0)
		{
			return QuadrantPoint(r, num);
		}
		return r.CenterCell;
	}

	private static List<IntVec3> FindBarricadeLineCells(Map map, Faction faction, CellRect rect, int dir, int count)
	{
		List<IntVec3> list = new List<IntVec3>();
		if (map?.listerBuildings == null || rect.Width <= 0)
		{
			return list;
		}
		List<KeyValuePair<int, IntVec3>> list2 = new List<KeyValuePair<int, IntVec3>>();
		List<Building> allBuildingsNonColonist = map.listerBuildings.allBuildingsNonColonist;
		for (int i = 0; i < allBuildingsNonColonist.Count; i++)
		{
			Building building = allBuildingsNonColonist[i];
			if (building == null || building.Destroyed || building.Faction != faction)
			{
				continue;
			}
			ThingDef def = building.def;
			if (def.building == null || !(def.fillPercent > 0f) || !(def.fillPercent < 1f) || def.building.IsTurret || def.IsDoor || def.building.isNaturalRock)
			{
				continue;
			}
			IntVec3 position = building.Position;
			bool flag;
			int key;
			IntVec3 intVec;
			switch (dir)
			{
			case 0:
				flag = position.z >= rect.maxZ - 1 && position.x >= rect.minX - 2 && position.x <= rect.maxX + 2;
				key = position.x;
				intVec = new IntVec3(0, 0, -1);
				break;
			case 1:
				flag = position.x >= rect.maxX - 1 && position.z >= rect.minZ - 2 && position.z <= rect.maxZ + 2;
				key = position.z;
				intVec = new IntVec3(-1, 0, 0);
				break;
			case 2:
				flag = position.z <= rect.minZ + 1 && position.x >= rect.minX - 2 && position.x <= rect.maxX + 2;
				key = position.x;
				intVec = new IntVec3(0, 0, 1);
				break;
			case 3:
				flag = position.x <= rect.minX + 1 && position.z >= rect.minZ - 2 && position.z <= rect.maxZ + 2;
				key = position.z;
				intVec = new IntVec3(1, 0, 0);
				break;
			default:
				flag = false;
				key = 0;
				intVec = IntVec3.Zero;
				break;
			}
			if (flag)
			{
				IntVec3 intVec2 = position + intVec;
				IntVec3 intVec3 = position + intVec + intVec;
				IntVec3 value = ((intVec2.InBounds(map) && intVec2.Standable(map)) ? intVec2 : ((intVec3.InBounds(map) && intVec3.Standable(map)) ? intVec3 : IntVec3.Invalid));
				if (value.IsValid)
				{
					list2.Add(new KeyValuePair<int, IntVec3>(key, value));
				}
			}
		}
		list2.Sort((KeyValuePair<int, IntVec3> a, KeyValuePair<int, IntVec3> b) => a.Key.CompareTo(b.Key));
		List<IntVec3> list3 = new List<IntVec3>();
		for (int num = 0; num < list2.Count; num++)
		{
			if (!list3.Contains(list2[num].Value))
			{
				list3.Add(list2[num].Value);
			}
		}
		if (list3.Count == 0)
		{
			return list;
		}
		if (count < list3.Count)
		{
			for (int num2 = 0; num2 < count; num2++)
			{
				list.Add(list3[(int)((long)num2 * (long)list3.Count / count)]);
			}
		}
		else
		{
			list.AddRange(list3);
		}
		return list;
	}

	private static bool AnyPlayerMech(Map map)
	{
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			if (allPawnsSpawned[i]?.Faction == Faction.OfPlayer && allPawnsSpawned[i].RaceProps != null && allPawnsSpawned[i].RaceProps.IsMechanoid)
			{
				return true;
			}
		}
		return false;
	}

	private static void EquipWeapon(Pawn p, ThingDef wd, QualityCategory? q = null)
	{
		if (wd == null || p?.equipment == null)
		{
			return;
		}
		try
		{
			p.equipment.DestroyAllEquipment();
			ThingDef stuff = (wd.MadeFromStuff ? GenStuff.DefaultStuffFor(wd) : null);
			ThingWithComps thingWithComps = (ThingWithComps)ThingMaker.MakeThing(wd, stuff);
			if (q.HasValue)
			{
				thingWithComps.TryGetComp<CompQuality>()?.SetQuality(q.Value, ArtGenerationContext.Outsider);
			}
			p.equipment.AddEquipment(thingWithComps);
		}
		catch (Exception ex)
		{
			EESLog.Warning("EquipWeapon " + wd.defName + ": " + ex.Message);
		}
	}

	private static void WearApparel(Pawn p, ThingDef ad)
	{
		if (ad == null || p?.apparel == null)
		{
			return;
		}
		try
		{
			ThingDef stuff = (ad.MadeFromStuff ? GenStuff.DefaultStuffFor(ad) : null);
			Apparel newApparel = (Apparel)ThingMaker.MakeThing(ad, stuff);
			p.apparel.Wear(newApparel, dropReplacedApparel: false);
		}
		catch (Exception ex)
		{
			EESLog.Warning("WearApparel " + ad.defName + ": " + ex.Message);
		}
	}

	private static void UpgradeEliteGear(Pawn p, int id)
	{
		QualityCategory q = ((Frac(id, 0xE3 ^ p.thingIDNumber) < 0.5f) ? QualityCategory.Excellent : QualityCategory.Masterwork);
		p.equipment?.Primary?.TryGetComp<CompQuality>()?.SetQuality(q, ArtGenerationContext.Outsider);
		if (p.apparel == null)
		{
			return;
		}
		foreach (Apparel item in p.apparel.WornApparel)
		{
			item.TryGetComp<CompQuality>()?.SetQuality(q, ArtGenerationContext.Outsider);
		}
	}

	private static void GiveRangedWeapon(Pawn p, Faction faction)
	{
		ThingWithComps thingWithComps = p.equipment?.Primary;
		if (thingWithComps == null || !thingWithComps.def.IsRangedWeapon)
		{
			TechLevel techLevel = faction.def?.techLevel ?? TechLevel.Industrial;
			if ((int)techLevel > 2)
			{
				ThingDef wd = DefDatabase<ThingDef>.GetNamedSilentFail(((int)techLevel >= 5) ? "Gun_ChargeRifle" : (((int)techLevel >= 4) ? "Gun_BoltActionRifle" : "Bow_Recurve")) ?? DefDatabase<ThingDef>.GetNamedSilentFail("Gun_Revolver");
				EquipWeapon(p, wd);
			}
		}
	}

	private static void Disarm(Pawn p)
	{
		try
		{
			p.equipment?.DestroyAllEquipment();
		}
		catch
		{
		}
	}

	private static void GiveDisruptorWeapon(Pawn p, Faction faction, bool emp, int id)
	{
		string[] array = (emp ? DisruptorEMP : DisruptorGeneral);
		List<ThingDef> list = new List<ThingDef>();
		for (int i = 0; i < array.Length; i++)
		{
			ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail(array[i]);
			if (namedSilentFail != null)
			{
				list.Add(namedSilentFail);
			}
		}
		if (list.Count > 0)
		{
			int index = Mathf.Clamp(Mathf.FloorToInt(Frac(id, 0x5A0 ^ p.thingIDNumber) * (float)list.Count), 0, list.Count - 1);
			EquipWeapon(p, list[index]);
		}
		WearApparel(p, DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_SmokepopBelt"));
	}
}
