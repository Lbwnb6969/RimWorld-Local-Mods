using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class StrongholdNativeCenter
{
    public static bool TryFind(Map map, CellRect town, CellRect knownCastle, out CellRect footprint, out List<CellRect> rooms)
    {
        footprint = CellRect.Empty;
        rooms = null;
        var boundedPrefab = knownCastle.Width >= 23 && knownCastle.Height >= 19 && knownCastle.Width <= 95 && knownCastle.Height <= 95;
        if (boundedPrefab) return TryPrefabRooms(map, knownCastle, out footprint, out rooms);
        var search = (boundedPrefab ? knownCastle : town.ExpandedBy(12)).ClipInsideMap(map);
        var cellRooms = new int[map.Size.x * map.Size.z];
        for (var i = 0; i < cellRooms.Length; i++) cellRooms[i] = -1;
        var interiors = new List<CellRect>();
        var areas = new List<int>();
        var roofedAreas = new List<int>();
        var enclosed = new List<bool>();
        // Closed outdoor courts connect rooms; the open map is never a castle connector.
        var queue = new Queue<IntVec3>();
        foreach (var root in search)
        {
            if (cellRooms[map.cellIndices.CellToIndex(root)] >= 0 || !InteriorCell(map, root)) continue;
            var id = interiors.Count;
            var minX = root.x;
            var maxX = root.x;
            var minZ = root.z;
            var maxZ = root.z;
            var area = 0;
            var roofed = 0;
            var touchesBoundary = false;
            queue.Enqueue(root);
            cellRooms[map.cellIndices.CellToIndex(root)] = id;
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                area++;
                if (cell.Roofed(map)) roofed++;
                touchesBoundary |= cell.x == search.minX || cell.x == search.maxX || cell.z == search.minZ || cell.z == search.maxZ;
                minX = Math.Min(minX, cell.x);
                maxX = Math.Max(maxX, cell.x);
                minZ = Math.Min(minZ, cell.z);
                maxZ = Math.Max(maxZ, cell.z);
                foreach (var offset in GenAdj.CardinalDirections)
                {
                    var next = cell + offset;
                    if (!search.Contains(next)) continue;
                    var index = map.cellIndices.CellToIndex(next);
                    if (cellRooms[index] >= 0 || !InteriorCell(map, next)) continue;
                    cellRooms[index] = id;
                    queue.Enqueue(next);
                }
            }
            interiors.Add(CellRect.FromLimits(minX, minZ, maxX, maxZ));
            areas.Add(area);
            roofedAreas.Add(roofed);
            enclosed.Add((boundedPrefab || !touchesBoundary) && maxX - minX < 95 && maxZ - minZ < 95);
        }
        if (interiors.Count < 5)
        {
            EESLog.Message("Native castle scan: regions=" + interiors.Count + "; fewer than five rooms.");
            return false;
        }
        var adjacency = interiors.Select(_ => new HashSet<int>()).ToArray();
        foreach (var door in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial).Where(t => t.def.IsDoor && search.Contains(t.Position)))
        {
            var neighbours = new HashSet<int>();
            foreach (var cell in door.OccupiedRect())
            foreach (var offset in GenAdj.CardinalDirections)
            {
                var next = cell + offset;
                if (!search.Contains(next)) continue;
                var id = cellRooms[map.cellIndices.CellToIndex(next)];
                if (id >= 0 && enclosed[id]) neighbours.Add(id);
            }
            foreach (var a in neighbours)
            foreach (var b in neighbours)
                if (a != b) adjacency[a].Add(b);
        }
        var visited = new HashSet<int>();
        var bestScore = 0;
        var largestRoomCount = 0;
        var missingAnchor = 0;
        var badRoof = 0;
        var missingVault = 0;
        for (var root = 0; root < interiors.Count; root++)
        {
            if (!visited.Add(root)) continue;
            var connected = new List<int>();
            var pending = new Queue<int>();
            pending.Enqueue(root);
            while (pending.Count > 0)
            {
                var id = pending.Dequeue();
                connected.Add(id);
                foreach (var next in adjacency[id]) if (visited.Add(next)) pending.Enqueue(next);
            }
            var usable = connected.Where(id => enclosed[id] && roofedAreas[id] == areas[id] && interiors[id].Width >= 5 && interiors[id].Height >= 5 &&
                interiors[id].All(cell => cellRooms[map.cellIndices.CellToIndex(cell)] == id || cell.GetEdifice(map)?.def.defName == "Column")).ToList();
            largestRoomCount = Math.Max(largestRoomCount, usable.Count);
            if (usable.Count < 5 || usable.Sum(id => areas[id]) < 180) continue;
            var candidateRooms = usable.Select(id => interiors[id].ExpandedBy(1)).ToList();
            var candidate = Bounds(candidateRooms);
            if (candidate.Width < 23 || candidate.Height < 19 || candidate.Width > 95 || candidate.Height > 95) continue;
            var throne = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial).FirstOrDefault(t =>
                candidate.Contains(t.Position) && (t.def.defName == "Throne" || t.def.defName == "GrandThrone" || t.def.defName == "DankPyon_RoyalThrone" ||
                    t.def.defName == "RoyalBed" || t.def.defName == "DankPyon_RoyalTudorBed"));
            if (throne == null) { missingAnchor++; continue; }
            if (!RoofSupported(map, candidate)) { badRoof++; continue; }
            var command = candidateRooms.FirstOrDefault(room => room.Contains(throne.Position));
            if (command.Width < 7) continue;
            var vault = candidateRooms.Where(room => room != command && room.Width <= 15 && room.Height <= 15 &&
                room.ContractedBy(1).Count(c => c.Standable(map) && c.GetEdifice(map) == null && c.GetFirstItem(map) == null && c.GetFirstPawn(map) == null) >= 12 &&
                room.CenterCell.Standable(map) && room.CenterCell.GetEdifice(map) == null)
                .OrderByDescending(room => room.Area).ThenByDescending(room => (room.CenterCell - command.CenterCell).LengthHorizontalSquared).FirstOrDefault();
            if (vault.Width < 7) { missingVault++; continue; }
            var others = candidateRooms.Where(room => room != command && room != vault).OrderByDescending(room => room.Area).Take(3).ToList();
            var score = usable.Sum(id => areas[id]);
            if (score <= bestScore) continue;
            bestScore = score;
            footprint = candidate;
            rooms = new List<CellRect> { command, vault, others[0], others[1], others[2] };
        }
        EESLog.Message("Native castle scan: regions=" + interiors.Count + ", largestLinkedRoomCount=" + largestRoomCount +
            ", missingAnchor=" + missingAnchor + ", unsupportedRoof=" + badRoof + ", missingVault=" + missingVault + ", selected=" + (rooms != null) + ".");
        return rooms != null;
    }

    private static bool TryPrefabRooms(Map map, CellRect bounds, out CellRect footprint, out List<CellRect> rooms)
    {
        footprint = CellRect.Empty;
        rooms = null;
        if (!map.regionAndRoomUpdater.Enabled) return false;
        map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
        var nativeRooms = bounds.ClipInsideMap(map).Select(cell => cell.GetRoom(map)).Where(room => room != null &&
            !room.PsychologicallyOutdoors && !room.TouchesMapEdge).Distinct().ToList();
        var candidates = new List<CellRect>();
        foreach (var room in nativeRooms)
        {
            var cells = room.Cells.ToList();
            if (cells.Count < 25 || cells.Any(cell => !bounds.Contains(cell))) continue;
            var interior = CellRect.FromLimits(cells.Min(cell => cell.x), cells.Min(cell => cell.z), cells.Max(cell => cell.x), cells.Max(cell => cell.z));
            if (interior.Width < 5 || interior.Height < 5) continue;
            // A native Room can wind around partitions and outdoor courts. Its
            // bounding rectangle is usable only when every cell is roofed and
            // belongs to that same room (a native supporting column may fill a
            // hole). Never treat the bounding box of a winding room as a hall.
            if (!interior.All(cell => cell.Roofed(map) &&
                (cell.GetRoom(map) == room || cell.GetEdifice(map)?.def.defName == "Column"))) continue;
            candidates.Add(interior.ExpandedBy(1));
        }
        EESLog.Message("Native prefab rooms: total=" + nativeRooms.Count + ", usable=" + candidates.Count + ", bounds=" + bounds + ".");
        if (candidates.Count < 5 || !RoofSupported(map, bounds)) return false;
        var anchor = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial).FirstOrDefault(thing => bounds.Contains(thing.Position) &&
            (thing.def.defName == "Throne" || thing.def.defName == "GrandThrone" || thing.def.defName == "DankPyon_RoyalThrone" ||
             thing.def.defName == "RoyalBed" || thing.def.defName == "DankPyon_RoyalTudorBed"));
        if (anchor == null) return false;
        var command = candidates.FirstOrDefault(room => room.Contains(anchor.Position));
        if (command.Width < 7) return false;
        // Native indoor rooms may be separate buildings in the same template.
        // Require actual door routes within the complete native footprint.
        var reached = new HashSet<IntVec3>();
        var pending = new Queue<IntVec3>();
        var start = command.ContractedBy(1).FirstOrDefault(cell => cell.Standable(map));
        if (!command.Contains(start) || !start.Standable(map)) return false;
        reached.Add(start); pending.Enqueue(start);
        while (pending.Count > 0)
        {
            var cell = pending.Dequeue();
            foreach (var offset in GenAdj.CardinalDirections)
            {
                var next = cell + offset;
                if (!bounds.Contains(next) || !next.InBounds(map) || reached.Contains(next) ||
                    (!next.Standable(map) && next.GetEdifice(map)?.def.IsDoor != true)) continue;
                reached.Add(next); pending.Enqueue(next);
            }
        }
        candidates = candidates.Where(room => room.ContractedBy(1).Any(reached.Contains)).ToList();
        if (candidates.Count < 5) return false;
        var vault = candidates.Where(room => room != command && room.Width <= 15 && room.Height <= 15 && room.CenterCell.Standable(map) &&
            room.CenterCell.GetEdifice(map) == null && room.ContractedBy(1).Count(cell => cell.Standable(map) && cell.GetEdifice(map) == null &&
                cell.GetFirstItem(map) == null && cell.GetFirstPawn(map) == null) >= 12).OrderByDescending(room => room.Area).FirstOrDefault();
        if (vault.Width < 7) return false;
        var others = candidates.Where(room => room != command && room != vault).OrderByDescending(room => room.Area).Take(3).ToList();
        footprint = bounds.ClipInsideMap(map);
        rooms = new List<CellRect> { command, vault, others[0], others[1], others[2] };
        return true;
    }

    private static bool InteriorCell(Map map, IntVec3 cell)
    {
        if (!cell.GetTerrain(map).affordances.Contains(TerrainAffordanceDefOf.Heavy)) return false;
        var edifice = cell.GetEdifice(map);
        return edifice?.def.building?.isNaturalRock != true && edifice?.def.building?.isResourceRock != true &&
            !StrongholdCenterPlanner.StructuralWall(edifice?.def) && !(edifice?.def.passability == Traversability.Impassable &&
                edifice.def.holdsRoof && edifice.def.size == IntVec2.One);
    }

    private static CellRect Bounds(List<CellRect> rooms)
    {
        return CellRect.FromLimits(rooms.Min(r => r.minX), rooms.Min(r => r.minZ), rooms.Max(r => r.maxX), rooms.Max(r => r.maxZ));
    }

    private static bool RoofSupported(Map map, CellRect rect)
    {
        var supported = new HashSet<IntVec3>();
        foreach (var cell in rect.ExpandedBy(6).ClipInsideMap(map))
        {
            if (cell.GetEdifice(map)?.def.holdsRoof != true) continue;
            foreach (var nearby in GenRadial.RadialCellsAround(cell, 6f, true)) if (rect.Contains(nearby)) supported.Add(nearby);
        }
        return rect.All(cell => cell.GetRoof(map) != RoofDefOf.RoofConstructed || supported.Contains(cell));
    }
}
