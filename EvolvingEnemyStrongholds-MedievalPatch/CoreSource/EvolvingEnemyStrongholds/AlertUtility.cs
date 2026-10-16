using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class AlertUtility
{
	public const int MaxAlert = 5;

	private static bool Enabled
	{
		get
		{
			if (StrongholdMod.Settings != null)
			{
				return StrongholdMod.Settings.enableAlertSystem;
			}
			return true;
		}
	}

	public static string AlertLabel(int level)
	{
		return Mathf.Clamp(level, 0, 5) switch
		{
			0 => "Calm",
			1 => "Patrols up",
			2 => "Reinforced",
			3 => "Hardened perimeter",
			4 => "Elite + mortars",
			5 => "Retaliation ready",
			_ => "?",
		};
	}

	public static void RaiseAlert(StrongholdData d, Settlement settlement, int amount, string reason)
	{
		if (Enabled && d != null && amount != 0)
		{
			int alertLevel = d.alertLevel;
			d.alertLevel = Mathf.Clamp(d.alertLevel + amount, 0, 5);
			if (settlement != null)
			{
				StrongholdScoreUtility.ApplyDerived(d, settlement);
			}
			if (d.alertLevel > alertLevel)
			{
				d.lastAlertRaisedTick = Find.TickManager.TicksGame;
			}
			if (d.alertLevel != alertLevel)
			{
				EESLog.Message("Alert " + (settlement?.LabelCap ?? d.settlementId.ToString()) + " " + $"{alertLevel}->{d.alertLevel} ({AlertLabel(d.alertLevel)}) [{reason}]");
			}
		}
	}

	public static void OnAttacked(StrongholdData attacked, Settlement attackedSettlement)
	{
		if (!Enabled || attacked == null || attackedSettlement == null || RimPactsCompat.NotAPlayerAssault(attackedSettlement))
		{
			return;
		}
		StrongholdSettings settings = StrongholdMod.Settings;
		int amount = settings?.alertOnAttack ?? 2;
		int num = settings?.alertSpreadToFaction ?? 1;
		RaiseAlert(attacked, attackedSettlement, amount, "base attacked");
		if (num <= 0)
		{
			return;
		}
		Faction faction = attackedSettlement.Faction;
		if (faction == null)
		{
			return;
		}
		WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
		if (instance == null)
		{
			return;
		}
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (settlement != attackedSettlement && settlement.Faction == faction)
			{
				RaiseAlert(instance.GetOrCreate(settlement), settlement, num, "faction-wide spread");
			}
		}
	}

	public static void DecayTick(StrongholdData d, int nowTick)
	{
		if (!Enabled || d == null || d.alertLevel <= 0)
		{
			return;
		}
		int num = Mathf.RoundToInt((StrongholdMod.Settings?.alertDecayDays ?? 2f) * 60000f);
		if (d.lastAlertRaisedTick < 0)
		{
			d.lastAlertRaisedTick = nowTick;
		}
		else if (nowTick - d.lastAlertRaisedTick >= num)
		{
			int alertLevel = d.alertLevel;
			d.alertLevel = Mathf.Max(0, d.alertLevel - 1);
			d.lastAlertRaisedTick = nowTick;
			Settlement settlement = WorldComponent_Strongholds.Instance?.GetSettlement(d.settlementId);
			if (settlement != null)
			{
				StrongholdScoreUtility.ApplyDerived(d, settlement);
			}
			EESLog.Message($"Alert decay id={d.settlementId} {alertLevel}->{d.alertLevel} ({AlertLabel(d.alertLevel)})");
		}
	}

	public static void ForceDecay(StrongholdData d, Settlement settlement)
	{
		if (d != null && d.alertLevel > 0)
		{
			int alertLevel = d.alertLevel;
			d.alertLevel = Mathf.Max(0, d.alertLevel - 1);
			if (settlement != null)
			{
				StrongholdScoreUtility.ApplyDerived(d, settlement);
			}
			EESLog.Message($"Alert FORCE decay {settlement?.LabelCap ?? d.settlementId.ToString()} {alertLevel}->{d.alertLevel} ({AlertLabel(d.alertLevel)})");
		}
	}
}
