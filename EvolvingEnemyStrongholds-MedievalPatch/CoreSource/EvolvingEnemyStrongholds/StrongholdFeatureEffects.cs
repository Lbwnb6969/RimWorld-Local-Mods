using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

/// <summary>Effects belong to an ordinary hostile EES settlement, never a global faction buff.</summary>
[StaticConstructorOnStartup]
public static class StrongholdFeatureEffects
{
    private static readonly ConditionalWeakTable<StrongholdGenerationPlan, HashSet<IntVec3>> contourDefenseCells = new();
    private static readonly HashSet<string> castleWallDefs = new(StringComparer.Ordinal)
    {
        "JDS_WallsTwo", "JDS_Walls", "JDS_Walls_Embrasure", "Castle_Walls_Corner_Embrasure",
        "JDS_Walls_End", "JDS_Walls_L_connector", "JDS_Walls_T_connector", "JDS_Wall_X_connector",
        "JDS_Tower", "JDS_Tower_connector", "JDS_Tower_I_connector", "JDS_Tower_T_connector", "JDS_Tower_L_connector"
    };

    private static bool FeatureDefense(ThingDef def) => StrongholdCenterPlanner.StructuralWall(def) ||
        def?.building?.IsTurret == true || def != null && castleWallDefs.Contains(def.defName) &&
        string.Equals(def.modContentPack?.PackageId, "Gideon.CastleWalls", StringComparison.OrdinalIgnoreCase);

    private static HashSet<IntVec3> ContourDefenseCells(StrongholdGenerationPlan plan)
    {
        // Committed contours are immutable during this plan's lifetime. Reload
        // creates a new plan, so the weak cache never retains a previous map.
        var cells = new HashSet<IntVec3>();
        foreach (var pad in plan.castleInnerPads.Concat(plan.castleOuterPads))
            cells.UnionWith(CellRect.FromLimits(pad.x, pad.z, pad.x + 1, pad.z + 1).Cells);
        var outer = plan.castleContourNominal.ExpandedBy(8);
        for (int side = 0; side < 4; side++)
        for (int depth = 1; depth < 8; depth++)
        foreach (int offset in new[] { -8, 8 })
        {
            int along = plan.castleGateCenters[side] + offset;
            cells.Add(side switch
            {
                0 => new IntVec3(along, 0, outer.minZ + depth),
                1 => new IntVec3(along, 0, outer.maxZ - depth),
                2 => new IntVec3(outer.minX + depth, 0, along),
                _ => new IntVec3(outer.maxX - depth, 0, along)
            });
        }
        return cells;
    }
    static StrongholdFeatureEffects()
    {
        StatDefOf.MaxHitPoints.parts ??= new List<StatPart>();
        if (!StatDefOf.MaxHitPoints.parts.Any(p => p is StatPart_StrongholdFeatureDefense))
            StatDefOf.MaxHitPoints.parts.Add(new StatPart_StrongholdFeatureDefense { parentStat = StatDefOf.MaxHitPoints });
    }

    public static bool Eligible(Map map, Faction faction, StrongholdData data)
    {
        return map?.Parent is Settlement settlement && faction != null && settlement.Faction == faction &&
            faction.HostileTo(Faction.OfPlayer) && StrongholdCenterPlanner.Eligible(map, data) &&
            data.settlementId == settlement.ID && StrongholdFeatureUtility.IsValid(data.featureKey);
    }

    public static int ReinforcementInterval(Map map, StrongholdData data, int original)
    {
        return Eligible(map, map?.ParentFaction, data) && data.featureKey == "WatchNetwork"
            ? Mathf.Max(1, Mathf.RoundToInt(original * .90f)) : original;
    }

    public static float DefenseMultiplier(Thing thing)
    {
        var map = thing?.Map;
        if (map?.Parent is not Settlement settlement || thing.Faction != settlement.Faction ||
            thing.def.category != ThingCategory.Building ||
            !FeatureDefense(thing.def)) return 1f;
        var data = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
        if (!Eligible(map, thing.Faction, data) || data.featureKey != "ArtisansGuild") return 1f;
        var plan = data.generationPlan;
        if (plan == null || plan.mapId != map.uniqueID || plan.baseRect.Width <= 0) return 1f;
        if (!plan.legacyMap && thing.def.building?.IsTurret == true && plan.generatedTurretIds?.Contains(thing.thingIDNumber) == true)
            return 1.06f;
        var footprint = thing.OccupiedRect();
        if (footprint.Cells.All(plan.baseRect.Contains)) return 1.06f;
        if (plan.legacyMap || plan.castleContourVersion != 1 || plan.castleContourNominal.Width <= 0 ||
            plan.castleGateCenters?.Count != 4 || (plan.castleInnerPads?.Count ?? 0) <= 4 || (plan.castleOuterPads?.Count ?? 0) <= 4)
            return 1f;
        var contour = contourDefenseCells.GetValue(plan, ContourDefenseCells);
        return footprint.Cells.All(contour.Contains) ? 1.06f : 1f;
    }

