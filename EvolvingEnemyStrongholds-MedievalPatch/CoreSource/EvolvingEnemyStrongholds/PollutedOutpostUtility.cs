using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class PollutedOutpostUtility
{
	public const string Role = "PollutedOutpost";

	public const string HediffName = "EES_ToxicEmpowerment";

	public const float RewardMul = 1.1f;

	public static bool IsPolluted(string role)
	{
		return role == "PollutedOutpost";
	}

	public static void ApplyPollutedEffects(Map map, StrongholdData data, Faction faction)
	{
		if (map == null || faction == null)
		{
			return;
		}
		try
		{
			int num = RegisterPermanentToxicFallout(map);
			int num2 = SwapGeneratorsToToxifier(map, faction);
			int num3 = EmpowerAllPawns(map, faction);
			EESLog.Message(string.Format("PollutedOutpost [{0} T{1}]: toxicFallout={2} ", faction.Name, data?.tier, (num > 0) ? "ON" : "(이미 활성)") + $"발전기→독소 {num2}개 | 강화 폰 {num3}명(방독면+Hediff+스킬).");
		}
		catch (Exception ex)
		{
			EESLog.Error("PollutedOutpost ApplyPollutedEffects failed: " + ex);
		}
	}

	private static int RegisterPermanentToxicFallout(Map map)
	{
		GameConditionDef namedSilentFail = DefDatabase<GameConditionDef>.GetNamedSilentFail("ToxicFallout");
		if (namedSilentFail == null)
		{
			return 0;
		}
		if (map.gameConditionManager.ConditionIsActive(namedSilentFail))
		{
			return 0;
		}
		GameCondition cond = GameConditionMaker.MakeConditionPermanent(namedSilentFail);
		map.gameConditionManager.RegisterCondition(cond);
		return 1;
	}

	public static int SwapGeneratorsToToxifier(Map map, Faction faction)
	{
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("ToxifierGenerator");
		if (namedSilentFail == null)
		{
			return 0;
		}
		List<Building> list = new List<Building>();
		List<Building> allBuildingsNonColonist = map.listerBuildings.allBuildingsNonColonist;
		for (int i = 0; i < allBuildingsNonColonist.Count; i++)
		{
			Building building = allBuildingsNonColonist[i];
			if (building != null && !building.Destroyed && building.Faction == faction && building.def != namedSilentFail)
			{
				ThingWithComps thingWithComps = building;
				if (thingWithComps != null && thingWithComps.GetComp<CompPowerPlant>() != null)
				{
					list.Add(building);
				}
			}
		}
		int num = 0;
		foreach (Building item in list)
		{
			if (item.Destroyed)
			{
				continue;
			}
			CellRect cellRect = item.OccupiedRect();
			try
			{
				item.Destroy();
			}
			catch
			{
			}
			for (int j = cellRect.minX; j + 1 <= cellRect.maxX; j += 2)
			{
				for (int k = cellRect.minZ; k + 1 <= cellRect.maxZ; k += 2)
				{
					IntVec3 pos = new IntVec3(j, 0, k);
					if (PlaceToxifier(map, faction, namedSilentFail, pos))
					{
						num++;
					}
				}
			}
		}
		return num;
	}

	private static bool PlaceToxifier(Map map, Faction faction, ThingDef toxifier, IntVec3 pos)
	{
		foreach (IntVec3 item in new CellRect(pos.x, pos.z, 2, 2))
		{
			if (!item.InBounds(map))
			{
				return false;
			}
			Building edifice = item.GetEdifice(map);
			if (edifice != null && !edifice.Destroyed)
			{
				try
				{
					edifice.Destroy();
				}
				catch
				{
				}
			}
		}
		try
		{
			Thing thing = ThingMaker.MakeThing(toxifier);
			thing.SetFactionDirect(faction);
			GenSpawn.Spawn(thing, pos, map, Rot4.North);
			return true;
		}
		catch (Exception ex)
		{
			EESLog.Warning("PollutedOutpost toxifier 배치 실패: " + ex.Message);
			return false;
		}
	}

	private static int EmpowerAllPawns(Map map, Faction faction)
	{
		int num = 0;
		foreach (Pawn item in new List<Pawn>(map.mapPawns.SpawnedPawnsInFaction(faction)))
		{
			if (EnsurePawnPolluted(item))
			{
				num++;
			}
		}
		return num;
	}

	public static bool EnsurePawnPolluted(Pawn p)
	{
		if (p == null || p.Dead || !p.RaceProps.Humanlike)
		{
			return false;
		}
		bool result = false;
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_GasMask");
		if (namedSilentFail != null && p.apparel != null && !PawnWearsGasMask(p, namedSilentFail))
		{
			try
			{
				Apparel newApparel = (Apparel)ThingMaker.MakeThing(namedSilentFail);
				p.apparel.Wear(newApparel, dropReplacedApparel: false);
				result = true;
			}
			catch (Exception ex)
			{
				EESLog.Warning("PollutedOutpost 방독면 착용 실패: " + ex.Message);
			}
		}
		HediffDef namedSilentFail2 = DefDatabase<HediffDef>.GetNamedSilentFail("EES_ToxicEmpowerment");
		if (namedSilentFail2 != null && p.health != null && !p.health.hediffSet.HasHediff(namedSilentFail2))
		{
			try
			{
				p.health.AddHediff(namedSilentFail2);
				BumpCombatSkills(p);
				result = true;
			}
			catch (Exception ex2)
			{
				EESLog.Warning("PollutedOutpost Hediff 부여 실패: " + ex2.Message);
			}
		}
		return result;
	}

	private static bool PawnWearsGasMask(Pawn p, ThingDef maskDef)
	{
		List<Apparel> wornApparel = p.apparel.WornApparel;
		for (int i = 0; i < wornApparel.Count; i++)
		{
			if (wornApparel[i].def == maskDef)
			{
				return true;
			}
		}
		return false;
	}

	private static void BumpCombatSkills(Pawn p)
	{
		if (p.skills != null)
		{
			SkillRecord skill = p.skills.GetSkill(SkillDefOf.Shooting);
			SkillRecord skill2 = p.skills.GetSkill(SkillDefOf.Melee);
			if (skill != null)
			{
				skill.Level = Math.Min(20, skill.Level + 2);
			}
			if (skill2 != null)
			{
				skill2.Level = Math.Min(20, skill2.Level + 2);
			}
		}
	}
}
