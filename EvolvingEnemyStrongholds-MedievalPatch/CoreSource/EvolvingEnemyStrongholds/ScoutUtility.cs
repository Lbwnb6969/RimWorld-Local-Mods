using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class ScoutUtility
{
	private enum Fate
	{
		Escaped,
		Wounded,
		Captured,
		Dead,
		DeviceSaved
	}

	private const int ScoutCooldownTicks = 60000;

	private const int FailAlertRaise = 2;

	private const int FailGoodwillPenalty = -25;

	private static readonly float[] ScoutFloor = new float[14]
	{
		0f, 0.5f, 0.43f, 0.37f, 0.3f, 0.21f, 0.12f, 0.06f, 0f, 0f,
		0f, 0f, 0f, 0f
	};

	private static readonly float[] ScoutUnlock = new float[14]
	{
		0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0.18f, 0.27f,
		0.35f, 0.43f, 0.5f, 0.65f
	};

	private static readonly float[] ScoutRamp = new float[14]
	{
		0f, 0.25f, 0.28f, 0.32f, 0.35f, 0.4f, 0.45f, 0.48f, 0.5f, 0.52f,
		0.55f, 0.52f, 0.5f, 0.35f
	};

	private static readonly float[] ScoutMax = new float[14]
	{
		0f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 0.9f, 0.85f,
		0.8f, 0.75f, 0.7f, 0.5f
	};

	private const float HighTierUnlockedFloor = 0.06f;

	private const float HopelessDisplayThreshold = 0.005f;

	private const int InfiltrateCooldownTicks = 60000;

	private const int InfiltrateAlertDrop = -3;

	private static readonly int[] DirectSabotageByTier = new int[14]
	{
		0, 0, 2, 2, 3, 3, 4, 4, 6, 6,
		8, 8, 10, 0
	};

	public const int InfiltrateMaxTier = 12;

	public const int InfiltrateMaxParty = 3;

	private const float PartyDecay = 0.35f;

	private const float ScoutBaseMoveSpeed = 4.6f;

	private const float ScoutMaxMoveSpeed = 11f;

	private static readonly float[] CasualtyByTier = new float[14]
	{
		0f, 0.15f, 0.19f, 0.24f, 0.3f, 0.38f, 0.46f, 0.54f, 0.62f, 0.71f,
		0.8f, 0.88f, 0.94f, 1f
	};

	private static readonly float[] DeathByTier = new float[14]
	{
		0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f,
		0.01f, 0.02f, 0.03f, 1f
	};

	public static bool Enabled
	{
		get
		{
			if (StrongholdMod.Settings != null)
			{
				return StrongholdMod.Settings.enableScouting;
			}
			return true;
		}
	}

	public static bool HighTierUnlocked => StrongholdMod.Settings?.scoutUnlockHighTier ?? false;

	private static bool LetterNotify => StrongholdMod.Settings?.scoutLetterNotify ?? false;

	public static bool IsScouted(StrongholdData d)
	{
		if (d != null)
		{
			return d.intelLevel >= 1;
		}
		return false;
	}

	public static bool CanScoutNow(StrongholdData d, out string reason, Settlement settlement = null)
	{
		reason = null;
		if (!Enabled)
		{
			reason = EESText.T("EES.CanScout.Disabled");
			return false;
		}
		if (d == null)
		{
			reason = EESText.T("EES.CanScout.NoData");
			return false;
		}
		Settlement s = settlement ?? WorldComponent_Strongholds.Instance?.GetSettlement(d.settlementId);
		if (EESPuppetUtility.BlocksIntel(s, out var reason2))
		{
			reason = reason2;
			return false;
		}
		if (RimPactsCompat.BlocksIntel(s, out var reason3))
		{
			reason = reason3;
			return false;
		}
		if (IsScouted(d))
		{
			reason = EESText.T("EES.CanScout.AlreadyScouted");
			return false;
		}
		if (d.lastScoutedTick >= 0)
		{
			int num = Find.TickManager.TicksGame - d.lastScoutedTick;
			if (num < 60000)
			{
				reason = EESText.T("EES.CanScout.Cooldown", (60000 - num).ToStringTicksToPeriod());
				return false;
			}
		}
		return true;
	}

	private static void TierScoutParams(int tier, out float floor, out float unlock, out float ramp, out float max)
	{
		int num = Mathf.Clamp(tier, 1, 13);
		floor = ScoutFloor[num];
		unlock = ScoutUnlock[num];
		ramp = ScoutRamp[num];
		max = ScoutMax[num];
	}

	public static string CurveDebug(int tier)
	{
		TierScoutParams(tier, out var floor, out var unlock, out var ramp, out var max);
		bool highTierUnlocked = HighTierUnlocked;
		string text = $"곡선[floor={floor:0.00} unlock={unlock:0.00} ramp={ramp:0.00} max={max:0.00}]";
		if (highTierUnlocked && unlock > 0f)
		{
			return $"{text} 고티어해제=On(적용: unlock {unlock:0.00}→0.00 · floor {floor:0.00}→{0.06f:0.00})";
		}
		return text + " 고티어해제=" + (highTierUnlocked ? "On(이 티어는 대상 아님 — unlock 이 원래 0)" : "Off");
	}

	public static float SuccessChance(StrongholdData d, Caravan caravan = null)
	{
		if (d == null)
		{
			return 0f;
		}
		if (FakeOutpostUtility.IsFake(d.roleDefName))
		{
			return 1f;
		}
		float num = StrongholdScoreUtility.PlayerLevel01();
		TierScoutParams(d.tier, out var floor, out var unlock, out var ramp, out var max);
		if (HighTierUnlocked && unlock > 0f)
		{
			unlock = 0f;
			if (floor < 0.06f)
			{
				floor = 0.06f;
			}
		}
		float t = ((ramp > 0.0001f) ? Mathf.Clamp01((num - unlock) / ramp) : ((num >= unlock) ? 1f : 0f));
		float value = Mathf.Lerp(floor, max, t) - (float)d.alertLevel * 0.05f - ThreatEmitterUtility.JammingPenaltyFor(d.settlementId);
		float num2 = StrongholdMod.Settings?.scoutSuccessMul ?? 1f;
		return ApplySightFactor(Mathf.Clamp01(Mathf.Clamp(value, 0f, max) * num2), caravan);
	}

	public static bool PartyCanSee(Caravan caravan)
	{
		return BestSight(caravan) > 0.001f;
	}

	private static float BestSight(Caravan caravan)
	{
		if (caravan != null)
		{
			return BestSight(caravan.PawnsListForReading);
		}
		return 1f;
	}

	private static float BestSight(List<Pawn> pawns)
	{
		if (pawns == null)
		{
			return 1f;
		}
		float num = 0f;
		bool flag = false;
		for (int i = 0; i < pawns.Count; i++)
		{
			Pawn pawn = pawns[i];
			if (pawn != null && !pawn.Dead && pawn.health != null && pawn.RaceProps != null && pawn.RaceProps.Humanlike)
			{
				flag = true;
				float level = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Sight);
				if (level > num)
				{
					num = level;
				}
			}
		}
		if (!flag)
		{
			return 1f;
		}
		return num;
	}

	private static float ApplySightFactor(float chance, Caravan caravan)
	{
		return ApplySightFactorCore(chance, BestSight(caravan));
	}

	private static float ApplySightFactorCore(float chance, float sight)
	{
		if (sight >= 1f)
		{
			return Mathf.Clamp(chance + Mathf.Clamp01((sight - 1f) / 0.5f) * 0.1f, 0f, 1f);
		}
		return Mathf.Clamp01(chance * sight);
	}

	public static List<Pawn> PawnsInPods(IEnumerable<IThingHolder> pods)
	{
		List<Pawn> list = new List<Pawn>();
		if (pods == null)
		{
			return list;
		}
		List<Thing> list2 = new List<Thing>();
		foreach (IThingHolder pod in pods)
		{
			list2.Clear();
			ThingOwnerUtility.GetAllThingsRecursively(pod, list2);
			for (int i = 0; i < list2.Count; i++)
			{
				if (list2[i] is Pawn { Dead: false, RaceProps: not null } pawn && pawn.Faction == Faction.OfPlayer)
				{
					list.Add(pawn);
				}
			}
		}
		return list;
	}

	public static float RollChance(StrongholdData d, Caravan caravan, out float baseChance, out int humans, out int mechs, out int animals, out int partyExtra, out float partyMul)
	{
		return RollChanceOf(d, caravan?.PawnsListForReading, out baseChance, out humans, out mechs, out animals, out partyExtra, out partyMul);
	}

	public static float RollChanceOf(StrongholdData d, List<Pawn> pawns, out float baseChance, out int humans, out int mechs, out int animals, out int partyExtra, out float partyMul)
	{
		baseChance = SuccessChance(d);
		CountPartyOf(pawns, out humans, out mechs, out animals);
		partyExtra = Mathf.Max(0, humans - 1) + Mathf.Max(0, mechs - 1);
		partyMul = Mathf.Pow(0.35f, partyExtra);
		return ApplySightFactorCore(Mathf.Clamp01(baseChance * partyMul), BestSight(pawns));
	}

	public static float RollChance(StrongholdData d, Caravan caravan)
	{
		float baseChance;
		int humans;
		int mechs;
		int animals;
		int partyExtra;
		float partyMul;
		return RollChance(d, caravan, out baseChance, out humans, out mechs, out animals, out partyExtra, out partyMul);
	}

	public static float RollChanceInPods(StrongholdData d, IEnumerable<IThingHolder> pods)
	{
		float baseChance;
		int humans;
		int mechs;
		int animals;
		int partyExtra;
		float partyMul;
		return RollChanceOf(d, PawnsInPods(pods), out baseChance, out humans, out mechs, out animals, out partyExtra, out partyMul);
	}

	public static bool IsHopeless(float chance)
	{
		return chance < 0.005f;
	}

	public static bool TryScout(Settlement settlement, StrongholdData d, Caravan caravan, bool showMessage = true, bool forceSuccess = false)
	{
		if (settlement == null || d == null || !Enabled)
		{
			return false;
		}
		float baseChance;
		int humans;
		int mechs;
		int animals;
		int partyExtra;
		float partyMul;
		float num = RollChance(d, caravan, out baseChance, out humans, out mechs, out animals, out partyExtra, out partyMul);
		int num2 = humans + mechs + animals;
		float num3 = StrongholdScoreUtility.PlayerLevel01();
		float value = Rand.Value;
		bool num4 = forceSuccess || value < num;
		if (forceSuccess)
		{
			EESLog.Message("Scout FORCED-SUCCESS (debug godmode) " + settlement.LabelCap + ": " + $"chance was {num:0.00} roll {value:0.00} → 강제 성공");
		}
		d.lastScoutedTick = Find.TickManager.TicksGame;
		if (num4)
		{
			d.intelLevel = 1;
			EESLog.Message("Scout SUCCESS " + settlement.LabelCap + " [" + settlement.Faction?.Name + "]: " + $"T{d.tier} {CurveDebug(d.tier)} playerLvl={num3:0.00} baseChance={baseChance:0.00} party={num2}[H{humans}M{mechs}A{animals}(동물=페널티무관) extra{partyExtra} x{partyMul:0.00}] " + $"chance={num:0.00} roll={value:0.00} alert={d.alertLevel} -> 전체 정보 공개");
			if (showMessage)
			{
				if (LetterNotify)
				{
					Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_ScoutSuccess"), EESText.T("EES.Ltr.ScoutSuccess.Text", settlement.LabelCap), EESLetterDefOf.EES_ScoutSuccess, settlement);
				}
				else
				{
					Messages.Message(EESText.T("EES.Msg.Success", settlement.LabelCap), settlement, MessageTypeDefOf.PositiveEvent, historical: false);
				}
			}
			return true;
		}
		AlertUtility.RaiseAlert(d, settlement, 2, "scout failed");
		int num5 = 0;
		Faction faction = settlement.Faction;
		if (faction != null && faction != Faction.OfPlayer)
		{
			int playerGoodwill = faction.PlayerGoodwill;
			faction.TryAffectGoodwillWith(Faction.OfPlayer, -25);
			num5 = faction.PlayerGoodwill - playerGoodwill;
		}
		string text = ApplyScoutCasualties(caravan, faction, d.tier, num, d);
		RemoveCaravanIfEmpty(caravan, settlement);
		EESLog.Message("Scout FAIL " + settlement.LabelCap + " [" + faction?.Name + "]: " + $"T{d.tier} {CurveDebug(d.tier)} playerLvl={num3:0.00} baseChance={baseChance:0.00} party={num2}[H{humans}M{mechs}A{animals}(동물=페널티무관) extra{partyExtra} x{partyMul:0.00}] " + $"chance={num:0.00} roll={value:0.00} | alert+{2} -> {d.alertLevel} | " + $"goodwill {-25}(실제 {num5}) -> {faction?.PlayerGoodwill} | casualties=[{text}]");
		if (showMessage)
		{
			if (LetterNotify)
			{
				Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_ScoutFail"), EESText.T("EES.Ltr.ScoutFail.Text", settlement.LabelCap, text), EESLetterDefOf.EES_ScoutFail, settlement);
			}
			else
			{
				Messages.Message(EESText.T("EES.Msg.Fail", settlement.LabelCap, text), settlement, MessageTypeDefOf.NegativeEvent, historical: false);
			}
		}
		return false;
	}

	private static int DirectSabotage(int tier)
	{
		int num = DirectSabotageByTier[Mathf.Clamp(tier, 1, 13)];
		float num2 = StrongholdMod.Settings?.sabotageMul ?? 1f;
		if (num <= 0)
		{
			return 0;
		}
		return Mathf.Max(1, Mathf.RoundToInt((float)num * num2));
	}

	private static int MortarSabotage(StrongholdData d)
	{
		if (!d.hasMortarBattery)
		{
			return 0;
		}
		if (d.tier != 12)
		{
			return 1;
		}
		return 2;
	}

	public static void ApplySabotageSuccess(Settlement settlement, StrongholdData d, bool showMessage, string source)
	{
		if (settlement == null || d == null)
		{
			return;
		}
		int num = DirectSabotage(d.tier);
		int num2 = MortarSabotage(d);
		d.sabotagedTurrets = num;
		d.sabotagedMortars = num2;
		d.infiltrated = true;
		AlertUtility.RaiseAlert(d, settlement, -3, source + " success");
		EESLog.Message(source + " SABOTAGE " + settlement.LabelCap + " [" + settlement.Faction?.Name + "]: " + $"직사-{num} 박격포-{num2} (영구 종료), alert{-3} -> {d.alertLevel}.");
		if (!showMessage)
		{
			return;
		}
		bool flag = FakeOutpostUtility.IsFake(d.roleDefName);
		bool flag2 = source == "Strike";
		if (LetterNotify)
		{
			if (flag2)
			{
				Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_StrikeSuccess"), flag ? EESText.T("EES.Ltr.StrikeFake.Text", settlement.LabelCap, num) : EESText.T("EES.Ltr.StrikeSuccess.Text", settlement.LabelCap, num), EESLetterDefOf.EES_StrikeSuccess, settlement);
			}
			else
			{
				Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_InfilSuccess"), flag ? EESText.T("EES.Ltr.InfilFake.Text", settlement.LabelCap, num) : EESText.T("EES.Ltr.InfilSuccess.Text", settlement.LabelCap, num), EESLetterDefOf.EES_InfilSuccess, settlement);
			}
		}
		else
		{
			Messages.Message((!flag2) ? (flag ? EESText.T("EES.Infil.FakeEmpty", settlement.LabelCap, num) : EESText.T("EES.Infil.Success", settlement.LabelCap, num)) : (flag ? EESText.T("EES.Strike.SabFake", settlement.LabelCap, num) : EESText.T("EES.Strike.SabSuccess", settlement.LabelCap, num)), settlement, MessageTypeDefOf.PositiveEvent, historical: false);
		}
	}

	public static bool CanInfiltrateNow(StrongholdData d, out string reason, Settlement settlement = null)
	{
		reason = null;
		if (!Enabled)
		{
			reason = EESText.T("EES.CanScout.Disabled");
			return false;
		}
		if (d == null)
		{
			reason = EESText.T("EES.CanScout.NoData");
			return false;
		}
		Settlement settlement2 = settlement ?? WorldComponent_Strongholds.Instance?.GetSettlement(d.settlementId);
		if (EESPuppetUtility.BlocksIntel(settlement2, out var reason2))
		{
			reason = reason2;
			return false;
		}
		if (RimPactsCompat.BlocksIntel(settlement2, out var reason3))
		{
			reason = reason3;
			return false;
		}
		if (settlement2 != null && (EESCustomBaseDetector.IsDedicatedOutpostFaction(settlement2.Faction) || EESFactionKind.IsInsect(settlement2.Faction)))
		{
			reason = EESText.T("EES.Infil.DedicatedBase");
			return false;
		}
		if (!IsScouted(d))
		{
			reason = EESText.T("EES.Infil.NeedScout");
			return false;
		}
		if (d.infiltrated)
		{
			reason = EESText.T("EES.Infil.AlreadyDone");
			return false;
		}
		if (d.tier > 12)
		{
			reason = EESText.T("EES.Infil.TierTooHigh");
			return false;
		}
		if (d.lastInfiltrateTick >= 0)
		{
			int num = Find.TickManager.TicksGame - d.lastInfiltrateTick;
			if (num < 60000)
			{
				reason = EESText.T("EES.CanScout.Cooldown", (60000 - num).ToStringTicksToPeriod());
				return false;
			}
		}
		return true;
	}

	public static List<Pawn> InfiltratorsOf(Caravan caravan)
	{
		List<Pawn> list = new List<Pawn>();
		if (caravan == null)
		{
			return list;
		}
		List<Pawn> pawnsListForReading = caravan.PawnsListForReading;
		for (int i = 0; i < pawnsListForReading.Count; i++)
		{
			Pawn pawn = pawnsListForReading[i];
			if (pawn != null && !pawn.Dead && pawn.RaceProps != null && pawn.RaceProps.Humanlike && pawn.skills != null && !pawn.IsPrisoner && !pawn.IsSlave)
			{
				list.Add(pawn);
			}
		}
		return list;
	}

	public static int InfiltratorsInPods(IEnumerable<IThingHolder> pods)
	{
		return InfiltratorsListInPods(pods).Count;
	}

	public static List<Pawn> InfiltratorsListInPods(IEnumerable<IThingHolder> pods)
	{
		List<Pawn> list = new List<Pawn>();
		if (pods == null)
		{
			return list;
		}
		List<Thing> list2 = new List<Thing>();
		foreach (IThingHolder pod in pods)
		{
			list2.Clear();
			ThingOwnerUtility.GetAllThingsRecursively(pod, list2);
			for (int i = 0; i < list2.Count; i++)
			{
				if (list2[i] is Pawn { Dead: false, RaceProps: not null } pawn && pawn.RaceProps.Humanlike && !pawn.IsPrisoner && !pawn.IsSlave && pawn.Faction == Faction.OfPlayer)
				{
					list.Add(pawn);
				}
			}
		}
		return list;
	}

	public static float InfiltrateSuccessChance(List<Pawn> agents, StrongholdData d)
	{
		if (agents == null || agents.Count == 0 || d == null)
		{
			return 0f;
		}
		if (FakeOutpostUtility.IsFake(d.roleDefName))
		{
			return 1f;
		}
		int num = Mathf.Min(agents.Count, 3);
		float num2 = 0f;
		float num3 = 0f;
		for (int i = 0; i < agents.Count; i++)
		{
			Pawn pawn = agents[i];
			num2 += pawn.GetStatValue(StatDefOf.MoveSpeed);
			int valueOrDefault = (pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level).GetValueOrDefault();
			int valueOrDefault2 = (pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level).GetValueOrDefault();
			num3 += (float)(valueOrDefault + valueOrDefault2);
		}
		float num4 = Mathf.Clamp01(num2 / (float)agents.Count / 9.2f);
		float num5 = Mathf.Clamp01(num3 / (float)agents.Count / 40f);
		float num6 = 0.6f * num4 + 0.4f * num5;
		float num7 = ((num >= 3) ? 0.15f : ((num == 2) ? 0.05f : 0f));
		float a = num6 + num7 - (float)d.alertLevel * 0.05f - (float)(d.tier - 1) * 0.015f;
		float num8 = StrongholdMod.Settings?.infiltrateSuccessMul ?? 1f;
		return Mathf.Clamp01(Mathf.Max(a, 0f) * num8);
	}

	public static bool TryInfiltrate(Settlement settlement, StrongholdData d, Caravan caravan, bool showMessage = true, bool forceSuccess = false)
	{
		if (settlement == null || d == null || !Enabled)
		{
			return false;
		}
		List<Pawn> list = InfiltratorsOf(caravan);
		if (list.Count == 0 && !forceSuccess)
		{
			if (showMessage)
			{
				Messages.Message(EESText.T("EES.Infil.NoAgent"), settlement, MessageTypeDefOf.RejectInput, historical: false);
			}
			return false;
		}
		if (list.Count > 3 && !forceSuccess)
		{
			if (showMessage)
			{
				Messages.Message(EESText.T("EES.Infil.TooMany", 3), settlement, MessageTypeDefOf.RejectInput, historical: false);
			}
			return false;
		}
		float num = InfiltrateSuccessChance(list, d);
		float value = Rand.Value;
		bool num2 = forceSuccess || value < num;
		d.lastInfiltrateTick = Find.TickManager.TicksGame;
		Faction faction = settlement.Faction;
		if (num2)
		{
			EESLog.Message($"Infiltrate SUCCESS {settlement.LabelCap} [{faction?.Name}]: party={list.Count} chance={num:0.00} roll={value:0.00}.");
			ApplySabotageSuccess(settlement, d, showMessage, "Infiltrate");
			return true;
		}
		AlertUtility.RaiseAlert(d, settlement, 2, "infiltrate failed");
		int num3 = 0;
		if (faction != null && faction != Faction.OfPlayer)
		{
			int playerGoodwill = faction.PlayerGoodwill;
			faction.TryAffectGoodwillWith(Faction.OfPlayer, -25);
			num3 = faction.PlayerGoodwill - playerGoodwill;
		}
		List<string> list2 = new List<string>();
		for (int i = 0; i < list.Count; i++)
		{
			string text = InfiltrateAgentFallout(list[i], caravan, faction, d);
			list2.Add(EESText.T("EES.Infil.FateLine", list[i].LabelShortCap, text));
		}
		RemoveCaravanIfEmpty(caravan, settlement);
		string text2 = string.Join(", ", list2);
		EESLog.Message($"Infiltrate FAIL {settlement.LabelCap} [{faction?.Name}]: party={list.Count} chance={num:0.00} roll={value:0.00} | " + $"alert+{2} -> {d.alertLevel} | goodwill {-25}(실제 {num3}) | fates={text2}.");
		if (showMessage)
		{
			if (LetterNotify)
			{
				Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_InfilFail"), EESText.T("EES.Ltr.InfilFail.Text", settlement.LabelCap, text2), EESLetterDefOf.EES_InfilFail, settlement);
			}
			else
			{
				Messages.Message(EESText.T("EES.Infil.FailParty", settlement.LabelCap, text2), settlement, MessageTypeDefOf.NegativeEvent, historical: false);
			}
		}
		return false;
	}

	private static string InfiltrateAgentFallout(Pawn p, Caravan caravan, Faction faction, StrongholdData d)
	{
		int tier = d.tier;
		float num = StrongholdScoreUtility.PlayerLevel01();
		TierScoutParams(tier, out var _, out var unlock, out var ramp, out var _);
		float num2 = ((ramp > 0.0001f) ? Mathf.Clamp01((num - unlock) / ramp) : ((num >= unlock) ? 1f : 0f));
		float num3 = 1f - num2 * 0.85f;
		float num4 = Mathf.Clamp01(TierCasualtyChance(tier) * num3);
		float num5 = TierDeathChance(tier);
		float statValue = p.GetStatValue(StatDefOf.MoveSpeed);
		bool flag = StrongholdMod.Settings?.preventScoutDeath ?? false;
		EESLog.Message($"Infiltrate fallout [{faction?.Name} T{tier}]: pl={num:0.00} readiness={num2:0.00} " + $"baseCaught={num4:0.00} death={num4 * num5:0.00} spd={statValue:0.0} preventDeath={flag} device={EscapeDeviceUtility.WornBy(p)}.");
		return ResolveFate(p, caravan, faction, d, num4, num5) switch
		{
			Fate.Dead => EESText.T("EES.Infil.Fate.Dead"),
			Fate.Captured => EESText.T("EES.Infil.Fate.Captured"),
			Fate.Wounded => EESText.T("EES.Infil.Fate.Wounded"),
			Fate.DeviceSaved => EESText.T("EES.Infil.Fate.Device"),
			_ => EESText.T("EES.Infil.Fate.Escaped"),
		};
	}

	private static Fate ResolveFate(Pawn p, Caravan caravan, Faction faction, StrongholdData data, float baseCaught, float deathChance)
	{
		StrongholdSettings settings = StrongholdMod.Settings;
		if ((settings == null || !settings.preventScoutDeath) && Rand.Value < baseCaught * deathChance)
		{
			if (EscapeDeviceUtility.ConsumeWornBy(p))
			{
				WoundScout(p);
				return Fate.DeviceSaved;
			}
			KillScout(p, caravan);
			return Fate.Dead;
		}
		float statValue = p.GetStatValue(StatDefOf.MoveSpeed);
		if (Rand.Value < MoveAdjustedCaught(baseCaught, statValue))
		{
			if (Rand.Value < MoveCaptureWeight(statValue))
			{
				if (EscapeDeviceUtility.ConsumeWornBy(p))
				{
					WoundScout(p);
					return Fate.DeviceSaved;
				}
				CaptureScout(p, caravan, faction, data);
				return Fate.Captured;
			}
			WoundScout(p);
			return Fate.Wounded;
		}
		return Fate.Escaped;
	}

	private static void CountParty(Caravan caravan, out int humans, out int mechs, out int animals)
	{
		CountPartyOf(caravan?.PawnsListForReading, out humans, out mechs, out animals);
	}

	private static void CountPartyOf(List<Pawn> pawns, out int humans, out int mechs, out int animals)
	{
		humans = 0;
		mechs = 0;
		animals = 0;
		if (pawns == null)
		{
			humans = 1;
			return;
		}
		for (int i = 0; i < pawns.Count; i++)
		{
			Pawn pawn = pawns[i];
			if (pawn != null && !pawn.Dead && pawn.RaceProps != null)
			{
				if (pawn.RaceProps.Humanlike)
				{
					humans++;
				}
				else if (pawn.RaceProps.IsMechanoid)
				{
					mechs++;
				}
				else if (pawn.RaceProps.Animal)
				{
					animals++;
				}
			}
		}
		if (humans + mechs + animals == 0)
		{
			humans = 1;
		}
	}

	public static int ScoutHumanlikeCount(Caravan caravan)
	{
		CountParty(caravan, out var humans, out var _, out var _);
		return humans;
	}

	private static float MoveAdjustedCaught(float baseCaught, float moveSpeed)
	{
		if (moveSpeed >= 4.6f)
		{
			float num = Mathf.Clamp01((moveSpeed - 4.6f) / 6.4f);
			return Mathf.Clamp01(baseCaught - num * 0.3f);
		}
		float t = Mathf.Clamp01(moveSpeed / 4.6f);
		return Mathf.Lerp(1f, baseCaught, t);
	}

	private static float MoveCaptureWeight(float moveSpeed)
	{
		if (moveSpeed >= 4.6f)
		{
			return 0.25f;
		}
		float t = Mathf.Clamp01(moveSpeed / 4.6f);
		return Mathf.Lerp(1f, 0.25f, t);
	}

	private static string ApplyScoutCasualties(Caravan caravan, Faction faction, int tier, float scoutChance, StrongholdData data)
	{
		if (caravan == null)
		{
			return EESText.T("EES.Casualty.NoCaravan");
		}
		float num = StrongholdScoreUtility.PlayerLevel01();
		TierScoutParams(tier, out var _, out var unlock, out var ramp, out var _);
		float num2 = ((ramp > 0.0001f) ? Mathf.Clamp01((num - unlock) / ramp) : ((num >= unlock) ? 1f : 0f));
		float num3 = 1f - num2 * 0.85f;
		float num4 = Mathf.Clamp01(TierCasualtyChance(tier) * num3);
		float deathChance = TierDeathChance(tier);
		bool flag = StrongholdMod.Settings?.preventScoutDeath ?? false;
		EESLog.Message($"Scout casualty [{faction?.Name} T{tier}]: pl={num:0.00} researchReadiness={num2:0.00} " + $"reduce={num3:0.00} baseCaught={num4:0.00} preventDeath={flag} (도주=연구+이동속도, 눈효율 무관).");
		int num5 = 0;
		int num6 = 0;
		int num7 = 0;
		int num8 = 0;
		foreach (Pawn item in new List<Pawn>(caravan.PawnsListForReading))
		{
			if (item != null && !item.Dead && item.RaceProps != null && item.RaceProps.Humanlike)
			{
				switch (ResolveFate(item, caravan, faction, data, num4, deathChance))
				{
				case Fate.Dead:
					num7++;
					break;
				case Fate.Captured:
					num6++;
					break;
				case Fate.Wounded:
					num5++;
					break;
				case Fate.DeviceSaved:
					num8++;
					break;
				}
			}
		}
		if (num5 == 0 && num6 == 0 && num7 == 0 && num8 == 0)
		{
			return EESText.T("EES.Casualty.AllEscaped");
		}
		string text = EESText.T("EES.Casualty.Summary", num5 + num8, num6, num7);
		if (num8 > 0)
		{
			text += EESText.T("EES.Casualty.DeviceNote", num8);
		}
		return text;
	}

	private static float TierCasualtyChance(int tier)
	{
		return CasualtyByTier[Mathf.Clamp(tier, 1, 13)];
	}

	private static float TierDeathChance(int tier)
	{
		return DeathByTier[Mathf.Clamp(tier, 1, 13)];
	}

	private static void WoundScout(Pawn p)
	{
		try
		{
			HealthUtility.DamageUntilDowned(p, allowBleedingWounds: false);
			int num = 0;
			while (p.Downed && !p.Dead && num++ < 60)
			{
				Hediff_Injury hediff_Injury = FindWorstInjury(p);
				if (hediff_Injury != null)
				{
					hediff_Injury.Heal(hediff_Injury.Severity * 0.5f + 2f);
					continue;
				}
				break;
			}
		}
		catch (Exception ex)
		{
			EESLog.Error("WoundScout " + p?.LabelShort + ": " + ex.Message);
		}
	}

	private static Hediff_Injury FindWorstInjury(Pawn p)
	{
		Hediff_Injury hediff_Injury = null;
		List<Hediff> list = p.health?.hediffSet?.hediffs;
		if (list == null)
		{
			return null;
		}
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i] is Hediff_Injury hediff_Injury2 && (hediff_Injury == null || hediff_Injury2.Severity > hediff_Injury.Severity))
			{
				hediff_Injury = hediff_Injury2;
			}
		}
		return hediff_Injury;
	}

	private static void RemoveCaravanIfEmpty(Caravan caravan, Settlement settlement)
	{
		if (caravan == null || caravan.Destroyed)
		{
			return;
		}
		List<Pawn> pawnsListForReading = caravan.PawnsListForReading;
		for (int i = 0; i < pawnsListForReading.Count; i++)
		{
			Pawn pawn = pawnsListForReading[i];
			if (pawn != null && !pawn.Dead && !pawn.Destroyed)
			{
				return;
			}
		}
		EESLog.Message("Scout: caravan emptied (all captured/killed) at " + settlement?.LabelCap + " -> removing stranded caravan.");
		caravan.Destroy();
	}

	private static void CaptureScout(Pawn p, Caravan caravan, Faction faction, StrongholdData data)
	{
		try
		{
			caravan.RemovePawn(p);
			if (p.Spawned)
			{
				p.DeSpawn();
			}
			if (p.Faction != null)
			{
				p.SetFaction(null);
			}
			if (!p.Dead && !p.Destroyed && !p.IsWorldPawn())
			{
				Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.KeepForever);
			}
			if (data != null && !p.Dead)
			{
				if (data.capturedPrisoners == null)
				{
					data.capturedPrisoners = new List<Pawn>();
				}
				if (!data.capturedPrisoners.Contains(p))
				{
					data.capturedPrisoners.Add(p);
				}
				PrisonerMarkerUtil.Invalidate();
				EESLog.Message($"Scout: {p.LabelShortCap} captured -> held at stronghold id={data.settlementId} (attack to rescue).");
			}
		}
		catch (Exception ex)
		{
			EESLog.Error("CaptureScout " + p?.LabelShort + ": " + ex.Message);
		}
	}

	private static void KillScout(Pawn p, Caravan caravan)
	{
		try
		{
			caravan.RemovePawn(p);
			p.Kill(null, null);
		}
		catch (Exception ex)
		{
			EESLog.Error("KillScout " + p?.LabelShort + ": " + ex.Message);
		}
	}

	public static string IntelReport(Settlement settlement, StrongholdData d)
	{
		StringBuilder stringBuilder = new StringBuilder();
		string text = EESText.T("EES.Common.Unknown");
		stringBuilder.AppendLine(settlement.LabelCap);
		stringBuilder.AppendLine(EESText.T("EES.Intel.Faction", settlement.Faction?.Name ?? text, (settlement.Faction?.def?.techLevel)?.ToString() ?? text));
		if (!IsScouted(d))
		{
			stringBuilder.AppendLine(EESText.T("EES.Intel.TierOnly", d.tier));
			stringBuilder.AppendLine(EESText.T("EES.Intel.NotScouted"));
			return stringBuilder.ToString().TrimEndNewlines();
		}
		string text2 = StrongholdScoreUtility.DisplayRole(d, settlement);
		stringBuilder.AppendLine(EESText.T("EES.Intel.ScoutedHeader"));
		stringBuilder.AppendLine((text2 == null) ? EESText.T("EES.Intel.TierOnly", d.tier) : EESText.T("EES.Intel.TierRole", d.tier, text2));
		bool flag = SupplyCaravanUtility.SupplyCutActive(d);
		bool flag2 = text2 == "CryoBarracks";
		float num = (flag2 ? 0.65f : 1f);
		int num2 = Mathf.RoundToInt((float)d.militaryPower * num);
		int num3 = Mathf.RoundToInt((flag ? ((float)d.militaryPower * 0.75f) : ((float)d.militaryPower)) * num);
		stringBuilder.AppendLine(EESText.T("EES.Intel.PawnsAlert", EstPawnRange(num3), AlertUtility.AlertLabel(d.alertLevel)));
		if (flag2)
		{
			stringBuilder.AppendLine(EESText.T("EES.Scout.CryoSign"));
		}
		int num4 = MechRelayUtility.EstimateMechCount(settlement, d);
		if (num4 > 0)
		{
			stringBuilder.AppendLine(EESText.T("EES.Scout.Fac.Mechs", num4));
		}
		stringBuilder.AppendLine(EESText.T("EES.Intel.Defense", d.fortification, d.supply));
		stringBuilder.AppendLine(EESText.T("EES.Intel.Commander", d.hasCommander ? EESText.T("EES.Common.Yes") : EESText.T("EES.Common.No"), text2 ?? EESText.T("EES.Common.Generic")));
		stringBuilder.AppendLine(EESText.T("EES.Intel.Military", num3) + (flag ? $" <color=#ff3333>(-{num2 - num3})</color>" : ""));
		if (!EESPuppetUtility.IsPuppetOutpost(settlement))
		{
			stringBuilder.AppendLine((d.vaultValue > 0f) ? EESText.T("EES.Scout.VaultValueExact", Mathf.RoundToInt(d.vaultValue)) : EESText.T("EES.Scout.VaultValueEst", Mathf.RoundToInt(StrongholdZoneUtility.EstimateVaultValue(d) / 100f) * 100));
		}
		if (d.sabotagedTurrets > 0)
		{
			stringBuilder.AppendLine(EESText.T("EES.Intel.Sabotaged", d.sabotagedTurrets));
		}
		if (flag)
		{
			int numTicks = d.supplyCutUntilTick - Find.TickManager.TicksGame;
			stringBuilder.AppendLine("<color=#ff3333>" + EESText.T("EES.Supply.CutInspect", numTicks.ToStringTicksToPeriod()) + "</color>");
		}
		return stringBuilder.ToString().TrimEndNewlines();
	}

	private static string EstPawnRange(int militaryPower)
	{
		int num = Mathf.RoundToInt((float)militaryPower / 140f);
		int num2 = Mathf.RoundToInt((float)militaryPower / 90f);
		return EESText.T("EES.Intel.PawnRange", num, num2);
	}

	public static void ApplyEntryAlertModifier(Settlement settlement, StrongholdData d)
	{
		if (Enabled && settlement != null && d != null && !RimPactsCompat.NotAPlayerAssault(settlement))
		{
			bool flag = IsScouted(d);
			int num = (flag ? (-2) : 2);
			string text = (flag ? "informed surprise(정찰 기습)" : "uninformed assault(직공 페널티)");
			EESLog.Message($"Scout entry-mod {settlement.LabelCap}: scouted={flag} alertDelta={num} " + $"({text}) alertBefore={d.alertLevel}");
			AlertUtility.RaiseAlert(d, settlement, num, text);
		}
	}
}
