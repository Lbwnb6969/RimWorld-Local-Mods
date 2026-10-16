using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace EvolvingEnemyStrongholds;

public static partial class StrongholdLayoutGenerator
{
    public static void BuildTown(Map map, StrongholdData data, CellRect town)
    {
        var plan = data?.generationPlan;
        if (!StrongholdCenterPlanner.Eligible(map, data) || plan?.centerBuilt != true ||
            plan.legacyMap || plan.townBuilt || town.Width < 20) return;
        var streets = TownStreetCells(plan.centerRect, town);
        var entrances = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
            .Where(thing => thing.def.IsDoor && town.Contains(thing.Position) && !plan.centerRect.Contains(thing.Position))
            .OrderBy(thing => (thing.Position - plan.commandCell).LengthHorizontalSquared)
            .Take(32).Select(thing => thing.Position).Distinct().ToList();
        StrongholdDefenseUtility.ResolveWall(map, map.ParentFaction, data.ContentTier, out var wall, out var stuff);
        wall ??= ThingDefOf.Wall;
        if (plan.centerKind == "clanHall") { wall = ThingDefOf.Wall; stuff = ThingDefOf.WoodLog; }
        if (wall.size != IntVec2.One) wall = ThingDefOf.Wall;
        stuff ??= ThingDefOf.BlocksGranite;
        var floor = StrongholdCultureCenters.FloorFor(map.ParentFaction,
            DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor"));
        var road = DefDatabase<TerrainDef>.GetNamedSilentFail("FlagstoneGranite") ??
            DefDatabase<TerrainDef>.GetNamedSilentFail("PackedDirt");
        int limit = data.tier >= 16 ? 20 : data.tier == 15 ? 16 : data.tier >= 10 ? 12 : data.tier >= 5 ? 8 : 4;
        foreach (var plot in TownPlots(plan.centerRect))
        {
            if (plan.townHouseRects.Count >= limit) break;
            if (!TownPlotClear(map, plot.Room, town, streets)) continue;
            if (!TownEntranceReachable(map, plot.Room, plot.Door, plan.commandCell)) continue;
            if (!TownPreparePlot(map, plot.Room)) continue;
            foreach (var cell in plot.Room)
            {
                foreach (var plant in cell.GetThingList(map).OfType<Plant>().ToList()) plant.Destroy();
                var rock = cell.GetEdifice(map);
                if (rock?.def.building?.isNaturalRock == true && rock.def.building.isResourceRock != true)
                    rock.Destroy(DestroyMode.Vanish);
            }
            BuildEnclosedRoom(map, map.ParentFaction, plot.Room, plot.Door, wall, stuff,
                ThingDefOf.Door, StuffOrDefault(ThingDefOf.Door, stuff), floor, true);
            var interior = plot.Room.ContractedBy(1);
            var occupied = new HashSet<IntVec3>();
            int kind = (plan.townHouseRects.Count + plan.layoutVariant) % 3;
            if (kind == 0)
            {
                PlaceCenterFurniture(map, map.ParentFaction, "Bed", interior, occupied);
                PlaceCenterFurniture(map, map.ParentFaction, "Bed", interior, occupied);
                PlaceCenterFurniture(map, map.ParentFaction, "Table1x2c", interior, occupied);
                PlaceCenterFurniture(map, map.ParentFaction, "Stool", interior, occupied);
            }
            else if (kind == 1)
            {
                if (!PlaceCenterFurniture(map, map.ParentFaction, "HandTailoringBench", interior, occupied))
                    PlaceCenterFurniture(map, map.ParentFaction, "TableSculpting", interior, occupied);
                PlaceCenterFurniture(map, map.ParentFaction, "Shelf", interior, occupied);
            }
            else
            {
                PlaceCenterFurniture(map, map.ParentFaction, "Shelf", interior, occupied);
                PlaceCenterFurniture(map, map.ParentFaction, "Shelf", interior, occupied);
                PlaceCenterFurniture(map, map.ParentFaction, "Table1x2c", interior, occupied);
            }
            LightRoom(map, map.ParentFaction, plot.Room, false);
            plan.townHouseRects.Add(plot.Room);
            entrances.Add(plot.Door);
        }
        foreach (var entry in entrances)
            ConnectTownRoad(map, entry, plan.commandCell, town, plan.centerRect, streets);
        // Connect the four planned town gates using native pathfinding before walls are erected.
        foreach (var entry in new[] {
            new IntVec3(town.CenterCell.x, 0, town.minZ + 2), new IntVec3(town.CenterCell.x, 0, town.maxZ - 2),
            new IntVec3(town.minX + 2, 0, town.CenterCell.z), new IntVec3(town.maxX - 2, 0, town.CenterCell.z) })
            ConnectTownRoad(map, entry, plan.commandCell, town, plan.centerRect, streets);
        foreach (var cell in streets)
        {
            if (!cell.InBounds(map) || plan.centerRect.Contains(cell) || !cell.Standable(map) ||
                cell.GetEdifice(map) != null || road == null) continue;
            var terrain = cell.GetTerrain(map);
            if (terrain?.natural != true || terrain.IsWater ||
                !terrain.affordances.Contains(TerrainAffordanceDefOf.Heavy)) continue;
            foreach (var plant in cell.GetThingList(map).OfType<Plant>().ToList()) plant.Destroy();
            map.terrainGrid.SetTerrain(cell, road);
            plan.townRoadCount++;
        }
        plan.townBuilt = true;
        EESLog.Message("Town district built: houses=" + plan.townHouseRects.Count + ",roadCells=" + plan.townRoadCount +
            ",castle=" + plan.centerRect + ",town=" + town + "; native buildings retained.");
    }

    private static HashSet<IntVec3> TownStreetCells(CellRect center, CellRect town)
    {
        var streets = new HashSet<IntVec3>();
        var outside = center.ExpandedBy(4);
        var inside = center.ExpandedBy(1);
        foreach (var cell in town.ContractedBy(2))
            if (!center.Contains(cell) &&
                (outside.Contains(cell) && !inside.Contains(cell) ||
                 Math.Abs(cell.x - center.CenterCell.x) <= 1 || Math.Abs(cell.z - center.CenterCell.z) <= 1))
                streets.Add(cell);
        return streets;
    }

    private static IEnumerable<(CellRect Room, IntVec3 Door)> TownPlots(CellRect center)
    {
        for (int row = 0; row < 2; row++)
        for (int offset = 0; offset < Math.Max(center.Width, center.Height); offset += 2)
        {
            for (int side = 0; side < 4; side++)
            {
                bool horizontal = side < 2;
                int along = (horizontal ? center.minX : center.minZ) - 2 + offset;
                if (along > (horizontal ? center.maxX : center.maxZ) - 5) continue;
                var room = horizontal
                    ? new CellRect(along, side == 0 ? center.minZ - 11 - row * 12 : center.maxZ + 5 + row * 12, 9, 7)
                    : new CellRect(side == 2 ? center.minX - 11 - row * 12 : center.maxX + 5 + row * 12, along, 7, 9);
                var door = side == 0 ? new IntVec3(room.CenterCell.x, 0, room.maxZ) :
                    side == 1 ? new IntVec3(room.CenterCell.x, 0, room.minZ) :
                    side == 2 ? new IntVec3(room.maxX, 0, room.CenterCell.z) : new IntVec3(room.minX, 0, room.CenterCell.z);
                yield return (room, door);
            }
        }
    }

    private static bool TownPlotClear(Map map, CellRect room, CellRect town, HashSet<IntVec3> streets)
    {
        if (!town.ContractedBy(3).Contains(room.Min) || !town.ContractedBy(3).Contains(room.Max)) return false;
        foreach (var cell in room.ExpandedBy(1))
        {
            if (!cell.InBounds(map) || room.Contains(cell) && streets.Contains(cell) ||
                !cell.GetTerrain(map).affordances.Contains(TerrainAffordanceDefOf.Heavy)) return false;
            if (cell.GetThingList(map).Any(thing => thing is not Plant && thing is not Filth &&
                !(thing.def.category == ThingCategory.Item && thing.def.IsWithinCategory(ThingCategoryDefOf.StoneChunks)) &&
                !(thing.def.building?.isNaturalRock == true && thing.def.building.isResourceRock != true) &&
                !(thing.def.mineable && thing.def.modExtensions?.Any(extension => extension.GetType().FullName == "ReGrowthCore.BoulderProperties") == true))) return false;
        }
        return true;
    }

    private static bool TownEntranceReachable(Map map, CellRect room, IntVec3 door, IntVec3 command)
    {
        // Natural rock is permitted inside a new plot and cleared during building,
        // but an exit surrounded by preserved rock/native buildings is a dead end.
        // Choose a different plot rather than build an inaccessible occupied house.
        var outside = door.z == room.minZ ? door + IntVec3.South :
            door.z == room.maxZ ? door + IntVec3.North :
            door.x == room.minX ? door + IntVec3.West : door + IntVec3.East;
        return outside.InBounds(map) && outside.Walkable(map) &&
            map.reachability.CanReach(outside, command, PathEndMode.OnCell,
                TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly));
    }

