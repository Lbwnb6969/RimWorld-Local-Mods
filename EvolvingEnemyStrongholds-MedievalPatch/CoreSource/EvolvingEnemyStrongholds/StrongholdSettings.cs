using System.Collections.Generic;
using Verse;

namespace EvolvingEnemyStrongholds;

public class StrongholdSettings : ModSettings
{
	public int maxTier = 13;

	public float growthRate = 1f;

	public bool usePlayerWealthScaling = true;

	public bool usePlayerResearchScaling = true;

	public bool enableCEIntegration = true;

	public bool enableScouting = true;

	public bool enableReinforcements = true;

	public bool enableCommanders = true;

	public bool enableAlertSystem = true;

	public bool disableBattlefield;

	public int alertOnAttack = 2;

	public int alertSpreadToFaction = 1;

	public float alertDecayDays = 2f;

	public float lootMultiplier = 1f;

	public float turretDensity = 1f;

	public bool allowMortars = true;

	public bool preventScoutDeath;

	public bool scoutUnlockHighTier;

	public float scoutSuccessMul = 1f;

	public float infiltrateSuccessMul = 1f;

	public float ambushPointsFraction = 0.25f;

	public int ambushMaxPoints = 3500;

	public float ambushAlertScale = 0.2f;

	public float sabotageMul = 1f;

	public float interceptCooldownDays = 2f;

	public float interceptMechChance = 0.1f;

	public bool enableTierAmmo = true;

	public int legendaryGearTier = 12;

	public float militaryPowerMul = 1f;

	public int milPerTier = 350;

	public float alertMilScale = 0.1f;

	public float facilityChance = 0.35f;

	public float reinforceMinHours = 8f;

	public float reinforceMaxHours = 48f;

	public float reinforceSizeFraction = 0.25f;

	public int reinforceMaxPoints = 2000;

	public bool enableSupplyCaravans = true;

	public float supplySpawnDays = 8f;

	public int supplyMaxActive = 3;

	public bool supplySpawnLetter = true;

	public bool scoutLetterNotify = true;

	public bool enableCounterScouts = true;

	public bool enableThreatEmitters;

	public float threatEmitterStartDay = 600f;

	public float threatEmitterSpawnDays = 45f;

	public float artilleryVolleyDays = 1f;

	public bool enableRebuildConvoys = true;

	public float rebuildDelayDays = 12f;

	public bool rebuildWithWD;

	public float counterScoutDays = 30f;

	public bool enableHVTQuests = true;

	public float hvtOfferDays = 15f;

	public bool worldDominationTierSync = true;

	public bool debugLogging;

	public HashSet<string> blacklistedFactionDefs = new HashSet<string>();

	public HashSet<string> blacklistedTurretDefs = new HashSet<string>();

	private int schemaVersion;

