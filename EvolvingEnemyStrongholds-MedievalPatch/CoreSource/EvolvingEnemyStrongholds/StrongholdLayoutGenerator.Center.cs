using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public static partial class StrongholdLayoutGenerator
{
    internal static bool TryFindReusableNativeCenter(Map map, StrongholdData data, CellRect town,
        StrongholdGenerationPlan plan, out CellRect footprint, out List<CellRect> rooms)
    {
        footprint = CellRect.Empty;
        rooms = null;
        return !plan.centerStyle.EndsWith("Alternative", StringComparison.Ordinal) &&
            data.tier >= 10 && !HasLivePrisoner(data) && !data.hasPrison && !RoleHas(data.roleDefName, "prison") &&
            plan.stage == "native-center-pending" &&
            StrongholdNativeCenter.TryFind(map, town, plan.nativeBounds, out footprint, out rooms);
    }

    public static bool TryBuildUnifiedCenter(Map map, StrongholdData data, CellRect town)
    {
        if (!StrongholdCenterPlanner.Eligible(map, data)) return false;
        var plan = StrongholdGenerationPlan.GetOrCreate(map, data);
        if (plan.legacyMap) return false;
        if (plan.centerBuilt) return true;
        if (plan.nativeTemplate != null)
            EESLog.Message("Native center preflight: template=" + plan.nativeTemplate + ", stage=" + plan.stage + ", tier=" + data.tier +
                ", prison=" + data.hasPrison + ", liveCaptives=" + HasLivePrisoner(data) + ".");
        List<CellRect> nativeRooms = null;
        if (TryFindReusableNativeCenter(map, data, town, plan, out var nativeFootprint, out nativeRooms))
        {
            plan.centerRect = nativeFootprint;
            plan.centerReserved = true;
            plan.nativeCenterReused = true;
            plan.placementNote = "native-castle-reused";
            plan.tier = data.tier;
            plan.roleDefName = data.roleDefName;
        }
        else if (!StrongholdCenterPlanner.Prepare(map, data, town)) return false;
        var siege = map.GetComponent<MapComponent_StrongholdSiege>();
        var faction = map.ParentFaction;
        siege.centerBuilding = true;
        try
        {
            if (!plan.nativeCenterReused)
            {
                foreach (var item in plan.centerRect.SelectMany(c => c.GetThingList(map)).Where(t => t.def.category == ThingCategory.Item && t is not Plant).Distinct().ToList())
                {
                    if (!StrongholdCenterPlanner.FindOutsideCell(map, item.def, item.Rotation, item.Position, plan.centerRect, out var safe)) continue;
                    var original = item.Position;
                    item.DeSpawn();
                    try { GenSpawn.Spawn(item, safe, map, item.Rotation); }
                    catch { if (!item.Spawned) GenSpawn.Spawn(item, original, map, item.Rotation); throw; }
                }
                for (var attempt = 0; attempt < 2 && CenterOccupied(map, plan.centerRect); attempt++)
                {
                    plan.centerReserved = false;
                    plan.centerRect = CellRect.Empty;
                    plan.placementNote = "replanned-after-native-occupancy";
                    if (!StrongholdCenterPlanner.Prepare(map, data, town)) return false;
                }
                if (CenterOccupied(map, plan.centerRect))
                {
                    plan.stage = "center-deferred-occupied";
                    EESLog.Warning("Unified center deferred because the safe replacement footprint is occupied; existing objects retained.");
                    return false;
                }
            }
            int downgradedNativeFurniture = ReplaceLowTierNativeFurniture(map, data.tier, town);
            CaptureCenterLoot(map, plan, town);
            plan.stage = "center-building";
            StrongholdDefenseUtility.ResolveWall(map, faction, data.ContentTier, out var wall, out var stuff);
            var open = plan.centerKind == "clanHall";
            if (open) { wall = ThingDefOf.Wall; stuff = ThingDefOf.WoodLog; }
            wall ??= ThingDefOf.Wall;
            stuff ??= ThingDefOf.BlocksGranite;
            var door = ThingDefOf.Door;
            var doorStuff = StuffOrDefault(door, stuff);
            var floor = DefDatabase<TerrainDef>.GetNamedSilentFail(open ? "WoodPlankFloor" : "TileGranite") ??
                DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor");
            if (!open) floor = StrongholdCultureCenters.FloorFor(faction, floor);
            data.ClearSealedRects();
            (CellRect Command, CellRect Vault, CellRect Residence, CellRect Utility, CellRect Role) rooms;
            if (plan.nativeCenterReused)
            {
                rooms = (nativeRooms[0], nativeRooms[1], nativeRooms[2], nativeRooms[3], nativeRooms[4]);
                data.commandRoomCenter = rooms.Command.ContractedBy(1).Where(cell => cell.Standable(map) && cell.GetEdifice(map) == null && cell.GetFirstPawn(map) == null)
                    .OrderBy(cell => (cell - rooms.Command.CenterCell).LengthHorizontalSquared).DefaultIfEmpty(IntVec3.Invalid).First();
                if (!data.commandRoomCenter.IsValid) throw new InvalidOperationException("Native castle has no safe command position.");
                data.hasCommsRoom = rooms.Command.Any(cell => cell.GetEdifice(map)?.def.defName == "CommsConsole");
                if (data.commanderType == CommanderType.Comms && !data.hasCommsRoom)
                    data.hasCommsRoom = PlaceCenterFurniture(map, faction, "CommsConsole", rooms.Command.ContractedBy(1), new HashSet<IntVec3>());
                if (RoleHas(data.roleDefName, "research")) FurnishLab(map, faction, data, rooms.Role);
                else if (RoleHas(data.roleDefName, "mine")) FurnishScanner(map, faction, data, rooms.Role, town);
            }
            else
            {
                foreach (var cell in plan.centerRect)
                {
                    if (cell.Roofed(map)) map.roofGrid.SetRoof(cell, null);
                    foreach (var plant in cell.GetThingList(map).OfType<Plant>().ToList()) plant.Destroy();
                }
                rooms = !open && plan.centerRect.Width <= 23 ?
                    plan.centerStyle.EndsWith("Alternative", StringComparison.Ordinal) ? BuildCompactCourt(map, faction, plan, stuff, floor) :
                    BuildCompactKeep(map, faction, plan, stuff, floor) :
                    open ? (plan.centerStyle == "councilHallAlternative" ?
                        BuildCouncilHall(map, faction, plan, wall, stuff, door, doorStuff, floor) :
                        BuildLeaderHall(map, faction, plan, wall, stuff, door, doorStuff, floor)) :
                    plan.centerStyle == "greatHallKeep" ? BuildGreatHallKeep(map, faction, plan, wall, stuff, door, doorStuff, floor) :
                    plan.centerStyle == "palaceCourts" || plan.centerStyle == "palaceCourtsAlternative" ? BuildPalaceCourts(map, faction, plan, wall, stuff, door, doorStuff, floor) :
                    BuildCastleKeep(map, faction, plan, wall, stuff, door, doorStuff, floor);
                FurnishCommand(map, faction, data, rooms.Command);
                if (data.hasPrison || HasLivePrisoner(data)) FurnishPrison(map, faction, data, rooms.Utility);
                else FurnishStoreroom(map, faction, data, rooms.Utility, data.roleDefName);
                if (RoleHas(data.roleDefName, "mine")) FurnishScanner(map, faction, data, rooms.Role, town);
                else if (RoleHas(data.roleDefName, "research")) FurnishLab(map, faction, data, rooms.Role);
                else if (RoleHas(data.roleDefName, "shrine")) ThreatEmitterUtility.FurnishShrine(map, faction, data, rooms.Role);
                else FurnishCenterCommons(map, faction, rooms.Role);
                LightRoom(map, faction, rooms.Command, false);
                LightRoom(map, faction, rooms.Vault, false);
            }
            FurnishCenterResidence(map, faction, data, rooms.Residence, open);
            if (data.tier >= 10)
            {
                // Compact bedrooms cannot contain the entire noble suite. Fill
                // missing slots in connected functional rooms without moving or
                // destroying existing furniture, walls or task objects.
                foreach (string name in new[] { "DankPyon_RoyalTudorBed", "DankPyon_RoyalTable2x4c", "DankPyon_RoyalThrone",
                    "DankPyon_RoyalArmchair", "DankPyon_RoyalBookshelf", "DankPyon_RoyalDresser", "DankPyon_RoyalEndTable" })
                {
                    var requested = StrongholdCultureCenters.FurnitureFor(name, data.tier);
                    if (requested == null || map.listerThings.AllThings.Any(t => t.def == requested && plan.centerRect.Contains(t.Position))) continue;
                    var candidates = new[] { rooms.Residence, rooms.Command, rooms.Role, rooms.Utility };
                    bool placed = false;
                    foreach (var room in candidates)
                    {
                        if (room.Equals(rooms.Utility) && (data.hasPrison || HasLivePrisoner(data))) continue;
                        if (!PlaceCenterFurniture(map, faction, name, room.ContractedBy(1), new HashSet<IntVec3>())) continue;
                        placed = true; break;
                    }
                    if (!placed) EESLog.Warning("Noble furniture slot has no safe footprint: " + name + ", center=" + plan.centerRect);
                }
            }
            var native = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
                .Where(t => t.def.category == ThingCategory.Item && t.def.plant == null && t is not Plant && plan.nativeLootIds.Contains(t.thingIDNumber)).ToList();
            var movedNative = native.OrderByDescending(t => t.MarketValue * t.stackCount)
                .Take(Math.Max(1, rooms.Vault.ContractedBy(1).Area / 2)).Count(thing => MoveNativeLootIntoVault(thing, map, rooms.Vault.ContractedBy(1)));
            if (!RimPactsCompat.NotAPlayerAssault(map.Parent as Settlement))
                StrongholdZoneUtility.FillVault(map, faction, data, rooms.Vault);
            plan.RecordVault(rooms.Vault.ContractedBy(1));
            siege.eesVaultRect = rooms.Vault;
            plan.commandCell = data.commandRoomCenter;
            plan.centerBuilt = true;
            plan.lootFinalized = true;
            plan.stage = "center-built";
            data.customLayout = true;
            data.vaultValue = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
                .Where(t => t.def.category == ThingCategory.Item && t.def.plant == null && t is not Plant && plan.vaultRect.Contains(t.Position)).Sum(t => t.MarketValue * t.stackCount);
            EESLog.Message("Unified " + plan.centerKind + " (" + plan.centerStyle + ", native=" + plan.nativeCenterReused + ") built: " + plan.centerRect + ", vault=" + plan.vaultRect +
                ", command=" + plan.commandCell + ", nativeLoot=" + native.Count + ", movedNative=" + movedNative + ", downgradedNativeFurniture=" + downgradedNativeFurniture + ", lootValue=" + data.vaultValue + ".");
        }
        catch (Exception exception)
        {
            plan.stage = "center-failed";
            EESLog.Error("Unified center generation failed; legacy replacement disabled to preserve the partially built center: " + exception);
            return false;
        }
        finally { siege.centerBuilding = false; }
        return true;
    }

    private static int ReplaceLowTierNativeFurniture(Map map, int tier, CellRect town)
    {
        // Only touch furniture created as part of the current map generation.
        // Existing saved maps keep every native object intact.
        if (map == null || tier >= 10 || MapGenerator.mapBeingGenerated != map) return 0;
        CellRect area = (town.Width > 0 ? town : CellRect.WholeMap(map)).ClipInsideMap(map);
        var candidates = area.Cells.SelectMany(cell => cell.GetThingList(map))
            .Where(thing => thing?.def?.category == ThingCategory.Building && thing.Spawned)
            .Distinct().ToList();
        int replaced = 0;
        foreach (Thing original in candidates)
        {
            ThingDef replacementDef = StrongholdCultureCenters.NativeFurnitureFallback(original.def, tier);
            if (replacementDef == null || replacementDef.category != ThingCategory.Building || replacementDef.size != original.def.size)
                continue;
            IntVec3 position = original.Position;
            Rot4 rotation = original.Rotation;
            ThingDef stuff = replacementDef.MadeFromStuff ? CompatibleFurnitureStuff(original, replacementDef) : null;
            Thing replacement;
            try
            {
                replacement = ThingMaker.MakeThing(replacementDef, stuff);
                replacement.SetFactionDirect(original.Faction);
            }
            catch (Exception exception)
            {
                EESLog.Warning("Skipped low-tier native furniture " + original.def.defName + ": replacement creation failed: " + exception.Message);
                continue;
            }

            QualityCategory? quality = (original as ThingWithComps)?.TryGetComp<CompQuality>()?.Quality;
            original.DeSpawn();
            try
            {
                GenSpawn.Spawn(replacement, position, map, rotation);
                if (quality.HasValue) (replacement as ThingWithComps)?.TryGetComp<CompQuality>()?.SetQuality(quality.Value, ArtGenerationContext.Outsider);
                original.Destroy(DestroyMode.Vanish);
                replaced++;
            }
            catch (Exception exception)
            {
                if (replacement.Spawned) replacement.Destroy(DestroyMode.Vanish);
                if (!original.Spawned)
                {
                    try { GenSpawn.Spawn(original, position, map, rotation); }
                    catch (Exception restoreException)
                    {
                        EESLog.Error("Low-tier native furniture restore failed for " + original.def.defName + ": " + restoreException.Message);
                    }
                }
                EESLog.Warning("Skipped low-tier native furniture " + original.def.defName + ": replacement spawn failed: " + exception.Message);
            }
        }
        if (replaced > 0) EESLog.Message("Downgraded " + replaced + " native royal furniture objects for fresh low-tier generation.");
        return replaced;
    }

    private static ThingDef CompatibleFurnitureStuff(Thing original, ThingDef replacement)
    {
        ThingDef originalStuff = original?.Stuff;
        if (originalStuff?.stuffProps?.categories != null && replacement.stuffCategories != null &&
            originalStuff.stuffProps.categories.Any(category => replacement.stuffCategories.Contains(category)))
            return originalStuff;
        return GenStuff.DefaultStuffFor(replacement);
    }

    private static void CaptureCenterLoot(Map map, StrongholdGenerationPlan plan, CellRect town)
    {
        var protectedIds = new HashSet<int>(plan.rewardProtectedThingIds ?? new List<int>());
        foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
        {
            if (thing?.def?.category != ThingCategory.Item || thing is Plant || thing.def.plant != null ||
                (!town.Contains(thing.Position) && !plan.centerRect.Contains(thing.Position))) continue;
            protectedIds.Add(thing.thingIDNumber);
        }
        // Every native item in the captured center/town footprint, including chunks,
        // must survive the generated-reward cap. Keep the complete ID set so a
        // large template cannot lose ordinary native resources merely because the
        // serialized protection list reached an arbitrary size limit.
        plan.rewardProtectedThingIds = protectedIds.OrderBy(id => id).ToList();
        plan.nativeLootIds = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).Where(t =>
            t.def.category == ThingCategory.Item && t is not Plant && t is not Corpse && t.def.plant == null &&
            !t.def.IsWithinCategory(ThingCategoryDefOf.Chunks) &&
            (town.Contains(t.Position) || plan.centerRect.Contains(t.Position) || plan.nativeLootIds.Contains(t.thingIDNumber)) &&
            (plan.nativeLootIds.Contains(t.thingIDNumber) || t.def.IsWeapon || t.def.IsApparel || t.def == ThingDefOf.Silver ||
             t.def == ThingDefOf.Gold || t.def == ThingDefOf.Jade ||
             t.def.building?.isNaturalRock != true && t.def.BaseMarketValue >= 50f && t.MarketValue * t.stackCount >= 250f))
            .OrderByDescending(t => t.MarketValue * t.stackCount).Take(64).Select(t => t.thingIDNumber).ToList();
    }

    private static (CellRect Command, CellRect Vault, CellRect Residence, CellRect Utility, CellRect Role) BuildGreatHallKeep(
        Map map, Faction faction, StrongholdGenerationPlan plan, ThingDef wall, ThingDef stuff, ThingDef door, ThingDef doorStuff, TerrainDef floor)
    {
        var outer = plan.centerRect;
        BuildEnclosedRoom(map, faction, outer, new IntVec3(outer.CenterCell.x, 0, outer.minZ), wall, stuff, door, doorStuff, floor, false);
        return BuildLeaderHall(map, faction, plan, wall, stuff, door, doorStuff, floor);
    }

    private static (CellRect Command, CellRect Vault, CellRect Residence, CellRect Utility, CellRect Role) BuildCompactKeep(
        Map map, Faction faction, StrongholdGenerationPlan plan, ThingDef stuff, TerrainDef floor)
    {
        // A 17/19/23-cell fallback cannot fit the large keep's hall and wings. Use
        // five enclosed rooms along a three-cell court, with single-cell walls
        // so each door and room stays inside the reserved terrain-safe plot.
        var outer = plan.centerRect;
        var wall = ThingDefOf.Wall;
        var door = ThingDefOf.Door;
        var wallStuff = StuffOrDefault(wall, stuff);
        var doorStuff = StuffOrDefault(door, stuff);
        BuildEnclosedRoom(map, faction, outer, new IntVec3(outer.CenterCell.x, 0, outer.minZ),
            wall, wallStuff, door, doorStuff, floor, false,
            new HashSet<IntVec3> { new IntVec3(outer.CenterCell.x, 0, outer.maxZ) });
        var width = (outer.Width - 5) / 2;
        var height = (outer.Height - 2) / 3;
        var leftX = outer.minX + 1;
        var rightX = outer.maxX - width;
        var frontZ = outer.minZ + 1;
        var middleZ = frontZ + height;
        var rearZ = middleZ + height;
        var utility = new CellRect(leftX, frontZ, width, height);
        var role = new CellRect(leftX, middleZ, width, height);
        var vault = new CellRect(leftX, rearZ, width, outer.maxZ - rearZ);
        var residence = new CellRect(rightX, frontZ, width, height);
        var command = new CellRect(rightX, middleZ, width, outer.maxZ - middleZ);
        foreach (var room in new[] { utility, role, vault, residence, command })
        {
            var entrance = new IntVec3(room.minX == leftX ? room.maxX : room.minX, 0, room.CenterCell.z);
            BuildEnclosedRoom(map, faction, room, entrance, wall, wallStuff, door, doorStuff, floor, true);
        }
        return (command, vault, residence, utility, role);
    }

    private static (CellRect Command, CellRect Vault, CellRect Residence, CellRect Utility, CellRect Role) BuildCompactCourt(
        Map map, Faction faction, StrongholdGenerationPlan plan, ThingDef stuff, TerrainDef floor)
    {
        // Four side wings and a rear cross-hall remain a different floor plan
        // from the primary's three left / two right rooms even at 17 cells.
        var outer = plan.centerRect;
        var wall = ThingDefOf.Wall;
        var door = ThingDefOf.Door;
        var wallStuff = StuffOrDefault(wall, stuff);
        var doorStuff = StuffOrDefault(door, stuff);
        BuildEnclosedRoom(map, faction, outer, new IntVec3(outer.CenterCell.x, 0, outer.minZ),
            wall, wallStuff, door, doorStuff, floor, false,
            new HashSet<IntVec3> { new IntVec3(outer.CenterCell.x, 0, outer.maxZ) });
        var width = (outer.Width - 5) / 2;
        var commandHeight = Math.Min(9, Math.Max(5, (outer.Height - 2) / 3 + 2));
        var command = new CellRect(outer.minX + 1, outer.maxZ - commandHeight, outer.Width - 2, commandHeight);
        var frontHeight = (command.minZ - outer.minZ) / 2 + 1;
        var rearZ = outer.minZ + frontHeight;
        var leftX = outer.minX + 1;
        var rightX = outer.maxX - width;
        var utility = new CellRect(leftX, outer.minZ + 1, width, frontHeight);
        var role = new CellRect(rightX, outer.minZ + 1, width, frontHeight);
        var residence = new CellRect(leftX, rearZ, width, command.minZ - rearZ + 1);
        var vault = new CellRect(rightX, rearZ, width, command.minZ - rearZ + 1);
        foreach (var room in new[] { utility, role, residence, vault })
            BuildEnclosedRoom(map, faction, room,
                new IntVec3(room.minX == leftX ? room.maxX : room.minX, 0, room.CenterCell.z),
                wall, wallStuff, door, doorStuff, floor, true);
        BuildEnclosedRoom(map, faction, command, new IntVec3(command.CenterCell.x, 0, command.minZ),
            wall, wallStuff, door, doorStuff, floor, true,
            new HashSet<IntVec3> { new IntVec3(command.CenterCell.x, 0, command.maxZ) });
        return (command, vault, residence, utility, role);
    }

    private static (CellRect Command, CellRect Vault, CellRect Residence, CellRect Utility, CellRect Role) BuildPalaceCourts(
        Map map, Faction faction, StrongholdGenerationPlan plan, ThingDef wall, ThingDef stuff, ThingDef door, ThingDef doorStuff, TerrainDef floor)
    {
        var outer = plan.centerRect;
        BuildEnclosedRoom(map, faction, outer, new IntVec3(outer.CenterCell.x, 0, outer.minZ), wall, stuff, door, doorStuff, floor, false);
        var side = Math.Max(5, (outer.Width - 8) / 3);
        var frontLeft = new CellRect(outer.minX + 1, outer.minZ + 1, side, side);
        var frontRight = new CellRect(outer.maxX - side, outer.minZ + 1, side, side);
        var rearLeft = new CellRect(outer.minX + 1, outer.maxZ - side, side, side);
        var rearRight = new CellRect(outer.maxX - side, outer.maxZ - side, side, side);
        var command = new CellRect(outer.CenterCell.x - side / 2, outer.maxZ - side, side, side);
        var gatehouse = new CellRect(outer.CenterCell.x - side / 2, outer.minZ + 1, side, Math.Min(side, 9));
        foreach (var room in new[] { frontLeft, frontRight, rearLeft, rearRight, command })
        {
            var entrance = room.minZ == outer.minZ + 1 ? new IntVec3(room.CenterCell.x, 0, room.maxZ) :
                new IntVec3(room.CenterCell.x, 0, room.minZ);
            BuildEnclosedRoom(map, faction, room, entrance, wall, stuff, door, doorStuff, floor, true);
        }
        BuildEnclosedRoom(map, faction, gatehouse, new IntVec3(gatehouse.CenterCell.x, 0, gatehouse.minZ), wall, stuff, door, doorStuff, floor, true,
            new HashSet<IntVec3> { new IntVec3(gatehouse.CenterCell.x, 0, gatehouse.maxZ) });
        FurnishCenterCommons(map, faction, gatehouse);
        return (command, plan.layoutVariant == 1 ? rearRight : rearLeft, plan.layoutVariant == 1 ? rearLeft : rearRight, frontLeft, frontRight);
    }

    private static (CellRect Command, CellRect Vault, CellRect Residence, CellRect Utility, CellRect Role) BuildCastleKeep(
        Map map, Faction faction, StrongholdGenerationPlan plan, ThingDef wall, ThingDef stuff, ThingDef door, ThingDef doorStuff, TerrainDef floor)
    {
        var outer = plan.centerRect;
        var side = Math.Max(4, (outer.Width - 8) / 3);
        // Integer room spacing can shift the middle row/column one cell away
        // from the outer rectangle midpoint (notably a 55-cell keep). Align
        // outer doors with the actual inward room doors, including when an
        // existing native wall blocks the external side of one entrance.
        var middleRoom = new CellRect(outer.minX + side + 4, outer.minZ + side + 4, side, side);
        var gate = new IntVec3(middleRoom.CenterCell.x, 0, outer.minZ);
        var gates = new HashSet<IntVec3>
        {
            gate + IntVec3.East, gate + IntVec3.West,
            new IntVec3(outer.minX, 0, middleRoom.CenterCell.z), new IntVec3(outer.maxX, 0, middleRoom.CenterCell.z),
            new IntVec3(middleRoom.CenterCell.x, 0, outer.maxZ)
        };
        BuildEnclosedRoom(map, faction, outer, gate, wall, stuff, door, doorStuff, floor, false, gates);
        var rooms = new CellRect[3, 3];
        for (var z = 0; z < 3; z++)
        for (var x = 0; x < 3; x++)
        {
            var room = new CellRect(outer.minX + 1 + x * (side + 3), outer.minZ + 1 + z * (side + 3), side, side);
            rooms[x, z] = room;
            if (x == 1 && z == 1) { LayFloor(map, room, floor); continue; }
            var entry = z == 0 ? new IntVec3(room.CenterCell.x, 0, room.maxZ) :
                z == 2 ? new IntVec3(room.CenterCell.x, 0, room.minZ) :
                x == 0 ? new IntVec3(room.maxX, 0, room.CenterCell.z) : new IntVec3(room.minX, 0, room.CenterCell.z);
            var extra = new HashSet<IntVec3>();
            if (x == 1 && z == 0) extra.Add(new IntVec3(room.CenterCell.x, 0, room.minZ));
            if (x == 1 && z == 2) extra.Add(new IntVec3(room.CenterCell.x, 0, room.maxZ));
            if (x == 0 && z == 1) extra.Add(new IntVec3(room.minX, 0, room.CenterCell.z));
            if (x == 2 && z == 1) extra.Add(new IntVec3(room.maxX, 0, room.CenterCell.z));
            BuildEnclosedRoom(map, faction, room, entry, wall, stuff, door, doorStuff, floor, true, extra);
            if (z == 0)
            {
                if (x == 1) FurnishCenterCommons(map, faction, room);
                else
                {
                    var occupied = new HashSet<IntVec3>();
                    for (var i = 0; i < 4; i++) PlaceCenterFurniture(map, faction, "Bed", room.ContractedBy(1), occupied);
                    LightRoom(map, faction, room, false);
                }
            }
        }
        return (rooms[1, 2], rooms[plan.layoutVariant == 1 ? 0 : 2, 2], rooms[plan.layoutVariant == 1 ? 2 : 0, 2],
            rooms[0, 1], rooms[2, 1]);
    }

    private static bool CenterOccupied(Map map, CellRect rect)
    {
        foreach (var cell in rect)
            if (cell.GetEdifice(map) != null || cell.GetFirstPawn(map) != null || cell.GetFirstItem(map) != null) return true;
        return false;
    }

    private static (CellRect Command, CellRect Vault, CellRect Residence, CellRect Utility, CellRect Role) BuildCouncilHall(
        Map map, Faction faction, StrongholdGenerationPlan plan, ThingDef wall, ThingDef stuff, ThingDef door, ThingDef doorStuff, TerrainDef floor)
    {
        // A transverse council hall with four rear chambers, rather than a
        // rotation of the longitudinal leader hall. Open cultures retain wood,
        // a central outdoor passage and their own perimeter/barricade rules.
        var outer = plan.centerRect;
        var main = new CellRect(outer.minX + 1, outer.minZ + 1, outer.Width - 2, Math.Max(5, outer.Height / 3));
        var roomWidth = Math.Max(5, (outer.Width - 5) / 2);
        var roomHeight = Math.Max(3, (outer.Height - main.Height - 5) / 2);
        var rearZ = main.maxZ + 3;
        var residence = new CellRect(outer.minX + 1, rearZ, roomWidth, roomHeight);
        var utility = new CellRect(outer.maxX - roomWidth, rearZ, roomWidth, roomHeight);
        var vault = new CellRect(residence.minX, rearZ + roomHeight + 1, roomWidth, roomHeight);
        var role = new CellRect(utility.minX, vault.minZ, roomWidth, roomHeight);
        BuildEnclosedRoom(map, faction, main, new IntVec3(main.CenterCell.x, 0, main.minZ), wall, stuff, door, doorStuff, floor, true,
            new HashSet<IntVec3> { new IntVec3(main.CenterCell.x, 0, main.maxZ) });
        foreach (var room in new[] { residence, utility, vault, role })
        {
            var entry = new IntVec3(room.minX == residence.minX ? room.maxX : room.minX, 0, room.CenterCell.z);
            BuildEnclosedRoom(map, faction, room, entry, wall, stuff, door, doorStuff, floor, true);
        }
        LayFloor(map, new CellRect(residence.maxX + 1, main.maxZ + 1,
            utility.minX - residence.maxX - 1, outer.maxZ - main.maxZ - 1), floor);
        LayFloor(map, new CellRect(outer.minX + 1, main.maxZ + 1, outer.Width - 2, 2), floor);
        FurnishCenterCommons(map, faction, main);
        return (main, vault, residence, utility, role);
    }

    private static (CellRect Command, CellRect Vault, CellRect Residence, CellRect Utility, CellRect Role) BuildLeaderHall(
        Map map, Faction faction, StrongholdGenerationPlan plan, ThingDef wall, ThingDef stuff, ThingDef door, ThingDef doorStuff, TerrainDef floor)
    {
        var outer = plan.centerRect;
        var hallWidth = outer.Width >= 29 ? 13 : 9;
        var main = new CellRect(outer.CenterCell.x - hallWidth / 2, outer.minZ + 1, hallWidth, outer.Height - 12);
        var rearDoor = new IntVec3(main.CenterCell.x, 0, main.maxZ);
        BuildEnclosedRoom(map, faction, main, new IntVec3(main.CenterCell.x, 0, main.minZ), wall, stuff, door, doorStuff, floor, false,
            new HashSet<IntVec3> { rearDoor });
        RoofRect(map, main);
        var vault = new CellRect(main.minX, main.maxZ + 2, main.Width, 9);
        BuildEnclosedRoom(map, faction, vault, new IntVec3(vault.CenterCell.x, 0, vault.minZ), wall, stuff, door, doorStuff, floor, true);
        var wingWidth = Math.Min(13, Math.Max(4, (outer.Width - hallWidth - 4) / 2));
        var left = new CellRect(outer.minX + 1, outer.minZ + 2, wingWidth, Math.Min(9, main.Height));
        var right = new CellRect(outer.maxX - wingWidth, outer.minZ + 2, wingWidth, Math.Min(9, main.Height));
        BuildEnclosedRoom(map, faction, left, new IntVec3(left.maxX, 0, left.CenterCell.z), wall, stuff, door, doorStuff, floor, true);
        BuildEnclosedRoom(map, faction, right, new IntVec3(right.minX, 0, right.CenterCell.z), wall, stuff, door, doorStuff, floor, true);
        var role = new CellRect(right.minX, vault.minZ, right.Width, vault.Height);
        BuildEnclosedRoom(map, faction, role, new IntVec3(role.minX, 0, role.CenterCell.z), wall, stuff, door, doorStuff, floor, true);
        LayFloor(map, new CellRect(main.minX, main.maxZ + 1, main.Width, 1), floor);
        FurnishCenterCommons(map, faction, main);
        return (main, vault, left, right, role);
    }

    private static void FurnishCenterResidence(Map map, Faction faction, StrongholdData data, CellRect room, bool open)
    {
        var interior = room.ContractedBy(1);
        var occupied = new HashSet<IntVec3>();
        if (data.tier >= 10)
        {
            foreach (var name in new[]
            { "DankPyon_RoyalTudorBed", "DankPyon_RoyalDresser", "DankPyon_RoyalEndTable", "DankPyon_RoyalTable2x4c",
                "DankPyon_RoyalArmchair", "DankPyon_RoyalThrone", "DankPyon_RoyalBookshelf" })
                PlaceCenterFurniture(map, faction, name, interior, occupied);
        }
        else
        {
            foreach (var name in new[] { "Bed", "DiningChair", "Table1x2c", "EndTable" }) PlaceCenterFurniture(map, faction, name, interior, occupied);
        }
    }

    private static void FurnishCenterCommons(Map map, Faction faction, CellRect room)
    {
        var occupied = new HashSet<IntVec3>();
        foreach (var name in new[] { "Table1x2c", "DiningChair", "DiningChair", "ChessTable", "DiningChair" })
            PlaceCenterFurniture(map, faction, name, room.ContractedBy(1), occupied);
        LightRoom(map, faction, room, false);
    }

    private static bool PlaceCenterFurniture(Map map, Faction faction, string name, CellRect interior, HashSet<IntVec3> occupied)
    {
        var tier = map?.Parent is Settlement settlement ? WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID)?.tier ?? 1 : 1;
        var def = StrongholdCultureCenters.FurnitureFor(name, tier);
        if (def == null) return false;
        // Keep a cross through every room so furniture cannot close a doorway or the command position.
        // A compact palace room may not fit a 2x4 royal table in any quadrant.
        // Permit a second placement pass only when all real door approaches stay connected.
        var data = map?.Parent is Settlement parent ? WorldComponent_Strongholds.Instance?.GetExisting(parent.ID) : null;
        bool PassagesConnected(CellRect candidate)
        {
            var traversable = new HashSet<IntVec3>(interior.Where(c => c.Standable(map) && !candidate.Contains(c) &&
                (!occupied.Contains(c) || c.GetEdifice(map)?.def.passability != Traversability.Impassable)));
            var approaches = interior.Where(c => GenAdj.CardinalDirections.Any(offset => (c + offset).InBounds(map) &&
                (c + offset).GetEdifice(map)?.def.IsDoor == true)).ToList();
            if (data?.commandRoomCenter.IsValid == true && interior.Contains(data.commandRoomCenter)) approaches.Add(data.commandRoomCenter);
            if (approaches.Count == 0 || approaches.Any(c => !traversable.Contains(c))) return false;
            var reached = new HashSet<IntVec3>(); var pending = new Queue<IntVec3>();
            reached.Add(approaches[0]); pending.Enqueue(approaches[0]);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                foreach (var offset in GenAdj.CardinalDirections)
                {
                    var next = current + offset;
                    if (traversable.Contains(next) && reached.Add(next)) pending.Enqueue(next);
                }
            }
            return approaches.All(reached.Contains);
        }
        for (int pass = 0; pass < 2; pass++)
        foreach (var rotation in def.rotatable ? new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West } : new[] { Rot4.North })
        foreach (var cell in interior)
        {
            var footprint = GenAdj.OccupiedRect(cell, rotation, def.size);
            if (footprint.Any(c => !interior.Contains(c) || pass == 0 && (c.x == interior.CenterCell.x || c.z == interior.CenterCell.z) ||
                occupied.Contains(c) || !c.Standable(map) || c.GetEdifice(map) != null || c.GetFirstItem(map) != null || c.GetFirstPawn(map) != null)) continue;
            if (!PassagesConnected(footprint)) continue;
            if (def.hasInteractionCell)
            {
                var interaction = ThingUtility.InteractionCellWhenAt(def, cell, rotation, map);
                // Royalty thrones use their standable seat as the interaction
                // cell. Only blocking furniture needs it outside its footprint.
                if (!interior.Contains(interaction) || footprint.Contains(interaction) && def.passability != Traversability.Standable || occupied.Contains(interaction) ||
                    !interaction.Standable(map) || interaction.GetEdifice(map) != null) continue;
            }
            if (PlaceBuildingRot(map, faction, def, cell, rotation) == null) return false;
            foreach (var point in footprint) occupied.Add(point);
            if (def.hasInteractionCell) occupied.Add(ThingUtility.InteractionCellWhenAt(def, cell, rotation, map));
            return true;
        }
        return false;
    }

    private static bool MoveNativeLootIntoVault(Thing thing, Map map, CellRect interior)
    {
        var original = thing.Position;
        if (thing.OccupiedRect().Cells.All(c => interior.Contains(c))) return true;
        foreach (var cell in interior)
        {
            if (!cell.Standable(map) || cell.GetEdifice(map) != null || cell.GetFirstItem(map) != null || cell.GetFirstPawn(map) != null) continue;
            thing.DeSpawn();
            try { GenSpawn.Spawn(thing, cell, map); return true; }
            catch { if (!thing.Spawned) GenSpawn.Spawn(thing, original, map); return false; }
        }
        return false;
    }
}
