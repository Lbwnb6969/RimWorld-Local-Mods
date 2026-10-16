using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace EvolvingEnemyStrongholds;

public static class InterceptUtility
{
	private static JobDef _jobDef;

	private static RaidStrategyDef _sappers;

	private static RaidStrategyDef _breaching;

	private static bool _resolved;

	private const float FailRaidPointsMul = 1.1f;

	private static int InterceptCooldownTicks => Mathf.RoundToInt((StrongholdMod.Settings?.interceptCooldownDays ?? 2f) * 60000f);

	private static void Resolve()
	{
		if (!_resolved)
		{
			_jobDef = DefDatabase<JobDef>.GetNamedSilentFail("EES_InterceptComms");
			_sappers = DefDatabase<RaidStrategyDef>.GetNamedSilentFail("ImmediateAttackSappers");
			_breaching = DefDatabase<RaidStrategyDef>.GetNamedSilentFail("ImmediateAttackBreaching");
			_resolved = true;
		}
	}

	public static bool CanInterceptNow(StrongholdData d, Settlement s, out string reason)
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
		if (ScoutUtility.IsScouted(d))
		{
			reason = EESText.T("EES.CanScout.AlreadyScouted");
			return false;
		}
		if (d.lastInterceptTick >= 0)
		{
			int num = Find.TickManager.TicksGame - d.lastInterceptTick;
			if (num < InterceptCooldownTicks)
			{
				reason = EESText.T("EES.CanScout.Cooldown", (InterceptCooldownTicks - num).ToStringTicksToPeriod());
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
		int valueOrDefault = (pawn.skills?.GetSkill(SkillDefOf.Intellectual)?.Level).GetValueOrDefault();
		float num = 0.5f + (float)valueOrDefault * 0.04f;
		return Mathf.Clamp01(ScoutUtility.SuccessChance(d) * num);
	}

	public static void BeginInterceptTargeting(Pawn pawn, Building_CommsConsole console)
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
		if (!CanInterceptNow(WorldComponent_Strongholds.Instance?.GetOrCreate(settlement), settlement, out var reason))
		{
			Messages.Message(reason, MessageTypeDefOf.RejectInput, historical: false);
			return false;
		}
		StartInterceptJob(pawn, console, settlement);
		return true;
	}

	private static void StartInterceptJob(Pawn pawn, Building_CommsConsole console, Settlement s)
	{
		Resolve();
		if (_jobDef == null)
		{
			EESLog.Error("Intercept: EES_InterceptComms JobDef not found.");
			return;
		}
		Job job = JobMaker.MakeJob(_jobDef, console);
		pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
		if (pawn.jobs.curDriver is JobDriver_InterceptComms jobDriver_InterceptComms)
		{
			jobDriver_InterceptComms.target = s;
		}
		else
		{
			EESLog.Warning("Intercept: job not started for " + pawn.LabelShort + " (curDriver != InterceptComms).");
		}
		Messages.Message(EESText.T("EES.Intercept.Started", s.LabelCap, pawn.LabelShortCap), new GlobalTargetInfo(s), MessageTypeDefOf.NeutralEvent, historical: false);
		EESLog.Message($"Intercept START {s.LabelCap} by {pawn.LabelShort} (console @ {console.Position}).");
	}

	public static void Resolve(Pawn pawn, Settlement s)
	{
		if (pawn == null || s == null)
		{
			return;
		}
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetOrCreate(s);
		if (!CanInterceptNow(strongholdData, s, out var reason))
		{
			EESLog.Message("Intercept resolve aborted " + s.LabelCap + ": " + reason + ".");
			return;
		}
		strongholdData.lastInterceptTick = Find.TickManager.TicksGame;
		float num = SuccessChance(pawn, strongholdData);
		float value = Rand.Value;
		bool flag = value < num;
		int valueOrDefault = (pawn.skills?.GetSkill(SkillDefOf.Intellectual)?.Level).GetValueOrDefault();
		EESLog.Message($"Intercept RESOLVE {s.LabelCap} [{s.Faction?.Name}]: pawn {pawn.LabelShort} intel={valueOrDefault} T{strongholdData.tier} {ScoutUtility.CurveDebug(strongholdData.tier)} " + string.Format("chance={0:0.00} roll={1:0.00} → {2}.", num, value, flag ? "SUCCESS" : "FAIL"));
		if (flag)
		{
			strongholdData.intelLevel = 1;
			strongholdData.lastScoutedTick = Find.TickManager.TicksGame;
			Messages.Message(EESText.T("EES.Intercept.Success", s.LabelCap), new GlobalTargetInfo(s), MessageTypeDefOf.PositiveEvent, historical: false);
		}
		else
		{
			ApplyFailure(pawn.MapHeld, s, strongholdData);
		}
	}

