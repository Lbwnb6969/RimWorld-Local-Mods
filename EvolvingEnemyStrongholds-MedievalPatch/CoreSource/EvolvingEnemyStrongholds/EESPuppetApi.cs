using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class EESPuppetApi
{
	public static bool Available()
	{
		return WorldComponent_Strongholds.Instance != null;
	}

	public static bool IsPuppetOutpost(Settlement s)
	{
		return EESPuppetUtility.IsPuppetOutpost(s);
	}

	public static int[] GetOutpostState(Settlement s)
	{
		StrongholdData strongholdData = DataOf(s);
		if (strongholdData == null)
		{
			return null;
		}
		int num = EESPuppetUtility.CapFor(s);
		bool flag = strongholdData.tier >= num;
		int num2 = -1;
		if (!flag)
		{
			int num3 = EESPuppetUtility.TicksToNextTier(strongholdData.tier);
			int num4 = ((strongholdData.tierClockTick >= 0) ? (Find.TickManager.TicksGame - strongholdData.tierClockTick) : 0);
			num2 = Mathf.Max(0, num3 - num4);
		}
		return new int[10]
		{
			strongholdData.tier,
			num,
			strongholdData.hasMortarBattery ? 1 : 0,
			flag ? (-1) : EESPuppetUtility.UpgradeCost(strongholdData.tier),
			EESPuppetUtility.CanAddMortar(strongholdData) ? 5000 : (-1),
			flag ? (-1) : EESPuppetUtility.DaysToNextTier(strongholdData.tier),
			num2,
			strongholdData.hasCommander ? 1 : 0,
			6,
			EESPuppetUtility.AssaultAllowed(s) ? 1 : 0
		};
	}

	public static string GetRole(Settlement s)
	{
		return DataOf(s)?.roleDefName;
	}

	/// <summary>
	/// Returns the persistent territory-local feature key through the public
	/// compatibility surface. External configuration/puppet integrations can
	/// use this API without reaching into StrongholdData directly.
	/// </summary>
	public static string GetFeature(Settlement s)
	{
		if (s?.Faction == null || s.Faction.IsPlayer || EESFactionBlacklist.IsBlocked(s.Faction) ||
			EESCustomBaseDetector.IsForeignSettlement(s)) return null;
		StrongholdData data = WorldComponent_Strongholds.Instance?.GetExisting(s.ID);
		return StrongholdFeatureUtility.IsValid(data?.featureKey) ? data.featureKey : null;
	}

	public static string GetFeatureLabel(Settlement s)
	{
		return StrongholdFeatureUtility.Label(GetFeature(s));
	}

	public static string GetFeatureDescription(Settlement s)
	{
		return StrongholdFeatureUtility.Description(GetFeature(s));
	}

	public static bool TrySetForcedFeature(Settlement s, string key)
	{
		return StrongholdFeatureUtility.TrySetForcedFeature(s, key);
	}

	public static bool TryClearForcedFeature(Settlement s)
	{
		return StrongholdFeatureUtility.TryClearForcedFeature(s);
	}

	public static string GetCommanderType(Settlement s)
	{
		return (DataOf(s)?.commanderType ?? CommanderType.None).ToString();
	}

	public static string RoleLabel(string role)
	{
		return Patch_Settlement_GetGizmos.RoleLabel(role);
	}

	public static string CommanderTypeLabel(string type)
	{
		return ParseCommanderType(type).Nickname();
	}

	public static string[] SelectableRoles(Settlement s)
	{
		StrongholdData strongholdData = DataOf(s);
		if (strongholdData != null)
		{
			return EESPuppetUtility.SelectableRoles(s, strongholdData.tier).ToArray();
		}
		return null;
	}

	public static string[] SelectableCommanderTypes(Settlement s)
	{
		if (DataOf(s) == null)
		{
			return null;
		}
		Faction f = s?.Faction;
		List<string> list = new List<string>();
		foreach (CommanderType value in Enum.GetValues(typeof(CommanderType)))
		{
			if (value != CommanderType.None && value != CommanderType.Security && value != CommanderType.Research && CommanderTypeUtil.AllowedForFactionTech(value, f))
			{
				list.Add(value.ToString());
			}
		}
		return list.ToArray();
	}

	public static int[] GetGarrisonHeadcount(Settlement s)
	{
		StrongholdData strongholdData = DataOf(s);
		if (strongholdData == null)
		{
			return null;
		}
		int num = EstimateCombatants(s, strongholdData);
		GarrisonGroupUtility.PlanConsumption(strongholdData, num, out var min, out var max);
		return new int[3] { num, min, max };
	}

	private static int EstimateCombatants(Settlement s, StrongholdData d)
	{
		if (d.militaryPower <= 0)
		{
			return 0;
		}
		float num = (float)d.militaryPower * (SupplyCaravanUtility.SupplyCutCombatActive(d) ? 0.75f : 1f) * (CryoBarracksUtility.IsCryo(d.roleDefName) ? 0.65f : 1f);
		float num2 = AverageCombatPower(s.Faction);
		if (num2 <= 0f)
		{
			return 0;
		}
		return Mathf.Max(1, Mathf.RoundToInt(num / num2));
	}

	private static float AverageCombatPower(Faction f)
	{
		if (f?.def?.pawnGroupMakers == null)
		{
			return 0f;
		}
		float num = 0f;
		float num2 = 0f;
		for (int i = 0; i < f.def.pawnGroupMakers.Count; i++)
		{
			PawnGroupMaker pawnGroupMaker = f.def.pawnGroupMakers[i];
			if (pawnGroupMaker == null || pawnGroupMaker.kindDef != PawnGroupKindDefOf.Combat || pawnGroupMaker.options == null)
			{
				continue;
			}
			for (int j = 0; j < pawnGroupMaker.options.Count; j++)
			{
				PawnGenOption pawnGenOption = pawnGroupMaker.options[j];
				float valueOrDefault = (pawnGenOption?.kind?.combatPower).GetValueOrDefault();
				if (!(valueOrDefault <= 0f) && !(pawnGenOption.selectionWeight <= 0f))
				{
					num += pawnGenOption.selectionWeight;
					num2 += valueOrDefault * pawnGenOption.selectionWeight;
				}
			}
		}
		if (!(num > 0f))
		{
			return 0f;
		}
		return num2 / num;
	}

	public static string TryUpgradeTier(Settlement s)
	{
		StrongholdData strongholdData = DataOf(s);
		if (strongholdData == null)
		{
			return EESText.T("EES.Puppet.NotOutpost");
		}
		int num = EESPuppetUtility.CapFor(s);
		if (strongholdData.tier >= num)
		{
			return EESText.T("EES.Puppet.AtCap", num);
		}
		int num2 = EESPuppetUtility.UpgradeCost(strongholdData.tier);
		Map map = PaymentMap(num2);
		if (map == null || !TryConsumeSilver(map, num2))
		{
			return EESText.T("EES.Puppet.NeedSilver", num2);
		}
		WorldComponent_Strongholds.Instance.RaisePuppetTierPaid(strongholdData, s);
		return null;
	}

	public static string TryDowngradeTier(Settlement s, int steps)
	{
		StrongholdData strongholdData = DataOf(s);
		if (strongholdData == null)
		{
			return EESText.T("EES.Puppet.NotOutpost");
		}
		if (strongholdData.tier <= 1)
		{
			return EESText.T("EES.Puppet.AtFloor", 1);
		}
		if (WorldComponent_Strongholds.Instance.LowerPuppetTier(strongholdData, s, steps) <= 0)
		{
			EESLog.Warning($"[괴뢰국] {s.LabelCap}: 티어 하향 요청 {steps}단계가 0단계로 끝났다(현재 T{strongholdData.tier}).");
			return EESText.T("EES.Puppet.AtFloor", 1);
		}
		return null;
	}

	public static string TrySetRole(Settlement s, string role)
	{
		StrongholdData strongholdData = DataOf(s);
		if (strongholdData == null)
		{
			return EESText.T("EES.Puppet.NotOutpost");
		}
		if (!EESPuppetUtility.CanSelectRole(s, strongholdData.tier, role))
		{
			return EESText.T("EES.Puppet.RoleUnavailable");
		}
		strongholdData.roleDefName = role;
		strongholdData.roleForced = true;
		StrongholdFeatureUtility.EnsureAssigned(strongholdData, s, WorldComponent_Strongholds.Instance);
		StrongholdScoreUtility.ApplyDerived(strongholdData, s);
		CommanderUtility.DecideCommander(strongholdData, s);
		EESLog.Message("[괴뢰국] " + s.LabelCap + ": 역할 지정 → " + role + " (roleForced).");
		return null;
	}

	public static string TrySetCommander(Settlement s, string commanderType)
	{
		StrongholdData strongholdData = DataOf(s);
		if (strongholdData == null)
		{
			return EESText.T("EES.Puppet.NotOutpost");
		}
		CommanderType commanderType2 = ParseCommanderType(commanderType);
		if (!CommanderTypeUtil.AllowedForFactionTech(commanderType2, s?.Faction))
		{
			EESLog.Message($"[괴뢰국] {s?.LabelCap}: 지휘관 지정 거절 {commanderType2} — 세력 기술수준 " + $"{s?.Faction?.def?.techLevel} < 요구 {CommanderTypeUtil.RequiredTech(commanderType2)}.");
			return EESText.T("EES.Puppet.CommanderTechLow", CommanderTypeUtil.RequiredTech(commanderType2).ToStringHuman());
		}
		strongholdData.commanderType = commanderType2;
		strongholdData.hasCommander = commanderType2 != CommanderType.None;
		strongholdData.commanderForced = true;
		if (strongholdData.hasCommander)
		{
			if (strongholdData.commanderKindDefName.NullOrEmpty())
			{
				CommanderUtility.ForceCommanderKind(strongholdData, s);
			}
		}
		else
		{
			strongholdData.commanderKindDefName = null;
		}
		EESLog.Message($"[괴뢰국] {s.LabelCap}: 지휘관 지정 → {commanderType2} (commanderForced).");
		return null;
	}

	public static string TryAddMortarBattery(Settlement s)
	{
		StrongholdData strongholdData = DataOf(s);
		if (strongholdData == null)
		{
			return EESText.T("EES.Puppet.NotOutpost");
		}
		if (strongholdData.hasMortarBattery)
		{
			return EESText.T("EES.Puppet.MortarAlready");
		}
		if (strongholdData.tier < 6)
		{
			return EESText.T("EES.Puppet.MortarTierLow", 6);
		}
		Map map = PaymentMap(5000);
		if (map == null || !TryConsumeSilver(map, 5000))
		{
			return EESText.T("EES.Puppet.NeedSilver", 5000);
		}
		strongholdData.hasMortarBattery = true;
		strongholdData.facilitiesForced = true;
		EESLog.Message($"[괴뢰국] {s.LabelCap}: 박격포 진지 추가(은 {5000}, facilitiesForced).");
		return null;
	}

	public static int[] GetGarrisonPlan(Settlement s)
	{
		StrongholdData strongholdData = DataOf(s);
		if (strongholdData == null)
		{
			return null;
		}
		return new int[9]
		{
			strongholdData.hasElite ? 1 : 0,
			strongholdData.eliteCount,
			strongholdData.hasZoneGuard ? 1 : 0,
			strongholdData.zoneGuardDir,
			strongholdData.pursuerGroups,
			strongholdData.hasHerald ? 1 : 0,
			strongholdData.disruptorGroups,
			strongholdData.hasAssault ? 1 : 0,
			strongholdData.forcedAssaultMode
		};
	}

	public static string TrySetGarrisonPlan(Settlement s, int[] plan)
	{
		StrongholdData strongholdData = DataOf(s);
		if (strongholdData == null)
		{
			return EESText.T("EES.Puppet.NotOutpost");
		}
		if (plan == null || plan.Length < 9)
		{
			return EESText.T("EES.Puppet.BadPlan");
		}
		if (plan[7] != 0 && !EESPuppetUtility.AssaultAllowed(s))
		{
			return EESText.T("EES.Puppet.AssaultTechLow");
		}
		strongholdData.hasElite = plan[0] != 0;
		strongholdData.eliteCount = (strongholdData.hasElite ? Mathf.Clamp(plan[1], 3, 5) : 0);
		strongholdData.hasZoneGuard = plan[2] != 0;
		strongholdData.zoneGuardDir = (strongholdData.hasZoneGuard ? Mathf.Clamp(plan[3], 0, 3) : (-1));
		strongholdData.pursuerGroups = Mathf.Clamp(plan[4], 0, 3);
		strongholdData.hasHerald = plan[5] != 0;
		strongholdData.disruptorGroups = Mathf.Clamp(plan[6], 0, 2);
		strongholdData.hasAssault = plan[7] != 0;
		strongholdData.forcedAssaultMode = (strongholdData.hasAssault ? Mathf.Clamp(plan[8], -1, 2) : (-1));
		strongholdData.groupPlanDecided = true;
		strongholdData.groupPlanForced = true;
		EESLog.Message($"[괴뢰국] {s.LabelCap}: 수비대 그룹 지정 — 정예{strongholdData.eliteCount} 경비{strongholdData.zoneGuardDir} " + $"추격{strongholdData.pursuerGroups} 전령{strongholdData.hasHerald} 교란{strongholdData.disruptorGroups} 강습{strongholdData.hasAssault}/{strongholdData.forcedAssaultMode}.");
		return null;
	}

	internal static StrongholdData DataOf(Settlement s)
	{
		if (s == null || !EESPuppetUtility.IsPuppetOutpost(s))
		{
			return null;
		}
		return WorldComponent_Strongholds.Instance?.GetOrCreate(s);
	}

	private static CommanderType ParseCommanderType(string type)
	{
		if (!type.NullOrEmpty())
		{
			foreach (CommanderType value in Enum.GetValues(typeof(CommanderType)))
			{
				if (value.ToString() == type)
				{
					return value;
				}
			}
		}
		return CommanderType.None;
	}

	internal static Map PaymentMap(int cost)
	{
		Map currentMap = Find.CurrentMap;
		if (currentMap != null && currentMap.IsPlayerHome && SilverOnMap(currentMap) >= cost)
		{
			return currentMap;
		}
		List<Map> maps = Find.Maps;
		for (int i = 0; i < maps.Count; i++)
		{
			if (maps[i].IsPlayerHome && SilverOnMap(maps[i]) >= cost)
			{
				return maps[i];
			}
		}
		return null;
	}

	internal static int SilverOnMap(Map map)
	{
		if (map == null)
		{
			return 0;
		}
		int num = 0;
		List<Thing> list = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
		for (int i = 0; i < list.Count; i++)
		{
			Thing thing = list[i];
			if (thing != null && !thing.Destroyed && thing.Spawned && !thing.IsForbidden(Faction.OfPlayer) && !thing.IsBurning())
			{
				num += thing.stackCount;
			}
		}
		return num;
	}

	internal static bool TryConsumeSilver(Map map, int cost)
	{
		if (map == null || cost <= 0)
		{
			return cost <= 0;
		}
		if (SilverOnMap(map) < cost)
		{
			return false;
		}
		int num = cost;
		List<Thing> list = new List<Thing>(map.listerThings.ThingsOfDef(ThingDefOf.Silver));
		for (int i = 0; i < list.Count; i++)
		{
			if (num <= 0)
			{
				break;
			}
			Thing thing = list[i];
			if (thing != null && !thing.Destroyed && thing.Spawned && !thing.IsForbidden(Faction.OfPlayer) && !thing.IsBurning())
			{
				int num2 = Mathf.Min(num, thing.stackCount);
				thing.SplitOff(num2).Destroy();
				num -= num2;
			}
		}
		if (num > 0)
		{
			EESLog.Warning($"[괴뢰국] 은 결제 부족분 {num} (요구 {cost}) — 결제 도중 은이 사라졌다.");
			return false;
		}
		EESLog.Message($"[괴뢰국] 은 {cost} 소모 ({map}).");
		return true;
	}
}
