using System;
using System.Collections.Generic;
using System.Linq;
using EvolvingEnemyStrongholds;
using HarmonyLib;
using RimWorld;
using Verse;

namespace EESMedievalPatch
{
    internal static class CastleDefenseLayout
    {
        internal const int WallGap = 8;
        internal const int ChamberHalfWidth = 8;

        internal static CellRect SelectTownBounds(Map map, CellRect town, int tier, CellRect required)
        {
            var plan = WorldComponent_Strongholds.Instance?.GetExisting(map.Parent.ID)?.generationPlan;
            if (tier >= 14 && MapGenerator.mapBeingGenerated == map && plan?.centerBuilt == true &&
                !plan.townBuilt && !plan.legacyMap && plan.centerKind == "castle" && plan.mapId == map.uniqueID &&
                CastleContourPlanner.TryCreate(map, town, required, plan)) return plan.castleContourTown;
            CellRect best = town;
            int bestScore = int.MaxValue;
            for (int margin = 0; margin <= 32; margin++)
            {
                CellRect candidate = town.ExpandedBy(margin);
                candidate.ClipInsideRect(new CellRect(9, 9, map.Size.x - 18, map.Size.z - 18));
                // The native town and actual keep are required. Their empty
                // padding can be clipped to leave room for the outer wall.
                if (!candidate.Contains(required.Min) || !candidate.Contains(required.Max)) continue;
                int score = 0;
                CellRect[] rings = Rings(candidate, 4, tier, map.Size);
                var construction = new HashSet<IntVec3>();
                for (int layer = 0; layer < rings.Length; layer++)
                {
                    CellRect ring = rings[layer];
                    construction.UnionWith(ring.EdgeCells.Concat(ring.ContractedBy(1).EdgeCells));
                    for (int side = 0; side < 4; side++)
                        construction.UnionWith(GateCorridor(GateFootprint(ring, side, layer == 1), EdgeRotation(side)));
                }
                if (rings.Length == 2) construction.UnionWith(ChamberWalls(rings[0], rings[1]));
                foreach (IntVec3 cell in construction)
                {
                    if (!cell.InBounds(map)) continue;
                    TerrainDef terrain = cell.GetTerrain(map);
                    if (!terrain.affordances.Contains(TerrainAffordanceDefOf.Heavy) &&
                        !terrain.affordances.Contains(TerrainAffordanceDefOf.Bridgeable) && !terrain.bridge) score++;
                    if (cell.GetThingList(map).Any(thing => MedievalUtility.IsProtectedPerimeterOccupant(thing, map))) score++;
                }
                if (score >= bestScore) continue;
                best = candidate; bestScore = score;
                if (score == 0) break;
            }
            return best;
        }

        internal static bool IsBoulder(ThingDef def) => def?.mineable == true &&
            def.modExtensions?.Any(extension => extension.GetType().FullName == "ReGrowthCore.BoulderProperties") == true;

        internal static Rot4 EdgeRotation(int side)
        {
            switch (side)
            {
                case 0: return Rot4.South;
                case 1: return Rot4.North;
                case 2: return Rot4.West;
                default: return Rot4.East;
            }
        }

        internal static Rot4 CornerRotation(int corner, bool tower)
        {
            // The tower and wall corner sprites use different north/south conventions.
            switch (corner)
            {
                case 0: return tower ? Rot4.North : Rot4.South;
                case 1: return tower ? Rot4.South : Rot4.North;
                case 2: return Rot4.East;
                default: return Rot4.West;
            }
        }

        internal static CellRect WallFootprint(CellRect ring, int side, int along)
        {
            IntVec3 edge = SideCell(ring, side, along);
            IntVec3 inside = SideCell(ring, side, along, 1);
            return CellRect.FromLimits(Math.Min(edge.x, inside.x), Math.Min(edge.z, inside.z),
                Math.Max(edge.x, inside.x), Math.Max(edge.z, inside.z));
        }

        internal static CellRect OpenRing(CellRect settlement, int offset, IntVec3 mapSize)
        {
            CellRect ring = settlement.ExpandedBy(Math.Max(3, offset));
            ring.ClipInsideRect(new CellRect(1, 1, mapSize.x - 2, mapSize.z - 2));
            return ring;
        }

        internal static IEnumerable<IntVec3> OpenRingCells(CellRect ring)
        {
            int centerX = ring.minX + ring.Width / 2;
            int centerZ = ring.minZ + ring.Height / 2;
            foreach (IntVec3 cell in ring.EdgeCells)
            {
                bool entrance = ((cell.x == ring.minX || cell.x == ring.maxX) && Math.Abs(cell.z - centerZ) <= 2) ||
                    ((cell.z == ring.minZ || cell.z == ring.maxZ) && Math.Abs(cell.x - centerX) <= 2);
                if (!entrance) yield return cell;
            }
        }

