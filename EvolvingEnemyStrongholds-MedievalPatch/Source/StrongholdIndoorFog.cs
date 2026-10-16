using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using EvolvingEnemyStrongholds;

namespace EESMedievalPatch
{
    internal static class StrongholdIndoorFog
    {
        internal static bool Applies(Map map)
        {
            return MedievalUtility.IsSupportedHostileSettlement(map) &&
                map.GetComponent<MapComponent_StrongholdSiege>()?.eesGenerated == true;
        }

        internal static bool IsInteriorSpace(Map map, IntVec3 cell,
            Dictionary<Room, bool> indoorRooms = null)
        {
            if (!cell.InBounds(map) || !cell.Roofed(map)) return false;
            Building edifice = cell.GetEdifice(map);
            if (edifice != null && (edifice.def.IsDoor || edifice.def.MakeFog)) return false;
            Room room = cell.GetRoom(map);
            if (room == null) return false;
            if (indoorRooms != null && indoorRooms.TryGetValue(room, out bool indoor)) return indoor;
            bool result = !room.IsDoorway && !room.TouchesMapEdge && !room.UsesOutdoorTemperature;
            if (indoorRooms != null) indoorRooms[room] = result;
            return result;
        }

        private static CellRect ImportantBounds(Map map)
        {
            StrongholdGenerationPlan plan = WorldComponent_Strongholds.Instance?.GetExisting(map.Parent.ID)?.generationPlan;
            if (plan?.centerBuilt == true) return plan.centerRect.ClipInsideMap(map);
            // Legacy generated maps without a center plan keep only their vault concealed.
            return (map.GetComponent<MapComponent_StrongholdSiege>()?.eesVaultRect ?? CellRect.Empty).ClipInsideMap(map);
        }

        internal static int RevealExterior(Map map)
        {
            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();
            var indoorRooms = new Dictionary<Room, bool>();
            CellRect important = ImportantBounds(map);
            int revealed = 0;
            foreach (IntVec3 cell in map.AllCells)
            {
                if (!map.fogGrid.IsFogged(cell)) continue;
                // Solid natural rock keeps RimWorld's exploration state.
                // It is not an outdoor space or a constructed wall.
                if (cell.GetEdifice(map)?.def.building?.isNaturalRock == true) continue;
                if (important.Contains(cell) && IsInteriorSpace(map, cell, indoorRooms)) continue;
                map.fogGrid.Unfog(cell);
                revealed++;
            }
            return revealed;
        }

        internal static int RefogRooms(Map map, CellRect bounds)
        {
            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();
            var indoorRooms = new Dictionary<Room, bool>();
            var visibleCells = new HashSet<IntVec3>();
            var occupiedRooms = new HashSet<Room>();
            CellRect important = ImportantBounds(map);
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Faction != Faction.OfPlayer && pawn.HostFaction != Faction.OfPlayer) continue;
                Room room = pawn.Position.GetRoom(map);
                if (room != null) occupiedRooms.Add(room);
                foreach (IntVec3 cell in CellRect.CenteredOn(pawn.Position, GravshipReinforceUtility.GravshipVisionRadius))
                    visibleCells.Add(cell);
            }

            int fogged = 0;
            foreach (IntVec3 cell in bounds.ClipInsideMap(map))
            {
                if (!important.Contains(cell) || !IsInteriorSpace(map, cell, indoorRooms)) continue;
                if (visibleCells.Contains(cell) || occupiedRooms.Contains(cell.GetRoom(map)) ||
                    cell.GetTerrain(map)?.IsSubstructure == true)
                {
                    if (map.fogGrid.IsFogged(cell)) map.fogGrid.Unfog(cell);
                    continue;
                }
                map.fogGrid.Refog(new CellRect(cell.x, cell.z, 1, 1));
                fogged++;
            }
            int revealed = RevealExterior(map);
            Log.Message($"[EES Medieval Patch] Indoor-only refog: map={map.uniqueID}, indoorCells={fogged}, exteriorAndWallCellsRevealed={revealed}.");
            return fogged;
        }
    }

    [HarmonyPatch(typeof(GravshipReinforceUtility), nameof(GravshipReinforceUtility.HideBaseInterior))]
    internal static class MedievalIndoorGravshipFogPatch
    {
        private static bool Prefix(Map map, CellRect rect)
        {
            if (!StrongholdIndoorFog.Applies(map)) return true;
            StrongholdIndoorFog.RefogRooms(map, rect);
            return false;
        }
    }

    [HarmonyPatch(typeof(Map), nameof(Map.FinalizeInit))]
    internal static class MedievalIndoorFogLoadPatch
    {
        private static void Postfix(Map __instance)
        {
            if (StrongholdIndoorFog.Applies(__instance))
                StrongholdIndoorFog.RevealExterior(__instance);
        }
    }

    [HarmonyPatch(typeof(SectionLayer_FogOfWar), nameof(SectionLayer_FogOfWar.Regenerate))]
    internal static class MedievalIndoorFogMeshPatch
    {
        private static void Postfix(SectionLayer_FogOfWar __instance, Section ___section)
        {
            Map map = ___section.map;
            if (!StrongholdIndoorFog.Applies(map)) return;
            LayerSubMesh mesh = __instance.GetSubMesh(MatBases.FogOfWar);
            CellRect cells = ___section.CellRect;
            if (mesh.disabled || mesh.colors.Count != cells.Area * 9) return;

            // The native feathering also colors neighboring unfogged cells.
            // Keep it within fogged cells so walls and outdoor space stay visible.
            bool changed = false;
            int vertex = 0;
            for (int x = cells.minX; x <= cells.maxX; x++)
                for (int z = cells.minZ; z <= cells.maxZ; z++, vertex += 9)
                {
                    if (map.fogGrid.IsFogged(new IntVec3(x, 0, z))) continue;
                    for (int i = 0; i < 9; i++)
                    {
                        UnityEngine.Color32 color = mesh.colors[vertex + i];
                        if (color.a == 0) continue;
                        color.a = 0;
                        mesh.colors[vertex + i] = color;
                        changed = true;
                    }
                }
            if (changed) mesh.mesh.SetColors(mesh.colors);
        }
    }
}
