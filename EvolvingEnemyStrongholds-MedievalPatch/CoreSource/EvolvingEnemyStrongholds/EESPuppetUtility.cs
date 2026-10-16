using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class EESPuppetUtility
{
	public const int CapNeolithic = 3;

	public const int CapMedieval = 5;

	public const int CapIndustrial = 8;

	public const int CapSpacer = 10;

	public const int CapUltra = 12;

	public const int TierGrowDaysPerStep = 5;

	public const int UpgradeSilverPerTier = 500;

	public const int MortarSilverCost = 5000;

	private static readonly HashSet<string> ExcludedRoles = new HashSet<string> { "Battlefield", "ArtilleryBase", "PsychicShrine", "JammingStation", "InsectHive", "PrisonCamp", "ResearchOutpost" };

	public static int CapFor(Faction faction)
	{
		int a;
		switch (faction?.def?.techLevel ?? TechLevel.Neolithic)
		{
		case TechLevel.Undefined:
		case TechLevel.Animal:
		case TechLevel.Neolithic:
			a = 3;
			break;
		case TechLevel.Medieval:
			a = 5;
			break;
		case TechLevel.Industrial:
			a = 8;
			break;
		case TechLevel.Spacer:
			a = 10;
			break;
		case TechLevel.Ultra:
			a = 12;
			break;
		case TechLevel.Archotech:
			a = 12;
			break;
		default:
			a = 8;
			break;
		}
		int b = StrongholdMod.Settings?.maxTier ?? 13;
		return Mathf.Clamp(Mathf.Min(a, b), 1, 13);
	}

	public static int CapFor(Settlement s)
	{
		return CapFor(s?.Faction);
	}

	public static int DaysToNextTier(int tier)
	{
		return 5 * Mathf.Max(1, (Mathf.Max(1, tier) + 1) / 2);
	}

	public static int TicksToNextTier(int tier)
	{
		return DaysToNextTier(tier) * 60000;
	}

	public static int UpgradeCost(int tier)
	{
		return 500 * Mathf.Max(1, tier);
	}

	public static List<string> SelectableRoles(Settlement s, int tier)
	{
		List<string> list = new List<string>();
		if (s == null)
		{
			return list;
		}
		List<string> list2 = StrongholdScoreUtility.ValidRolesFor(s.Faction, tier, s);
		for (int i = 0; i < list2.Count; i++)
		{
			if (!ExcludedRoles.Contains(list2[i]) && !FakeOutpostUtility.IsFake(list2[i]))
			{
				list.Add(list2[i]);
			}
		}
		if (ThrumboRanchUtility.RanchConditionsMet(s, tier))
		{
			list.Add("ThrumboRanch");
			if (ModsConfig.OdysseyActive)
			{
				list.Add("AlphaThrumboRanch");
			}
		}
		return list;
	}

	public static bool IsExcludedRole(string role)
	{
		if (!role.NullOrEmpty())
		{
			return ExcludedRoles.Contains(role);
		}
		return false;
	}

	public static bool ShouldRerollAutoRole(Settlement s, string role)
	{
		if (!IsPuppetOutpost(s))
		{
			return true;
		}
		if (!role.NullOrEmpty())
		{
			return IsExcludedRole(role);
		}
		return true;
	}

	public static bool CanSelectRole(Settlement s, int tier, string role)
	{
		if (!string.IsNullOrEmpty(role))
		{
			return SelectableRoles(s, tier).Contains(role);
		}
		return false;
	}

	public static bool RoleStillValid(Settlement s, int tier, string role)
	{
		if (s == null || string.IsNullOrEmpty(role))
		{
			return false;
		}
		if (ThrumboRanchUtility.IsRanch(role))
		{
			return ThrumboRanchUtility.RanchConditionsMet(s, tier);
		}
		if (ExcludedRoles.Contains(role))
		{
			return false;
		}
		if (StrongholdRoles.MinTier.TryGetValue(role, out var value))
		{
			return StrongholdScoreUtility.ContentTier(tier) >= value;
		}
		return false;
	}

	public static bool RoleBelowMinTier(int tier, string role)
	{
		if (!role.NullOrEmpty() && StrongholdRoles.MinTier.TryGetValue(role, out var value))
		{
			return StrongholdScoreUtility.ContentTier(tier) < value;
		}
		return false;
	}

	public static bool AssaultAllowed(Settlement s)
	{
		Faction faction = s?.Faction;
		if (faction?.def == null)
		{
			return false;
		}
		if (Faction.OfEmpire != null && faction == Faction.OfEmpire)
		{
			return true;
		}
		return (int)faction.def.techLevel >= 5;
	}

	public static bool BlockSpecialRole(Settlement s)
	{
		if (!RimPactsCompat.IsPuppet(s))
		{
			return RimPactsCompat.StrategyMode;
		}
		return true;
	}

	public static bool CanAddMortar(StrongholdData d)
	{
		if (d != null && !d.hasMortarBattery)
		{
			return d.tier >= 6;
		}
		return false;
	}

	public static bool IsPuppetOutpost(Settlement s)
	{
		return RimPactsCompat.IsPuppet(s);
	}

	public static bool BlocksIntel(Settlement s, out string reason)
	{
		if (RimPactsCompat.IsPuppet(s))
		{
			reason = EESText.T("EES.CanScout.Puppet");
			return true;
		}
		reason = null;
		return false;
	}
}