	public override void ExposeData()
	{
		Scribe_Values.Look(ref maxTier, "maxTier", 13);
		Scribe_Values.Look(ref schemaVersion, "schemaVersion", 0);
		if (Scribe.mode == LoadSaveMode.LoadingVars && schemaVersion < 1)
		{
			if (maxTier <= 7)
			{
				maxTier = 13;
			}
			schemaVersion = 1;
		}
		Scribe_Values.Look(ref growthRate, "growthRate", 1f);
		Scribe_Values.Look(ref usePlayerWealthScaling, "usePlayerWealthScaling", defaultValue: true);
		Scribe_Values.Look(ref usePlayerResearchScaling, "usePlayerResearchScaling", defaultValue: true);
		Scribe_Values.Look(ref enableCEIntegration, "enableCEIntegration", defaultValue: true);
		Scribe_Values.Look(ref enableScouting, "enableScouting", defaultValue: true);
		Scribe_Values.Look(ref enableReinforcements, "enableReinforcements", defaultValue: true);
		Scribe_Values.Look(ref enableCommanders, "enableCommanders", defaultValue: true);
		Scribe_Values.Look(ref enableAlertSystem, "enableAlertSystem", defaultValue: true);
		Scribe_Values.Look(ref disableBattlefield, "disableBattlefield", defaultValue: false);
		Scribe_Values.Look(ref alertOnAttack, "alertOnAttack", 2);
		Scribe_Values.Look(ref alertSpreadToFaction, "alertSpreadToFaction", 1);
		Scribe_Values.Look(ref alertDecayDays, "alertDecayDays", 2f);
		Scribe_Values.Look(ref lootMultiplier, "lootMultiplier", 1f);
		Scribe_Values.Look(ref turretDensity, "turretDensity", 1f);
		Scribe_Values.Look(ref allowMortars, "allowMortars", defaultValue: true);
		Scribe_Values.Look(ref preventScoutDeath, "preventScoutDeath", defaultValue: false);
		Scribe_Values.Look(ref scoutUnlockHighTier, "scoutUnlockHighTier", defaultValue: false);
		Scribe_Values.Look(ref scoutSuccessMul, "scoutSuccessMul", 1f);
		Scribe_Values.Look(ref infiltrateSuccessMul, "infiltrateSuccessMul", 1f);
		Scribe_Values.Look(ref ambushPointsFraction, "ambushPointsFraction", 0.25f);
		Scribe_Values.Look(ref ambushMaxPoints, "ambushMaxPoints", 3500);
		Scribe_Values.Look(ref ambushAlertScale, "ambushAlertScale", 0.2f);
		Scribe_Values.Look(ref sabotageMul, "sabotageMul", 1f);
		Scribe_Values.Look(ref interceptCooldownDays, "interceptCooldownDays", 2f);
		Scribe_Values.Look(ref interceptMechChance, "interceptMechChance", 0.1f);
		Scribe_Values.Look(ref enableTierAmmo, "enableTierAmmo", defaultValue: true);
		Scribe_Values.Look(ref legendaryGearTier, "legendaryGearTier", 12);
		Scribe_Values.Look(ref militaryPowerMul, "militaryPowerMul", 1f);
		Scribe_Values.Look(ref milPerTier, "milPerTier", 350);
		Scribe_Values.Look(ref alertMilScale, "alertMilScale", 0.1f);
		Scribe_Values.Look(ref facilityChance, "facilityChance", 0.35f);
		Scribe_Values.Look(ref reinforceMinHours, "reinforceMinHours", 8f);
		Scribe_Values.Look(ref reinforceMaxHours, "reinforceMaxHours", 48f);
		Scribe_Values.Look(ref reinforceSizeFraction, "reinforceSizeFraction", 0.25f);
		Scribe_Values.Look(ref reinforceMaxPoints, "reinforceMaxPoints", 2000);
		Scribe_Values.Look(ref enableThreatEmitters, "enableThreatEmitters", defaultValue: false);
		Scribe_Values.Look(ref threatEmitterStartDay, "threatEmitterStartDay", 600f);
		Scribe_Values.Look(ref threatEmitterSpawnDays, "threatEmitterSpawnDays", 45f);
		Scribe_Values.Look(ref artilleryVolleyDays, "artilleryVolleyDays", 1f);
		Scribe_Values.Look(ref enableRebuildConvoys, "enableRebuildConvoys", defaultValue: true);
		Scribe_Values.Look(ref rebuildDelayDays, "rebuildDelayDays", 12f);
		Scribe_Values.Look(ref rebuildWithWD, "rebuildWithWD", defaultValue: false);
		Scribe_Values.Look(ref enableSupplyCaravans, "enableSupplyCaravans", defaultValue: true);
		Scribe_Values.Look(ref supplySpawnDays, "supplySpawnDays", 8f);
		Scribe_Values.Look(ref supplyMaxActive, "supplyMaxActive", 3);
		if (Scribe.mode == LoadSaveMode.LoadingVars && schemaVersion < 2)
		{
			if (supplyMaxActive == 1)
			{
				supplyMaxActive = 3;
			}
			schemaVersion = 2;
		}
		Scribe_Values.Look(ref supplySpawnLetter, "supplySpawnLetter", defaultValue: true);
		Scribe_Values.Look(ref scoutLetterNotify, "scoutLetterNotify", defaultValue: true);
		Scribe_Values.Look(ref enableCounterScouts, "enableCounterScouts", defaultValue: true);
		Scribe_Values.Look(ref counterScoutDays, "counterScoutDays", 30f);
		Scribe_Values.Look(ref enableHVTQuests, "enableHVTQuests", defaultValue: true);
		Scribe_Values.Look(ref hvtOfferDays, "hvtOfferDays", 15f);
		Scribe_Values.Look(ref worldDominationTierSync, "worldDominationTierSync", defaultValue: true);
		Scribe_Values.Look(ref debugLogging, "debugLogging", defaultValue: false);
		Scribe_Collections.Look(ref blacklistedFactionDefs, "blacklistedFactionDefs", LookMode.Value);
		if (blacklistedFactionDefs == null)
		{
			blacklistedFactionDefs = new HashSet<string>();
		}
		Scribe_Collections.Look(ref blacklistedTurretDefs, "blacklistedTurretDefs", LookMode.Value);
		if (blacklistedTurretDefs == null)
		{
			blacklistedTurretDefs = new HashSet<string>();
		}
	}
}
