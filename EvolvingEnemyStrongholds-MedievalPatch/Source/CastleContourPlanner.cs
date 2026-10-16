using System;
using System.Collections.Generic;
using System.Linq;
using EvolvingEnemyStrongholds;
using RimWorld;
using Verse;

namespace EESMedievalPatch;

// Deterministic construction planning: exact 2x2 pads remain outside the full
// town, with four straight paired native gates and a six-cell clear wall gap.
// A failed search is explicit and never permission to wipe protected contents.
internal static class CastleContourPlanner
{
    internal static bool HasPlan(StrongholdGenerationPlan plan, Map map) => plan?.castleContourVersion == 1 &&
        !plan.legacyMap && plan.mapId == map.uniqueID && plan.castleGateCenters?.Count == 4 &&
        plan.castleInnerPads?.Count > 4 && plan.castleOuterPads?.Count > 4;
    internal static CellRect Pad(IntVec3 p) => CellRect.FromLimits(p.x, p.z, p.x + 1, p.z + 1);
    internal static IEnumerable<IntVec3> Cells(IEnumerable<IntVec3> pads) => pads.SelectMany(p => Pad(p).Cells).Distinct();
    internal static CellRect Gate(StrongholdGenerationPlan plan, int layer, int side)
    {
        var r = layer == 0 ? plan.castleContourNominal : plan.castleContourNominal.ExpandedBy(8);
        int center = plan.castleGateCenters[side] + layer * 4;
        return From(CastleDefenseLayout.SideCell(r, side, center - 1), CastleDefenseLayout.SideCell(r, side, center + 1, 1));
    }
    private static CellRect From(IntVec3 a, IntVec3 b) => CellRect.FromLimits(Math.Min(a.x, b.x), Math.Min(a.z, b.z), Math.Max(a.x, b.x), Math.Max(a.z, b.z));
    internal static IEnumerable<IntVec3> ChamberWalls(StrongholdGenerationPlan plan)
    {
        var outer = plan.castleContourNominal.ExpandedBy(8);
        for (int side = 0; side < 4; side++)
        for (int depth = 1; depth < 8; depth++)
        foreach (int offset in new[] { -8, 8 })
            yield return CastleDefenseLayout.SideCell(outer, side, plan.castleGateCenters[side] + offset, depth);
    }
    internal static IEnumerable<IntVec3> ChamberPassage(StrongholdGenerationPlan plan)
    {
        var outer = plan.castleContourNominal.ExpandedBy(8);
        for (int side = 0; side < 4; side++)
        for (int depth = 2; depth < 8; depth++)
        for (int offset = -1; offset <= 5; offset++)
            yield return CastleDefenseLayout.SideCell(outer, side, plan.castleGateCenters[side] + offset, depth);
    }

