using System.Collections.Generic;
using Verse;

namespace EvolvingEnemyStrongholds;

public class StrongholdData : IExposable
{
	public int settlementId = -1;

	public int tier = 1;

	public string roleDefName;

	public string featureKey;

	public int featureVersion;

	// Persisted center/town layout variant. A value below zero means an old save
	// has not been assigned a structural variant yet.
	public int structureVariant = -1;

	public bool featureForced;

	public int militaryPower;

	public int fortification;

	public int supply;

	public int alertLevel;

	public IntVec3 commandRoomCenter = IntVec3.Invalid;

	public StrongholdGenerationPlan generationPlan;

	public int intelLevel;

	public int sabotagedTurrets;

	public int sabotagedMortars;

	public bool infiltrated;

	public float vaultValue;

	public bool vaultValueAnnounced;

	public List<CellRect> sealedRects = new List<CellRect>();

	public List<string> sealedRectKinds = new List<string>();

	public int lastGrowthTick = -1;

	public int lastScoutedTick = -1;

	public int lastInfiltrateTick = -1;

	public int lastInterceptTick = -1;

	public int lastStrikeTick = -1;

	public int lastAttackedTick = -1;

	public int lastAlertRaisedTick = -1;

	public bool hasCommander;

	public bool hasAmmoDepot;

	public bool hasMortarBattery;

	public bool hasPrison;

	public bool hasCommsRoom;

	public string commanderKindDefName;

	public CommanderType commanderType;

	public bool commanderForced;

	public int tierClockTick = -1;

	public int lastCap = -1;

	public bool roleForced;

	public bool facilitiesForced;

	public int warTierPenalty;

	public int warTierRecoverTick = -1;

	public List<Pawn> capturedPrisoners = new List<Pawn>();

	public bool customLayout;

	public bool isOverlayBase;

	public int maxTurretFootprint;

	public string tierSource = "auto";

	public int lastFactionLoadId = -1;

	public bool groupPlanDecided;

	public bool groupPlanForced;

	public bool hasElite;

	public int eliteCount;

	public bool hasZoneGuard;

	public int zoneGuardDir = -1;

	public int pursuerGroups;

	public bool hasHerald;

	public int disruptorGroups;

	public bool hasAssault;

	public int forcedAssaultMode = -1;

	public int supplyCutUntilTick;

	public int emitterWarnedTick = -1;

	public bool emitterStartNotified;

	public int artilleryNextVolleyTick = -1;

	public bool artillerySilenced;

	public bool artillerySawMortars;

	public int emitterSpawnFactionLoadId = -1;

	public string emitterSpawnFactionName;

	public bool rebuiltOnce;

	public bool finalStandActive;

	public bool finalStandApplied;

	public int finalStandClusterId = -1;

	public int finalStandStartedTick = -1;
	public int finalStandBudgetVersion;
	public int finalStandReservePeople;
	public int finalStandReserveElites;
	public float finalStandReservePoints;
	public List<string> finalStandReserveKindDefs = new List<string>();

	public int ContentTier => StrongholdScoreUtility.ContentTier(tier);

	public void RegisterSealedRect(CellRect r, string kind)
	{
		if (r.Width <= 0 || r.Height <= 0)
		{
			return;
		}
		if (sealedRects == null)
		{
			sealedRects = new List<CellRect>();
		}
		if (sealedRectKinds == null)
		{
			sealedRectKinds = new List<string>();
		}
		for (int i = 0; i < sealedRects.Count; i++)
		{
			if (sealedRects[i] == r)
			{
				return;
			}
		}
		sealedRects.Add(r);
		sealedRectKinds.Add(kind ?? "봉인방");
	}

	public void ClearSealedRects()
	{
		sealedRects?.Clear();
		sealedRectKinds?.Clear();
	}

	public void ClearSealedRectsOfKind(params string[] kinds)
	{
		if (sealedRects == null || sealedRectKinds == null || kinds == null)
		{
			return;
		}
		for (int num = sealedRects.Count - 1; num >= 0; num--)
		{
			string text = ((num < sealedRectKinds.Count) ? sealedRectKinds[num] : null);
			for (int i = 0; i < kinds.Length; i++)
			{
				if (text == kinds[i])
				{
					sealedRects.RemoveAt(num);
					if (num < sealedRectKinds.Count)
					{
						sealedRectKinds.RemoveAt(num);
					}
					break;
				}
			}
		}
	}

