using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

/// <summary>
/// Persistent, territory-local stronghold traits. A trait is selected once when a
/// stronghold is first registered and then contributes to the same derived values
/// used by generation, garrison planning and loot estimation.
/// </summary>
public static class StrongholdFeatureUtility
{
    public const int CurrentVersion = 1;

    private const int FeatureClusterRadius = 15;
    private const int FeatureSelectionSeed = 0x6EED31;

    public struct FeatureSpec
    {
        public string Key;
        public float MilitaryMultiplier;
        public int FortificationBonus;
        public int SupplyBonus;
        public float LootBonus;
        public string LabelKey;
        public string DescriptionKey;

        public FeatureSpec(string key, float militaryMultiplier, int fortificationBonus, int supplyBonus,
            float lootBonus, string labelKey, string descriptionKey)
        {
            Key = key;
            MilitaryMultiplier = militaryMultiplier;
            FortificationBonus = fortificationBonus;
            SupplyBonus = supplyBonus;
            LootBonus = lootBonus;
            LabelKey = labelKey;
            DescriptionKey = descriptionKey;
        }
    }

    private static readonly FeatureSpec[] Specs =
    {
        new FeatureSpec("HillFort", 1.05f, 12, 0, 0.02f, "EES.Feature.HillFort", "EES.Feature.HillFort.Desc"),
        new FeatureSpec("WarCollege", 1.12f, 4, 8, 0.04f, "EES.Feature.WarCollege", "EES.Feature.WarCollege.Desc"),
        new FeatureSpec("MerchantQuarter", 1.00f, 0, 30, 0.18f, "EES.Feature.MerchantQuarter", "EES.Feature.MerchantQuarter.Desc"),
        new FeatureSpec("GranaryNetwork", 1.03f, 2, 45, 0.10f, "EES.Feature.GranaryNetwork", "EES.Feature.GranaryNetwork.Desc"),
        new FeatureSpec("SacredKeep", 1.07f, 8, 10, 0.08f, "EES.Feature.SacredKeep", "EES.Feature.SacredKeep.Desc"),
        new FeatureSpec("Arsenal", 1.04f, 4, 10, 0.06f, "EES.Feature.Arsenal", "EES.Feature.Arsenal.Desc"),
        new FeatureSpec("WatchNetwork", 1.03f, 6, 4, 0.02f, "EES.Feature.WatchNetwork", "EES.Feature.WatchNetwork.Desc"),
        new FeatureSpec("VeteranGarrison", 1.08f, 2, 2, 0.04f, "EES.Feature.VeteranGarrison", "EES.Feature.VeteranGarrison.Desc"),
        new FeatureSpec("FieldInfirmary", 1.01f, 0, 12, 0.05f, "EES.Feature.FieldInfirmary", "EES.Feature.FieldInfirmary.Desc"),
        new FeatureSpec("ArtisansGuild", 1.02f, 6, 10, 0.03f, "EES.Feature.ArtisansGuild", "EES.Feature.ArtisansGuild.Desc")
    };

    public static IReadOnlyList<FeatureSpec> AllFeatures => Specs;

    public static bool IsValid(string key)
    {
        return !key.NullOrEmpty() && Specs.Any(spec => spec.Key == key);
    }

    public static string Label(string key)
    {
        FeatureSpec spec = SpecFor(key);
        return spec.Key.NullOrEmpty() ? EESText.T("EES.Feature.None") : EESText.T(spec.LabelKey);
    }

    public static string Description(string key)
    {
        FeatureSpec spec = SpecFor(key);
        return spec.Key.NullOrEmpty() ? string.Empty : EESText.T(spec.DescriptionKey);
    }

    public static float MilitaryMultiplier(string key) => SpecFor(key).MilitaryMultiplier <= 0f ? 1f : SpecFor(key).MilitaryMultiplier;

    public static int FortificationBonus(string key) => SpecFor(key).FortificationBonus;

    public static int SupplyBonus(string key) => SpecFor(key).SupplyBonus;

    public static float LootBonus(string key) => SpecFor(key).LootBonus;

    public static FeatureSpec SpecFor(string key)
    {
        if (!key.NullOrEmpty())
        {
            for (int i = 0; i < Specs.Length; i++)
                if (Specs[i].Key == key) return Specs[i];
        }
        return default;
    }

    /// <summary>
    /// Applies an explicit, persistent feature override for an existing or
    /// newly registered settlement. This is the narrow integration point for
    /// scenario/configuration mods; invalid keys and player settlements are
    /// rejected before any data is changed.
    /// </summary>
    public static bool TrySetForcedFeature(Settlement settlement, string key)
    {
        if (!IsValid(key) || settlement == null || settlement.Faction == null || settlement.Faction.IsPlayer || !settlement.Tile.Valid)
            return false;
        WorldComponent_Strongholds world = WorldComponent_Strongholds.Instance;
        StrongholdData data = world?.GetOrCreate(settlement);
        if (data == null || FakeOutpostUtility.IsFake(data.roleDefName) || BattlefieldUtility.IsBattlefield(data.roleDefName))
            return false;
        data.featureKey = key;
        data.featureVersion = CurrentVersion;
        data.featureForced = true;
        StrongholdScoreUtility.ApplyDerived(data, settlement);
        EESLog.Message($"Feature override [{settlement.Faction.Name}]: {settlement.LabelCap} -> {key} (featureForced).");
        return true;
    }