    internal static bool TryCreate(Map map, CellRect desired, CellRect required, StrongholdGenerationPlan plan)
    {
        if (HasPlan(plan, map)) return true;
        var town = From(new IntVec3(Math.Min(desired.minX, required.minX), 0, Math.Min(desired.minZ, required.minZ)),
            new IntVec3(Math.Max(desired.maxX, required.maxX), 0, Math.Max(desired.maxZ, required.maxZ)));
        var blocked = new bool[map.Size.x, map.Size.z];
        var passageBlocked = new bool[map.Size.x, map.Size.z];
        bool footing = DefDatabase<TerrainDef>.GetNamedSilentFail("EES_MedievalCastleFoundation") != null;
        foreach (var cell in map.AllCells)
        {
            var terrain = cell.GetTerrain(map);
            blocked[cell.x, cell.z] = terrain == null ||
                !terrain.affordances.Contains(TerrainAffordanceDefOf.Heavy) && (!footing || terrain.IsIce) ||
                cell.GetThingList(map).Any(t => MedievalUtility.IsProtectedPerimeterOccupant(t, map));
            passageBlocked[cell.x, cell.z] = blocked[cell.x, cell.z] || !MedievalUtility.CanPrepareCastlePassageCell(map, cell);
        }
        var solver = new Solver(map, town, new Prefix(blocked), new Prefix(passageBlocked));
        int attempts = 0;
        // Four lattice origins are material: a valid 2-thick corridor can be
        // blocked on one parity while the same actual objects permit another.
        for (int px = 0; px < 2; px++)
        for (int pz = 0; pz < 2; pz++)
        for (int margin = 3; margin <= Math.Max(map.Size.x, map.Size.z) / 3; margin++)
        {
            int a = Math.Max(10 + px, Down(town.minX - margin, px));
            int b = Math.Max(10 + pz, Down(town.minZ - margin, pz));
            int c = Math.Min(Down(map.Size.x - 12, px) + 1, Down(town.maxX + margin, px) + 1);
            int d = Math.Min(Down(map.Size.z - 12, pz) + 1, Down(town.maxZ + margin, pz) + 1);
            var nominal = CellRect.FromLimits(a, b, c, d);
            if (a >= town.minX || b >= town.minZ || c <= town.maxX || d <= town.maxZ) continue;
            attempts++;
            var centers = new int[4]; bool found = true;
            for (int side = 0; side < 4; side++)
            {
                int low = side < 2 ? a : b, high = side < 2 ? c : d;
                var choices = new List<int>();
                for (int n = low + 22; n < high - 22; n += 2)
                    if (solver.Free(GateArea(nominal, side, n)) && solver.GatePassageClear(nominal, side, n) &&
                        Strip(nominal, side, n, 18).All(p => solver.Free(Pad(p)))) choices.Add(n);
                if (choices.Count == 0) { found = false; break; }
                centers[side] = choices.OrderBy(n => Math.Abs(n - (low + high) / 2)).ThenBy(n => n).First();
            }
            if (!found) continue;
            var inner = solver.Loop(nominal, centers, null, new CellRect(10, 10, map.Size.x - 20, map.Size.z - 20));
            if (inner == null) continue;
            var innerArea = Enclosed(map, inner);
            if (!town.All(p => innerArea[p.x, p.z])) continue;
            var outer = solver.Loop(nominal.ExpandedBy(8), centers, new Prefix(innerArea), new CellRect(0, 0, map.Size.x, map.Size.z));
            if (outer == null) continue;
            var outerArea = Enclosed(map, outer);
            if (!town.All(p => outerArea[p.x, p.z])) continue;
            plan.castleContourVersion = 1; plan.castleContourTown = town; plan.castleContourNominal = nominal;
            plan.castleInnerPads = inner; plan.castleOuterPads = outer; plan.castleGateCenters = centers.ToList();
            Log.Message($"[EES Castle Contour] planned map={map.uniqueID},town={town},nominal={nominal},parity={px}/{pz},margin={margin},attempts={attempts},pads={inner.Count}/{outer.Count},centers={string.Join(",", centers)}; construction/physical passage still require validation.");
            return true;
        }
        Log.Warning($"[EES Castle Contour] No complete protected-object/terrain-compatible double contour found in {attempts} deterministic candidate frames for full town {town}; retaining explicit existing perimeter fallback, full double-wall requirement is not met.");
        return false;
    }
    private static int Down(int value, int parity) => (int)Math.Floor((value - parity) / 2d) * 2 + parity;
    private static IEnumerable<IntVec3> Strip(CellRect r, int side, int center, int half)
    {
        if (side == 0 || side == 3)
            for (int n = center - half; n < center + half + 3; n += 2)
                yield return side == 0 ? new IntVec3(n, 0, r.minZ) : new IntVec3(r.maxX - 1, 0, n);
        else
            for (int n = center + half + 2; n >= center - half; n -= 2)
                yield return side == 1 ? new IntVec3(n, 0, r.maxZ - 1) : new IntVec3(r.minX, 0, n);
    }
    private static CellRect GateArea(CellRect r, int side, int n) => side switch
    {
        0 => CellRect.FromLimits(n - 10, r.minZ - 10, n + 13, r.minZ + 3),
        1 => CellRect.FromLimits(n - 10, r.maxZ - 3, n + 13, r.maxZ + 10),
        2 => CellRect.FromLimits(r.minX - 10, n - 10, r.minX + 3, n + 13),
        _ => CellRect.FromLimits(r.maxX - 3, n - 10, r.maxX + 10, n + 13)
    };