	public StrongholdData()
	{
	}

	public StrongholdData(int settlementId)
	{
		this.settlementId = settlementId;
	}

	public void ExposeData()
	{
		Scribe_Values.Look(ref settlementId, "settlementId", -1);
		Scribe_Values.Look(ref tier, "tier", 1);
		Scribe_Values.Look(ref roleDefName, "roleDefName");
		Scribe_Values.Look(ref featureKey, "featureKey");
		Scribe_Values.Look(ref featureVersion, "featureVersion", 0);
		Scribe_Values.Look(ref structureVariant, "structureVariant", -1);
		Scribe_Values.Look(ref featureForced, "featureForced", defaultValue: false);
		Scribe_Values.Look(ref militaryPower, "militaryPower", 0);
		Scribe_Values.Look(ref fortification, "fortification", 0);
		Scribe_Values.Look(ref supply, "supply", 0);
		Scribe_Values.Look(ref alertLevel, "alertLevel", 0);
		Scribe_Values.Look(ref intelLevel, "intelLevel", 0);
		Scribe_Values.Look(ref sabotagedTurrets, "sabotagedTurrets", 0);
		Scribe_Values.Look(ref sabotagedMortars, "sabotagedMortars", 0);
		Scribe_Values.Look(ref infiltrated, "infiltrated", defaultValue: false);
		Scribe_Values.Look(ref vaultValue, "vaultValue", 0f);
		Scribe_Values.Look(ref vaultValueAnnounced, "vaultValueAnnounced", defaultValue: false);
		Scribe_Collections.Look(ref sealedRects, "sealedRects", LookMode.Value);
		Scribe_Collections.Look(ref sealedRectKinds, "sealedRectKinds", LookMode.Value);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (sealedRects == null)
			{
				sealedRects = new List<CellRect>();
			}
			if (sealedRectKinds == null)
			{
				sealedRectKinds = new List<string>();
			}
			while (sealedRectKinds.Count < sealedRects.Count)
			{
				sealedRectKinds.Add("봉인방");
			}
			while (sealedRectKinds.Count > sealedRects.Count)
			{
				sealedRectKinds.RemoveAt(sealedRectKinds.Count - 1);
			}
		}
		Scribe_Values.Look(ref lastGrowthTick, "lastGrowthTick", -1);
		Scribe_Values.Look(ref lastScoutedTick, "lastScoutedTick", -1);
		Scribe_Values.Look(ref lastInfiltrateTick, "lastInfiltrateTick", -1);
		Scribe_Values.Look(ref lastInterceptTick, "lastInterceptTick", -1);
		Scribe_Values.Look(ref lastStrikeTick, "lastStrikeTick", -1);
		Scribe_Values.Look(ref lastAttackedTick, "lastAttackedTick", -1);
		Scribe_Values.Look(ref lastAlertRaisedTick, "lastAlertRaisedTick", -1);
		Scribe_Values.Look(ref hasCommander, "hasCommander", defaultValue: false);
		Scribe_Values.Look(ref hasAmmoDepot, "hasAmmoDepot", defaultValue: false);
		Scribe_Values.Look(ref hasMortarBattery, "hasMortarBattery", defaultValue: false);
		Scribe_Values.Look(ref hasPrison, "hasPrison", defaultValue: false);
		Scribe_Values.Look(ref hasCommsRoom, "hasCommsRoom", defaultValue: false);
		Scribe_Values.Look(ref commanderKindDefName, "commanderKindDefName");
		Scribe_Values.Look(ref commanderType, "commanderType", CommanderType.None);
		Scribe_Values.Look(ref commanderForced, "commanderForced", defaultValue: false);
		Scribe_Values.Look(ref tierClockTick, "tierClockTick", -1);
		Scribe_Values.Look(ref lastCap, "lastCap", -1);
		Scribe_Values.Look(ref roleForced, "roleForced", defaultValue: false);
		Scribe_Values.Look(ref facilitiesForced, "facilitiesForced", defaultValue: false);
		Scribe_Values.Look(ref warTierPenalty, "warTierPenalty", 0);
		Scribe_Values.Look(ref warTierRecoverTick, "warTierRecoverTick", -1);
		Scribe_Collections.Look(ref capturedPrisoners, "capturedPrisoners", LookMode.Reference);
		Scribe_Values.Look(ref customLayout, "customLayout", defaultValue: false);
		Scribe_Values.Look(ref isOverlayBase, "isOverlayBase", defaultValue: false);
		Scribe_Values.Look(ref tierSource, "tierSource", "auto");
		Scribe_Values.Look(ref lastFactionLoadId, "lastFactionLoadId", -1);
		Scribe_Values.Look(ref groupPlanDecided, "groupPlanDecided", defaultValue: false);
		Scribe_Values.Look(ref groupPlanForced, "groupPlanForced", defaultValue: false);
		Scribe_Values.Look(ref hasElite, "hasElite", defaultValue: false);
		Scribe_Values.Look(ref eliteCount, "eliteCount", 0);
		Scribe_Values.Look(ref hasZoneGuard, "hasZoneGuard", defaultValue: false);
		Scribe_Values.Look(ref zoneGuardDir, "zoneGuardDir", -1);
		Scribe_Values.Look(ref pursuerGroups, "pursuerGroups", 0);
		Scribe_Values.Look(ref hasHerald, "hasHerald", defaultValue: false);
		Scribe_Values.Look(ref disruptorGroups, "disruptorGroups", 0);
		Scribe_Values.Look(ref hasAssault, "hasAssault", defaultValue: false);
		Scribe_Values.Look(ref forcedAssaultMode, "forcedAssaultMode", -1);
		Scribe_Values.Look(ref supplyCutUntilTick, "supplyCutUntilTick", 0);
		Scribe_Values.Look(ref emitterWarnedTick, "emitterWarnedTick", -1);
		Scribe_Values.Look(ref emitterStartNotified, "emitterStartNotified", defaultValue: false);
		Scribe_Values.Look(ref artilleryNextVolleyTick, "artilleryNextVolleyTick", -1);
		Scribe_Values.Look(ref artillerySilenced, "artillerySilenced", defaultValue: false);
		Scribe_Values.Look(ref artillerySawMortars, "artillerySawMortars", defaultValue: false);
		Scribe_Values.Look(ref emitterSpawnFactionLoadId, "emitterSpawnFactionLoadId", -1);
		Scribe_Values.Look(ref emitterSpawnFactionName, "emitterSpawnFactionName");
		Scribe_Values.Look(ref rebuiltOnce, "rebuiltOnce", defaultValue: false);
		Scribe_Values.Look(ref finalStandActive, "finalStandActive", defaultValue: false);
		Scribe_Values.Look(ref finalStandApplied, "finalStandApplied", defaultValue: false);
		Scribe_Values.Look(ref finalStandClusterId, "finalStandClusterId", -1);
		Scribe_Values.Look(ref finalStandStartedTick, "finalStandStartedTick", -1);
		Scribe_Values.Look(ref finalStandBudgetVersion, "finalStandBudgetVersion", 0);
		Scribe_Values.Look(ref finalStandReservePeople, "finalStandReservePeople", 0);
		Scribe_Values.Look(ref finalStandReserveElites, "finalStandReserveElites", 0);
		Scribe_Values.Look(ref finalStandReservePoints, "finalStandReservePoints", 0f);
		Scribe_Collections.Look(ref finalStandReserveKindDefs, "finalStandReserveKindDefs", LookMode.Value);
		if (Scribe.mode == LoadSaveMode.PostLoadInit) finalStandReserveKindDefs ??= new List<string>();
		Scribe_Values.Look(ref commandRoomCenter, "ees_commandRoomCenter", IntVec3.Invalid);
		Scribe_Deep.Look(ref generationPlan, "ees_generationPlan");
		if (Scribe.mode == LoadSaveMode.PostLoadInit && capturedPrisoners == null)
		{
			capturedPrisoners = new List<Pawn>();
		}
	}

	public override string ToString()
	{
		return string.Format("Stronghold(id={0}, T{1}[{2}], role={3}, feature={4}, ", settlementId, tier, tierSource, roleDefName ?? "none", featureKey ?? "none") + $"mil={militaryPower}, fort={fortification}, alert={alertLevel}, intel={intelLevel}" + ((warTierPenalty > 0) ? $", 전쟁페널티=-{warTierPenalty}" : "") + ")";
	}
}
