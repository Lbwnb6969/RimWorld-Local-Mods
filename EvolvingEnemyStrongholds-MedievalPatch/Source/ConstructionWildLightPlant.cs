using System;
using System.Collections.Generic;
using System.Linq;
using EvolvingEnemyStrongholds;
using RimWorld;
using Verse;

namespace EESMedievalPatch;

internal static partial class MedievalUtility
{
    // Only these two audited ReGrowth definitions have a native Plant with one
    // native glower. The installed Quality Of Building also adds an audited
    // cache-only QualityCheckComp without spawn/load hooks. Neither is recreated.
    // A changed definition/component set fails closed and remains protected.
    private static bool CanRelocateWildLightPlant(Plant plant, Map map)
    {
        return ConstructionContext(map) && plant != null && plant.Spawned && !plant.Destroyed && plant.Map == map &&
            plant.GetType() == typeof(Plant) && plant.def.plant?.IsTree == false &&
            plant.def.defName is "RG_Plant_GlowLeaf" or "RG_Plant_LureWeed" &&
            string.Equals(plant.def.modContentPack?.PackageId, "ReGrowth.BOTR.Core", StringComparison.OrdinalIgnoreCase) &&
            !plant.sown && !plant.Blighted && plant.Faction == null && !(plant.questTags?.Count > 0) &&
            !plant.Position.Roofed(map) && map.zoneManager.ZoneAt(plant.Position) == null &&
            !map.designationManager.HasMapDesignationOn(plant) && !IsCapturedNativeDefense(plant, map) &&
            !IsProtectedStrongholdDef(plant.def) && plant.AllComps.Count is 1 or 2 &&
            plant.AllComps.Count(comp => comp.GetType() == typeof(CompGlower)) == 1 &&
            plant.AllComps.All(comp => comp.GetType() == typeof(CompGlower) ||
                comp.GetType().FullName == "QualityOfBuilding.QualityCheckComp" &&
                comp.GetType().BaseType == typeof(ThingComp) &&
                comp.GetType().Assembly.GetName().Name == "QualityOfBuilding");
    }

    private static bool RelocateWildLightPlant(Map map, Plant plant, HashSet<IntVec3> footprint)
    {
        if (!CanRelocateWildLightPlant(plant, map)) return false;
        IntVec3 original = plant.Position;
        Rot4 rotation = plant.Rotation;
        var plan = WorldComponent_Strongholds.Instance?.GetExisting(map.Parent.ID)?.generationPlan;
        foreach (IntVec3 cell in GenRadial.RadialCellsAround(original, 65f, true))
        {
            if (!cell.InBounds(map) || footprint.Contains(cell) || cell.Roofed(map) ||
                cell.GetTerrain(map) != original.GetTerrain(map) || cell.IsPolluted(map) != original.IsPolluted(map) ||
                map.zoneManager.ZoneAt(cell) != null || cell.GetThingList(map).Count != 0 ||
                plan?.baseRect.Contains(cell) == true ||
                IntersectsCastleGateEntrance(map, CellRect.SingleCell(cell)) ||
                !plant.def.CanEverPlantAt(cell, map, checkMapTemperature: false)) continue;
            plant.DeSpawn();
            try
            {
                // Reuse native respawn registration without reinitializing an
                // existing Plant's leafless/growth state; the glower re-registers.
                SpawnRelocatedConstructionThing(plant, cell, map, rotation, preserveSpawnState: true);
                return true;
            }
            catch (Exception exception)
            {
                if (plant.Spawned) plant.DeSpawn();
                SpawnRelocatedConstructionThing(plant, original, map, rotation, preserveSpawnState: true);
                Log.Warning($"[EES Medieval Patch] Retained wild light plant {plant.ThingID} at {original} after relocation failed: {exception}");
                return false;
            }
        }
        Log.Warning($"[EES Medieval Patch] Retained wild light plant {plant.ThingID} at {original}; no empty compatible destination outside the construction footprint.");
        return false;
    }
}