    /// <summary>
    /// Clears only the explicit override. A valid existing feature is kept;
    /// an empty/invalid value is assigned once using the normal local-diversity
    /// rule, so clearing an override never causes an existing base to reroll.
    /// </summary>
    public static bool TryClearForcedFeature(Settlement settlement)
    {
        if (settlement == null || settlement.Faction == null || settlement.Faction.IsPlayer || !settlement.Tile.Valid ||
            WorldComponent_Strongholds.Instance == null)
            return false;
        StrongholdData data = WorldComponent_Strongholds.Instance.GetExisting(settlement.ID);
        if (data == null || !data.featureForced)
            return false;
        data.featureForced = false;
        EnsureAssigned(data, settlement, WorldComponent_Strongholds.Instance);
        StrongholdScoreUtility.ApplyDerived(data, settlement);
        EESLog.Message($"Feature override cleared: {settlement.LabelCap} -> {data.featureKey ?? "none"}.");
        return true;
    }

    public static void ApplyOnCreation(StrongholdData data, Settlement settlement, WorldComponent_Strongholds world)
    {
        if (data == null || settlement?.Faction == null || world == null || data.featureForced || IsValid(data.featureKey))
            return;
        if (FakeOutpostUtility.IsFake(data.roleDefName) || BattlefieldUtility.IsBattlefield(data.roleDefName))
            return;

        List<FeatureSpec> candidates = Specs.ToList();
        if (candidates.Count == 0) return;

        Dictionary<string, int> counts = candidates.ToDictionary(spec => spec.Key, _ => 0);
        foreach (Settlement nearby in Find.WorldObjects.Settlements)
        {
            if (nearby == settlement || nearby.Faction?.loadID != settlement.Faction.loadID ||
                !nearby.Tile.Valid || !settlement.Tile.Valid || nearby.Tile.Layer != settlement.Tile.Layer ||
                !FinalStandUtility.WithinClusterRadius(settlement.Tile, nearby.Tile))
                continue;
            StrongholdData existing = world.GetExisting(nearby.ID);
            if (existing != null && IsValid(existing.featureKey) && counts.ContainsKey(existing.featureKey))
                counts[existing.featureKey]++;
        }

        int minimum = counts.Values.Min();
        List<FeatureSpec> leastUsed = candidates.Where(spec => counts[spec.Key] == minimum).ToList();
        int seed = Gen.HashCombineInt(data.settlementId, FeatureSelectionSeed);
        seed = Gen.HashCombineInt(seed, FeatureRoleSalt(data.roleDefName));
        Rand.PushState(seed);
        try
        {
            data.featureKey = leastUsed[Rand.Range(0, leastUsed.Count)].Key;
            data.featureVersion = CurrentVersion;
        }
        finally
        {
            Rand.PopState();
        }
        EESLog.Message($"Feature diversity [{settlement.Faction.Name}]: {settlement.LabelCap} T{data.tier} role={data.roleDefName} -> {data.featureKey} (cluster={FeatureClusterRadius}, used={string.Join(",", counts.Select(pair => pair.Key + "=" + pair.Value))}).");
    }

    public static void EnsureAssigned(StrongholdData data, Settlement settlement, WorldComponent_Strongholds world)
    {
        if (data == null)
            return;
        if (FakeOutpostUtility.IsFake(data.roleDefName) || BattlefieldUtility.IsBattlefield(data.roleDefName))
        {
            if (!data.featureKey.NullOrEmpty() || data.featureForced)
            {
                data.featureKey = null;
                data.featureVersion = CurrentVersion;
                data.featureForced = false;
                StrongholdScoreUtility.ApplyDerived(data, settlement);
                EESLog.Warning($"Cleared ordinary feature from dedicated role {data.roleDefName} on settlement {data.settlementId}.");
            }
            return;
        }
        if (IsValid(data.featureKey))
			return;
		if (data.featureForced)
		{
			// A removed/renamed key from an old save must not leave the base without
			// a visible feature or derived bonuses forever. Treat it as an invalid
			// override, clear only the override flag, and assign once normally.
			data.featureForced = false;
			EESLog.Warning($"Invalid forced feature '{data.featureKey ?? "<null>"}' on settlement {data.settlementId}; clearing override and reassigning.");
		}
		ApplyOnCreation(data, settlement, world);
    }

    private static int FeatureRoleSalt(string role)
    {
        return role switch
        {
            "CommandFortress" => 11,
            "NobleEstate" => 17,
            "ResearchOutpost" => 23,
            "MechanoidRelay" => 29,
            "Mine" => 31,
            "PrisonCamp" => 37,
            "SupplyDepot" => 41,
            _ => 7
        };
    }
}
