using System;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public class WITab_Scouting : WITab
{
	public static readonly WITab_Scouting Instance = new WITab_Scouting();

	private Vector2 scrollPos = Vector2.zero;

	private Settlement SelSettlement => base.SelObject as Settlement;

	public override bool IsVisible
	{
		get
		{
			Settlement selSettlement = SelSettlement;
			if (selSettlement != null && selSettlement.Faction != null && !selSettlement.Faction.IsPlayer && !EESFactionBlacklist.IsBlocked(selSettlement.Faction))
			{
				return !EESCustomBaseDetector.IsForeignSettlement(selSettlement);
			}
			return false;
		}
	}

	public WITab_Scouting()
	{
		size = new Vector2(440f, 360f);
		labelKey = "EES.Scout.TabLabel";
	}

	protected override void FillTab()
	{
		Settlement selSettlement = SelSettlement;
		if (selSettlement == null)
		{
			return;
		}
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetOrCreate(selSettlement);
		Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(12f);
		Text.Font = GameFont.Small;
		if (strongholdData == null)
		{
			Widgets.Label(rect, EESText.T("EES.Scout.NoData"));
		}
		else if (!ScoutUtility.IsScouted(strongholdData))
		{
			Widgets.Label(rect, "<b>" + EESText.T("EES.Scout.TierOnly", strongholdData.tier) + "</b>");
			Rect rect2 = new Rect(rect.x, rect.y + 28f, rect.width, 28f);
			GUI.color = new Color(1f, 0.85f, 0.4f);
			Widgets.Label(rect2, EESText.T("EES.Scout.NotScouted"));
			GUI.color = Color.white;
			bool num = EESCustomBaseDetector.IsDedicatedOutpostFaction(selSettlement.Faction);
			float num2 = (num ? 64f : 0f);
			Widgets.Label(new Rect(rect.x, rect.y + 56f, rect.width, rect.height - 56f - num2), EESText.T("EES.Scout.NotScoutedDesc"));
			if (num)
			{
				Rect rect3 = new Rect(rect.x, rect.yMax - 60f, rect.width, 56f);
				GUI.color = new Color(0.62f, 0.62f, 0.64f);
				Widgets.Label(rect3, "(" + EESText.T("EES.Scout.DedicatedNote") + ")");
				GUI.color = Color.white;
			}
		}
		else
		{
			string text = BuildScoutReport(selSettlement, strongholdData);
			float a = Text.CalcHeight(text, rect.width - 16f);
			Rect viewRect = new Rect(0f, 0f, rect.width - 16f, Mathf.Max(a, rect.height));
			Widgets.BeginScrollView(rect, ref scrollPos, viewRect);
			Widgets.Label(new Rect(0f, 0f, viewRect.width, viewRect.height), text);
			Widgets.EndScrollView();
		}
	}

	private static string BuildScoutReport(Settlement s, StrongholdData d)
	{
		StringBuilder stringBuilder = new StringBuilder();
		bool flag = EESCustomBaseDetector.IsDedicatedOutpostFaction(s.Faction);
		string text = StrongholdScoreUtility.DisplayRole(d, s);
		stringBuilder.AppendLine((text == null) ? ("<b>" + EESText.T("EES.Scout.TierHdr", d.tier) + "</b>") : ("<b>" + EESText.T("EES.Scout.TierRole", d.tier, RoleLabel(text)) + "</b>"));
		if (StrongholdFeatureUtility.IsValid(d.featureKey))
			stringBuilder.AppendLine(EESText.T("EES.Scout.Feature", StrongholdFeatureUtility.Label(d.featureKey), StrongholdFeatureUtility.Description(d.featureKey)));
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("<b>" + EESText.T("EES.Scout.Hdr.Strength") + "</b>");
		bool flag2 = SupplyCaravanUtility.SupplyCutActive(d);
		bool flag3 = text == "CryoBarracks";
		float num = (flag3 ? 0.65f : 1f);
		int num2 = Mathf.RoundToInt((float)d.militaryPower * num);
		int num3 = Mathf.RoundToInt((flag2 ? ((float)d.militaryPower * 0.75f) : ((float)d.militaryPower)) * num);
		stringBuilder.AppendLine(EESText.T("EES.Scout.Mil", num3) + (flag2 ? $" <color=#ff3333>(-{num2 - num3})</color>" : ""));
		if (!flag)
		{
			stringBuilder.AppendLine(EESText.T("EES.Scout.Fort", d.fortification));
			stringBuilder.AppendLine(EESText.T("EES.Scout.Supply", d.supply));
		}
		stringBuilder.AppendLine(EESText.T("EES.Scout.Alert", AlertText(d.alertLevel)));
		if (flag3)
		{
			stringBuilder.AppendLine(EESText.T("EES.Scout.CryoSign"));
		}
		stringBuilder.AppendLine();
		if (flag)
		{
			stringBuilder.AppendLine("<b>" + EESText.T("EES.Scout.Hdr.Facilities") + "</b>");
			if (AppendFacility(stringBuilder, d.hasCommander, EESText.T("EES.Scout.Fac.Commander")) == 0)
			{
				stringBuilder.AppendLine(EESText.T("EES.Scout.Fac.None"));
			}
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("<color=#9e9ea3>(" + EESText.T("EES.Scout.DedicatedNote") + ")</color>");
			return stringBuilder.ToString();
		}
		stringBuilder.AppendLine("<b>" + EESText.T("EES.Scout.Hdr.Defense") + "</b>");
		EstimateTurrets(s, d, out var direct, out var mortarMax);
		if (direct <= 0 && mortarMax <= 0)
		{
			stringBuilder.AppendLine(EESText.T("EES.Scout.NoDefense"));
		}
		else
		{
			stringBuilder.AppendLine(EESText.T("EES.Scout.Turrets", direct));
			if (mortarMax > 0)
			{
				stringBuilder.AppendLine(EESText.T("EES.Scout.Mortars", mortarMax));
			}
		}
		if (d.sabotagedTurrets > 0)
		{
			stringBuilder.AppendLine(EESText.T("EES.Scout.Sabotaged", d.sabotagedTurrets));
		}
		if (d.sabotagedMortars > 0)
		{
			stringBuilder.AppendLine(EESText.T("EES.Scout.SabotagedMortar", d.sabotagedMortars));
		}
		if (flag2)
		{
			int numTicks = d.supplyCutUntilTick - Find.TickManager.TicksGame;
			stringBuilder.AppendLine("<color=#ff3333>  " + EESText.T("EES.Scout.SupplyCut", 2, 1, numTicks.ToStringTicksToPeriod()) + "</color>");
		}
		stringBuilder.AppendLine();
		if (!EESPuppetUtility.IsPuppetOutpost(s))
		{
			stringBuilder.AppendLine("<b>" + EESText.T("EES.Scout.Hdr.Loot") + "</b>");
			AppendLootEstimate(stringBuilder, d);
			stringBuilder.AppendLine();
		}
		stringBuilder.AppendLine("<b>" + EESText.T("EES.Scout.Hdr.Facilities") + "</b>");
		int num4 = 0 + AppendFacility(stringBuilder, d.hasCommsRoom, EESText.T("EES.Scout.Fac.Comms")) + AppendFacility(stringBuilder, d.hasAmmoDepot, EESText.T("EES.Scout.Fac.Ammo")) + AppendFacility(stringBuilder, d.hasMortarBattery, EESText.T("EES.Scout.Fac.Mortar")) + AppendFacility(stringBuilder, d.hasPrison, EESText.T("EES.Scout.Fac.Prison")) + AppendFacility(stringBuilder, d.hasCommander, EESText.T("EES.Scout.Fac.Commander")) + AppendFacility(stringBuilder, d.hasElite, EESText.T("EES.Scout.Fac.Elite")) + AppendFacility(stringBuilder, d.hasZoneGuard, EESText.T("EES.Scout.Fac.Zone", EESText.T("EES.Dir." + DirKey(d.zoneGuardDir)))) + AppendFacility(stringBuilder, d.pursuerGroups > 0, EESText.T("EES.Scout.Fac.Pursuer")) + AppendFacility(stringBuilder, d.hasHerald, EESText.T("EES.Scout.Fac.Herald")) + AppendFacility(stringBuilder, d.disruptorGroups > 0, EESText.T("EES.Scout.Fac.Disruptor")) + AppendFacility(stringBuilder, d.hasAssault, EESText.T("EES.Scout.Fac.Assault"));
		int num5 = MechRelayUtility.EstimateMechCount(s, d);
		if (num4 + AppendFacility(stringBuilder, num5 > 0, EESText.T("EES.Scout.Fac.Mechs", num5)) + AppendFacility(stringBuilder, text == "ArtilleryBase" && !d.artillerySilenced, EESText.T("EES.Scout.Fac.Artillery")) + AppendFacility(stringBuilder, text == "PsychicShrine", EESText.T("EES.Scout.Fac.Shrine")) + AppendFacility(stringBuilder, text == "JammingStation", EESText.T("EES.Scout.Fac.Jamming")) == 0)
		{
			stringBuilder.AppendLine(EESText.T("EES.Scout.Fac.None"));
		}
		stringBuilder.AppendLine();
		stringBuilder.AppendLine("<b>" + EESText.T("EES.Scout.Hdr.Scouted") + "</b>");
		stringBuilder.AppendLine(EESPuppetUtility.IsPuppetOutpost(s) ? ("  " + EESText.T("EES.Puppet.OwnOutpost")) : ("  " + ScoutDateString(s, d)));
		return stringBuilder.ToString().TrimEnd(Array.Empty<char>());
	}

	private static int AppendFacility(StringBuilder sb, bool has, string label)
	{
		if (!has)
		{
			return 0;
		}
		sb.AppendLine("  · " + label);
		return 1;
	}

	private static string ScoutDateString(Settlement s, StrongholdData d)
	{
		if (d.lastScoutedTick < 0)
		{
			return EESText.T("EES.Scout.Date.Done");
		}
		int ticksGame = Find.TickManager.TicksGame;
		int num = Mathf.Max(0, (ticksGame - d.lastScoutedTick) / 60000);
		string text = EESText.T("EES.Scout.Date.DaysAgo", num);
		try
		{
			long absTicks = d.lastScoutedTick + (Find.TickManager.TicksAbs - Find.TickManager.TicksGame);
			Vector2 location = Find.WorldGrid.LongLatOf(s.Tile);
			string text2 = GenDate.DateFullStringAt(absTicks, location);
			return EESText.T("EES.Scout.Date.Full", text2, text);
		}
		catch
		{
			return text;
		}
	}

	private static void EstimateTurrets(Settlement s, StrongholdData d, out int direct, out int mortarMax)
	{
		StrongholdGarrisonUtility.EstimateGarrison(d, s, out direct, out mortarMax);
		if (!d.hasMortarBattery)
		{
			mortarMax = 0;
		}
	}

	private static void AppendLootEstimate(StringBuilder sb, StrongholdData d)
	{
		float num = (StrongholdScoreUtility.RewardBudgetMultiplier(d.tier, d.featureKey) + (float)d.supply * 0.02f) * (StrongholdMod.Settings?.lootMultiplier ?? 1f);
		if (d.vaultValue > 0f)
		{
			sb.AppendLine(EESText.T("EES.Scout.VaultValueExact", Mathf.RoundToInt(d.vaultValue)));
		}
		else
		{
			int num2 = Mathf.RoundToInt(StrongholdZoneUtility.EstimateVaultValue(d) / 100f) * 100;
			sb.AppendLine(EESText.T("EES.Scout.VaultValueEst", num2));
		}
		int num3 = Mathf.RoundToInt(350f * num);
		sb.AppendLine(EESText.T("EES.Scout.Silver", num3));
		sb.AppendLine(EESText.T("EES.Scout.Resources"));
		sb.AppendLine(EESText.T("EES.Scout.RoleLoot"));
		if (d.tier >= 13)
		{
			sb.AppendLine(EESText.T("EES.Scout.LegendaryT7"));
		}
		else if (d.tier >= StrongholdZoneUtility.LegendaryGearTier)
		{
			sb.AppendLine(EESText.T("EES.Scout.LegendaryT6"));
		}
	}

	private static string AlertText(int alert)
	{
		if (alert <= 0)
		{
			return EESText.T("EES.Alert.None");
		}
		if (alert <= 2)
		{
			return EESText.T("EES.Alert.Low");
		}
		if (alert == 3)
		{
			return EESText.T("EES.Alert.Medium");
		}
		return EESText.T("EES.Alert.High");
	}

	private static string DirKey(int dir)
	{
		return dir switch
		{
			1 => "E",
			2 => "S",
			3 => "W",
			_ => "N",
		};
	}

	private static string RoleLabel(string roleDefName)
	{
		if (roleDefName.NullOrEmpty())
		{
			return EESText.T("EES.Role.Generic");
		}
		return roleDefName switch
		{
			"CommandFortress" => EESText.T("EES.Role.CommandFortress"),
			"MilitaryBase" => EESText.T("EES.Role.MilitaryBase"),
			"Garrison" => EESText.T("EES.Role.Garrison"),
			"AmmoDepot" => EESText.T("EES.Role.AmmoDepot"),
			"MortarBattery" => EESText.T("EES.Role.MortarBattery"),
			"PrisonCamp" => EESText.T("EES.Role.PrisonCamp"),
			"ResearchOutpost" => EESText.T("EES.Role.ResearchOutpost"),
			"TreasureVault" => EESText.T("EES.Role.TreasureVault"),
			"Outpost" => EESText.T("EES.Role.Outpost"),
			"SupplyDepot" => EESText.T("EES.Role.SupplyDepot"),
			"TrainingCamp" => EESText.T("EES.Role.TrainingCamp"),
			"BlackMarketHub" => EESText.T("EES.Role.BlackMarketHub"),
			"NobleEstate" => EESText.T("EES.Role.NobleEstate"),
			"MechanoidRelay" => EESText.T("EES.Role.MechanoidRelay"),
			"InsectHive" => EESText.T("EES.Role.InsectHive"),
			"ThrumboRanch" => EESText.T("EES.Role.ThrumboRanch"),
			"AlphaThrumboRanch" => EESText.T("EES.Role.ThrumboRanch"),
			"PollutedOutpost" => EESText.T("EES.Role.PollutedOutpost"),
			"Battlefield" => EESText.T("EES.Role.Battlefield"),
			"CryoBarracks" => EESText.T("EES.Role.CryoBarracks"),
			"ArtilleryBase" => EESText.T("EES.Role.ArtilleryBase"),
			"PsychicShrine" => EESText.T("EES.Role.PsychicShrine"),
			"JammingStation" => EESText.T("EES.Role.JammingStation"),
			_ => roleDefName,
		};
	}
}