    internal static bool[,] Enclosed(Map map, IEnumerable<IntVec3> pads)
    {
        var wall = new bool[map.Size.x, map.Size.z];
        foreach (var p in Cells(pads)) if (p.InBounds(map)) wall[p.x, p.z] = true;
        var outside = new bool[map.Size.x, map.Size.z]; var queue = new Queue<IntVec3>();
        foreach (var cell in new CellRect(0, 0, map.Size.x, map.Size.z).EdgeCells)
            if (!wall[cell.x, cell.z] && !outside[cell.x, cell.z]) { outside[cell.x, cell.z] = true; queue.Enqueue(cell); }
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            foreach (var offset in GenAdj.CardinalDirections)
            {
                var c = p + offset;
                if (!c.InBounds(map) || wall[c.x, c.z] || outside[c.x, c.z]) continue;
                outside[c.x, c.z] = true; queue.Enqueue(c);
            }
        }
        foreach (var cell in map.AllCells) outside[cell.x, cell.z] = !outside[cell.x, cell.z];
        return outside;
    }

    internal static List<IntVec3> ExteriorCells(Map map, StrongholdGenerationPlan plan, int distance)
    {
        var inside = Enclosed(map, plan.castleOuterPads);
        var result = new HashSet<IntVec3>();
        foreach (var p in plan.castleOuterPads)
        foreach (var offset in GenAdj.CardinalDirections)
        {
            var probe = p + new IntVec3(offset.x > 0 ? 2 : offset.x < 0 ? -1 : 0, 0, offset.z > 0 ? 2 : offset.z < 0 ? -1 : 0);
            if (!probe.InBounds(map) || inside[probe.x, probe.z]) continue;
            var c = probe + offset * Math.Max(0, distance - 1);
            if (c.InBounds(map) && !inside[c.x, c.z]) result.Add(c);
        }
        return result.OrderBy(p => p.z).ThenBy(p => p.x).ToList();
    }

    private sealed class Prefix
    {
        private readonly int[,] sums;
        internal Prefix(bool[,] cells)
        {
            sums = new int[cells.GetLength(0) + 1, cells.GetLength(1) + 1];
            for (int z = 0; z < cells.GetLength(1); z++)
            {
                int n = 0;
                for (int x = 0; x < cells.GetLength(0); x++) { if (cells[x, z]) n++; sums[x + 1, z + 1] = sums[x + 1, z] + n; }
            }
        }
        internal int Count(CellRect r)
        {
            int a = Math.Max(0, r.minX), b = Math.Max(0, r.minZ), c = Math.Min(sums.GetLength(0) - 2, r.maxX), d = Math.Min(sums.GetLength(1) - 2, r.maxZ);
            return a > c || b > d ? 0 : sums[c + 1, d + 1] - sums[a, d + 1] - sums[c + 1, b] + sums[a, b];
        }
    }
    private sealed class Solver
    {
        private readonly Map map; private readonly CellRect town; private readonly Prefix blocked; private readonly Prefix passageBlocked;
        internal Solver(Map map, CellRect town, Prefix blocked, Prefix passageBlocked)
        { this.map = map; this.town = town; this.blocked = blocked; this.passageBlocked = passageBlocked; }
        internal bool Free(CellRect r) => r.Min.InBounds(map) && r.Max.InBounds(map) && blocked.Count(r) == 0;
        internal bool GatePassageClear(CellRect nominal, int side, int center)
        {
            bool Passage(CellRect r) => r.Min.InBounds(map) && r.Max.InBounds(map) && passageBlocked.Count(r) == 0;
            var outer = nominal.ExpandedBy(8);
            for (int layer = 0; layer < 2; layer++)
            {
                var ring = layer == 0 ? nominal : outer;
                int along = center + layer * 4;
                var gate = From(CastleDefenseLayout.SideCell(ring, side, along - 1), CastleDefenseLayout.SideCell(ring, side, along + 1, 1));
                if (!Passage(CastleDefenseLayout.GateCorridor(gate, CastleDefenseLayout.EdgeRotation(side)))) return false;
            }
            return Passage(From(CastleDefenseLayout.SideCell(outer, side, center - 1, 2),
                CastleDefenseLayout.SideCell(outer, side, center + 5, 7)));
        }
        internal List<IntVec3> Loop(CellRect nominal, int[] centers, Prefix inner, CellRect bounds)
        {
            var sides = new[] { 0, 3, 1, 2 };
            var segments = sides.Select(side => Strip(nominal, side, centers[side], inner == null ? 18 : 10).ToList()).ToArray();
            var result = new List<IntVec3>();
            for (int i = 0; i < 4; i++)
            {
                var segment = segments[i];
                if (segment.Any(p => !Free(Pad(p)) || inner != null && inner.Count(Pad(p).ExpandedBy(6)) > 0)) return null;
                result.AddRange(segment);
                var route = Path(segment.Last(), segments[(i + 1) % 4].First(), i, inner, bounds, nominal, centers);
                if (route == null) return null;
                result.AddRange(route.Skip(1).Take(route.Count - 2));
            }
            if (result.Distinct().Count() != result.Count || result.Where((p, i) => Manhattan(p, result[(i + 1) % result.Count]) != 2).Any()) return null;
            return result;
        }
        private List<IntVec3> Path(IntVec3 start, IntVec3 end, int quadrant, Prefix inner, CellRect bounds, CellRect nominal, int[] centers)
        {
            bool Allowed(IntVec3 p)
            {
                var pad = Pad(p);
                if (!bounds.Contains(pad.Min) || !bounds.Contains(pad.Max) || pad.Overlaps(town) || !Free(pad)) return false;
                if (quadrant == 0 && (p.x < start.x || p.z > end.z) || quadrant == 1 && (p.z < start.z || p.x < end.x) ||
                    quadrant == 2 && (p.x > start.x || p.z < end.z) || quadrant == 3 && (p.z > start.z || p.x > end.x)) return false;
                if (inner != null) return inner.Count(pad.ExpandedBy(6)) == 0;
                for (int side = 0; side < 4; side++)
                {
                    if (!pad.Overlaps(GateArea(nominal, side, centers[side]))) continue;
                    if (side == 0 && p.z < nominal.minZ || side == 1 && p.z > nominal.maxZ - 1 ||
                        side == 2 && p.x < nominal.minX || side == 3 && p.x > nominal.maxX - 1) return false;
                }
                return true;
            }
            if (!Allowed(start) || !Allowed(end)) return null;
            var costs = new Dictionary<IntVec3, int> { [start] = 0 }; var previous = new Dictionary<IntVec3, IntVec3>();
            int order = 0;
            var open = new SortedSet<(int F, int G, int Order, IntVec3 P)>(Comparer<(int F, int G, int Order, IntVec3 P)>.Create((a, b) =>
                a.F != b.F ? a.F.CompareTo(b.F) : a.G != b.G ? a.G.CompareTo(b.G) : a.Order.CompareTo(b.Order)));
            open.Add((Manhattan(start, end) / 2, 0, order++, start));
            var offsets = new[] { IntVec3.East * 2, IntVec3.North * 2, IntVec3.West * 2, IntVec3.South * 2 };
            while (open.Count > 0)
            {
                var item = open.Min; open.Remove(item);
                if (costs[item.P] != item.G) continue;
                if (item.P == end)
                {
                    var route = new List<IntVec3> { end }; var p = end;
                    while (p != start) { p = previous[p]; route.Add(p); }
                    route.Reverse(); return route;
                }
                foreach (var offset in offsets)
                {
                    var p = item.P + offset; int cost = item.G + 1;
                    if (!Allowed(p) || costs.TryGetValue(p, out int old) && old <= cost) continue;
                    costs[p] = cost; previous[p] = item.P; open.Add((cost + Manhattan(p, end) / 2, cost, order++, p));
                }
            }
            return null;
        }
        private static int Manhattan(IntVec3 a, IntVec3 b) => Math.Abs(a.x - b.x) + Math.Abs(a.z - b.z);
    }
}