	private static void ApplyFailure(Map map, Settlement s, StrongholdData d)
	{
		Resolve();
		Faction faction = s.Faction;
		AlertUtility.OnAttacked(d, s);
		if (faction != null && faction != Faction.OfPlayer)
		{
			faction.TryAffectGoodwillWith(Faction.OfPlayer, -200, canSendMessage: false);
		}
		if (map == null)
		{
			EESLog.Warning("Intercept failure: no map for raid — relations soured only.");
			return;
		}
		float num = StrongholdMod.Settings?.interceptMechChance ?? 0.1f;
		if (d.tier >= 9 && Rand.Value < num)
		{
			FireMechRaid(map, faction);
		}
		else
		{
			FireFactionRaid(map, faction);
		}
	}

	private static void FireFactionRaid(Map map, Faction faction)
	{
		if (!IgnoranceCompat.FactionAllowed(faction))
		{
			EESLog.Message("Intercept FAIL raid 취소 [" + faction?.Name + "] — Ignorance Is Bliss 테크 제한(적대 전환은 그대로 적용됨).");
			return;
		}
		float num = StorytellerUtility.DefaultThreatPointsNow(map);
		float points = Mathf.Max(num * 1.1f, 100f);
		IncidentParms incidentParms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
		incidentParms.faction = faction;
		incidentParms.forced = true;
		incidentParms.points = points;
		RaidStrategyDef raidStrategyDef = _sappers ?? _breaching;
		bool flag = PawnsArrivalModeDefOf.CenterDrop.Worker.CanUseWith(incidentParms);
		bool flag2 = raidStrategyDef?.Worker.CanUseWith(incidentParms, PawnGroupKindDefOf.Combat) ?? false;
		string text;
		if (flag2 && (!flag || Rand.Bool))
		{
			incidentParms.raidStrategy = raidStrategyDef;
			incidentParms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
			text = "sapper";
		}
		else if (flag)
		{
			incidentParms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
			incidentParms.raidArrivalMode = PawnsArrivalModeDefOf.CenterDrop;
			text = "centerdrop";
		}
		else
		{
			incidentParms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
			incidentParms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
			text = "edgewalk-fallback";
		}
		incidentParms.customLetterLabel = EESText.T("EES_Letter_InterceptRaid");
		incidentParms.customLetterText = EESText.T("EES.Intercept.RaidText", faction?.Name ?? "?");
		IncidentDefOf.RaidEnemy.Worker.TryExecute(incidentParms);
		EESLog.Message($"Intercept FAIL raid [{faction?.Name}] mode={text} (canDrop={flag}, sapperOk={flag2}) " + $"points={incidentParms.points:0}(자산기반 {num:0} × {1.1f:0.00}, 하한 100).");
	}

	private static void FireMechRaid(Map map, Faction offended)
	{
		Faction ofMechanoids = Faction.OfMechanoids;
		if (ofMechanoids == null || !IgnoranceCompat.FactionAllowed(ofMechanoids))
		{
			if (ofMechanoids != null)
			{
				EESLog.Message("Intercept FAIL mech-reroute 취소 — Ignorance Is Bliss 메카 제한 → 일반 세력 습격으로 폴백.");
			}
			FireFactionRaid(map, offended);
			return;
		}
		IncidentParms incidentParms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
		incidentParms.faction = ofMechanoids;
		incidentParms.forced = true;
		incidentParms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
		incidentParms.raidArrivalMode = PawnsArrivalModeDefOf.CenterDrop;
		incidentParms.customLetterLabel = EESText.T("EES_Letter_InterceptMech");
		incidentParms.customLetterText = EESText.T("EES.Intercept.MechText", offended?.Name ?? "?");
		IncidentDefOf.RaidEnemy.Worker.TryExecute(incidentParms);
		EESLog.Message($"Intercept FAIL mech-reroute [offended {offended?.Name}] points={incidentParms.points:0}.");
	}
}
