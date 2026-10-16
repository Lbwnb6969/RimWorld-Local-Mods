using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class StrongholdScoreUtility
{
	private struct EraSpec
	{
		public int startMin;

		public int startMax;

		public int capMin;

		public int capMax;

		public EraSpec(int sa, int sb, int ca, int cb)
		{
			startMin = sa;
			startMax = sb;
			capMin = ca;
			capMax = cb;
		}
	}

	public enum BountyBandKind
	{
		None,
		Industrial,
		Spacer
	}

	public const int MaxTierHardCap = 13;

	public const int ContentMaxTier = 7;

	private const float ClimbExp = 2f;

	private const int StartSeed = 358951;

	public const int BountyMinTier = 7;

	public const int BountyMaxTier = 12;

	private const float BountyFalloff = 1f;

	public const float BountyUnlockProgress = 0.6f;

	public const float BountyIndustrialUnlock = 0.2f;

	public const float BountyIndustrialMidProgress = 0.38f;

	public const int BountyIndustrialMinTier = 3;

	public const int BountyIndustrialMaxTier = 6;

	private static readonly int[] ContentTierMap = new int[13]
	{
		1, 1, 2, 2, 3, 3, 4, 4, 5, 5,
		6, 6, 7
	};

	private static readonly string[] EraGateDefs = new string[6] { "Smithing", "Electricity", "Machining", "MicroelectronicsBasics", "MultiAnalyzer", "Fabrication" };

	private static readonly float[] EraGateVals = new float[6] { 0.08f, 0.2f, 0.38f, 0.6f, 0.8f, 1f };

	private const int RoleSeed = 74565;

	private const int DisguiseSeed = 501005;

	private const int AmmoFacilitySeed = 659834;

	private const int MortarFacilitySeed = 525218;

	private const float FacilityChance = 0.35f;

	public const int MortarFacilityMinTier = 6;

	private const int MilPerTier = 350;

	private static EraSpec EraFor(Faction faction)
	{
		if (faction == null)
		{
			return new EraSpec(1, 1, 3, 3);
		}
		if (EESFactionKind.IsInsect(faction))
		{
			return new EraSpec(5, 13, 5, 13);
		}
		if (!faction.def.humanlikeFaction)
		{
			return new EraSpec(12, 13, 13, 13);
		}
		switch (faction.def.techLevel)
		{
		case TechLevel.Animal:
		case TechLevel.Neolithic:
			return new EraSpec(1, 1, 3, 3);
		case TechLevel.Medieval:
			return new EraSpec(3, 4, 5, 6);
		case TechLevel.Industrial:
			return new EraSpec(5, 7, 8, 10);
		case TechLevel.Spacer:
			return new EraSpec(7, 9, 10, 12);
		case TechLevel.Ultra:
			return new EraSpec(10, 10, 12, 12);
		case TechLevel.Archotech:
			return new EraSpec(11, 12, 12, 12);
		default:
			return new EraSpec(5, 7, 8, 10);
		}
	}

	private static int StartTierOf(int settlementId, EraSpec era)
	{
		Rand.PushState(Gen.HashCombineInt(settlementId, 358951));
		int result = Rand.RangeInclusive(era.startMin, era.startMax);
		Rand.PopState();
		return result;
	}

	private static int PersonalCap(int startTier, EraSpec era)
	{
		return Mathf.Min(era.capMin + (startTier - era.startMin), era.capMax);
	}

	public static float EraSaturation(Faction faction, int start)
	{
		if (faction == null)
		{
			return 0.6f;
		}
		if (!faction.def.humanlikeFaction)
		{
			return 1f;
		}
		switch (faction.def.techLevel)
		{
		case TechLevel.Animal:
		case TechLevel.Neolithic:
			return 0.6f;
		case TechLevel.Medieval:
			return 0.8f;
		case TechLevel.Spacer:
			if (start > 7)
			{
				return 1f;
			}
			return 0.8f;
		default:
			return 1f;
		}
	}

	public static int ComputeTier(Settlement settlement, float mapWealth01 = -1f)
	{
		if (EESPuppetUtility.IsPuppetOutpost(settlement))
		{
			return 1;
		}
		EraSpec era = EraFor(settlement.Faction);
		int num = StartTierOf(settlement.ID, era);
		int num2 = PersonalCap(num, era);
		float num3 = (WorldDominationCompat.TakesOver(settlement) ? WorldDominationCompat.GetStrengthRatio(settlement) : (-1f));
		float num4 = ((num3 >= 0f) ? num3 : ((mapWealth01 >= 0f) ? (Progress01() * 0.75f + Mathf.Clamp01(mapWealth01) * 0.25f) : Progress01()));
		float num5 = EraSaturation(settlement.Faction, num);
		float num6 = Mathf.Pow(Mathf.Clamp01(num4 / num5), 2f);
		int value = Mathf.Clamp(num + Mathf.RoundToInt((float)(num2 - num) * num6), num, num2);
		int b = StrongholdMod.Settings?.maxTier ?? 13;
		return Mathf.Clamp(value, 1, Mathf.Min(num2, b));
	}

	public static int RollTierForFaction(Faction faction)
	{
		EraSpec era = EraFor(faction);
		int num = Rand.RangeInclusive(era.startMin, era.startMax);
		int num2 = PersonalCap(num, era);
		float num3 = EraSaturation(faction, num);
		float num4 = Mathf.Pow(Mathf.Clamp01(Progress01() / num3), 2f);
		int value = Mathf.Clamp(num + Mathf.RoundToInt((float)(num2 - num) * num4), num, num2);
		int b = StrongholdMod.Settings?.maxTier ?? 13;
		return Mathf.Clamp(value, 1, Mathf.Min(num2, b));
	}

	public static BountyBandKind BountyBand()
	{
		float num = TechProgress01();
		if (num >= 0.6f)
		{
			return BountyBandKind.Spacer;
		}
		if (num >= 0.2f)
		{
			return BountyBandKind.Industrial;
		}
		return BountyBandKind.None;
	}

	public static string BountyBandName(BountyBandKind band)
	{
		return band switch
		{
			BountyBandKind.Industrial => "산업",
			BountyBandKind.Spacer => "스페이서",
			_ => "미도달",
		};
	}

	public static float BountyCenterTier()
	{
		float num = TechProgress01();
		if (num >= 1f)
		{
			return 10f;
		}
		if (num >= 0.8f)
		{
			return 9.5f;
		}
		if (num >= 0.6f)
		{
			return 7f;
		}
		if (!(num >= 0.38f))
		{
			return 3f;
		}
		return 4.5f;
	}

	public static bool BountyTierRange(Faction faction, out int lo, out int hi)
	{
		EraSpec era = EraFor(faction);
		int num = Rand.RangeInclusive(era.startMin, era.startMax);
		int a = PersonalCap(num, era);
		int b = StrongholdMod.Settings?.maxTier ?? 13;
		bool num2 = BountyBand() == BountyBandKind.Industrial;
		int b2 = (num2 ? 3 : 7);
		int b3 = (num2 ? 6 : 12);
		lo = Mathf.Max(num, b2);
		hi = Mathf.Min(Mathf.Min(a, b3), b);
		return hi >= lo;
	}

	public static int RollTierForBounty(Faction faction, out int lo, out int hi, out float center, out string dist)
	{
		center = BountyCenterTier();
		dist = "";
		if (!BountyTierRange(faction, out lo, out hi))
		{
			return -1;
		}
		int num = hi - lo + 1;
		float[] array = new float[num];
		float num2 = 0f;
		for (int i = 0; i < num; i++)
		{
			array[i] = 1f / Mathf.Pow(1f + Mathf.Abs((float)(lo + i) - center), 1f);
			num2 += array[i];
		}
		float num3 = Rand.Value * num2;
		int result = hi;
		for (int j = 0; j < num; j++)
		{
			num3 -= array[j];
			if (num3 <= 0f)
			{
				result = lo + j;
				break;
			}
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int k = 0; k < num; k++)
		{
			if (k > 0)
			{
				stringBuilder.Append(' ');
			}
			stringBuilder.Append(lo + k).Append(':').Append((array[k] / num2 * 100f).ToString("0.0"));
		}
		dist = stringBuilder.ToString();
		return result;
	}

	public static int ContentTier(int fineTier)
	{
		return ContentTierMap[Mathf.Clamp(fineTier, 1, 13) - 1];
	}

	public static float RewardBudgetMultiplier(int tier)
	{
		return RewardBudgetMultiplier(tier, null);
	}

	public static float RewardBudgetMultiplier(int tier, string featureKey)
	{
		int clamped = Mathf.Clamp(tier, 1, 16);
		return 1.4f + (float)(clamped - 1) * 0.3f + StrongholdFeatureUtility.LootBonus(featureKey);
	}

	/// <summary>Fine-tier rare reward opportunity. ContentTier still controls the item pool.</summary>
	public static float RareRewardChance(int tier)
	{
		int clamped = Mathf.Clamp(tier, 1, 16);
		return Mathf.Clamp(0.015f + (float)(clamped - 1) * 0.0125f, 0.015f, 0.22f);
	}

	public static QualityCategory RareRewardQuality(int tier)
	{
		int clamped = Mathf.Clamp(tier, 1, 16);
		if (clamped >= 13) return QualityCategory.Legendary;
		if (clamped >= 9) return QualityCategory.Masterwork;
		if (clamped >= 5) return QualityCategory.Excellent;
		return QualityCategory.Good;
	}

	public static float Progress01()
	{
		float num = ResearchComponent();
		float num2 = WealthComponent();
		float num3 = DaysComponent();
		return Mathf.Clamp01(num + num2 * 0.1f + num3 * 0.1f);
	}

	public static float PlayerLevel01()
	{
		int num;
		float num2;
		if (StrongholdMod.Settings != null)
		{
			num = (StrongholdMod.Settings.usePlayerResearchScaling ? 1 : 0);
			if (num == 0)
			{
				num2 = 0f;
				goto IL_0023;
			}
		}
		else
		{
			num = 1;
		}
		num2 = ResearchComponent();
		goto IL_0023;
		IL_0023:
		float num3 = num2;
		float num4 = DaysComponent();
		if (num == 0)
		{
			return num4;
		}
		return Mathf.Clamp01(num3 * 0.75f + num4 * 0.25f);
	}

	private static float DaysComponent()
	{
		float num = StrongholdMod.Settings?.growthRate ?? 1f;
		return Mathf.Clamp01((float)GenDate.DaysPassed * num / 120f);
	}

	private static float WealthComponent()
	{
		if (StrongholdMod.Settings != null && !StrongholdMod.Settings.usePlayerWealthScaling)
		{
			return 0f;
		}
		float num = 0f;
		List<Map> maps = Find.Maps;
		for (int i = 0; i < maps.Count; i++)
		{
			if (maps[i].IsPlayerHome)
			{
				num = Mathf.Max(num, maps[i].wealthWatcher.WealthTotal);
			}
		}
		return Mathf.Clamp01(num / 200000f);
	}

	public static float TechProgress01()
	{
		float num = 0f;
		for (int i = 0; i < EraGateDefs.Length; i++)
		{
			ResearchProjectDef namedSilentFail = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(EraGateDefs[i]);
			if (namedSilentFail != null && namedSilentFail.IsFinished && EraGateVals[i] > num)
			{
				num = EraGateVals[i];
			}
		}
		return num;
	}

	public static string EraGateName()
	{
		string result = "부족기본";
		float num = 0f;
		for (int i = 0; i < EraGateDefs.Length; i++)
		{
			ResearchProjectDef namedSilentFail = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(EraGateDefs[i]);
			if (namedSilentFail != null && namedSilentFail.IsFinished && EraGateVals[i] > num)
			{
				num = EraGateVals[i];
				result = EraGateDefs[i];
			}
		}
		return result;
	}

	private static float ResearchComponent()
	{
		if (StrongholdMod.Settings != null && !StrongholdMod.Settings.usePlayerResearchScaling)
		{
			return 0f;
		}
		return TechProgress01();
	}

	public static string ComputeRole(Settlement settlement, int tier)
	{
		if (EESFactionKind.IsInsect(settlement.Faction))
		{
			return ComputeNormalRole(settlement, tier, 74565);
		}
		bool flag = EESPuppetUtility.BlockSpecialRole(settlement);
		if (!flag && FakeOutpostUtility.ShouldBeFake(settlement, tier))
		{
			string text = FakeOutpostUtility.PickVariant(settlement, tier);
			if (text != null)
			{
				return text;
			}
		}
		if (ThrumboRanchUtility.ShouldBeRanch(settlement, tier))
		{
			return ThrumboRanchUtility.PickVariant(settlement, tier);
		}
		if (!flag && BattlefieldUtility.ShouldBeBattlefield(settlement, tier))
		{
			return "Battlefield";
		}
		return ComputeNormalRole(settlement, tier, 74565);
	}

	public static string ComputeDisguiseRole(Settlement settlement, int tier)
	{
		return ComputeNormalRole(settlement, tier, 501005);
	}

	public static string DisplayRole(StrongholdData data, Settlement settlement)
	{
		if (settlement != null && EESCustomBaseDetector.IsDedicatedOutpostFaction(settlement.Faction))
		{
			return null;
		}
		if (data != null && settlement != null && FakeOutpostUtility.IsFake(data.roleDefName))
		{
			return ComputeDisguiseRole(settlement, data.tier);
		}
		if (data != null && settlement != null && BattlefieldUtility.IsBattlefield(data.roleDefName))
		{
			return ComputeDisguiseRole(settlement, data.tier);
		}
		return data?.roleDefName;
	}

	public static List<string> ValidRolesFor(Faction faction, int tier, Settlement settlement = null)
	{
		int num = ContentTier(tier);
		List<string> list = CandidatesFor(faction);
		AddPuppetExtraCandidates(list, faction, settlement);
		bool flag = faction?.def != null && faction.def.humanlikeFaction;
		List<string> list2 = new List<string>();
		for (int i = 0; i < list.Count; i++)
		{
			if ((!flag || !(list[i] == "MechanoidRelay") || tier >= 11) && (!(list[i] == "CryoBarracks") || tier >= 8) && StrongholdRoles.MinTier.TryGetValue(list[i], out var value) && num >= value)
			{
				list2.Add(list[i]);
			}
		}
		return list2;
	}

	private static void AddPuppetExtraCandidates(List<string> candidates, Faction faction, Settlement settlement)
	{
		if (candidates == null || settlement == null || faction?.def == null || !EESPuppetUtility.IsPuppetOutpost(settlement))
		{
			return;
		}
		TechLevel techLevel = faction.def.techLevel;
		if (techLevel == TechLevel.Spacer || techLevel == TechLevel.Ultra)
		{
			if (!candidates.Contains("SupplyDepot"))
			{
				candidates.Add("SupplyDepot");
			}
			if (!candidates.Contains("Mine"))
			{
				candidates.Add("Mine");
			}
		}
	}

	private static string ComputeNormalRole(Settlement settlement, int tier, int seed)
	{
		List<string> list = ValidRolesFor(settlement.Faction, tier, settlement);
		if (EESPuppetUtility.IsPuppetOutpost(settlement))
		{
			List<string> list2 = new List<string>();
			for (int i = 0; i < list.Count; i++)
			{
				if (!EESPuppetUtility.IsExcludedRole(list[i]))
				{
					list2.Add(list[i]);
				}
			}
			if (list2.Count > 0)
			{
				list = list2;
			}
		}
		if (list.Count == 0)
		{
			return "SupplyDepot";
		}
		Rand.PushState(Gen.HashCombineInt(settlement.ID, seed));
		string text = list[Rand.Range(0, list.Count)];
		Rand.PopState();
		if (EESPuppetUtility.IsPuppetOutpost(settlement))
		{
			EESLog.Message($"[괴뢰국 역할] {settlement.LabelCap} [{settlement.Faction?.Name}/{settlement.Faction?.def?.techLevel}]: " + string.Format("T{0}(ct{1}) 후보 {2}종[{3}] → '{4}' (seed=0x{5:X}).", tier, ContentTier(tier), list.Count, string.Join(",", list), text, seed));
		}
		return text;
	}

	private static List<string> CandidatesFor(Faction faction)
	{
		if (faction == null)
		{
			return new List<string> { "SupplyDepot" };
		}
		if (EESFactionKind.IsInsect(faction))
		{
			return new List<string> { "InsectHive" };
		}
		if (!faction.def.humanlikeFaction)
		{
			return new List<string> { "MechanoidRelay" };
		}
		if (faction.def.permanentEnemy)
		{
			List<string> list = new List<string> { "PrisonCamp", "BlackMarketHub", "CommandFortress", "Mine", "MechanoidRelay" };
			if ((int)faction.def.techLevel >= 5)
			{
				list.Add("CryoBarracks");
			}
			if (ModsConfig.BiotechActive && (int)faction.def.techLevel >= 4)
			{
				list.Add("PollutedOutpost");
			}
			return list;
		}
		TechLevel techLevel = faction.def.techLevel;
		if ((int)techLevel >= 5)
		{
			List<string> list2 = new List<string> { "NobleEstate", "CommandFortress", "ResearchOutpost", "MechanoidRelay", "CryoBarracks" };
			if (ModsConfig.BiotechActive)
			{
				list2.Add("PollutedOutpost");
			}
			return list2;
		}
		if ((int)techLevel <= 3)
		{
			return new List<string> { "SupplyDepot", "PrisonCamp" };
		}
		List<string> list3 = new List<string> { "SupplyDepot", "ResearchOutpost", "Mine" };
		if (ModsConfig.BiotechActive)
		{
			list3.Add("PollutedOutpost");
		}
		return list3;
	}

	public static void DecideFacilities(StrongholdData data, Settlement settlement)
	{
		if (data == null || settlement == null || data.facilitiesForced)
		{
			return;
		}
		if (EESPuppetUtility.IsPuppetOutpost(settlement))
		{
			data.hasAmmoDepot = false;
			data.hasMortarBattery = false;
			return;
		}
		if (FakeOutpostUtility.IsFake(data.roleDefName))
		{
			data.hasAmmoDepot = false;
			data.hasMortarBattery = false;
			return;
		}
		if (BattlefieldUtility.IsBattlefield(data.roleDefName))
		{
			data.hasMortarBattery = false;
			Rand.PushState(Gen.HashCombineInt(data.settlementId, 659834));
			data.hasAmmoDepot = Rand.Value < (StrongholdMod.Settings?.facilityChance ?? 0.35f);
			Rand.PopState();
			EESLog.Message($"Facilities decide {settlement.LabelCap}: role=Battlefield -> mortar=FALSE(강제) ammo={data.hasAmmoDepot}.");
			return;
		}
		int num = ContentTier(data.tier);
		float num2 = StrongholdMod.Settings?.facilityChance ?? 0.35f;
		bool flag = false;
		bool flag2 = false;
		if (StrongholdRoles.MinTier.TryGetValue("AmmoDepot", out var value) && num >= value)
		{
			Rand.PushState(Gen.HashCombineInt(data.settlementId, 659834));
			flag = Rand.Value < num2;
			Rand.PopState();
		}
		if (data.tier >= 6)
		{
			Rand.PushState(Gen.HashCombineInt(data.settlementId, 525218));
			flag2 = Rand.Value < num2;
			Rand.PopState();
		}
		data.hasAmmoDepot = flag;
		data.hasMortarBattery = flag2;
		EESLog.Message("Facilities decide " + settlement.LabelCap + " [" + settlement.Faction?.Name + "]: " + $"T{data.tier}(ct{num}) -> ammo={flag} mortar={flag2} (각 {num2:P0}).");
	}

	private static int TierMilBase(int tier)
	{
		int num = StrongholdMod.Settings?.milPerTier ?? 350;
		return Mathf.Clamp(tier, 1, 13) * num;
	}

	public static void ApplyDerived(StrongholdData d, Settlement settlement)
	{
		float num = StrongholdMod.Settings?.militaryPowerMul ?? 1f;
		d.militaryPower = Mathf.RoundToInt((float)ComputeMilitaryPower(settlement.Faction, d.tier, d.roleDefName, d.alertLevel) * num * StrongholdFeatureUtility.MilitaryMultiplier(d.featureKey));
		d.fortification = Mathf.Clamp(ComputeFortification(d.tier, d.roleDefName, d.alertLevel) + StrongholdFeatureUtility.FortificationBonus(d.featureKey), 0, 100);
		d.supply = ComputeSupply(d.tier, d.roleDefName) + StrongholdFeatureUtility.SupplyBonus(d.featureKey);
		// Alert, feature and legacy-field refreshes recompute from the base values.
		// Preserve the derived part of an already-applied Final Stand without
		// repeating its one-time commander/elite/garrison changes.
		if (d.finalStandActive && d.finalStandApplied)
		{
			d.militaryPower = Mathf.RoundToInt(d.militaryPower * 1.6f);
			d.fortification = Mathf.Clamp(d.fortification + 30, 0, 100);
			d.supply += 30;
		}
	}

	public static int ComputeMilitaryPower(Faction faction, int tier, string role, int alertLevel = 0)
	{
		bool num = BattlefieldUtility.IsBattlefield(role);
		int num2 = TierMilBase(tier);
		if (num)
		{
			num2 = Mathf.RoundToInt((float)num2 * 0.9f);
		}
		float num3 = TechMul(faction);
		if (num)
		{
			num3 = Mathf.Min(num3, 1f);
		}
		int num4 = RoleMilBonus(role);
		float num5 = StrongholdMod.Settings?.alertMilScale ?? 0.1f;
		float num6 = (num ? 1f : (1f + (float)Mathf.Clamp(alertLevel, 0, 5) * num5));
		return Mathf.RoundToInt((float)(Mathf.RoundToInt((float)num2 * num3) + num4) * num6);
	}

	private static float TechMul(Faction f)
	{
		if (f == null)
		{
			return 1f;
		}
		if (!f.def.humanlikeFaction)
		{
			return 1.5f;
		}
		switch (f.def.techLevel)
		{
		case TechLevel.Animal:
		case TechLevel.Neolithic:
			return 0.8f;
		case TechLevel.Medieval:
			return 0.9f;
		case TechLevel.Industrial:
			return 1f;
		case TechLevel.Spacer:
			return 1.2f;
		case TechLevel.Ultra:
			return 1.35f;
		case TechLevel.Archotech:
			return 1.5f;
		default:
			return 1f;
		}
	}

	private static int RoleMilBonus(string role)
	{
		return role switch
		{
			"MechanoidRelay" => 300,
			"CommandFortress" => 250,
			"TrainingCamp" => 200,
			"MortarBattery" => 120,
			"AmmoDepot" => 100,
			_ => 0,
		};
	}

	public static int ComputeFortification(int tier, string role, int alertLevel = 0)
	{
		int num = Mathf.RoundToInt((float)tier / 13f * 100f);
		int num2 = 0;
		switch (role)
		{
		case "CommandFortress":
			num2 = 20;
			break;
		case "AmmoDepot":
			num2 = 10;
			break;
		case "MortarBattery":
			num2 = 10;
			break;
		case "MechanoidRelay":
			num2 = 15;
			break;
		}
		int num3 = Mathf.Clamp(alertLevel, 0, 5) * 3;
		return Mathf.Clamp(num + num2 + num3, 0, 100);
	}

	public static int ComputeSupply(int tier, string role)
	{
		int num = ContentTier(tier) * 10;
		int num2 = 0;
		switch (role)
		{
		case "SupplyDepot":
			num2 = 40;
			break;
		case "BlackMarketHub":
			num2 = 30;
			break;
		case "AmmoDepot":
			num2 = 25;
			break;
		case "NobleEstate":
			num2 = 20;
			break;
		}
		return num + num2;
	}

	public static float MapWealth01(Map map)
	{
		if (map?.wealthWatcher == null)
		{
			return -1f;
		}
		return Mathf.Clamp01(map.wealthWatcher.WealthTotal / 150000f);
	}

	public static string DebugDescribe(Settlement settlement)
	{
		EraSpec era = EraFor(settlement.Faction);
		int num = StartTierOf(settlement.ID, era);
		int num2 = PersonalCap(num, era);
		float num3 = Progress01();
		return $"era start{era.startMin}-{era.startMax} cap{era.capMin}-{era.capMax} | thisStart=T{num} thisCap=T{num2} | " + $"progress={num3:0.00} (res={ResearchComponent():0.00} wealth={WealthComponent():0.00} days={DaysComponent():0.00}) climbExp={2f}";
	}

	public static string TierBreakdown(Settlement settlement)
	{
		EraSpec era = EraFor(settlement.Faction);
		int num = StartTierOf(settlement.ID, era);
		int num2 = PersonalCap(num, era);
		int num3 = num2 - num;
		float num4 = 0f;
		List<Map> maps = Find.Maps;
		for (int i = 0; i < maps.Count; i++)
		{
			if (maps[i].IsPlayerHome)
			{
				num4 = Mathf.Max(num4, maps[i].wealthWatcher.WealthTotal);
			}
		}
		float num5 = (float)GenDate.DaysPassed * (StrongholdMod.Settings?.growthRate ?? 1f);
		float num6 = ResearchComponent();
		float num7 = WealthComponent();
		float num8 = DaysComponent();
		float num9 = Progress01();
		float num10 = EraSaturation(settlement.Faction, num);
		float num11 = Mathf.Pow(Mathf.Clamp01(num9 / num10), 2f);
		int num12 = ComputeTier(settlement);
		string text = "(이미 천장)";
		if (num12 < num2 && num3 > 0)
		{
			int num13 = num12 - num + 1;
			float num14 = num10 * Mathf.Pow(((float)num13 - 0.5f) / (float)num3, 0.5f);
			text = $"다음 T{num12 + 1}까지 prog≥{num14:0.000}";
		}
		return $"[{settlement.LabelCap}/{settlement.Faction?.def.techLevel}] start=T{num} cap=T{num2} gap={num3} 포화sat={num10:0.00} | " + $"기술관문 {EraGateName()}→tech={num6:0.000}, 부 {num4:N0}→w01={num7:0.000}(+{num7 * 0.1f:0.000}), 경과 {num5:0.0}일→d01={num8:0.000}(+{num8 * 0.1f:0.000}) | " + $"prog={num9:0.000}, climb=(prog/sat)^{2f}={num11:0.000}, gap×climb={(float)num3 * num11:0.00} → 계산티어=T{num12} | {text}";
	}
}