    public static void RegisterGeneratedTurret(Thing thing)
    {
        var map = thing?.Map;
        if (map == null || MapGenerator.mapBeingGenerated != map || thing.def.building?.IsTurret != true ||
            map.Parent is not Settlement site || thing.Faction != site.Faction) return;
        var data = WorldComponent_Strongholds.Instance?.GetExisting(site.ID);
        var plan = data?.generationPlan;
        if (!Eligible(map, thing.Faction, data) || plan == null || plan.legacyMap || plan.mapId != map.uniqueID) return;
        plan.generatedTurretIds ??= new List<int>();
        if (!plan.generatedTurretIds.Contains(thing.thingIDNumber)) plan.generatedTurretIds.Add(thing.thingIDNumber);
    }

    public static int ApplyToNewGarrison(Map map, Faction faction, StrongholdData data, HashSet<int> previousIds)
    {
        if (previousIds == null || !Eligible(map, faction, data)) return 0;
        return ApplyToPawns(map, faction, data, StrongholdPawnOwnership.Population(map, faction)
            .Where(p => !previousIds.Contains(p.thingIDNumber)).ToList());
    }

    public static int ApplyToNewDefense(Map map, Faction faction, StrongholdData data)
    {
        if (MapGenerator.mapBeingGenerated != map || !Eligible(map, faction, data) || data.featureKey != "ArtisansGuild") return 0;
        var plan = data.generationPlan;
        if (plan == null || plan.legacyMap || plan.mapId != map.uniqueID) return 0;
        var previous = new HashSet<int>(plan.nativeDefenseIds ?? new List<int>());
        var applied = new HashSet<int>(plan.featureAppliedDefenseIds ?? new List<int>());
        plan.featureAppliedDefenseIds ??= new List<int>();
        int count = 0;
        foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
        {
            if (!thing.def.useHitPoints || previous.Contains(thing.thingIDNumber) || applied.Contains(thing.thingIDNumber) ||
                DefenseMultiplier(thing) <= 1f) continue;
            // Preserve any damage ratio; the generation pass does not repair old buildings.
            // ThingMaker cached the unspawned maximum at this same game tick.
            // Recalculate with map/faction scope before capping the new health.
            int maximum = Mathf.RoundToInt(thing.GetStatValue(StatDefOf.MaxHitPoints, true, 0));
            thing.HitPoints = Math.Min(maximum, Mathf.RoundToInt(thing.HitPoints * 1.06f));
            plan.featureAppliedDefenseIds.Add(thing.thingIDNumber);
            count++;
        }
        return count;
    }

    public static int ApplyToPawns(Map map, Faction faction, StrongholdData data, IEnumerable<Pawn> newPawns)
    {
        if (newPawns == null || !Eligible(map, faction, data)) return 0;
        var plan = StrongholdGenerationPlan.GetOrCreate(map, data);
        if (plan == null) return 0;
        // Freeze this small allowance once. A feature override or later reinforcements cannot enlarge it.
        if (plan.featureGrantBudget < 0f)
            plan.featureGrantBudget = Mathf.Max(0f, StrongholdZoneUtility.EstimateVaultValue(data) * .03f);
        plan.featureAppliedPawnIds ??= new List<int>();
        var applied = new HashSet<int>(plan.featureAppliedPawnIds);
        int count = 0;
        // A reinforcement can be temporarily unspawned while it is held by a
        // map container. Scope the root before expanding mirror-image members,
        // then repeat the check for every member: flattening Group(root) must
        // not let a foreign-map holder or foreign mirror member receive this
        // settlement's feature.
        foreach (var pawn in newPawns
            .Where(root => RecruitInScope(root, map, faction))
            .SelectMany(StrongholdPawnOwnership.Group))
        {
            if (!RecruitInScope(pawn, map, faction) || applied.Contains(pawn.thingIDNumber)) continue;
            switch (data.featureKey)
            {
                case "VeteranGarrison":
                    RaiseSkill(pawn, SkillDefOf.Shooting, 1);
                    RaiseSkill(pawn, SkillDefOf.Melee, 1);
                    break;
                case "FieldInfirmary":
                    RaiseSkill(pawn, SkillDefOf.Medicine, 2);
                    if (PositiveHash(pawn.thingIDNumber, data.settlementId) % 4 == 0)
                        GiveSupply(pawn, ThingDefOf.MedicineHerbal, 2, data, plan);
                    break;
                case "ArtisansGuild":
                    RaiseSkill(pawn, SkillDefOf.Construction, 2);
                    break;
                case "Arsenal":
                    ImproveWeapon(pawn, data, plan);
                    if (pawn.inventory != null)
                        foreach (var ammo in pawn.inventory.innerContainer.Where(t => CECompat.IsAmmo(t.def)).ToList())
                            GiveSupply(pawn, ammo.def, Mathf.FloorToInt(ammo.stackCount * .15f), data, plan);
                    break;
                case "GranaryNetwork":
                    if (pawn.inventory != null)
                        foreach (var food in pawn.inventory.innerContainer.Where(t => t.def == ThingDefOf.MealSurvivalPack ||
                            t.def == ThingDefOf.Pemmican).ToList())
                            GiveSupply(pawn, food.def, Mathf.FloorToInt(food.stackCount * .15f), data, plan);
                    break;
            }
            applied.Add(pawn.thingIDNumber);
            plan.featureAppliedPawnIds.Add(pawn.thingIDNumber);
            count++;
        }
        if (count > 0) EESLog.Message($"Feature loadout {data.featureKey}: applied once to {count} new defenders on {map.Parent.LabelCap}; grantedValue={plan.featureGrantedValue:0.##}.");
        return count;
    }

