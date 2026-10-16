using System;
using System.Collections.Generic;
using System.Linq;
using EvolvingEnemyStrongholds;
using RimWorld;
using Verse;

namespace EESMedievalPatch;

internal static partial class MedievalUtility
{
    private static bool HasCastleContour(Map map, StrongholdData data) => map != null &&
        CastleContourPlanner.HasPlan(data?.generationPlan, map);

    private static bool BuildCastleContour(Map map, StrongholdData data)
    {
        if (!HasCastleContour(map, data)) return false;
        var plan = data.generationPlan; var faction = map.ParentFaction;
        var gateDef = CastleGate();
        if (gateDef == null)
        {
            Log.Error("[EES Castle Contour] Saved new-generation contour lacks the native 3x2 castle gate definition; full gate requirement is not met.");
            return true; // Handled: never run destructive rectangular Core fallback.
        }
        if (!ConstructionContext(map)) return true;
        var wall = DefDatabase<ThingDef>.GetNamedSilentFail("JDS_WallsTwo");
        var corner = DefDatabase<ThingDef>.GetNamedSilentFail("JDS_L_Defense_Tower");
        var connector = DefDatabase<ThingDef>.GetNamedSilentFail("JDS_Walls_L_connector");
        var straight = DefDatabase<ThingDef>.GetNamedSilentFail("JDS_I_Defense_Tower");
        var support = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_CastleWall") ?? ThingDefOf.Wall;
        var granite = ThingDefOf.BlocksGranite;
        var allGateCells = new HashSet<IntVec3>();
        var passages = new HashSet<IntVec3>();
        for (int layer = 0; layer < 2; layer++)
        for (int side = 0; side < 4; side++)
        {
            var footprint = CastleContourPlanner.Gate(plan, layer, side);
            allGateCells.UnionWith(footprint);
            passages.UnionWith(CastleDefenseLayout.GateCorridor(footprint, CastleDefenseLayout.EdgeRotation(side)));
            var approaches = passages.Where(c => c.InBounds(map) && !allGateCells.Contains(c)).ToArray();
            ClearWallCells(map, approaches);
            RelocateLooseDefenseBlockers(map, approaches);
            foreach (var c in CastleDefenseLayout.GateCorridor(footprint, CastleDefenseLayout.EdgeRotation(side)))
                SupportCastleEdgeCell(map, c, support);
            if (!footprint.All(c => c.GetThingList(map).Any(t => t.def == gateDef)))
            {
                ClearWallCells(map, footprint);
                if (!TryPlaceCastleFootprint(map, faction, gateDef, granite, footprint, CastleDefenseLayout.EdgeRotation(side)))
                    Log.Error($"[EES Castle Contour] Native gate placement failed layer={layer},side={side},footprint={footprint}; original objects retained, no ordinary-door substitution.");
            }
        }
        int failures = 0, pieces = 0, turns = 0, towers = 0;
        foreach (var pads in new[] { plan.castleInnerPads, plan.castleOuterPads })
        {
            var enclosed = CastleContourPlanner.Enclosed(map, pads);
            for (int i = 0; i < pads.Count; i++)
            {
                var p = pads[i]; var footprint = CastleContourPlanner.Pad(p);
                var before = pads[(i + pads.Count - 1) % pads.Count] - p;
                var after = pads[(i + 1) % pads.Count] - p;
                bool turn = before.x != -after.x || before.z != -after.z;
                bool horizontal = before.x != 0;
                // An existing rock or loose vanilla wall blocks movement, but
                // does not satisfy construction of an actual CWE wall piece.
                if (!turn && footprint.All(c => HasContourPiece(map, c, faction))) continue;
                Rot4 rotation;
                if (turn)
                {
                    bool east = before.x > 0 || after.x > 0, north = before.z > 0 || after.z > 0;
                    int index = north ? (east ? 0 : 1) : (east ? 2 : 3);
                    rotation = CastleDefenseLayout.CornerRotation(index, true);
                    if (footprint.SelectMany(c => c.GetThingList(map)).Distinct().Any(t =>
                        t.Faction == faction && t.OccupiedRect() == footprint &&
                        (t.def == corner && t.Rotation == rotation ||
                         t.def == connector && t.Rotation == CornerWallRotation(before, after)))) continue;
                }
                else
                {
                    var probe = horizontal ? p + IntVec3.South : p + IntVec3.West;
                    bool lowerOutside = probe.InBounds(map) && !enclosed[probe.x, probe.z];
                    rotation = horizontal ? (lowerOutside ? Rot4.South : Rot4.North) : (lowerOutside ? Rot4.West : Rot4.East);
                }
                foreach (var cell in footprint) SupportCastleEdgeCell(map, cell, support);
                if (footprint.Any(c => passages.Contains(c)))
                {
                    // A three-wide gate can occupy half a 2x2 pad. Keep its
                    // exact six cells and place only complete remaining native
                    // 1x2 end-wall columns, never overwrite the gate or approach.
                    foreach (var segment in Split(footprint, horizontal))
                    {
                        if (segment.Any(c => passages.Contains(c))) continue;
                        if (segment.All(c => HasContourPiece(map, c, faction))) continue;
                        ClearWallCells(map, segment);
                        if (TryPlaceCastleFootprint(map, faction, wall, granite, segment, rotation)) pieces++; else failures++;
                    }
                    continue;
                }
                ClearWallCells(map, footprint);
                var piece = turn ? corner ?? connector : i % Math.Max(3, CastleDefenseLayout.TowerSpacing(data.tier) / 2) == 0 ? straight : null;
                if (piece != null)
                {
                    var actualRotation = turn && piece == connector ? CornerWallRotation(before, after) : rotation;
                    if (TryPlaceCastleFootprint(map, faction, piece, granite, footprint, actualRotation))
                    { pieces++; if (turn) turns++; if (piece == corner || piece == straight) towers++; continue; }
                }
                if (turn && connector != null && TryPlaceCastleFootprint(map, faction, connector, granite, footprint, CornerWallRotation(before, after)))
                { pieces++; turns++; continue; }
                foreach (var segment in Split(footprint, horizontal))
                    if (TryPlaceCastleFootprint(map, faction, wall, granite, segment, rotation)) pieces++; else failures++;
            }
        }
        foreach (var gate in map.listerThings.AllThings.OfType<Building_Door>().Where(g => g.def == gateDef &&
                     Enumerable.Range(0, 2).Any(layer => Enumerable.Range(0, 4).Any(side => g.OccupiedRect() == CastleContourPlanner.Gate(plan, layer, side)))).ToList())
            if (!EnsureCastleGateSupportWalls(map, faction, gate, support)) failures++;

        var chamber = CastleContourPlanner.ChamberWalls(plan).Distinct().ToArray();
        // Keep completed support/chamber walls on a second construction pass.
        // The ordinary loose-blocker relocation policy must not move our own
        // completed walls out of the contour when the barbican overlaps it.
        var unclearedChamber = chamber.Where(c => !HasContourPiece(map, c, faction)).ToArray();
        if (unclearedChamber.Length > 0) ClearWallCells(map, unclearedChamber);
        foreach (var cell in chamber)
        {
            if (HasContourPiece(map, cell, faction)) continue;
            SupportCastleEdgeCell(map, cell, support);
            if (!TryPlaceDefenseBuilding(map, faction, support, granite, cell)) failures++;
        }
        var passage = CastleContourPlanner.ChamberPassage(plan).Except(allGateCells).Distinct().ToArray();
        ClearWallCells(map, passage);
        RelocateLooseDefenseBlockers(map, passage);
        foreach (var cell in passage) SupportCastleEdgeCell(map, cell, support);
        var missingCells = CastleContourPlanner.Cells(plan.castleInnerPads.Concat(plan.castleOuterPads))
            .Where(c => !HasOuterRingBuilding(map, c)).ToArray();
        int missing = missingCells.Length;
        int gateCount = Enumerable.Range(0, 2).Sum(layer => Enumerable.Range(0, 4).Count(side =>
            CastleContourPlanner.Gate(plan, layer, side).All(c => c.GetThingList(map).Any(t => t.def == gateDef))));
        Log.Message($"[EES Castle Contour] constructed map={map.uniqueID},pieces={pieces},newTurns={turns},newTowers={towers},placementFailures={failures},missingPadCells={missing},nativeGates={gateCount}/8; complete closure and actual routes require independent audit.");
        if (failures > 0 || missing > 0 || gateCount != 8) Log.Error("[EES Castle Contour] Incomplete perimeter; full double-wall/native-gate requirement is not met. Missing cells=" +
            string.Join(";", missingCells.Select(c => c + ":" + string.Join(",", c.GetThingList(map).Select(t => t.ThingID)))));
        return true;
    }
    private static bool HasContourPiece(Map map, IntVec3 cell, Faction faction) =>
        cell.GetEdifice(map) is Building building && building.Faction == faction &&
        building.def.defName is "JDS_WallsTwo" or "JDS_Walls_L_connector" or "JDS_I_Defense_Tower" or
            "JDS_L_Defense_Tower" or "JDS_Castle_Gate" or "DankPyon_CastleWall";
    private static IEnumerable<CellRect> Split(CellRect pad, bool horizontal)
    {
        if (horizontal)
        {
            yield return CellRect.FromLimits(pad.minX, pad.minZ, pad.minX, pad.maxZ);
            yield return CellRect.FromLimits(pad.maxX, pad.minZ, pad.maxX, pad.maxZ);
        }
        else
        {
            yield return CellRect.FromLimits(pad.minX, pad.minZ, pad.maxX, pad.minZ);
            yield return CellRect.FromLimits(pad.minX, pad.maxZ, pad.maxX, pad.maxZ);
        }
    }
    private static Rot4 CornerWallRotation(IntVec3 before, IntVec3 after)
    {
        bool east = before.x > 0 || after.x > 0, north = before.z > 0 || after.z > 0;
        return CastleDefenseLayout.CornerRotation(north ? (east ? 0 : 1) : (east ? 2 : 3), false);
    }
}
