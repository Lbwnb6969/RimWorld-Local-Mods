using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace EvolvingEnemyStrongholds;

public static class StrikeUtility
{
	public const string JobDefName = "EES_PrecisionStrike";

	private static int CooldownTicks => Mathf.RoundToInt((StrongholdMod.Settings?.interceptCooldownDays ?? 2f) * 60000f);

	public static bool CanStrikeNow(StrongholdData d, Settlement s, out string reason)
	{
		reason = null;
		if (!ScoutUtility.Enabled)
		{
			reason = EESText.T("EES.CanScout.Disabled");
			return false;
		}
		if (s == null || !s.Spawned || s.Faction == null || s.Faction == Faction.OfPlayer)
		{
			reason = EESText.T("EES.Intercept.BadTarget");
			return false;
		}
		if (d == null)
		{
			reason = EESText.T("EES.CanScout.NoData");
			return false;
		}
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
		if (ThreatEmitterUtility.JammingActive(s.Faction))
		{
			reason = EESText.T("EES.Jam.Blocked");
			return false;
		}
		if (EESCustomBaseDetector.IsDedicatedOutpostFaction(s.Faction))
		{
			reason = EESText.T("EES.Infil.DedicatedBase");
			return false;
		}
		if (!ScoutUtility.IsScouted(d))
		{
			reason = EESText.T("EES.Infil.NeedScout");
			return false;
		}
		if (d.infiltrated)
		{
			reason = EESText.T("EES.Infil.AlreadyDone");
			return false;
		}
		if (d.lastStrikeTick >= 0)
		{
			int num = Find.TickManager.TicksGame - d.lastStrikeTick;
			if (num < CooldownTicks)
			{
				reason = EESText.T("EES.CanScout.Cooldown", (CooldownTicks - num).ToStringTicksToPeriod());
				return false;
			}
		}
		return true;
	}

	public static float SuccessChance(Pawn pawn, StrongholdData d)
	{
		if (pawn == null || d == null)
		{
			return 0f;
		}
		int valueOrDefault = (pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level).GetValueOrDefault();
		float num = 0.5f + (float)valueOrDefault * 0.04f;
		return Mathf.Clamp01(ScoutUtility.SuccessChance(d) * num);
	}

	public static void BeginStrikeTargeting(Pawn pawn, Building_CommsConsole console)
	{
		if (pawn != null && console != null)
		{
			CameraJumper.TryShowWorld();
			Find.WorldTargeter.BeginTargeting((GlobalTargetInfo t) => OnTargetChosen(pawn, console, t), canTargetTiles: false, null, closeWorldTabWhenFinished: true, null, (GlobalTargetInfo t) => TargetChanceLabel(pawn, t), (GlobalTargetInfo t) => t.WorldObject is Settlement { Faction: not null } settlement && settlement.Faction != Faction.OfPlayer && WorldComponent_Strongholds.Instance != null);
		}
	}

	private static string TargetChanceLabel(Pawn pawn, GlobalTargetInfo t)
	{
		if (!t.IsValid || !(t.WorldObject is Settlement settlement))
		{
			return string.Empty;
		}
		if (settlement.Faction == null || settlement.Faction == Faction.OfPlayer)
		{
			return string.Empty;
		}
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
		if (strongholdData == null)
		{
			return string.Empty;
		}
		return EESText.T("EES.Common.SuccessChance", SuccessChance(pawn, strongholdData).ToStringPercent());
	}

	private static bool OnTargetChosen(Pawn pawn, Building_CommsConsole console, GlobalTargetInfo t)
	{
		if (!t.IsValid)
		{
			return false;
		}
		if (!(t.WorldObject is Settlement settlement))
		{
			Messages.Message(EESText.T("EES.Intercept.BadTarget"), MessageTypeDefOf.RejectInput, historical: false);
			return false;
		}
		if (!CanStrikeNow(WorldComponent_Strongholds.Instance?.GetOrCreate(settlement), settlement, out var reason))
		{
			Messages.Message(reason, MessageTypeDefOf.RejectInput, historical: false);
			return false;
		}
		StartStrikeJob(pawn, console, settlement);
		return true;
	}

	private static void StartStrikeJob(Pawn pawn, Building_CommsConsole console, Settlement s)
	{
		JobDef namedSilentFail = DefDatabase<JobDef>.GetNamedSilentFail("EES_PrecisionStrike");
		if (namedSilentFail == null)
		{
			EESLog.Error("Strike: EES_PrecisionStrike JobDef not found.");
			return;
		}
		Job job = JobMaker.MakeJob(namedSilentFail, console);
		pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
		if (pawn.jobs.curDriver is JobDriver_InterceptComms jobDriver_InterceptComms)
		{
			jobDriver_InterceptComms.target = s;
		}
		else
		{
			EESLog.Warning("Strike: job not started for " + pawn.LabelShort + " (curDriver != InterceptComms).");
		}
		Messages.Message(EESText.T("EES.Strike.Started", s.LabelCap, pawn.LabelShortCap), new GlobalTargetInfo(s), MessageTypeDefOf.NeutralEvent, historical: false);
		EESLog.Message($"Strike START {s.LabelCap} by {pawn.LabelShort} (console @ {console.Position}).");
	}