        internal static CellRect[] Rings(CellRect settlement, int offset, int tier, IntVec3 mapSize)
        {
            CellRect bounds = new CellRect(1, 1, mapSize.x - 2, mapSize.z - 2);
            CellRect inner = tier >= 14 ? settlement : settlement.ExpandedBy(offset);
            inner.ClipInsideRect(bounds);
            CellRect outer = inner.ExpandedBy(WallGap);
            if (tier >= 14 && inner.Width >= 32 && inner.Height >= 32 &&
                bounds.Contains(outer.Min) && bounds.Contains(outer.Max))
            {
                return new[] { inner, outer };
            }
            return new[] { inner };
        }

        internal static int TowerSpacing(int tier)
        {
            return tier >= 16 ? 9 : tier == 15 ? 12 : tier == 14 ? 16 :
                tier >= 11 ? 18 : tier >= 7 ? 25 : 30;
        }

        internal static IEnumerable<int> TowerStarts(int length, int tier)
        {
            if (tier < 6) yield break;
            int center = (length - 1) / 2;
            int clearance = tier >= 11 ? ChamberHalfWidth + 2 : 5;
            int spacing = TowerSpacing(tier);
            for (int along = 3; along + 1 < center - clearance; along += spacing)
            {
                yield return along;
                int mirrored = length - 2 - along;
                if (mirrored > center + clearance && mirrored <= length - 4)
                    yield return mirrored;
            }
        }

        internal static int GateCenter(CellRect ring, int side, bool outer)
        {
            return (side < 2 ? ring.CenterCell.x : ring.CenterCell.z) + (outer ? 4 : 0);
        }

        internal static CellRect GateFootprint(CellRect ring, int side, bool outer)
        {
            return GateFootprintAt(ring, side, GateCenter(ring, side, outer));
        }

        internal static CellRect GateFootprintAt(CellRect ring, int side, int center)
        {
            IntVec3 first = SideCell(ring, side, center - 1);
            IntVec3 last = SideCell(ring, side, center + 1, 1);
            return CellRect.FromLimits(Math.Min(first.x, last.x), Math.Min(first.z, last.z),
                Math.Max(first.x, last.x), Math.Max(first.z, last.z));
        }

        internal static CellRect GateCorridor(CellRect footprint, Rot4 rotation)
        {
            return rotation.IsHorizontal
                ? CellRect.FromLimits(footprint.minX - 2, footprint.minZ, footprint.maxX + 2, footprint.maxZ)
                : CellRect.FromLimits(footprint.minX, footprint.minZ - 2, footprint.maxX, footprint.maxZ + 2);
        }

        internal static IntVec3 SideCell(CellRect ring, int side, int along, int depth = 0)
        {
            switch (side)
            {
                case 0: return new IntVec3(along, 0, ring.minZ + depth);
                case 1: return new IntVec3(along, 0, ring.maxZ - depth);
                case 2: return new IntVec3(ring.minX + depth, 0, along);
                default: return new IntVec3(ring.maxX - depth, 0, along);
            }
        }

        internal static IEnumerable<IntVec3> ChamberWalls(CellRect inner, CellRect outer)
        {
            for (int side = 0; side < 4; side++)
            {
                int center = GateCenter(inner, side, false);
                for (int depth = 1; depth < WallGap; depth++)
                {
                    yield return SideCell(outer, side, center - ChamberHalfWidth, depth);
                    yield return SideCell(outer, side, center + ChamberHalfWidth, depth);
                }
            }
        }
    }

    [HarmonyPatch(typeof(StrongholdCenterPlanner), nameof(StrongholdCenterPlanner.IncludeCenter))]
    internal static class MedievalCastleTownBoundsPatch
    {
        private static void Postfix(CellRect town, StrongholdGenerationPlan plan, ref CellRect __result)
        {
            Map map = MapGenerator.mapBeingGenerated;
            if (map == null || plan?.centerBuilt != true || plan.legacyMap || plan.centerKind != "castle" ||
                plan.mapId != map.uniqueID || !MedievalUtility.IsSupportedHostileSettlement(map)) return;
            CellRect required = plan.centerRect.ExpandedBy(4);
            if (town.Width > 1 && town.Height > 1)
                required = CellRect.FromLimits(Math.Min(town.minX, required.minX), Math.Min(town.minZ, required.minZ),
                    Math.Max(town.maxX, required.maxX), Math.Max(town.maxZ, required.maxZ));
            __result = CastleDefenseLayout.SelectTownBounds(map, __result, plan.tier, required);
        }
    }
}