    private static bool TownPreparePlot(Map map, CellRect room)
    {
        foreach (var chunk in room.ExpandedBy(1).SelectMany(cell => cell.GetThingList(map))
            .Where(thing => thing.def.category == ThingCategory.Item || thing.def.mineable &&
                thing.def.modExtensions?.Any(extension => extension.GetType().FullName == "ReGrowthCore.BoulderProperties") == true).Distinct().ToList())
        {
            if (!StrongholdCenterPlanner.FindOutsideCell(map, chunk.def, chunk.Rotation, chunk.Position, room.ExpandedBy(1), out var safe)) return false;
            var original = chunk.Position;
            chunk.DeSpawn();
            try { GenSpawn.Spawn(chunk, safe, map, chunk.Rotation); }
            catch { if (!chunk.Spawned) GenSpawn.Spawn(chunk, original, map, chunk.Rotation); throw; }
        }
        return true;
    }

    private static void ConnectTownRoad(Map map, IntVec3 from, IntVec3 to, CellRect town, CellRect center, HashSet<IntVec3> streets)
    {
        if (!from.InBounds(map) || !to.InBounds(map) || !from.Walkable(map)) return;
        using var path = map.pathFinder.FindPathNow(from, to, TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly), peMode: PathEndMode.OnCell);
        if (!path.Found) return;
        foreach (var cell in path.NodesReversed)
        {
            if (!town.ContractedBy(2).Contains(cell) || center.Contains(cell)) continue;
            streets.Add(cell);
            foreach (var adjacent in GenAdj.CardinalDirections.Select(direction => cell + direction))
                if (town.ContractedBy(2).Contains(adjacent) && !center.Contains(adjacent) && adjacent.Standable(map) &&
                    adjacent.GetEdifice(map) == null) streets.Add(adjacent);
        }
    }
}