	public static void Resolve(Pawn pawn, Settlement s)
	{
		if (pawn == null || s == null)
		{
			return;
		}
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetOrCreate(s);
		if (!CanStrikeNow(strongholdData, s, out var reason))
		{
			EESLog.Message("Strike resolve aborted " + s.LabelCap + ": " + reason + ".");
			return;
		}
		strongholdData.lastStrikeTick = Find.TickManager.TicksGame;
		float num = SuccessChance(pawn, strongholdData);
		float value = Rand.Value;
		bool flag = value < num;
		int valueOrDefault = (pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level).GetValueOrDefault();
		EESLog.Message($"Strike RESOLVE {s.LabelCap} [{s.Faction?.Name}]: pawn {pawn.LabelShort} shoot={valueOrDefault} T{strongholdData.tier} {ScoutUtility.CurveDebug(strongholdData.tier)} " + string.Format("chance={0:0.00} roll={1:0.00} → {2}.", num, value, flag ? "SUCCESS" : "FAIL"));
		if (flag)
		{
			ScoutUtility.ApplySabotageSuccess(s, strongholdData, showMessage: true, "Strike");
		}
		else
		{
			ApplyFailure(pawn.MapHeld, s, strongholdData);
		}
	}

	private static void ApplyFailure(Map map, Settlement s, StrongholdData d)
	{
		AlertUtility.OnAttacked(d, s);
		if (map == null)
		{
			EESLog.Warning("Strike failure: no player map — short circuit skipped.");
			return;
		}
		int num = ForceShortCircuit(map, d.tier);
		if (StrongholdMod.Settings == null || StrongholdMod.Settings.scoutLetterNotify)
		{
			Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_StrikeFailed"), EESText.T("EES.Strike.FailText", s.LabelCap, num), EESLetterDefOf.EES_StrikeFailed);
		}
		else
		{
			Messages.Message(EESText.T("EES.Strike.FailText", s.LabelCap, num), MessageTypeDefOf.NegativeEvent, historical: false);
		}
		EESLog.Message($"Strike FAIL {s.LabelCap}: forced short-circuit on {num} conduit(s) (tier {d.tier}).");
	}

	public static int ConduitsToZap(int tier)
	{
		tier = Mathf.Clamp(tier, 1, 13);
		return Mathf.Max(1, tier / 2);
	}

	private static int ForceShortCircuit(Map map, int tier)
	{
		List<Building> list = new List<Building>();
		List<Building> list2 = map.listerBuildings?.allBuildingsColonist;
		if (list2 != null)
		{
			for (int i = 0; i < list2.Count; i++)
			{
				Building building = list2[i];
				if (building != null && !building.Destroyed)
				{
					CompPower compPower = building.TryGetComp<CompPower>();
					if (compPower != null && compPower.Props != null && compPower.Props.transmitsPower && compPower.PowerNet != null)
					{
						list.Add(building);
					}
				}
			}
		}
		if (list.Count == 0)
		{
			EESLog.Warning("Strike zzzt: 플레이어 맵에 전선(transmitsPower) 없음 → 단락 생략.");
			return 0;
		}
		list.Shuffle();
		int num = Mathf.Min(ConduitsToZap(tier), list.Count);
		int num2 = 0;
		for (int j = 0; j < num; j++)
		{
			if (ForceZapConduit(list[j]))
			{
				num2++;
			}
		}
		return num2;
	}

	private static bool ForceZapConduit(Building culprit)
	{
		Map map = culprit.Map;
		PowerNet powerNet = culprit.TryGetComp<CompPower>()?.PowerNet;
		if (map == null || powerNet == null)
		{
			return false;
		}
		bool flag = false;
		for (int i = 0; i < powerNet.batteryComps.Count; i++)
		{
			if (powerNet.batteryComps[i].StoredEnergy > 20f)
			{
				flag = true;
				break;
			}
		}
		if (flag)
		{
			float num = 0f;
			for (int j = 0; j < powerNet.batteryComps.Count; j++)
			{
				CompPowerBattery compPowerBattery = powerNet.batteryComps[j];
				num += compPowerBattery.StoredEnergy;
				compPowerBattery.DrawPower(compPowerBattery.StoredEnergy);
			}
			float num2 = Mathf.Clamp(Mathf.Sqrt(num) * 0.05f, 1.5f, 14.9f);
			GenExplosion.DoExplosion(culprit.Position, map, num2, DamageDefOf.Flame, null);
			if (num2 > 3.5f)
			{
				GenExplosion.DoExplosion(culprit.Position, map, num2 * 0.3f, DamageDefOf.Bomb, null);
			}
		}
		else
		{
			TryStartFireNearConduit(culprit);
		}
		return true;
	}

	private static void TryStartFireNearConduit(Building b)
	{
		List<IntVec3> list = new List<IntVec3>();
		int num = GenRadial.NumCellsInRadius(3f);
		CellRect startRect = b.OccupiedRect();
		for (int i = 0; i < num; i++)
		{
			IntVec3 intVec = b.Position + GenRadial.RadialPattern[i];
			if (GenSight.LineOfSight(b.Position, intVec, b.Map, startRect, CellRect.SingleCell(intVec)) && FireUtility.ChanceToStartFireIn(intVec, b.Map) > 0f)
			{
				list.Add(intVec);
			}
		}
		if (list.Count > 0)
		{
			FireUtility.TryStartFireIn(list.RandomElement(), b.Map, Rand.Range(0.1f, 0.925f), null);
		}
	}
}
