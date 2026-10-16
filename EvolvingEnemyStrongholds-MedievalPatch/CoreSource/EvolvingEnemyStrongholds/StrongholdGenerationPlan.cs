using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public sealed class StrongholdGenerationPlan : IExposable
{
    public int schemaVersion = 1;
    public int seed;
    public int tier;
    public int factionLoadId = -1;
    public int mapId = -1;
    public string roleDefName;
    public string centerKind;
    public string stage = "planned";
    public int layoutVariant;
    public CellRect baseRect = CellRect.Empty;
    public CellRect centerRect = CellRect.Empty;
    public CellRect vaultRect = CellRect.Empty;
    public IntVec3 commandCell = IntVec3.Invalid;
    public bool legacyMap;
    public bool lootFinalized;
    // One persisted ledger covers native recoverables, furniture, and all generated vault additions.
    public int rewardBudgetVersion = 1;
    // Persisted source tags make the single ledger auditable by generation entry point.
    // Final Stand takes precedence over overlay because it is a stronger campaign mode.
    public string rewardEntryPoint = "standard";
    public string rewardRoleKey;
    public bool rewardAdapterFinalizeObserved;
    public float rewardBudgetTarget;
    public float rewardBudgetProtected;
    public float rewardBudgetFurniture;
    public float rewardBudgetGenerated;
    public bool rewardBudgetFinalized;
    public List<int> rewardProtectedThingIds = new List<int>();
    public bool centerReserved;
    public bool centerBuilt;
    public string placementNote;
    public List<int> nativeLootIds = new List<int>();
    public string centerStyle = "courtyardKeep";
    public string nativeTemplate;
    public bool nativeCenterReused;
    public CellRect nativeBounds = CellRect.Empty;
    public bool townBuilt;
    public List<CellRect> townHouseRects = new List<CellRect>();
    public int townRoadCount;
    // New recruits receive their feature loadout once, including after a save/reload.
    public List<int> featureAppliedPawnIds = new List<int>();
    public float featureGrantedValue;
    public float featureGrantBudget = -1f;
    public float rewardBudgetEquipment;
    public float rewardBudgetOverflow;
    public List<int> nativeDefenseIds = new List<int>();
    public List<int> featureAppliedDefenseIds = new List<int>();
    // Exact new EES construction provenance. Old saves default to no claim;
    // a turret outside the town is not inferred to belong to the stronghold.
    public List<int> generatedTurretIds = new List<int>();
    // New generation only: exact two-thick CWE contours, independent of the
    // rectangular town/housing envelope. Existing saves default to no contour.
    public int castleContourVersion;
    public CellRect castleContourTown = CellRect.Empty;
    public CellRect castleContourNominal = CellRect.Empty;
    public List<IntVec3> castleInnerPads = new List<IntVec3>();
    public List<IntVec3> castleOuterPads = new List<IntVec3>();
    public List<int> castleGateCenters = new List<int>();

    public static StrongholdGenerationPlan GetOrCreate(Map map, StrongholdData data)
    {
        if (map?.Parent is not Settlement settlement || data == null || settlement.Faction?.def?.humanlikeFaction != true) return null;
        if (data.generationPlan != null)
        {
            data.generationPlan.RefreshRewardSource(data);
            return data.generationPlan;
        }
        var def = settlement.Faction.def;
        var open = def.techLevel == TechLevel.Neolithic || def.categoryTag == "Tribal" || def.defName.StartsWith("VFEM2_Clan") || def.defName == "VFEM2_CivilClan";
        var seed = Gen.HashCombineInt(data.settlementId, 152971);
        int layoutVariant = data.structureVariant >= 0 && data.structureVariant < 3
            ? data.structureVariant
            : (seed & int.MaxValue) % 3;
        if (data.structureVariant < 0)
            data.structureVariant = layoutVariant;
        data.generationPlan = new StrongholdGenerationPlan
        {
            seed = seed,
            tier = data.tier,
            factionLoadId = settlement.Faction.loadID,
            mapId = map.uniqueID,
            roleDefName = data.roleDefName,
            centerKind = open ? "clanHall" : "castle",
            centerStyle = open ? (layoutVariant == 1 ? "councilHallAlternative" : "leaderHall") :
                StrongholdCultureCenters.StyleFor(settlement.Faction, data.roleDefName, layoutVariant),
            layoutVariant = layoutVariant
        };
        data.generationPlan.RefreshRewardSource(data);
        return data.generationPlan;
    }

    public void RefreshRewardSource(StrongholdData data)
    {
        if (data == null) return;
        rewardEntryPoint = data.finalStandActive ? "final-stand" : data.isOverlayBase ? "overlay" : "standard";
        rewardRoleKey = data.roleDefName;
    }

    private static string CenterStyleFor(Faction faction, string role)
    {
        string cultureStyle = StrongholdCultureCenters.StyleFor(faction);
        if (cultureStyle != "courtyardKeep") return cultureStyle;
        return role switch
        {
            "NobleEstate" => "palaceCourts",
            "ResearchOutpost" => "palaceCourts",
            "CommandFortress" => "greatHallKeep",
            "PrisonCamp" => "greatHallKeep",
            "AmmoDepot" => "greatHallKeep",
            "Mine" => "greatHallKeep",
            _ => cultureStyle
        };
    }

    public void RecordBase(CellRect rect)
    {
        if (rect.Width > 0 && rect.Height > 0) baseRect = rect;
    }

    public void RecordVault(CellRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        vaultRect = rect;
        if (centerRect.Width <= 0) centerRect = rect.ExpandedBy(1);
    }

    public void ExposeData()
    {
        Scribe_Values.Look(ref schemaVersion, "schemaVersion", 1);
        Scribe_Values.Look(ref seed, "seed", 0);
        Scribe_Values.Look(ref tier, "tier", 1);
        Scribe_Values.Look(ref factionLoadId, "factionLoadId", -1);
        Scribe_Values.Look(ref mapId, "mapId", -1);
        Scribe_Values.Look(ref roleDefName, "roleDefName");
        Scribe_Values.Look(ref centerKind, "centerKind");
        Scribe_Values.Look(ref stage, "stage", "planned");
        Scribe_Values.Look(ref layoutVariant, "layoutVariant", 0);
        Scribe_Values.Look(ref baseRect, "baseRect", CellRect.Empty);
        Scribe_Values.Look(ref centerRect, "centerRect", CellRect.Empty);
        Scribe_Values.Look(ref vaultRect, "vaultRect", CellRect.Empty);
        Scribe_Values.Look(ref commandCell, "commandCell", IntVec3.Invalid);
        Scribe_Values.Look(ref legacyMap, "legacyMap", false);
        Scribe_Values.Look(ref lootFinalized, "lootFinalized", false);
        Scribe_Values.Look(ref rewardBudgetVersion, "rewardBudgetVersion", 1);
        Scribe_Values.Look(ref rewardEntryPoint, "rewardEntryPoint", "standard");
        Scribe_Values.Look(ref rewardRoleKey, "rewardRoleKey");
        Scribe_Values.Look(ref rewardAdapterFinalizeObserved, "rewardAdapterFinalizeObserved", false);
        Scribe_Values.Look(ref rewardBudgetTarget, "rewardBudgetTarget", 0f);
        Scribe_Values.Look(ref rewardBudgetProtected, "rewardBudgetProtected", 0f);
        Scribe_Values.Look(ref rewardBudgetFurniture, "rewardBudgetFurniture", 0f);
        Scribe_Values.Look(ref rewardBudgetGenerated, "rewardBudgetGenerated", 0f);
        Scribe_Values.Look(ref rewardBudgetFinalized, "rewardBudgetFinalized", false);
        Scribe_Collections.Look(ref rewardProtectedThingIds, "rewardProtectedThingIds", LookMode.Value);
        Scribe_Values.Look(ref centerReserved, "centerReserved", false);
        Scribe_Values.Look(ref centerBuilt, "centerBuilt", false);
        Scribe_Values.Look(ref placementNote, "placementNote");
        Scribe_Collections.Look(ref nativeLootIds, "nativeLootIds", LookMode.Value);
        Scribe_Values.Look(ref centerStyle, "centerStyle", "courtyardKeep");
        Scribe_Values.Look(ref nativeTemplate, "nativeTemplate");
        Scribe_Values.Look(ref nativeCenterReused, "nativeCenterReused", false);
        Scribe_Values.Look(ref nativeBounds, "nativeBounds", CellRect.Empty);
        Scribe_Values.Look(ref townBuilt, "townBuilt", false);
        Scribe_Collections.Look(ref townHouseRects, "townHouseRects", LookMode.Value);
        Scribe_Values.Look(ref townRoadCount, "townRoadCount", 0);
        Scribe_Collections.Look(ref featureAppliedPawnIds, "featureAppliedPawnIds", LookMode.Value);
        Scribe_Values.Look(ref featureGrantedValue, "featureGrantedValue", 0f);
        Scribe_Values.Look(ref featureGrantBudget, "featureGrantBudget", -1f);
        Scribe_Values.Look(ref rewardBudgetEquipment, "rewardBudgetEquipment", 0f);
        Scribe_Values.Look(ref rewardBudgetOverflow, "rewardBudgetOverflow", 0f);
        Scribe_Collections.Look(ref nativeDefenseIds, "nativeDefenseIds", LookMode.Value);
        Scribe_Collections.Look(ref featureAppliedDefenseIds, "featureAppliedDefenseIds", LookMode.Value);
        Scribe_Collections.Look(ref generatedTurretIds, "generatedTurretIds", LookMode.Value);
        Scribe_Values.Look(ref castleContourVersion, "castleContourVersion", 0);
        Scribe_Values.Look(ref castleContourTown, "castleContourTown", CellRect.Empty);
        Scribe_Values.Look(ref castleContourNominal, "castleContourNominal", CellRect.Empty);
        Scribe_Collections.Look(ref castleInnerPads, "castleInnerPads", LookMode.Value);
        Scribe_Collections.Look(ref castleOuterPads, "castleOuterPads", LookMode.Value);
        Scribe_Collections.Look(ref castleGateCenters, "castleGateCenters", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            nativeDefenseIds ??= new List<int>();
            featureAppliedDefenseIds ??= new List<int>();
            generatedTurretIds ??= new List<int>();
            castleInnerPads ??= new List<IntVec3>();
            castleOuterPads ??= new List<IntVec3>();
            castleGateCenters ??= new List<int>();
        }
        if (Scribe.mode == LoadSaveMode.PostLoadInit) featureAppliedPawnIds ??= new List<int>();
        if (Scribe.mode == LoadSaveMode.PostLoadInit) townHouseRects ??= new List<CellRect>();
        if (Scribe.mode == LoadSaveMode.PostLoadInit && nativeLootIds == null) nativeLootIds = new List<int>();
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            if (rewardProtectedThingIds == null) rewardProtectedThingIds = new List<int>();
            if (rewardEntryPoint.NullOrEmpty()) rewardEntryPoint = "standard";
        }
    }
}