    private static bool RecruitInScope(Pawn pawn, Map map, Faction faction) => pawn != null &&
        pawn.Faction == faction && !pawn.Dead && !pawn.Destroyed && !pawn.Discarded && !pawn.IsPrisoner &&
        pawn.RaceProps?.Humanlike == true && (pawn.MapHeld == null || pawn.MapHeld == map) &&
        (pawn.Spawned || pawn.MapHeld != null || !Find.WorldPawns.Contains(pawn));

    private static int PositiveHash(int pawnId, int settlementId) => Gen.HashCombineInt(pawnId, settlementId) & int.MaxValue;

    private static void RaiseSkill(Pawn pawn, SkillDef skillDef, int levels)
    {
        var skill = pawn.skills?.GetSkill(skillDef);
        // Level includes gene/trait aptitude, whereas its setter writes the learned level.
        // Adding to Level would apply that aptitude a second time.
        if (skill != null && !skill.TotallyDisabled && skill.Level < 20)
            skill.levelInt = Math.Min(20, skill.levelInt + levels);
    }

    private static float GrantHeadroom(StrongholdData data, StrongholdGenerationPlan plan)
    {
        // Supplemental gear/supplies share a small, fixed slice of the existing reward target.
        // Neither the number of recruits nor repeated generation can expand this allowance.
        return Mathf.Max(0f, plan.featureGrantBudget - plan.featureGrantedValue);
    }

    private static void GiveSupply(Pawn pawn, ThingDef def, int amount, StrongholdData data, StrongholdGenerationPlan plan)
    {
        if (def == null || pawn.inventory == null || amount <= 0) return;
        float unitValue = def.GetStatValueAbstract(StatDefOf.MarketValue);
        amount = Math.Min(amount, Mathf.FloorToInt(GrantHeadroom(data, plan) / Mathf.Max(.01f, unitValue)));
        if (amount <= 0) return;
        var item = ThingMaker.MakeThing(def);
        item.stackCount = Math.Min(amount, def.stackLimit);
        int count = item.stackCount;
        if (pawn.inventory.innerContainer.TryAdd(item)) plan.featureGrantedValue += unitValue * count;
        else item.Destroy(); // A newly created, unheld supply; no original item is removed.
    }

    private static void ImproveWeapon(Pawn pawn, StrongholdData data, StrongholdGenerationPlan plan)
    {
        if (PositiveHash(pawn.thingIDNumber, data.settlementId) % 5 != 0) return;
        var weapon = pawn.equipment?.Primary;
        var quality = weapon?.TryGetComp<CompQuality>();
        if (quality == null || quality.Quality >= QualityCategory.Excellent) return;
        // Reserve a conservative cost before increasing one grade. Never downgrade existing superior equipment.
        float reserved = Mathf.Max(1f, weapon.MarketValue * .5f);
        if (reserved > GrantHeadroom(data, plan)) return;
        quality.SetQuality(quality.Quality + 1, ArtGenerationContext.Outsider);
        plan.featureGrantedValue += reserved;
    }
}

public sealed class StatPart_StrongholdFeatureDefense : StatPart
{
    public override void TransformValue(StatRequest req, ref float val)
    {
        if (req.HasThing) val *= StrongholdFeatureEffects.DefenseMultiplier(req.Thing);
    }

    public override string ExplanationPart(StatRequest req)
    {
        return req.HasThing && StrongholdFeatureEffects.DefenseMultiplier(req.Thing) > 1f
            ? EESText.T("EES.Feature.ArtisansGuild.DefenseEffect") : null;
    }
}
