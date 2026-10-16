using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public class StrongholdMod : Mod
{
	private Vector2 scrollPos;

	private float viewHeight = 3710f;

	private int activeTab;

	private Vector2 blScrollPos;

	private Vector2 raceTurretScrollPos;

	private Vector2 modTurretScrollPos;

	private const float BlRowH = 40f;

	private const float TurretHdrH = 26f;

	public static StrongholdSettings Settings { get; private set; }

	public StrongholdMod(ModContentPack content)
		: base(content)
	{
		LocalSettingsMigration.ImportIfMissing(content);
		Settings = GetSettings<StrongholdSettings>();
	}

	public override string SettingsCategory()
	{
		return "Evolving Enemy Strongholds";
	}

	private static string L(string key)
	{
		return key.Translate();
	}

	private static bool LabelWithReset(Listing_Standard l, string text)
	{
		Rect rect = l.GetRect(24f);
		Widgets.Label(new Rect(rect.x, rect.y, rect.width - 64f - 6f, rect.height), text);
		return Widgets.ButtonText(new Rect(rect.xMax - 64f, rect.y, 64f, rect.height), L("EES.Set.Reset"));
	}

	private static void FloatRow(Listing_Standard l, string label, ref float val, float min, float max, float def, string suffix, string fmt = "0.00")
	{
		if (LabelWithReset(l, label + ": " + val.ToString(fmt) + suffix))
		{
			val = def;
		}
		val = l.Slider(val, min, max);
	}

	private static void IntRow(Listing_Standard l, string label, ref int val, float min, float max, int def, string prefix = "", string suffix = "")
	{
		if (LabelWithReset(l, label + ": " + prefix + val + suffix))
		{
			val = def;
		}
		val = (int)l.Slider(val, min, max);
	}

	public override void DoSettingsWindowContents(Rect inRect)
	{
		Rect rect = inRect;
		rect.yMin += 32f;
		Widgets.DrawMenuSection(rect);
		List<TabRecord> tabs = new List<TabRecord>
		{
			new TabRecord(L("EES.Set.Tab.Values"), delegate
			{
				activeTab = 0;
			}, activeTab == 0),
			new TabRecord(L("EES.Set.Tab.Blacklist"), delegate
			{
				activeTab = 1;
			}, activeTab == 1),
			new TabRecord(L("EES.Set.Tab.TurretBL"), delegate
			{
				activeTab = 2;
			}, activeTab == 2)
		};
		TabDrawer.DrawTabs(rect, tabs);
		Rect inRect2 = rect.ContractedBy(10f);
		if (activeTab == 2)
		{
			DrawTurretBlacklistTab(inRect2, Settings);
		}
		else if (activeTab == 1)
		{
			DrawBlacklistTab(inRect2, Settings);
		}
		else
		{
			DrawValuesTab(inRect2, Settings);
		}
		base.DoSettingsWindowContents(inRect);
	}

	private void DrawBlacklistTab(Rect inRect, StrongholdSettings s)
	{
		List<FactionDef> list = EESFactionBlacklist.ConfigurableFactionDefs();
		Rect rect = new Rect(0f, 0f, inRect.width - 20f, 120f + (float)list.Count * 44f);
		Widgets.BeginScrollView(inRect, ref blScrollPos, rect);
		Listing_Standard listing_Standard = new Listing_Standard();
		listing_Standard.Begin(rect);
		listing_Standard.Label(L("EES.Set.Blacklist.Desc"));
		listing_Standard.GapLine();
		ModContentPack modContentPack = null;
		bool flag = true;
		foreach (FactionDef item in list)
		{
			if (item.modContentPack != modContentPack)
			{
				modContentPack = item.modContentPack;
				if (!flag)
				{
					listing_Standard.GapLine(6f);
				}
				flag = false;
			}
			Rect rect2 = listing_Standard.GetRect(40f);
			if (Mouse.IsOver(rect2))
			{
				Widgets.DrawHighlight(rect2);
			}
			float y = rect2.y + 8f;
			bool flag2 = s.blacklistedFactionDefs.Contains(item.defName);
			bool checkOn = flag2;
			Rect rect3 = new Rect(rect2.xMax - 28f, y, 24f, 24f);
			Widgets.Checkbox(rect3.x, rect3.y, ref checkOn);
			Rect position = new Rect(rect2.x + 6f, y, 24f, 24f);
			if (item.FactionIcon != null)
			{
				Color color = GUI.color;
				GUI.color = item.DefaultColor;
				GUI.DrawTexture(position, item.FactionIcon);
				GUI.color = color;
			}
			string text = item.LabelCap.ToString();
			Vector2 vector = Text.CalcSize(text);
			float num = position.xMax + 10f;
			Widgets.Label(new Rect(num, rect2.y + 8f, vector.x + 6f, 26f), text);
			string text2 = ((item.modContentPack != null) ? item.modContentPack.Name : "");
			if (!text2.NullOrEmpty())
			{
				float num2 = num + vector.x + 14f;
				float num3 = rect3.x - 8f - num2;
				if (num3 > 30f)
				{
					Color color2 = GUI.color;
					GUI.color = new Color(0.6f, 0.6f, 0.62f);
					Widgets.Label(new Rect(num2, rect2.y + 9f, num3, 24f), text2);
					GUI.color = color2;
				}
			}
			if (Widgets.ButtonInvisible(new Rect(rect2.x, rect2.y, rect3.x - rect2.x - 4f, 40f)))
			{
				checkOn = !checkOn;
			}
			if (checkOn != flag2)
			{
				if (checkOn)
				{
					s.blacklistedFactionDefs.Add(item.defName);
				}
				else
				{
					s.blacklistedFactionDefs.Remove(item.defName);
				}
			}
		}
		listing_Standard.End();
		Widgets.EndScrollView();
	}

	private void DrawTurretBlacklistTab(Rect inRect, StrongholdSettings s)
	{
		float num = (inRect.height - 16f) / 2f;
		Rect rect = new Rect(inRect.x, inRect.y, inRect.width, num);
		Rect rect2 = new Rect(inRect.x, inRect.y + num + 16f, inRect.width, num);
		float y = inRect.y + num + 8f;
		Widgets.DrawLineHorizontal(inRect.x, y, inRect.width, new Color(0.55f, 0.55f, 0.58f));
		DrawRaceTurretPane(rect, s);
		DrawModTurretPane(rect2, s);
	}

	private void DrawRaceTurretPane(Rect rect, StrongholdSettings s)
	{
		Widgets.Label(new Rect(rect.x, rect.y, rect.width, 26f), L("EES.Set.TurretBL.RaceHdr"));
		Rect outRect = new Rect(rect.x, rect.y + 26f, rect.width, rect.height - 26f);
		List<EESTurretBlacklist.RaceTurret> list = EESTurretBlacklist.RaceLinkedTurrets();
		int num = list.Select((EESTurretBlacklist.RaceTurret e) => e.mod).Distinct().Count();
		float a = 30f + (float)num * 14f + (float)list.Count * 42f + 12f;
		Rect rect2 = new Rect(0f, 0f, outRect.width - 20f, Mathf.Max(a, outRect.height));
		Widgets.BeginScrollView(outRect, ref raceTurretScrollPos, rect2);
		Listing_Standard listing_Standard = new Listing_Standard();
		listing_Standard.Begin(rect2);
		listing_Standard.Label(L("EES.Set.TurretBL.RaceDesc"));
		if (list.Count == 0)
		{
			listing_Standard.Label(L("EES.Set.TurretBL.None"));
		}
		ModContentPack modContentPack = null;
		bool flag = true;
		foreach (EESTurretBlacklist.RaceTurret item in list)
		{
			if (item.mod != modContentPack)
			{
				modContentPack = item.mod;
				if (!flag)
				{
					listing_Standard.GapLine(6f);
				}
				flag = false;
			}
			TurretRow(listing_Standard, s, item.turret, item.raceLabel);
		}
		listing_Standard.End();
		Widgets.EndScrollView();
	}

	private void DrawModTurretPane(Rect rect, StrongholdSettings s)
	{
		Widgets.Label(new Rect(rect.x, rect.y, rect.width, 26f), L("EES.Set.TurretBL.ModHdr"));
		Rect outRect = new Rect(rect.x, rect.y + 26f, rect.width, rect.height - 26f);
		List<ThingDef> list = EESTurretBlacklist.ModTurrets();
		int num = list.Select((ThingDef t) => t.modContentPack).Distinct().Count();
		float a = 30f + (float)num * 14f + (float)list.Count * 42f + 12f;
		Rect rect2 = new Rect(0f, 0f, outRect.width - 20f, Mathf.Max(a, outRect.height));
		Widgets.BeginScrollView(outRect, ref modTurretScrollPos, rect2);
		Listing_Standard listing_Standard = new Listing_Standard();
		listing_Standard.Begin(rect2);
		listing_Standard.Label(L("EES.Set.TurretBL.ModDesc"));
		if (list.Count == 0)
		{
			listing_Standard.Label(L("EES.Set.TurretBL.None"));
		}
		ModContentPack modContentPack = null;
		bool flag = true;
		foreach (ThingDef item in list)
		{
			if (item.modContentPack != modContentPack)
			{
				modContentPack = item.modContentPack;
				if (!flag)
				{
					listing_Standard.GapLine(6f);
				}
				flag = false;
			}
			string raceLabel = StrongholdGarrisonUtility.TurretEffectiveTechLevel(item).ToStringHuman().CapitalizeFirst();
			TurretRow(listing_Standard, s, item, raceLabel);
		}
		listing_Standard.End();
		Widgets.EndScrollView();
	}

	private void TurretRow(Listing_Standard l, StrongholdSettings s, ThingDef turret, string raceLabel)
	{
		Rect rect = l.GetRect(40f);
		if (Mouse.IsOver(rect))
		{
			Widgets.DrawHighlight(rect);
		}
		float y = rect.y + 8f;
		bool flag = s.blacklistedTurretDefs.Contains(turret.defName);
		bool checkOn = flag;
		Rect rect2 = new Rect(rect.xMax - 28f, y, 24f, 24f);
		Widgets.Checkbox(rect2.x, rect2.y, ref checkOn);
		Rect position = new Rect(rect.x + 6f, y, 24f, 24f);
		if (turret.uiIcon != null)
		{
			Color color = GUI.color;
			GUI.color = turret.uiIconColor;
			GUI.DrawTexture(position, turret.uiIcon);
			GUI.color = color;
		}
		string text = (turret.LabelCap.NullOrEmpty() ? turret.defName : turret.LabelCap.ToString());
		Vector2 vector = Text.CalcSize(text);
		float num = position.xMax + 10f;
		Widgets.Label(new Rect(num, rect.y + 8f, vector.x + 6f, 26f), text);
		num += vector.x + 14f;
		if (!raceLabel.NullOrEmpty())
		{
			Vector2 vector2 = Text.CalcSize(raceLabel);
			if (num + vector2.x < rect2.x - 8f)
			{
				Color color2 = GUI.color;
				GUI.color = new Color(0.72f, 0.78f, 0.88f);
				Widgets.Label(new Rect(num, rect.y + 9f, vector2.x + 6f, 24f), raceLabel);
				GUI.color = color2;
				num += vector2.x + 14f;
			}
		}
		string text2 = ((turret.modContentPack != null) ? turret.modContentPack.Name : "");
		if (!text2.NullOrEmpty())
		{
			float num2 = rect2.x - 8f - num;
			if (num2 > 30f)
			{
				Color color3 = GUI.color;
				GUI.color = new Color(0.6f, 0.6f, 0.62f);
				Widgets.Label(new Rect(num, rect.y + 9f, num2, 24f), text2);
				GUI.color = color3;
			}
		}
		if (Widgets.ButtonInvisible(new Rect(rect.x, rect.y, rect2.x - rect.x - 4f, 40f)))
		{
			checkOn = !checkOn;
		}
		if (checkOn != flag)
		{
			if (checkOn)
			{
				s.blacklistedTurretDefs.Add(turret.defName);
			}
			else
			{
				s.blacklistedTurretDefs.Remove(turret.defName);
			}
		}
	}

	private void DrawValuesTab(Rect inRect, StrongholdSettings s)
	{
		Rect rect = new Rect(0f, 0f, inRect.width - 20f, viewHeight);
		Widgets.BeginScrollView(inRect, ref scrollPos, rect);
		Listing_Standard listing_Standard = new Listing_Standard();
		listing_Standard.Begin(rect);
		if (listing_Standard.RadioButton(L("EES.Set.ScoutNotify.Letter"), s.scoutLetterNotify, 0f, L("EES.Set.ScoutNotify.Letter.Tip")))
		{
			s.scoutLetterNotify = true;
		}
		if (listing_Standard.RadioButton(L("EES.Set.ScoutNotify.Msg"), !s.scoutLetterNotify, 0f, L("EES.Set.ScoutNotify.Msg.Tip")))
		{
			s.scoutLetterNotify = false;
		}
		listing_Standard.Label(L("EES.Set.Hdr.Basic"));
		IntRow(listing_Standard, L("EES.Set.MaxTier"), ref s.maxTier, 3f, 13f, 13, "T");
		FloatRow(listing_Standard, L("EES.Set.Growth"), ref s.growthRate, 0.25f, 2f, 1f, "x");
		FloatRow(listing_Standard, L("EES.Set.LootValue"), ref s.lootMultiplier, 0.5f, 2f, 1f, "x");
		IntRow(listing_Standard, L("EES.Set.LegendaryTier"), ref s.legendaryGearTier, 6f, 13f, 12, "T");
		listing_Standard.GapLine();
		listing_Standard.Label(L("EES.Set.Hdr.Combat"));
		FloatRow(listing_Standard, L("EES.Set.MilMul"), ref s.militaryPowerMul, 0.25f, 2f, 1f, "x");
		IntRow(listing_Standard, L("EES.Set.MilPerTier"), ref s.milPerTier, 200f, 1000f, 350);
		FloatRow(listing_Standard, L("EES.Set.AlertMil") + " (/" + L("EES.Set.PerAlert") + ")", ref s.alertMilScale, 0f, 0.5f, 0.1f, "", "0%");
		listing_Standard.GapLine();
		listing_Standard.Label(L("EES.Set.Hdr.Turret"));
		FloatRow(listing_Standard, L("EES.Set.TurretDensity"), ref s.turretDensity, 0f, 2f, 1f, "x");
		FloatRow(listing_Standard, L("EES.Set.Facility"), ref s.facilityChance, 0f, 1f, 0.35f, "", "0%");
		listing_Standard.GapLine();
		listing_Standard.Label(L("EES.Set.Hdr.Scout"));
		FloatRow(listing_Standard, L("EES.Set.ScoutSuccess"), ref s.scoutSuccessMul, 0.5f, 4f, 1f, "x");
		FloatRow(listing_Standard, L("EES.Set.InfiltrateSuccess"), ref s.infiltrateSuccessMul, 0.5f, 4f, 1f, "x");
		FloatRow(listing_Standard, L("EES.Set.Sabotage"), ref s.sabotageMul, 0.5f, 2f, 1f, "x");
		FloatRow(listing_Standard, L("EES.Set.InterceptCd"), ref s.interceptCooldownDays, 0f, 7f, 2f, L("EES.Set.Days"), "0.0");
		FloatRow(listing_Standard, L("EES.Set.InterceptMech"), ref s.interceptMechChance, 0f, 0.5f, 0.1f, "", "0%");
		listing_Standard.GapLine();
		listing_Standard.Label(L("EES.Set.Hdr.Ambush"));
		FloatRow(listing_Standard, L("EES.Set.AmbushSize"), ref s.ambushPointsFraction, 0f, 1f, 0.25f, "", "0%");
		IntRow(listing_Standard, L("EES.Set.AmbushMax"), ref s.ambushMaxPoints, 500f, 8000f, 3500);
		FloatRow(listing_Standard, L("EES.Set.AmbushAlert") + " (/" + L("EES.Set.PerAlert") + ")", ref s.ambushAlertScale, 0f, 0.5f, 0.2f, "", "0%");
		listing_Standard.GapLine();
		listing_Standard.Label(L("EES.Set.Hdr.Reinforce"));
		FloatRow(listing_Standard, L("EES.Set.ReinInterval") + " (min)", ref s.reinforceMinHours, 1f, 48f, 8f, L("EES.Set.Hours"), "0");
		FloatRow(listing_Standard, L("EES.Set.ReinInterval") + " (max)", ref s.reinforceMaxHours, 4f, 96f, 48f, L("EES.Set.Hours"), "0");
		if (s.reinforceMaxHours < s.reinforceMinHours + 1f)
		{
			s.reinforceMaxHours = s.reinforceMinHours + 1f;
		}
		FloatRow(listing_Standard, L("EES.Set.ReinSize"), ref s.reinforceSizeFraction, 0.05f, 1f, 0.25f, "", "0%");
		IntRow(listing_Standard, L("EES.Set.ReinSize") + " (max)", ref s.reinforceMaxPoints, 500f, 6000f, 2000);
		listing_Standard.GapLine();
		listing_Standard.Label(L("EES.Set.Hdr.Supply"));
		FloatRow(listing_Standard, L("EES.Set.SupplyDays"), ref s.supplySpawnDays, 2f, 60f, 8f, L("EES.Set.Days"), "0");
		IntRow(listing_Standard, L("EES.Set.SupplyMax"), ref s.supplyMaxActive, 1f, 4f, 3);
		listing_Standard.CheckboxLabeled(L("EES.Set.SupplyLetter"), ref s.supplySpawnLetter, L("EES.Set.SupplyLetter.Tip"));
		listing_Standard.GapLine();
		listing_Standard.Label(L("EES.Set.Hdr.Emitters"));
		listing_Standard.CheckboxLabeled(L("EES.Set.ThreatEmitters"), ref s.enableThreatEmitters, L("EES.Set.ThreatEmitters.Tip"));
		FloatRow(listing_Standard, L("EES.Set.ThreatStartDay"), ref s.threatEmitterStartDay, 0f, 1200f, 600f, L("EES.Set.Days"), "0");
		FloatRow(listing_Standard, L("EES.Set.EmitterSpawnDays"), ref s.threatEmitterSpawnDays, 10f, 60f, 45f, L("EES.Set.Days"), "0");
		FloatRow(listing_Standard, L("EES.Set.ArtyVolleyDays"), ref s.artilleryVolleyDays, 1f, 10f, 1f, L("EES.Set.Days"), "0");
		listing_Standard.GapLine();
		listing_Standard.Label(L("EES.Set.Hdr.Rebuild"));
		listing_Standard.CheckboxLabeled(L("EES.Set.Rebuild"), ref s.enableRebuildConvoys, L("EES.Set.Rebuild.Tip"));
		FloatRow(listing_Standard, L("EES.Set.RebuildDelay"), ref s.rebuildDelayDays, 5f, 30f, 12f, L("EES.Set.Days"), "0");
		listing_Standard.CheckboxLabeled(L("EES.Set.RebuildWD"), ref s.rebuildWithWD, L("EES.Set.RebuildWD.Tip"));
		listing_Standard.GapLine();
		listing_Standard.Label(L("EES.Set.Hdr.CScout"));
		FloatRow(listing_Standard, L("EES.Set.CScoutDays"), ref s.counterScoutDays, 4f, 60f, 30f, L("EES.Set.Days"), "0");
		listing_Standard.GapLine();
		listing_Standard.Label(L("EES.Set.Hdr.HVT"));
		FloatRow(listing_Standard, L("EES.Set.HVTDays"), ref s.hvtOfferDays, 5f, 60f, 15f, L("EES.Set.Days"), "0");
		listing_Standard.GapLine();
		listing_Standard.Label(L("EES.Set.Hdr.Toggles"));
		listing_Standard.CheckboxLabeled(L("EES.Set.Scouting"), ref s.enableScouting, L("EES.Set.Scouting.Tip"));
		listing_Standard.CheckboxLabeled(L("EES.Set.PreventDeath"), ref s.preventScoutDeath, L("EES.Set.PreventDeath.Tip"));
		listing_Standard.CheckboxLabeled(L("EES.Set.ScoutUnlockHigh"), ref s.scoutUnlockHighTier, L("EES.Set.ScoutUnlockHigh.Tip"));
		listing_Standard.CheckboxLabeled(L("EES.Set.Reinforcements"), ref s.enableReinforcements, L("EES.Set.Reinforcements.Tip"));
		listing_Standard.CheckboxLabeled(L("EES.Set.Commanders"), ref s.enableCommanders, L("EES.Set.Commanders.Tip"));
		listing_Standard.CheckboxLabeled(L("EES.Set.AlertSystem"), ref s.enableAlertSystem, L("EES.Set.AlertSystem.Tip"));
		listing_Standard.CheckboxLabeled(L("EES.Set.AllowMortars"), ref s.allowMortars, L("EES.Set.AllowMortars.Tip"));
		listing_Standard.CheckboxLabeled(L("EES.Set.DisableBattlefield"), ref s.disableBattlefield, L("EES.Set.DisableBattlefield.Tip"));
		listing_Standard.CheckboxLabeled(L("EES.Set.Supply"), ref s.enableSupplyCaravans, L("EES.Set.Supply.Tip"));
		listing_Standard.CheckboxLabeled(L("EES.Set.CScout"), ref s.enableCounterScouts, L("EES.Set.CScout.Tip"));
		listing_Standard.CheckboxLabeled(L("EES.Set.HVT"), ref s.enableHVTQuests, L("EES.Set.HVT.Tip"));
		listing_Standard.CheckboxLabeled(L("EES.Set.ResearchScale"), ref s.usePlayerResearchScaling, L("EES.Set.ResearchScale.Tip"));
		listing_Standard.CheckboxLabeled(L("EES.Set.WealthScale"), ref s.usePlayerWealthScaling, L("EES.Set.WealthScale.Tip"));
		listing_Standard.CheckboxLabeled(L("EES.Set.WDTierSync"), ref s.worldDominationTierSync, L("EES.Set.WDTierSync.Tip"));
		listing_Standard.CheckboxLabeled(L("EES.Set.CEIntegration"), ref s.enableCEIntegration, L("EES.Set.CEIntegration.Tip"));
		listing_Standard.CheckboxLabeled(L("EES.Set.TierAmmo"), ref s.enableTierAmmo, L("EES.Set.TierAmmo.Tip"));
		listing_Standard.GapLine();
		listing_Standard.CheckboxLabeled(L("EES.Set.DebugLog"), ref s.debugLogging, L("EES.Set.DebugLog.Tip"));
		viewHeight = listing_Standard.CurHeight + 12f;
		listing_Standard.End();
		Widgets.EndScrollView();
	}
}
