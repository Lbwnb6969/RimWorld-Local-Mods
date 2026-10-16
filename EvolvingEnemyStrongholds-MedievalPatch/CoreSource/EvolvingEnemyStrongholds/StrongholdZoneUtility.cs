using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class StrongholdZoneUtility
{
	public const int AncientCoreTier = 10;

	public const int LegendaryGearTierDefault = 12;

	private const int ReuseMinCells = 25;

	private const int ReuseMaxCells = 140;

	private const float T6ArmorChance = 0.5f;

	private const float T6LegendaryWeaponChance = 0.3f;

	private const float T7WeaponUniqueChance = 0.4f;

	public static int LegendaryGearTier => StrongholdMod.Settings?.legendaryGearTier ?? 12;

	/// <summary>
	/// Captures the native recoverables that existed before EES starts building the
	/// stronghold. The reward cap may remove unprotected items later, so this
	/// snapshot is the authoritative protection set for the whole generation pass.
	///
	/// The medieval adapter deliberately converts the listed resources; those
	/// entries remain outside this preservation set and are audited separately.
	/// </summary>
	public static void CaptureNativeItemsForLedger(Map map, StrongholdGenerationPlan plan)
	{
		if (map == null || plan == null || plan.centerBuilt || plan.lootFinalized) return;
		if (plan.rewardProtectedThingIds == null) plan.rewardProtectedThingIds = new List<int>();
		var converted = new HashSet<string>(new[]
		{
			"ComponentIndustrial", "ComponentSpacer", "Steel", "MedicineIndustrial",
			"MealSurvivalPack", "Neutroamine", "Chemfuel", "Prometheum", "Hyperweave",
			"Penoxycyline", "WakeUp", "Yayo", "Flake", "GoJuice", "Luciferium",
			"ReinforcedBarrel", "MechSerumHealer", "MechSerumResurrector", "Shell_HighExplosive",
			"Shell_Incendiary", "Shell_EMP"
		});
		var protectedIds = new HashSet<int>(plan.rewardProtectedThingIds);
		foreach (Thing thing in RecoverableItems(map, CellRect.WholeMap(map)))
		{
			if (thing == null || thing.def?.category != ThingCategory.Item || thing is Plant || thing.def.plant != null ||
				(thing.Spawned && converted.Contains(thing.def.defName) && thing.questTags.NullOrEmpty() && thing is not IThingHolder))
			{
				continue;
			}
			protectedIds.Add(thing.thingIDNumber);
		}
		plan.rewardProtectedThingIds = protectedIds.OrderBy(id => id).ToList();
		// Keep original low-cover defenses and wall attachments alongside walls and
		// turrets. New castle construction must preserve their identity and placement.
		// Preserve unknown/native quest items and Techprints regardless of their
		// tech-level label. Only the explicit adapter-conversion list is excluded.
		plan.nativeDefenseIds = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
			.Where(t => t.Faction == map.ParentFaction && (StrongholdCenterPlanner.StructuralWall(t.def) || t.def.building?.IsTurret == true || t.def.building?.isAttachment == true ||
                t.def == ThingDefOf.Sandbags || t.def.defName is "Barricade" or "BarbedWire" ||
                t.def == StrongholdDefenseUtility.CeEmbrasure || t.def == StrongholdDefenseUtility.CeReinforcedEmbrasure))
			.Select(t => t.thingIDNumber).Distinct().ToList();
		EESLog.Message($"Reward ledger captured {plan.rewardProtectedThingIds.Count} native recoverables before stronghold generation.");
	}

	private static int VaultSize(int tier)
	{
		return Mathf.Clamp(5 + tier, 7, 11);
	}

	public static void BuildCoreZone(Map map, StrongholdData data, CellRect settlementRect)
	{
		Faction faction = map?.ParentFaction;
		if (faction == null || data == null || settlementRect.Width < 7 || settlementRect.Height < 7)
		{
			return;
		}
		try
		{
			int contentTier = data.ContentTier;
			bool reused;
			CellRect cellRect = ResolveCoreRect(map, settlementRect, contentTier, out reused);
			if (cellRect.Width < 5 || cellRect.Height < 5)
			{
				EESLog.Message($"CoreZone [{faction.Name} T{contentTier}]: no valid core rect (skip).");
				return;
			}
			bool flag = data.tier >= 10;
			ThingDef wallDef;
			ThingDef wallStuff;
			ThingDef doorDef;
			ThingDef doorStuff;
			bool flag2 = ResolveCoreWallDoor(map, faction, contentTier, flag, out wallDef, out wallStuff, out doorDef, out doorStuff);
			ClearVaultFurniture(map, cellRect);
			if (!reused)
			{
				LayFloor(map, cellRect, data.roleDefName);
			}
			IntVec3 intVec = PickDoorCell(map, cellRect, data.settlementId);
			int num = BuildWallRing(map, faction, cellRect, intVec, wallDef, wallStuff);
			bool flag3 = SpawnDoor(map, faction, intVec, doorDef, doorStuff);
			int num2 = FillInterior(map, faction, data, cellRect);
			BuildRoleRoom(map, faction, data, settlementRect, cellRect);
			BuildPrisonBlock(map, faction, data, settlementRect, cellRect);
			EESLog.Message(string.Format("CoreZone [{0} T{1} role={2}]: ", faction.Name, contentTier, data.roleDefName ?? "none") + string.Format("{0} {1}x{2} @ {3} | ", reused ? "reused-room" : "built-vault", cellRect.Width, cellRect.Height, cellRect.CenterCell) + "wall=" + wallDef?.defName + ((wallStuff != null) ? ("/" + wallStuff.defName) : "") + " door=" + doorDef?.defName + ((doorStuff != null) ? ("/" + doorStuff.defName) : "") + " " + $"ancient={flag2}(want={flag}) | walls={num} doorOk={flag3} kinds={num2}.");
		}
		catch (Exception ex)
		{
			EESLog.Error("BuildCoreZone failed: " + ex);
		}
	}

	private static CellRect ResolveCoreRect(Map map, CellRect settlementRect, int tier, out bool reused)
	{
		reused = false;
		IntVec3 centerCell = settlementRect.CenterCell;
		Room room = centerCell.GetRoom(map);
		if (IsReusableRoom(room, settlementRect))
		{
			reused = true;
			return BoundingRect(room).ClipInsideMap(map);
		}
		int num = VaultSize(tier) / 2;
		CellRect cellRect = settlementRect.ContractedBy(2);
		int newX = Mathf.Clamp(centerCell.x, cellRect.minX + num, cellRect.maxX - num);
		int newZ = Mathf.Clamp(centerCell.z, cellRect.minZ + num, cellRect.maxZ - num);
		return CellRect.CenteredOn(new IntVec3(newX, 0, newZ), num).ClipInsideMap(map);
	}

	private static bool IsReusableRoom(Room room, CellRect settlementRect)
	{
		if (room == null || room.TouchesMapEdge || room.PsychologicallyOutdoors || !room.ProperRoom)
		{
			return false;
		}
		int cellCount = room.CellCount;
		if (cellCount < 25 || cellCount > 140)
		{
			return false;
		}
		return settlementRect.Contains(BoundingRect(room).CenterCell);
	}

	private static CellRect BoundingRect(Room room)
	{
		int num = int.MaxValue;
		int num2 = int.MaxValue;
		int num3 = int.MinValue;
		int num4 = int.MinValue;
		foreach (IntVec3 cell in room.Cells)
		{
			if (cell.x < num)
			{
				num = cell.x;
			}
			if (cell.z < num2)
			{
				num2 = cell.z;
			}
			if (cell.x > num3)
			{
				num3 = cell.x;
			}
			if (cell.z > num4)
			{
				num4 = cell.z;
			}
		}
		if (num > num3)
		{
			return CellRect.Empty;
		}
		return CellRect.FromLimits(num - 1, num2 - 1, num3 + 1, num4 + 1);
	}

	private static bool ResolveCoreWallDoor(Map map, Faction faction, int tier, bool wantAncient, out ThingDef wallDef, out ThingDef wallStuff, out ThingDef doorDef, out ThingDef doorStuff)
	{
		wallStuff = null;
		doorStuff = null;
		if (wantAncient)
		{
			StrongholdDefenseUtility.GetAncientCoreDefs(map, out var ancientWall, out var blastDoor);
			if (ancientWall != null)
			{
				wallDef = ancientWall;
				if (blastDoor != null)
				{
					doorDef = blastDoor;
					return true;
				}
				doorDef = ThingDefOf.Door;
				doorStuff = PickStrongestStuff(doorDef, faction);
				return true;
			}
			wallDef = ThingDefOf.Wall;
			wallStuff = PickStrongestStuff(wallDef, faction);
			doorDef = ThingDefOf.Door;
			doorStuff = PickStrongestStuff(doorDef, faction);
			return true;
		}
		StrongholdDefenseUtility.ResolveWall(map, faction, tier, out wallDef, out wallStuff);
		if (wallDef == null)
		{
			wallDef = ThingDefOf.Wall;
			wallStuff = PickStrongestStuff(wallDef, faction);
		}
		doorDef = ThingDefOf.Door;
		doorStuff = (doorDef.MadeFromStuff ? wallStuff : null);
		return false;
	}

	private static ThingDef PickStrongestStuff(ThingDef buildDef, Faction faction)
	{
		if (buildDef == null || !buildDef.MadeFromStuff)
		{
			return null;
		}
		TechLevel maxTechLevel = faction?.def?.techLevel ?? TechLevel.Industrial;
		List<ThingDef> list = GenStuff.AllowedStuffsFor(buildDef, maxTechLevel).ToList();
		if (list.Count == 0)
		{
			return GenStuff.DefaultStuffFor(buildDef);
		}
		ModContentPack raceMod = faction?.def?.modContentPack;
		if (raceMod != null && !raceMod.IsOfficialMod)
		{
			List<ThingDef> list2 = list.Where((ThingDef s) => s.modContentPack == raceMod).ToList();
			list = ((list2.Count > 0) ? list2 : list.Where((ThingDef s) => s.modContentPack == null || s.modContentPack.IsOfficialMod).ToList());
		}
		else
		{
			list = list.Where((ThingDef s) => s.modContentPack == null || s.modContentPack.IsOfficialMod).ToList();
		}
		if (list.Count == 0)
		{
			return GenStuff.DefaultStuffFor(buildDef);
		}
		ThingDef thingDef = null;
		float num = -1f;
		foreach (ThingDef item in list)
		{
			float statValueAbstract = buildDef.GetStatValueAbstract(StatDefOf.MaxHitPoints, item);
			if (statValueAbstract > num)
			{
				num = statValueAbstract;
				thingDef = item;
			}
		}
		return thingDef ?? GenStuff.DefaultStuffFor(buildDef);
	}

	private static void ClearInterior(Map map, CellRect core)
	{
		foreach (IntVec3 item in core.ContractedBy(1))
		{
			if (item.InBounds(map))
			{
				Building edifice = item.GetEdifice(map);
				if (edifice != null && !edifice.Destroyed)
				{
					edifice.Destroy();
				}
			}
		}
	}

	private static void ClearVaultFurniture(Map map, CellRect core)
	{
		foreach (IntVec3 item in core.ContractedBy(1))
		{
			if (!item.InBounds(map))
			{
				continue;
			}
			List<Thing> thingList = item.GetThingList(map);
			for (int num = thingList.Count - 1; num >= 0; num--)
			{
				if (thingList[num] is Building { Destroyed: false } building)
				{
					building.Destroy();
				}
			}
		}
	}

	private static void LayFloor(Map map, CellRect core, string role)
	{
		TerrainDef terrainDef = ResolveFloor(role);
		if (terrainDef == null)
		{
			return;
		}
		foreach (IntVec3 item in core.ContractedBy(1))
		{
			if (item.InBounds(map) && item.GetEdifice(map) == null)
			{
				map.terrainGrid.SetTerrain(item, terrainDef);
			}
		}
		EESLog.Message("CoreZone floor [role=" + (role ?? "none") + "]: " + terrainDef.defName);
	}

	private static TerrainDef ResolveFloor(string role)
	{
		string[] array = ((!RoleMatches(role, "noble") && !RoleMatches(role, "estate")) ? (RoleMatches(role, "research") ? new string[2] { "SterileTile", "MetalTile" } : ((!RoleMatches(role, "command") && !RoleMatches(role, "fortress") && !RoleMatches(role, "mech") && !RoleMatches(role, "relay")) ? ((!RoleMatches(role, "black") && !RoleMatches(role, "market")) ? new string[2] { "Concrete", "MetalTile" } : new string[2] { "WoodPlankFloor", "Concrete" }) : new string[2] { "MetalTile", "Concrete" })) : new string[3] { "CarpetFine", "Carpet", "MetalTile" });
		for (int i = 0; i < array.Length; i++)
		{
			TerrainDef namedSilentFail = DefDatabase<TerrainDef>.GetNamedSilentFail(array[i]);
			if (namedSilentFail != null)
			{
				return namedSilentFail;
			}
		}
		return DefDatabase<TerrainDef>.GetNamedSilentFail("Concrete") ?? DefDatabase<TerrainDef>.GetNamedSilentFail("MetalTile");
	}

	private static int BuildWallRing(Map map, Faction faction, CellRect core, IntVec3 doorCell, ThingDef wallDef, ThingDef wallStuff)
	{
		if (wallDef == null)
		{
			return 0;
		}
		int num = 0;
		foreach (IntVec3 edgeCell in core.EdgeCells)
		{
			if (edgeCell == doorCell || !edgeCell.InBounds(map))
			{
				continue;
			}
			Building edifice = edgeCell.GetEdifice(map);
			if (edifice == null || edifice.def != wallDef)
			{
				if (edifice != null && !edifice.Destroyed)
				{
					edifice.Destroy();
				}
				try
				{
					Thing thing = ThingMaker.MakeThing(wallDef, wallDef.MadeFromStuff ? wallStuff : null);
					thing.SetFactionDirect(faction);
					GenSpawn.Spawn(thing, edgeCell, map);
					num++;
				}
				catch (Exception ex)
				{
					EESLog.Error($"CoreZone wall {wallDef.defName} @ {edgeCell}: {ex.Message}");
				}
			}
		}
		return num;
	}

	private static bool SpawnDoor(Map map, Faction faction, IntVec3 cell, ThingDef doorDef, ThingDef doorStuff)
	{
		if (doorDef == null || !cell.InBounds(map))
		{
			return false;
		}
		Building edifice = cell.GetEdifice(map);
		if (edifice != null && !edifice.Destroyed)
		{
			edifice.Destroy();
		}
		try
		{
			Thing thing = ThingMaker.MakeThing(doorDef, doorDef.MadeFromStuff ? doorStuff : null);
			thing.SetFactionDirect(faction);
			GenSpawn.Spawn(thing, cell, map);
			return true;
		}
		catch (Exception ex)
		{
			EESLog.Error($"CoreZone door {doorDef.defName} @ {cell}: {ex.Message}");
			return false;
		}
	}

	private static IntVec3 PickDoorCell(Map map, CellRect core, int seed)
	{
		List<IntVec3> list = new List<IntVec3>();
		List<IntVec3> list2 = new List<IntVec3>();
		foreach (IntVec3 edgeCell in core.EdgeCells)
		{
			if (IsCorner(edgeCell, core))
			{
				continue;
			}
			IntVec3 c = OutwardNeighbor(edgeCell, core);
			if (c.InBounds(map))
			{
				if (c.GetEdifice(map) == null && c.Walkable(map))
				{
					list.Add(edgeCell);
				}
				else
				{
					list2.Add(edgeCell);
				}
			}
		}
		List<IntVec3> list3 = ((list.Count > 0) ? list : list2);
		if (list3.Count == 0)
		{
			return new IntVec3(core.CenterCell.x, 0, core.minZ);
		}
		Rand.PushState(Gen.HashCombineInt(seed, 6201837));
		IntVec3 result = list3[Rand.Range(0, list3.Count)];
		Rand.PopState();
		return result;
	}

	private static void CarveDoorOpening(Map map, IntVec3 doorCell, CellRect room)
	{
		IntVec3 intVec = OutwardNeighbor(doorCell, room) - doorCell;
		for (int i = 1; i <= 8; i++)
		{
			IntVec3 c = doorCell + new IntVec3(intVec.x * i, 0, intVec.z * i);
			if (!c.InBounds(map))
			{
				break;
			}
			Building edifice = c.GetEdifice(map);
			if (edifice == null || edifice.Destroyed)
			{
				if (c.Walkable(map))
				{
					break;
				}
			}
			else if (edifice.def.IsDoor || edifice.def.passability == Traversability.Impassable)
			{
				edifice.Destroy();
			}
		}
	}

	private static bool IsCorner(IntVec3 c, CellRect r)
	{
		if (c.x == r.minX || c.x == r.maxX)
		{
			if (c.z != r.minZ)
			{
				return c.z == r.maxZ;
			}
			return true;
		}
		return false;
	}

	private static IntVec3 OutwardNeighbor(IntVec3 c, CellRect r)
	{
		if (c.x == r.minX)
		{
			return new IntVec3(c.x - 1, 0, c.z);
		}
		if (c.x == r.maxX)
		{
			return new IntVec3(c.x + 1, 0, c.z);
		}
		if (c.z == r.minZ)
		{
			return new IntVec3(c.x, 0, c.z - 1);
		}
		return new IntVec3(c.x, 0, c.z + 1);
	}

	public static int FillVault(Map map, Faction faction, StrongholdData data, CellRect vaultInterior, float extraMul = 1f)
	{
		var plan = StrongholdGenerationPlan.GetOrCreate(map, data);
		plan?.RefreshRewardSource(data);
		plan?.RecordVault(vaultInterior);
		var siege = map?.GetComponent<MapComponent_StrongholdSiege>();
		if (siege != null && siege.eesVaultRect.Width <= 0) siege.eesVaultRect = vaultInterior.ExpandedBy(1).ClipInsideMap(map);
		float budgetMul = PrepareRewardBudget(map, data, vaultInterior, extraMul);
		int placed = budgetMul > 0f ? FillInterior(map, faction, data, vaultInterior, budgetMul) : 0;
		if (plan != null && placed > 0) plan.lootFinalized = true;
		return placed;
	}

	/// <summary>
	/// Reserves one reward budget for the whole stronghold. Native recoverables and furniture consume
	/// the same budget as generated vault goods; protected native things are never deleted by the cap.
	/// </summary>
	public static float PrepareRewardBudget(Map map, StrongholdData data, CellRect vaultInterior, float extraMul)
	{
		var plan = StrongholdGenerationPlan.GetOrCreate(map, data);
		if (map == null || data == null || plan == null || vaultInterior.Width <= 0 || vaultInterior.Height <= 0)
		{
			return Mathf.Max(0f, extraMul);
		}
		plan.RefreshRewardSource(data);
		if (plan.rewardBudgetFinalized && plan.vaultRect == vaultInterior)
		{
			return 0f;
		}
		plan.RecordVault(vaultInterior);
		if (plan.rewardProtectedThingIds == null) plan.rewardProtectedThingIds = new List<int>();
		var protectedIds = new HashSet<int>(plan.rewardProtectedThingIds);
		foreach (int thingId in plan.nativeLootIds ?? new List<int>()) protectedIds.Add(thingId);
		foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
		{
			if (thing != null && thing.def?.category == ThingCategory.Item && vaultInterior.Contains(thing.Position)) protectedIds.Add(thing.thingIDNumber);
		}
        // Preserve the complete native protection set across the budget pass. The
        // center capture already limits native loot relocation separately; this
        // list protects every captured item (including chunks) from cap trimming.
        plan.rewardProtectedThingIds = protectedIds.OrderBy(id => id).ToList();
		plan.rewardBudgetTarget = Mathf.Max(1f, RecoverableBudget(data) * Mathf.Max(1f, extraMul));
        // The protection set covers the whole map to prevent native item loss.
        // Only recoverables in the known stronghold footprint consume its budget;
        // the final pass recomputes this after the complete base rectangle exists.
        var knownArea = plan.baseRect.Width > 0 ? plan.baseRect : plan.centerRect.Width > 0 ? plan.centerRect : vaultInterior;
        plan.rewardBudgetProtected = RecoverableItems(map, knownArea)
            .Where(t => protectedIds.Contains(t.thingIDNumber))
            .Sum(t => t.MarketValue * t.stackCount);
		plan.rewardBudgetFurniture = FurnitureValue(map, plan);
		plan.rewardBudgetEquipment = GarrisonEquipmentValue(map, map.ParentFaction);
		float available = plan.rewardBudgetTarget - plan.rewardBudgetProtected - plan.rewardBudgetFurniture - plan.rewardBudgetEquipment;
		float baseEstimate = Mathf.Max(1f, EstimateVaultValue(data));
		return Mathf.Max(0f, extraMul * Mathf.Clamp01(available / baseEstimate));
	}

	/// <summary>Runs after adapter post-fixes so culture and high-tier gear also obey the ledger.</summary>
	public static void FinalizeRewardBudget(Map map, StrongholdData data, CellRect vaultInterior)
	{
		var plan = StrongholdGenerationPlan.GetOrCreate(map, data);
		if (map == null || data == null || plan == null || vaultInterior.Width <= 0 || vaultInterior.Height <= 0) return;
		plan.RefreshRewardSource(data);
		plan.RecordVault(vaultInterior);
		StrongholdFeatureEffects.ApplyToNewDefense(map, map.ParentFaction, data);
		if (plan.rewardBudgetTarget <= 0f) plan.rewardBudgetTarget = Mathf.Max(1f, RecoverableBudget(data));
		// Recalculate after the complete town and all native/EES/commander loadouts.
		plan.rewardBudgetFurniture = FurnitureValue(map, plan);
		plan.rewardBudgetEquipment = GarrisonEquipmentValue(map, map.ParentFaction);
		var protectedIds = new HashSet<int>(plan.rewardProtectedThingIds ?? new List<int>());
		foreach (int thingId in plan.nativeLootIds ?? new List<int>()) protectedIds.Add(thingId);
        // nativeLootIds is a bounded relocation list, while rewardProtectedThingIds
        // is the authoritative persisted protection set. Merge the relocation IDs
        // back into it before trimming so the post-generation audit uses the same
        // set that was used to cap the map.
        plan.rewardProtectedThingIds = protectedIds.OrderBy(id => id).ToList();
        var budgetArea = plan.baseRect.Width > 0 ? plan.baseRect : vaultInterior;
        var items = RecoverableItems(map, vaultInterior);
        var budgetItems = RecoverableItems(map, budgetArea);
        // Contents already inside a building or packed item are mandatory. Cap
        // trimming never destroys an entire holder to make optional loot fit.
        foreach (var held in budgetItems.Where(t => !t.Spawned || t is IThingHolder ||
            t is ThingWithComps tc && tc.AllComps.Any(c => c is IThingHolder))) protectedIds.Add(held.thingIDNumber);
        // Stone chunks are native map resources rather than generated rewards. They
        // may lie in the outer base rectangle, outside the center/town footprint,
        // so capture them before cap trimming and persist the same IDs used below.
        foreach (var chunk in budgetItems.Where(t => t.def.IsWithinCategory(ThingCategoryDefOf.Chunks)))
            protectedIds.Add(chunk.thingIDNumber);
        plan.rewardProtectedThingIds = protectedIds.OrderBy(id => id).ToList();
        float total = items.Sum(t => t.MarketValue * t.stackCount);
        float protectedInside = items.Where(t => protectedIds.Contains(t.thingIDNumber)).Sum(t => t.MarketValue * t.stackCount);
        float protectedValue = budgetItems.Where(t => protectedIds.Contains(t.thingIDNumber)).Sum(t => t.MarketValue * t.stackCount);
        float protectedOutside = Mathf.Max(0f, protectedValue - protectedInside);
		float generatedAllowance = Mathf.Max(0f, plan.rewardBudgetTarget - plan.rewardBudgetFurniture -
			plan.rewardBudgetEquipment - protectedOutside - protectedInside);
		float cap = protectedInside + generatedAllowance;
		if (total > cap)
		{
			foreach (Thing thing in items.Where(t => !protectedIds.Contains(t.thingIDNumber))
				.OrderBy(t => PriorityReward(t) ? 1 : 0)
				.ThenBy(t => t.MarketValue * t.stackCount).ToList())
			{
				if (total <= cap) break;
				float value = thing.MarketValue * thing.stackCount;
				thing.Destroy(DestroyMode.Vanish);
				total -= value;
			}
		}
        float vaultGenerated = RecoverableItems(map, vaultInterior)
            .Where(t => !protectedIds.Contains(t.thingIDNumber))
            .Sum(t => t.MarketValue * t.stackCount);
        var insideIds = new HashSet<int>(items.Select(t => t.thingIDNumber));
        var roleItems = budgetItems.Where(t => !insideIds.Contains(t.thingIDNumber) && !protectedIds.Contains(t.thingIDNumber)).ToList();
        float roleAllowance = Mathf.Max(0f, generatedAllowance - vaultGenerated);
        float roleTotal = roleItems.Sum(t => t.MarketValue * t.stackCount);
        if (roleTotal > roleAllowance)
        {
            foreach (Thing thing in roleItems
                .OrderBy(t => PriorityReward(t) ? 1 : 0)
                .ThenBy(t => t.MarketValue * t.stackCount).ToList())
            {
                if (roleTotal <= roleAllowance) break;
                float value = thing.MarketValue * thing.stackCount;
                thing.Destroy(DestroyMode.Vanish);
                roleTotal -= value;
            }
        }
        EnsureMinimumVaultLoot(map, data, vaultInterior, protectedIds, ref total);
        plan.rewardBudgetProtected = protectedInside + protectedOutside;
        plan.rewardBudgetGenerated = RecoverableItems(map, budgetArea)
            .Where(t => !protectedIds.Contains(t.thingIDNumber))
            .Sum(t => t.MarketValue * t.stackCount);
		plan.rewardBudgetFinalized = true;
		plan.rewardBudgetOverflow = Mathf.Max(0f, plan.rewardBudgetProtected + plan.rewardBudgetFurniture +
			plan.rewardBudgetEquipment + plan.rewardBudgetGenerated - plan.rewardBudgetTarget);
		if (plan.rewardBudgetOverflow > 1f)
			EESLog.Warning($"Reward mandatory/native contents exceed the tier budget by {plan.rewardBudgetOverflow:0.##}; existing gear/furniture retained, additional treasure capped. {map.Parent.LabelCap}");
		plan.lootFinalized = true;
		data.vaultValue = total;
	}

	private static void EnsureMinimumVaultLoot(Map map, StrongholdData data, CellRect vaultInterior,
		HashSet<int> protectedIds, ref float total)
	{
		if (map == null || data == null || vaultInterior.Width <= 0 || vaultInterior.Height <= 0 ||
			map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
				.Any(thing => thing != null && thing.def?.category == ThingCategory.Item && vaultInterior.Contains(thing.Position) &&
					thing.MarketValue * thing.stackCount > 0f))
		{
			return;
		}

		ThingDef silver = ThingDefOf.Silver;
		if (silver == null || !StrongholdLootFilter.IsDroppableLoot(silver, "vault")) return;
		var occupied = new HashSet<IntVec3>();
		var shelfCells = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
			.Where(thing => thing is Building_Storage && vaultInterior.Contains(thing.Position))
			.SelectMany(thing => thing.OccupiedRect().Cells)
			.Where(vaultInterior.Contains).Distinct().ToList();
		int placed = SpawnStacked(map, silver, 1, vaultInterior, occupied, shelfCells);
		if (placed <= 0) return;

		Thing item = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
			.Where(thing => thing != null && thing.def == silver && vaultInterior.Contains(thing.Position))
			.OrderByDescending(thing => thing.thingIDNumber).FirstOrDefault();
		if (item == null) return;
		protectedIds.Remove(item.thingIDNumber);
		total += item.MarketValue * item.stackCount;
		EESLog.Message($"Reward floor: added one Silver to empty vault for {map.ParentFaction?.Name} T{data.tier} at {item.Position}.");
	}

	private static bool PriorityReward(Thing thing)
	{
		return thing?.TryGetComp<CompQuality>()?.Quality >= QualityCategory.Good;
	}

	private static float ValueOfIds(Map map, HashSet<int> ids)
	{
		if (map == null || ids == null || ids.Count == 0) return 0f;
		return map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
			.Where(t => t != null && t.def?.category == ThingCategory.Item && ids.Contains(t.thingIDNumber))
			.Sum(t => t.MarketValue * t.stackCount);
	}

	private static float FurnitureValue(Map map, StrongholdGenerationPlan plan)
	{
		CellRect area = plan.baseRect.Width > 0 ? plan.baseRect : plan.centerRect;
		if (map == null || area.Width <= 0 || area.Height <= 0) return 0f;
		return map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
			.Where(t => t != null && area.Contains(t.Position) && t.Faction == map.ParentFaction &&
				!StrongholdCenterPlanner.StructuralWall(t.def) && t.def.building?.IsTurret != true)
			.Sum(t => Mathf.Max(0f, t.MarketValue));
	}

    /// <summary>Counts loose and held recoverables once, excluding living pawn loadouts.</summary>
    public static List<Thing> RecoverableItems(Map map, CellRect area)
    {
        var result = new List<Thing>();
        if (map == null || area.Width <= 0 || area.Height <= 0) return result;
        var seen = new HashSet<int>();
        void Add(Thing thing)
        {
            // MarketValue already inherits the inner thing's value for a packed
            // item. Include its wrapper and contents, but never price its inner
            // building/item a second time.
            if (thing != null && !thing.Destroyed && thing.def.category == ThingCategory.Item &&
                thing is not Plant && thing.ParentHolder is not MinifiedThing && seen.Add(thing.thingIDNumber)) result.Add(thing);
        }
        foreach (var root in map.listerThings.AllThings.Where(t => t is not Pawn && area.Contains(t.Position) &&
            (t.def.category == ThingCategory.Item || t is Building)))
        {
            Add(root);
            var holders = new List<IThingHolder>();
            if (root is IThingHolder holder) holders.Add(holder);
            if (root is ThingWithComps withComps) holders.AddRange(withComps.AllComps.OfType<IThingHolder>());
            foreach (var container in holders)
            {
                var contents = new List<Thing>();
                ThingOwnerUtility.GetAllThingsRecursively(container, contents, allowUnreal: false);
                foreach (var child in contents) Add(child);
            }
        }
        return result;
    }

	public static float GarrisonEquipmentValue(Map map, Faction faction)
	{
		if (map == null || faction == null || faction.IsPlayer) return 0f;
		var contents = new List<Thing>();
		foreach (var pawn in StrongholdPawnOwnership.Population(map, faction).Where(p => !p.IsPrisoner))
			contents.AddRange(ThingOwnerUtility.GetAllThingsRecursively(pawn, allowUnreal: false));
		return contents.Where(t => t != null && !t.Destroyed && t.def.category == ThingCategory.Item &&
			t.ParentHolder is not MinifiedThing).GroupBy(t => t.thingIDNumber).Select(g => g.First())
			.Sum(t => Mathf.Max(0f, t.MarketValue * t.stackCount));
	}

	public static float RecoverableBudget(StrongholdData data)
	{
		if (data == null) return 0f;
		int tier = Mathf.Clamp(data.tier, 1, 16);
		// A fixed tier curve funds the complete recoverable economy. Unspent
		// garrison/furniture allowance does not increase FillVault's loot multiplier.
		float gearAndTown = StrongholdGarrisonUtility.PopulationLimit(tier) * (160f + tier * 90f) + 400f * tier * tier;
		return EstimateVaultValue(data) + gearAndTown * FinalStandUtility.LootMultiplier(data);
	}

	public static float EstimateVaultValue(StrongholdData data)
	{
		if (data == null)
		{
			return 0f;
		}
		int contentTier = data.ContentTier;
		float num = (StrongholdScoreUtility.RewardBudgetMultiplier(data.tier, data.featureKey) + (float)data.supply * 0.02f) * (StrongholdMod.Settings?.lootMultiplier ?? 1f) * (PollutedOutpostUtility.IsPolluted(data.roleDefName) ? 1.1f : 1f) * FinalStandUtility.LootMultiplier(data);
		float num2 = 0f;
		num2 += 350f * num * MarketVal("Silver", 1f);
		if (contentTier >= 4)
		{
			num2 += 40f * num * MarketVal("Gold", 10f);
		}
		if (contentTier >= 5)
		{
			num2 += 60f * num * MarketVal("Plasteel", 2.7f);
			num2 += 4f * num * MarketVal("ComponentSpacer", 200f);
		}
		num2 += 5f * num * MarketVal("ComponentIndustrial", 12f);
		foreach (KeyValuePair<string, int> item in RoleLoot(data.roleDefName))
		{
			num2 += Mathf.Max(1f, (float)item.Value * num) * MarketVal(item.Key, 5f);
		}
		if (data.tier >= LegendaryGearTier)
		{
			num2 += ((data.tier >= 13) ? 6000f : 3000f);
		}
		return num2;
	}

	private static float MarketVal(string defName, float fallback)
	{
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
		if (namedSilentFail == null)
		{
			return fallback;
		}
		if (!StrongholdLootFilter.IsDroppableLoot(namedSilentFail))
		{
			return 0f;
		}
		return namedSilentFail.BaseMarketValue;
	}

	private static int FillInterior(Map map, Faction faction, StrongholdData data, CellRect core, float extraMul = 1f)
	{
		HashSet<IntVec3> occupied = new HashSet<IntVec3>();
		List<IntVec3> shelfCells = new List<IntVec3>();
		CellRect interior = core.ContractedBy(1);
		int num = 0;
		int contentTier = data.ContentTier;
		float valueMul = (StrongholdScoreUtility.RewardBudgetMultiplier(data.tier, data.featureKey) + (float)data.supply * 0.02f) * (StrongholdMod.Settings?.lootMultiplier ?? 1f) * (data.isOverlayBase ? 0.5f : 1f) * (PollutedOutpostUtility.IsPolluted(data.roleDefName) ? 1.1f : 1f) * extraMul * FinalStandUtility.LootMultiplier(data);
		Rand.PushState(Gen.HashCombineInt(data.settlementId, 65653));
		try
		{
			ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("Shelf");
			int num2 = Mathf.Clamp(3 + contentTier, 4, 10);
			for (int i = 0; i < num2; i++)
			{
				if (namedSilentFail == null)
				{
					break;
				}
				if (TryPlaceBuilding(map, faction, namedSilentFail, interior, occupied, shelfCells))
				{
					num++;
				}
			}
			if (data.tier >= LegendaryGearTier)
			{
				num += SpawnLegendaryGear(map, faction, data, interior, occupied);
			}
			num += SpawnFineTierRareGear(map, faction, data, interior, occupied);
			return num + SpawnTreasure(map, data, interior, occupied, shelfCells, valueMul);
		}
		finally
		{
			Rand.PopState();
		}
	}

	private static int SpawnFineTierRareGear(Map map, Faction faction, StrongholdData data, CellRect interior, HashSet<IntVec3> occupied)
	{
		if (data == null || data.tier < 1 || !Rand.Chance(StrongholdScoreUtility.RareRewardChance(data.tier))) return 0;
		bool wantWeapon = Rand.Chance(0.5f);
		ThingDef def = PickGearDef(faction, faction?.def?.techLevel ?? TechLevel.Industrial, wantWeapon, 70211 + data.tier);
		return def != null && SpawnQualityThing(map, def, StrongholdScoreUtility.RareRewardQuality(data.tier), interior, occupied) ? 1 : 0;
	}

	private static void BuildRoleRoom(Map map, Faction faction, StrongholdData data, CellRect settlementRect, CellRect vaultRect)
	{
		try
		{
			string roleDefName = data.roleDefName;
			int contentTier = data.ContentTier;
			int size = Mathf.Clamp(8 + contentTier, 9, 13);
			if (!PickRoleRoomRect(map, settlementRect, vaultRect, size, out var room))
			{
				EESLog.Message("RoleRoom [" + faction.Name + " role=" + roleDefName + "]: no space (skip).");
				return;
			}
			StrongholdDefenseUtility.ResolveWall(map, faction, contentTier, out var wallDef, out var stuff);
			ThingDef thingDef = StrongholdDefenseUtility.ResolveDoorDef(contentTier >= 5);
			ClearInterior(map, room);
			LayFloor(map, room, roleDefName);
			IntVec3 intVec = PickDoorCell(map, room, data.settlementId ^ 0x21);
			int num = BuildWallRing(map, faction, room, intVec, wallDef, stuff);
			bool flag = SpawnDoor(map, faction, intVec, thingDef, GenStuff.DefaultStuffFor(thingDef));
			CarveDoorOpening(map, intVec, room);
			HashSet<IntVec3> occupied = new HashSet<IntVec3>();
			List<IntVec3> shelfCells = new List<IntVec3>();
			CellRect interior = room.ContractedBy(1);
			int num2 = 0;
			Rand.PushState(Gen.HashCombineInt(data.settlementId, 133902));
			try
			{
				num2 += PlaceRequiredRoleBuildings(map, faction, data, interior, occupied, shelfCells);
				num2 += PlaceFacilitiesAndPrisoners(map, faction, data, interior, occupied);
				num2 += PlaceRoleShelfItems(map, faction, data, interior, occupied, shelfCells);
			}
			finally
			{
				Rand.PopState();
			}
			data.commandRoomCenter = room.CenterCell;
			int num3 = 0;
			if (RoleMatches(roleDefName, "mine"))
			{
				num3 = PlaceMineDrills(map, faction, settlementRect);
			}
			EESLog.Message($"RoleRoom [{faction.Name} role={roleDefName} T{contentTier}]: {room.Width}x{room.Height} @ {room.CenterCell} | " + string.Format("walls={0} doorOk={1} placed={2}{3}.", num, flag, num2, (num3 > 0) ? (" deepDrills=" + num3) : ""));
		}
		catch (Exception ex)
		{
			EESLog.Error("BuildRoleRoom failed: " + ex);
		}
	}

	private static bool PickRoleRoomRect(Map map, CellRect settlementRect, CellRect vaultRect, int size, out CellRect room)
	{
		room = CellRect.Empty;
		int num = size / 2;
		IntVec3 centerCell = vaultRect.CenterCell;
		IntVec3[] array = new IntVec3[8]
		{
			new IntVec3(1, 0, 0),
			new IntVec3(-1, 0, 0),
			new IntVec3(0, 0, 1),
			new IntVec3(0, 0, -1),
			new IntVec3(1, 0, 1),
			new IntVec3(1, 0, -1),
			new IntVec3(-1, 0, 1),
			new IntVec3(-1, 0, -1)
		};
		int num2 = vaultRect.Width / 2 + 3 + num;
		CellRect cellRect = CellRect.Empty;
		int num3 = int.MaxValue;
		for (int i = num2; i <= num2 + 16; i += 2)
		{
			CellRect cellRect2 = CellRect.Empty;
			int num4 = int.MaxValue;
			IntVec3[] array2 = array;
			for (int j = 0; j < array2.Length; j++)
			{
				IntVec3 intVec = array2[j];
				CellRect cellRect3 = CellRect.CenteredOn(centerCell + new IntVec3(intVec.x * i, 0, intVec.z * i), num);
				if (cellRect3.minX >= 3 && cellRect3.minZ >= 3 && cellRect3.maxX < map.Size.x - 3 && cellRect3.maxZ < map.Size.z - 3 && !cellRect3.Overlaps(vaultRect.ExpandedBy(2)))
				{
					int num5 = CountImpassable(map, cellRect3.ExpandedBy(1));
					if (num5 < num4)
					{
						num4 = num5;
						cellRect2 = cellRect3;
					}
					if (num5 < num3)
					{
						num3 = num5;
						cellRect = cellRect3;
					}
				}
			}
			if (cellRect2.Area > 0 && num4 <= 6)
			{
				room = cellRect2;
				return true;
			}
		}
		if (cellRect.Area <= 0)
		{
			return false;
		}
		room = cellRect;
		return true;
	}

	private static int CountImpassable(Map map, CellRect rect)
	{
		int num = 0;
		foreach (IntVec3 item in rect)
		{
			if (!item.InBounds(map))
			{
				num += 2;
				continue;
			}
			Building edifice = item.GetEdifice(map);
			if (edifice != null && !edifice.Destroyed && edifice.def.passability == Traversability.Impassable)
			{
				num++;
			}
		}
		return num;
	}

	private static int PlaceRequiredRoleBuildings(Map map, Faction faction, StrongholdData data, CellRect interior, HashSet<IntVec3> occupied, List<IntVec3> shelfCells)
	{
		int placed = 0;
		string roleDefName = data.roleDefName;
		int contentTier = data.ContentTier;
		if (RoleMatches(roleDefName, "research"))
		{
			Req((contentTier >= 5) ? "HiTechResearchBench" : "SimpleResearchBench", 2);
			Req("Shelf", 2);
		}
		else if (RoleMatches(roleDefName, "ammo"))
		{
			Req("Shelf", 4);
		}
		else if (RoleMatches(roleDefName, "supply"))
		{
			Req("NutrientPasteDispenser", 1);
			Req("NutrientPasteHopper", 2);
			Req("Shelf", 2);
		}
		else if (RoleMatches(roleDefName, "prison"))
		{
			Req("Shelf", 2);
		}
		else if (RoleMatches(roleDefName, "mortar"))
		{
			Req("Shelf", 3);
		}
		else if (RoleMatches(roleDefName, "black") || RoleMatches(roleDefName, "market"))
		{
			Req("BilliardsTable", 1);
			Req("PokerTable", 1);
			Req("Shelf", 2);
		}
		else if (RoleMatches(roleDefName, "noble") || RoleMatches(roleDefName, "estate"))
		{
			Req("Throne", 1);
			Req("Brazier", 2);
			Req("Drape", 2);
		}
		else if (RoleMatches(roleDefName, "command") || RoleMatches(roleDefName, "fortress"))
		{
			Req("Shelf", 1);
		}
		else if (RoleMatches(roleDefName, "mine"))
		{
			Req("GroundPenetratingScanner", 1);
			Req("Shelf", 2);
		}
		else
		{
			Req("Shelf", 2);
		}
		return placed;
		void Req(string defName, int count)
		{
			ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
			if (namedSilentFail != null)
			{
				for (int i = 0; i < count; i++)
				{
					if (TryPlaceBuilding(map, faction, namedSilentFail, interior, occupied, shelfCells))
					{
						placed++;
					}
				}
			}
		}
	}

	private static int PlaceRoleShelfItems(Map map, Faction faction, StrongholdData data, CellRect interior, HashSet<IntVec3> occupied, List<IntVec3> shelfCells)
	{
		int n = 0;
		string roleDefName = data.roleDefName;
		int contentTier = data.ContentTier;
		bool active = CECompat.Active;
		float mul = 0.8f + (float)contentTier * 0.5f;
		if (RoleMatches(roleDefName, "ammo"))
		{
			Item("Shell_HighExplosive", 12);
			Item("Shell_EMP", 6);
			Item("Shell_Incendiary", 6);
			if (active)
			{
				foreach (ThingDef item in CECompat.PickLootAmmo(faction, 4))
				{
					n += SpawnStacked(map, item, Mathf.RoundToInt(120f * Mathf.Min(mul, 3f)), interior, occupied, shelfCells);
				}
			}
		}
		else if (RoleMatches(roleDefName, "mortar"))
		{
			Item("Shell_HighExplosive", 14);
			Item("Chemfuel", 120);
			if (active)
			{
				Item("Prometheum", 120);
			}
		}
		else if (RoleMatches(roleDefName, "supply"))
		{
			Item("MealSimple", 18);
			Item("Pemmican", 80);
		}
		else if (RoleMatches(roleDefName, "prison"))
		{
			Item("Apparel_CollarShirt", 3);
			Item("Apparel_Jacket", 2);
			Item("Apparel_Pants", 3);
			if (ModsConfig.IdeologyActive)
			{
				Item("Apparel_Blindfold", 2);
				Item("Apparel_BodyStrap", 2);
				Item("Apparel_Collar", 2);
			}
		}
		else if (RoleMatches(roleDefName, "mine"))
		{
			Item("Steel", 180);
			Item("ComponentIndustrial", 8);
			Item("Plasteel", 50);
		}
		else if (RoleMatches(roleDefName, "black") || RoleMatches(roleDefName, "market"))
		{
			Item("Silver", 200);
			Item("Gold", 40);
		}
		return n;
		void Item(string defName, int amt)
		{
			n += SpawnStackedNamed(map, defName, Mathf.Max(1, Mathf.RoundToInt((float)amt * mul)), interior, occupied, shelfCells);
		}
	}

	public static int PlaceMineDrills(Map map, Faction faction, CellRect settlementRect)
	{
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("DeepDrill");
		if (namedSilentFail == null)
		{
			return 0;
		}
		int num = Rand.RangeInclusive(3, 4);
		CellRect cellRect = settlementRect.ExpandedBy(4);
		int num2 = 0;
		int num3 = 0;
		while (num2 < num && num3 < 300)
		{
			num3++;
			IntVec3 intVec = CellFinder.RandomCell(map);
			if (!cellRect.Contains(intVec) && intVec.InBounds(map) && intVec.Standable(map) && intVec.GetEdifice(map) == null && !intVec.Roofed(map))
			{
				try
				{
					Thing thing = ThingMaker.MakeThing(namedSilentFail, namedSilentFail.MadeFromStuff ? GenStuff.DefaultStuffFor(namedSilentFail) : null);
					thing.SetFactionDirect(faction);
					GenSpawn.Spawn(thing, intVec, map);
					num2++;
				}
				catch
				{
				}
			}
		}
		return num2;
	}

	private static int PlaceFacilitiesAndPrisoners(Map map, Faction faction, StrongholdData data, CellRect interior, HashSet<IntVec3> occupied)
	{
		int num = 0;
		int contentTier = data.ContentTier;
		StringBuilder stringBuilder = new StringBuilder();
		if (data.tier >= 6 && (RoleMatches(data.roleDefName, "command") || RoleMatches(data.roleDefName, "fortress") || data.tier >= 8) && PlaceFacility(map, faction, "CommsConsole", interior, occupied))
		{
			data.hasCommsRoom = true;
			num++;
			stringBuilder.Append("Comms ");
		}
		if (RoleMatches(data.roleDefName, "ammo"))
		{
			data.hasAmmoDepot = true;
		}
		if (RoleMatches(data.roleDefName, "mortar"))
		{
			data.hasMortarBattery = true;
		}
		EESLog.Message($"CoreZone facilities [{faction.Name} T{contentTier} role={data.roleDefName}]: " + ((stringBuilder.Length > 0) ? stringBuilder.ToString().TrimEnd(Array.Empty<char>()) : "none") + " | " + $"flags(comms={data.hasCommsRoom},ammo={data.hasAmmoDepot}," + $"mortar={data.hasMortarBattery},prison={data.hasPrison})");
		return num;
	}

	private static bool RoleMatches(string roleDefName, string token)
	{
		if (!roleDefName.NullOrEmpty())
		{
			return roleDefName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
		}
		return false;
	}

	private static bool PlaceFacility(Map map, Faction faction, string defName, CellRect interior, HashSet<IntVec3> occupied)
	{
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
		if (namedSilentFail == null)
		{
			return false;
		}
		return TryPlaceBuilding(map, faction, namedSilentFail, interior, occupied, null);
	}

	private static void BuildPrisonBlock(Map map, Faction faction, StrongholdData data, CellRect settlementRect, CellRect vaultRect)
	{
		try
		{
			List<Pawn> list = new List<Pawn>();
			if (data.capturedPrisoners != null)
			{
				data.capturedPrisoners.RemoveAll((Pawn p) => p == null || p.Dead || p.Destroyed || p.Spawned || p.Faction == Faction.OfPlayer);
				list.AddRange(data.capturedPrisoners);
			}
			int num = (RoleMatches(data.roleDefName, "prison") ? Mathf.Clamp(1 + data.ContentTier / 2, 1, 4) : 0);
			int num2 = list.Count + num;
			if (num2 > 0)
			{
				int size = Mathf.Clamp(num2 + 4, 6, 10);
				if (!PickRoleRoomRect(map, settlementRect, vaultRect, size, out var room))
				{
					EESLog.Message("Prison [" + faction.Name + "]: no space for sealed cell block (skip).");
					return;
				}
				StrongholdDefenseUtility.ResolveWall(map, faction, data.ContentTier, out var wallDef, out var stuff);
				ClearInterior(map, room);
				int num3 = BuildWallRing(map, faction, room, IntVec3.Invalid, wallDef, stuff);
				data.RegisterSealedRect(room, "감방");
				int num4 = PopulatePrison(map, faction, data, room.ContractedBy(1), list, num);
				EESLog.Message($"Prison [{faction.Name} T{data.tier}]: sealed cell {room.Width}x{room.Height} @ {room.CenterCell} " + $"walls={num3} imprisoned={num4} (captured={list.Count} generated={num}). 봉인—breach로 구출.");
			}
		}
		catch (Exception ex)
		{
			EESLog.Error("BuildPrisonBlock failed: " + ex);
		}
	}

	public static int PopulatePrison(Map map, Faction faction, StrongholdData data, CellRect interior, List<Pawn> captured, int genCount)
	{
		ThingDef bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("PrisonerBed") ?? DefDatabase<ThingDef>.GetNamedSilentFail("Bed");
		HashSet<IntVec3> occ = new HashSet<IntVec3>();
		int placed = 0;
		if (captured != null)
		{
			foreach (Pawn item in new List<Pawn>(captured))
			{
				try
				{
					Imprison(item);
				}
				catch (Exception ex)
				{
					EESLog.Error("Prison captured " + item?.LabelShort + ": " + ex.Message);
				}
			}
		}
		for (int i = 0; i < genCount; i++)
		{
			Pawn pawn = MakePrisonerPawn(map, faction);
			if (pawn == null)
			{
				break;
			}
			try
			{
				Imprison(pawn);
			}
			catch (Exception ex2)
			{
				EESLog.Error("Prison generated: " + ex2.Message);
				if (!pawn.Destroyed)
				{
					pawn.Destroy();
				}
			}
		}
		if (placed > 0)
		{
			data.hasPrison = true;
		}
		EESLog.Message($"PopulatePrison [{faction?.Name} role={data.roleDefName}]: placed={placed} " + $"(captured={captured?.Count ?? 0}, genCount={genCount}) interior={interior.Width}x{interior.Height} @ {interior.CenterCell}.");
		return placed;
		void Imprison(Pawn p)
		{
			if (p != null && !p.Spawned)
			{
				IntVec3 intVec = FindFreeStandableCell(map, interior, occ);
				if (intVec.IsValid)
				{
					if (bedDef != null && bedDef.size == IntVec2.One)
					{
						Thing thing = ThingMaker.MakeThing(bedDef, bedDef.MadeFromStuff ? GenStuff.DefaultStuffFor(bedDef) : null);
						thing.SetFactionDirect(faction);
						GenSpawn.Spawn(thing, intVec, map);
					}
					GenSpawn.Spawn(p, intVec, map);
					if (p.guest != null)
					{
						p.guest.SetGuestStatus(faction, GuestStatus.Prisoner);
					}
					if (ModsConfig.AnomalyActive && p.creepjoiner != null && p.creepjoiner.form != null)
					{
						try
						{
							p.creepjoiner.Notify_Created();
						}
						catch (Exception ex3)
						{
							EESLog.Error("creepjoiner Notify_Created: " + ex3.Message);
						}
					}
					try
					{
						if (p.health != null)
						{
							HediffDef namedSilentFail = DefDatabase<HediffDef>.GetNamedSilentFail("EES_PrisonerStasis");
							if (namedSilentFail != null)
							{
								if (!p.health.hediffSet.HasHediff(namedSilentFail))
								{
									p.health.AddHediff(namedSilentFail);
								}
							}
							else
							{
								EESLog.Warning("EES_PrisonerStasis def 없음 → 마취 폴백(시한부).");
								if (!p.Downed)
								{
									p.health.AddHediff(HediffDefOf.Anesthetic);
								}
							}
						}
					}
					catch (Exception ex4)
					{
						EESLog.Error("Imprison stasis: " + ex4.Message);
					}
					occ.Add(intVec);
					placed++;
					EESLog.Message($"Imprison: {p.LabelShortCap} @ {intVec} (creepjoiner={p.creepjoiner != null}, downed={p.Downed}).");
				}
			}
		}
	}

	public static int PopulatePrisonForLayout(Map map, Faction faction, StrongholdData data, CellRect interior)
	{
		List<Pawn> list = new List<Pawn>();
		if (data.capturedPrisoners != null)
		{
			data.capturedPrisoners.RemoveAll((Pawn p) => p == null || p.Dead || p.Destroyed || p.Spawned || p.Faction == Faction.OfPlayer);
			list.AddRange(data.capturedPrisoners);
		}
		int num = (RoleMatches(data.roleDefName, "prison") ? Mathf.Clamp(1 + data.ContentTier / 2, 1, 4) : 0);
		EESLog.Message("PopulatePrisonForLayout [" + faction?.Name + " role=" + data.roleDefName + "]: " + string.Format("capturedScouts={0} genCount={1} (ContentTier={2}, isPrison={3}).", list.Count, num, data.ContentTier, RoleMatches(data.roleDefName, "prison")));
		return PopulatePrison(map, faction, data, interior, list, num);
	}

	private static Pawn MakePrisonerPawn(Map map, Faction captor)
	{
		try
		{
			bool anomalyActive = ModsConfig.AnomalyActive;
			bool flag = anomalyActive && Rand.Chance(0.05f);
			EESLog.Message($"MakePrisonerPawn 롤: anomaly={anomalyActive} creepRoll={flag} (anomaly false면 크립조이너 불가).");
			if (flag)
			{
				Pawn pawn = TryMakeCreepJoinerPrisoner(map, captor);
				if (pawn != null)
				{
					if (pawn.guest != null)
					{
						pawn.guest.SetGuestStatus(captor, GuestStatus.Prisoner);
					}
					EESLog.Message("MakePrisonerPawn → 크립조이너 포로 반환: " + pawn.LabelShortCap + ".");
					return pawn;
				}
				EESLog.Warning("MakePrisonerPawn: 크립조이너 생성 실패(null) → 일반 포로 폴백.");
			}
			Faction faction = null;
			PawnKindDef pawnKindDef = DefaultPrisonerKind();
			if (pawnKindDef == null)
			{
				EESLog.Error("MakePrisonerPawn: no usable PawnKindDef found");
				return null;
			}
			Pawn pawn2 = PawnGenerator.GeneratePawn(new PawnGenerationRequest(pawnKindDef, faction, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true, allowDead: false, allowDowned: false, canGeneratePawnRelations: false, mustBeCapableOfViolence: false, 0f));
			if (Rand.Chance(0.1f))
			{
				TryApplySkillSpecialization(pawn2);
			}
			if (pawn2.guest != null)
			{
				pawn2.guest.SetGuestStatus(captor, GuestStatus.Prisoner);
			}
			return pawn2;
		}
		catch (Exception ex)
		{
			EESLog.Error("MakePrisonerPawn failed: " + ex.Message);
			return null;
		}
	}

	private static void TryApplySkillSpecialization(Pawn p)
	{
		try
		{
			if (p == null || p.skills == null)
			{
				return;
			}
			List<SkillRecord> list = new List<SkillRecord>();
			List<string> list2 = new List<string>();
			foreach (SkillRecord skill in p.skills.skills)
			{
				if (skill != null)
				{
					if (skill.TotallyDisabled)
					{
						list2.Add(skill.def.defName);
					}
					else
					{
						list.Add(skill);
					}
				}
			}
			if (list.Count == 0)
			{
				EESLog.Warning("SkillSpec " + p.LabelShortCap + ": 사용가능 스킬 0개(전부 비활성) → skip. 비활성=[" + string.Join(",", list2) + "]");
				return;
			}
			list.Shuffle();
			int num = Mathf.Min(Rand.RangeInclusive(1, 2), list.Count);
			int num2 = 0;
			StringBuilder stringBuilder = new StringBuilder();
			for (int i = 0; i < list.Count; i++)
			{
				if (num2 >= num)
				{
					break;
				}
				SkillRecord skillRecord = list[i];
				int num3 = Rand.RangeInclusive(10, 16);
				if (skillRecord.GetLevel() < num3)
				{
					skillRecord.Level = Mathf.Clamp(num3 - skillRecord.Aptitude, 0, 20);
				}
				skillRecord.xpSinceMidnight = 0f;
				skillRecord.xpSinceLastLevel = 0f;
				int level = skillRecord.GetLevel();
				if (level < 10)
				{
					EESLog.Message($"SkillSpec {p.LabelShortCap}: {skillRecord.def.defName} 적성과다음수(apt={skillRecord.Aptitude}) → 표시{level}<10, 다른 스킬 시도.");
					continue;
				}
				if (skillRecord.passion == Passion.None)
				{
					skillRecord.passion = ((!Rand.Chance(0.35f)) ? Passion.Minor : Passion.Major);
				}
				if (stringBuilder.Length > 0)
				{
					stringBuilder.Append(", ");
				}
				stringBuilder.Append(string.Format("{0}={1}(apt{2}{3},{4})", skillRecord.def.defName, level, (skillRecord.Aptitude >= 0) ? "+" : "", skillRecord.Aptitude, skillRecord.passion));
				num2++;
			}
			EESLog.Message(string.Format("SkillSpec {0}: {1}/{2} 부여 [{3}] | 비활성제외=[{4}]", p.LabelShortCap, num2, num, stringBuilder, string.Join(",", list2)));
		}
		catch (Exception ex)
		{
			EESLog.Error("TryApplySkillSpecialization: " + ex.Message);
		}
	}

	private static Pawn TryMakeCreepJoinerPrisoner(Map map, Faction captor)
	{
		try
		{
			if (!ModsConfig.AnomalyActive)
			{
				return null;
			}
			CreepJoinerFormKindDef form = null;
			CreepJoinerBenefitDef benefit = null;
			CreepJoinerDownsideDef downside = null;
			CreepJoinerAggressiveDef aggressive = null;
			CreepJoinerRejectionDef rejection = null;
			CreepJoinerUtility.GetCreepjoinerSpecifics(map, ref form, ref benefit, ref downside, ref aggressive, ref rejection);
			if (form == null)
			{
				EESLog.Warning("Creepjoiner: GetCreepjoinerSpecifics가 form=null 반환(Anomaly def 없음?) → 폴백.");
				return null;
			}
			EESLog.Message("Creepjoiner roll: form=" + form.defName + " benefit=" + benefit?.defName + " downside=" + downside?.defName + " aggressive=" + aggressive?.defName + " rejection=" + rejection?.defName + ".");
			PawnGenerationRequest request = new PawnGenerationRequest(form, null, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true, allowDead: false, allowDowned: false, canGeneratePawnRelations: false, mustBeCapableOfViolence: true, 0f);
			request.IsCreepJoiner = true;
			request.DontGiveWeapon = true;
			request.ForceNoIdeoGear = true;
			request.AllowedDevelopmentalStages = DevelopmentalStage.Adult;
			request.MaximumAgeTraits = 1;
			request.MinimumAgeTraits = 1;
			request.OnlyUseForcedBackstories = !form.fixedAdultBackstories.NullOrEmpty();
			Pawn pawn = PawnGenerator.GeneratePawn(request);
			if (pawn == null || pawn.creepjoiner == null)
			{
				EESLog.Warning("Creepjoiner: GeneratePawn 결과 부적합(p=" + ((pawn == null) ? "null" : "ok") + " creepjoinerTracker=" + ((pawn?.creepjoiner == null) ? "null" : "ok") + ") → 폴백.");
				if (pawn != null && !pawn.Destroyed)
				{
					pawn.Destroy();
				}
				return null;
			}
			Pawn_CreepJoinerTracker creepjoiner = pawn.creepjoiner;
			creepjoiner.form = form;
			creepjoiner.benefit = benefit;
			creepjoiner.downside = downside;
			creepjoiner.aggressive = aggressive;
			creepjoiner.rejection = rejection;
			ApplyCreepTraits(pawn, benefit?.traits);
			ApplyCreepTraits(pawn, downside?.traits);
			ApplyCreepHediffs(pawn, benefit?.hediffs);
			ApplyCreepHediffs(pawn, downside?.hediffs);
			ApplyCreepSkills(pawn, benefit?.skills);
			ApplyCreepAbilities(pawn, benefit?.abilities);
			ApplyCreepAbilities(pawn, downside?.abilities);
			if (pawn.guest != null)
			{
				pawn.guest.Recruitable = true;
			}
			string text = pawn.story?.Childhood?.defName ?? "null";
			string text2 = pawn.story?.Adulthood?.defName ?? "null";
			try
			{
				if (pawn.story != null)
				{
					if (!form.fixedChildBackstories.NullOrEmpty())
					{
						pawn.story.Childhood = form.fixedChildBackstories.RandomElement();
					}
					if (!form.fixedAdultBackstories.NullOrEmpty() && pawn.ageTracker != null && pawn.ageTracker.AgeBiologicalYearsFloat >= 20f)
					{
						pawn.story.Adulthood = form.fixedAdultBackstories.RandomElement();
					}
				}
			}
			catch (Exception ex)
			{
				EESLog.Error("Creepjoiner backstory force: " + ex.Message);
			}
			string text3 = ((pawn.story?.traits != null) ? string.Join(",", pawn.story.traits.allTraits.Select((Trait t) => t.def.defName + ((t.Degree != 0) ? (":" + t.Degree) : ""))) : "");
			string text4 = ((pawn.abilities != null) ? string.Join(",", pawn.abilities.AllAbilitiesForReading.Select((Ability a) => a.def.defName)) : "");
			EESLog.Message($"Creepjoiner prisoner: {pawn.LabelShortCap} race={pawn.def?.defName} age={pawn.ageTracker?.AgeBiologicalYears} " + "childhood=" + text + "->" + pawn.story?.Childhood?.defName + " adulthood=" + text2 + "->" + pawn.story?.Adulthood?.defName + " traits=[" + text3 + "] abilities=[" + text4 + "] | form=" + form.defName + " benefit=" + benefit?.defName + " downside=" + downside?.defName);
			return pawn;
		}
		catch (Exception ex2)
		{
			EESLog.Error("TryMakeCreepJoinerPrisoner: " + ex2);
			return null;
		}
	}

	private static void ApplyCreepTraits(Pawn p, List<BackstoryTrait> traits)
	{
		if (traits == null || p.story == null)
		{
			return;
		}
		foreach (BackstoryTrait trait in traits)
		{
			if (trait?.def != null && !p.story.traits.HasTrait(trait.def))
			{
				p.story.traits.GainTrait(new Trait(trait.def, trait.degree, forced: true));
			}
		}
	}

	private static void ApplyCreepHediffs(Pawn p, List<HediffDef> hediffs)
	{
		if (hediffs == null || p.health == null)
		{
			return;
		}
		foreach (HediffDef hediff in hediffs)
		{
			if (hediff != null)
			{
				p.health.AddHediff(hediff);
			}
		}
	}

	private static void ApplyCreepSkills(Pawn p, List<CreepJoinerBenefitDef.SkillValue> skills)
	{
		if (skills == null || p.skills == null)
		{
			return;
		}
		foreach (CreepJoinerBenefitDef.SkillValue skill2 in skills)
		{
			if (skill2?.skill != null)
			{
				SkillRecord skill = p.skills.GetSkill(skill2.skill);
				if (skill != null)
				{
					skill.Level = skill2.range.RandomInRange;
					skill.xpSinceMidnight = 0f;
					skill.xpSinceLastLevel = 0f;
				}
			}
		}
	}

	private static void ApplyCreepAbilities(Pawn p, List<AbilityDef> abilities)
	{
		if (abilities == null || p.abilities == null)
		{
			return;
		}
		foreach (AbilityDef ability in abilities)
		{
			if (ability != null)
			{
				p.abilities.GainAbility(ability);
			}
		}
	}

	private static PawnKindDef DefaultPrisonerKind()
	{
		string[] array = new string[6] { "Villager", "Drifter", "Pilgrim", "Town_Guard", "Slave", "Colonist" };
		for (int i = 0; i < array.Length; i++)
		{
			PawnKindDef namedSilentFail = DefDatabase<PawnKindDef>.GetNamedSilentFail(array[i]);
			if (namedSilentFail != null && namedSilentFail.RaceProps != null && namedSilentFail.RaceProps.Humanlike)
			{
				return namedSilentFail;
			}
		}
		foreach (PawnKindDef item in DefDatabase<PawnKindDef>.AllDefsListForReading)
		{
			if (item.RaceProps != null && item.RaceProps.Humanlike)
			{
				return item;
			}
		}
		return null;
	}

	private static IntVec3 FindFreeStandableCell(Map map, CellRect interior, HashSet<IntVec3> occupied)
	{
		foreach (IntVec3 item in interior.Cells.InRandomOrder())
		{
			if (!occupied.Contains(item) && item.InBounds(map) && item.Standable(map) && item.GetEdifice(map) == null)
			{
				return item;
			}
		}
		return IntVec3.Invalid;
	}

	private static bool TryPlaceBuilding(Map map, Faction faction, ThingDef def, CellRect interior, HashSet<IntVec3> occupied, List<IntVec3> shelfCells)
	{
		List<IntVec3> list = interior.Cells.InRandomOrder().ToList();
		Rot4[] array = ((!def.rotatable) ? new Rot4[1] { Rot4.North } : new Rot4[2]
		{
			Rot4.North,
			Rot4.East
		});
		bool flag = IsStorageBuilding(def);
		foreach (IntVec3 item in list)
		{
			Rot4[] array2 = array;
			foreach (Rot4 rot in array2)
			{
				CellRect rect = GenAdj.OccupiedRect(item, rot, def.size);
				if (!FitsFree(map, rect, interior, occupied))
				{
					continue;
				}
				try
				{
					Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
					thing.SetFactionDirect(faction);
					GenSpawn.Spawn(thing, item, map, rot);
					foreach (IntVec3 item2 in rect)
					{
						occupied.Add(item2);
						if (flag)
						{
							shelfCells?.Add(item2);
						}
					}
					return true;
				}
				catch (Exception ex)
				{
					EESLog.Error($"CoreZone building {def.defName} @ {item}: {ex.Message}");
					return false;
				}
			}
		}
		return false;
	}

	private static bool IsStorageBuilding(ThingDef d)
	{
		if (d.thingClass != null)
		{
			return typeof(Building_Storage).IsAssignableFrom(d.thingClass);
		}
		return false;
	}

	private static bool FitsFree(Map map, CellRect rect, CellRect interior, HashSet<IntVec3> occupied)
	{
		foreach (IntVec3 item in rect)
		{
			if (!interior.Contains(item) || !item.InBounds(map))
			{
				return false;
			}
			if (occupied.Contains(item) || item.GetEdifice(map) != null || item.GetFirstItem(map) != null || !item.Walkable(map))
			{
				return false;
			}
		}
		return true;
	}

	private static IntVec3 NextFreeCell(Map map, CellRect interior, HashSet<IntVec3> occupied)
	{
		foreach (IntVec3 item in interior.Cells.InRandomOrder())
		{
			if (!occupied.Contains(item) && item.InBounds(map) && item.GetEdifice(map) == null && item.GetFirstItem(map) == null && item.Walkable(map))
			{
				return item;
			}
		}
		return IntVec3.Invalid;
	}

	private static int SpawnLegendaryGear(Map map, Faction faction, StrongholdData data, CellRect interior, HashSet<IntVec3> occupied)
	{
		TechLevel tech = faction?.def?.techLevel ?? TechLevel.Industrial;
		int num = 0;
		if (data.tier >= 13)
		{
			if (SpawnLegendaryWeapon(map, faction, data, tech, 0.4f, interior, occupied, 0))
			{
				num++;
			}
			if (SpawnLegendaryArmor(map, faction, tech, interior, occupied, 0))
			{
				num++;
			}
			return num;
		}
		Rand.PushState(Gen.HashCombineInt(data.settlementId, 19994));
		float value = Rand.Value;
		Rand.PopState();
		if (value < 0.5f)
		{
			if (SpawnLegendaryArmor(map, faction, tech, interior, occupied, 0))
			{
				num++;
			}
		}
		else if (value < 0.8f)
		{
			if (SpawnLegendaryWeapon(map, faction, data, tech, 0f, interior, occupied, 0))
			{
				num++;
			}
		}
		else if (SpawnLegendaryWeapon(map, faction, data, tech, 1f, interior, occupied, 0))
		{
			num++;
		}
		return num;
	}

	private static bool SpawnLegendaryWeapon(Map map, Faction faction, StrongholdData data, TechLevel tech, float uniqueChance, CellRect interior, HashSet<IntVec3> occupied, int slot)
	{
		ThingDef thingDef = null;
		bool flag = false;
		if (uniqueChance > 0f)
		{
			Rand.PushState(Gen.HashCombineInt(data.settlementId, 4583 + slot));
			flag = Rand.Value < uniqueChance;
			Rand.PopState();
			if (flag)
			{
				thingDef = PickUniqueWeaponDef(faction, tech, 11036 + slot);
			}
		}
		bool flag2 = thingDef != null;
		if (thingDef == null)
		{
			thingDef = PickGearDef(faction, tech, wantWeapon: true, 14891 + slot);
		}
		bool flag3 = thingDef != null && SpawnQualityThing(map, thingDef, QualityCategory.Legendary, interior, occupied);
		EESLog.Message($"CoreZone gear[weapon] {faction?.Name} T{data.tier}: " + $"uniqueChance={uniqueChance:0.00} rolledUnique={flag} gotUnique={flag2} " + string.Format("def={0} spawned={1}", thingDef?.defName ?? "none", flag3));
		return flag3;
	}

	private static bool SpawnLegendaryArmor(Map map, Faction faction, TechLevel tech, CellRect interior, HashSet<IntVec3> occupied, int slot)
	{
		ThingDef thingDef = PickGearDef(faction, tech, wantWeapon: false, 31805 + slot);
		bool flag = thingDef != null && SpawnQualityThing(map, thingDef, QualityCategory.Legendary, interior, occupied);
		EESLog.Message(string.Format("CoreZone gear[armor] {0}: def={1} spawned={2}", faction?.Name, thingDef?.defName ?? "none", flag));
		return flag;
	}

	private static ThingDef PickUniqueWeaponDef(Faction faction, TechLevel tech, int salt)
	{
		List<ThingDef> allDefsListForReading = DefDatabase<ThingDef>.AllDefsListForReading;
		List<ThingDef> list = new List<ThingDef>();
		for (int i = 0; i < allDefsListForReading.Count; i++)
		{
			ThingDef thingDef = allDefsListForReading[i];
			if (thingDef != null && thingDef.IsWeapon && IsUniqueTraitWeapon(thingDef) && (thingDef.techLevel == TechLevel.Undefined || (int)thingDef.techLevel <= (int)tech))
			{
				list.Add(thingDef);
			}
		}
		if (list.Count == 0)
		{
			return null;
		}
		ModContentPack raceMod = faction?.def?.modContentPack;
		if (raceMod != null && !raceMod.IsOfficialMod)
		{
			List<ThingDef> list2 = list.Where((ThingDef d) => d.modContentPack == raceMod).ToList();
			if (list2.Count > 0)
			{
				list = list2;
			}
		}
		Rand.PushState(Gen.HashCombineInt(faction?.loadID ?? 0, salt));
		ThingDef result = list[Rand.Range(0, list.Count)];
		Rand.PopState();
		return result;
	}

	private static bool IsUniqueTraitWeapon(ThingDef d)
	{
		if (d.comps == null)
		{
			return false;
		}
		for (int i = 0; i < d.comps.Count; i++)
		{
			Type type = d.comps[i]?.compClass;
			if (type != null && type.Name == "CompUniqueWeapon")
			{
				return true;
			}
		}
		return false;
	}

	private static ThingDef PickGearDef(Faction faction, TechLevel tech, bool wantWeapon, int salt)
	{
		ModContentPack raceMod = faction?.def?.modContentPack;
		List<ThingDef> allDefsListForReading = DefDatabase<ThingDef>.AllDefsListForReading;
		List<ThingDef> list = new List<ThingDef>();
		for (int i = 0; i < allDefsListForReading.Count; i++)
		{
			ThingDef thingDef = allDefsListForReading[i];
			if (thingDef != null && !thingDef.destroyOnDrop && (wantWeapon ? thingDef.IsWeapon : thingDef.IsApparel) && thingDef.techLevel != TechLevel.Undefined && (int)thingDef.techLevel <= (int)tech && thingDef.HasComp(typeof(CompQuality)))
			{
				float baseMarketValue = thingDef.BaseMarketValue;
				if (!(baseMarketValue < 80f) && !(baseMarketValue > 30000f))
				{
					list.Add(thingDef);
				}
			}
		}
		if (list.Count == 0)
		{
			return null;
		}
		if (raceMod != null && !raceMod.IsOfficialMod)
		{
			List<ThingDef> list2 = list.Where((ThingDef d) => d.modContentPack == raceMod).ToList();
			list = ((list2.Count > 0) ? list2 : list.Where((ThingDef d) => d.modContentPack == null || d.modContentPack.IsOfficialMod).ToList());
		}
		else
		{
			list = list.Where((ThingDef d) => d.modContentPack == null || d.modContentPack.IsOfficialMod).ToList();
		}
		if (list.Count == 0)
		{
			return null;
		}
		List<ThingDef> list3 = list.OrderByDescending((ThingDef d) => d.BaseMarketValue).ToList();
		int maxExclusive = Mathf.Clamp(list3.Count, 1, 6);
		Rand.PushState(Gen.HashCombineInt(faction?.loadID ?? 0, salt));
		int index = Rand.Range(0, maxExclusive);
		Rand.PopState();
		return list3[index];
	}

	private static bool SpawnQualityThing(Map map, ThingDef def, QualityCategory q, CellRect interior, HashSet<IntVec3> occupied)
	{
		IntVec3 intVec = NextFreeCell(map, interior, occupied);
		if (!intVec.IsValid)
		{
			return false;
		}
		try
		{
			Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
			thing.TryGetComp<CompQuality>()?.SetQuality(q, ArtGenerationContext.Outsider);
			GenSpawn.Spawn(thing, intVec, map);
			occupied.Add(intVec);
			return true;
		}
		catch (Exception ex)
		{
			EESLog.Error($"CoreZone gear {def.defName} @ {intVec}: {ex.Message}");
			return false;
		}
	}

	private static int SpawnTreasure(Map map, StrongholdData data, CellRect interior, HashSet<IntVec3> occupied, List<IntVec3> shelfCells, float valueMul)
	{
		int num = 0;
		int contentTier = data.ContentTier;
		num += SpawnStacked(map, ThingDefOf.Silver, Mathf.RoundToInt(350f * valueMul), interior, occupied, shelfCells);
		if (contentTier >= 4)
		{
			num += SpawnStacked(map, ThingDefOf.Gold, Mathf.RoundToInt(40f * valueMul), interior, occupied, shelfCells);
		}
		if (contentTier >= 5)
		{
			num += SpawnStackedNamed(map, "Plasteel", Mathf.RoundToInt(60f * valueMul), interior, occupied, shelfCells);
			num += SpawnStackedNamed(map, "ComponentSpacer", Mathf.RoundToInt(4f * valueMul), interior, occupied, shelfCells);
		}
		num += SpawnStackedNamed(map, "ComponentIndustrial", Mathf.RoundToInt(5f * valueMul), interior, occupied, shelfCells);
		foreach (KeyValuePair<string, int> item in RoleLoot(data.roleDefName))
		{
			int total = Mathf.Max(1, Mathf.RoundToInt((float)item.Value * valueMul));
			num += SpawnStackedNamed(map, item.Key, total, interior, occupied, shelfCells);
		}
		if (CECompat.Active)
		{
			bool flag = RoleMatches(data.roleDefName, "ammo");
			if (flag || contentTier >= 3)
			{
				Faction parentFaction = map.ParentFaction;
				int maxKinds = (flag ? 4 : 2);
				int num2 = Mathf.RoundToInt((float)(flag ? 80 : 40) * Mathf.Min(valueMul, 3f));
				List<ThingDef> list = CECompat.PickLootAmmo(parentFaction, maxKinds);
				foreach (ThingDef item2 in list)
				{
					num += SpawnStacked(map, item2, num2, interior, occupied, shelfCells);
				}
				EESLog.Message(string.Format("CoreZone CE ammo loot [{0} T{1} ", (parentFaction != null) ? parentFaction.Name : "?", contentTier) + $"ammoDepot={flag}]: kinds={list.Count} perKind={num2}.");
			}
		}
		return num;
	}

	private static int SpawnStackedNamed(Map map, string defName, int total, CellRect interior, HashSet<IntVec3> occupied, List<IntVec3> shelfCells)
	{
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
		if (namedSilentFail == null)
		{
			return 0;
		}
		return SpawnStacked(map, namedSilentFail, total, interior, occupied, shelfCells);
	}

	private static int SpawnStacked(Map map, ThingDef def, int total, CellRect interior, HashSet<IntVec3> occupied, List<IntVec3> shelfCells)
	{
		if (def == null || total <= 0)
		{
			return 0;
		}
		if (!StrongholdLootFilter.IsDroppableLoot(def, "vault"))
		{
			return 0;
		}
		int num = 0;
		int num2 = total;
		int b = Mathf.Max(1, def.stackLimit);
		while (num2 > 0)
		{
			bool flag = false;
			IntVec3 intVec;
			if (shelfCells != null && shelfCells.Count > 0)
			{
				intVec = shelfCells[0];
				flag = true;
			}
			else
			{
				intVec = NextFreeCell(map, interior, occupied);
			}
			if (!intVec.IsValid)
			{
				break;
			}
			int num3 = Mathf.Min(num2, b);
			try
			{
				Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
				thing.stackCount = num3;
				GenSpawn.Spawn(thing, intVec, map);
				if (flag)
				{
					shelfCells.RemoveAt(0);
				}
				else
				{
					occupied.Add(intVec);
				}
				num2 -= num3;
				num++;
			}
			catch (Exception ex)
			{
				EESLog.Error($"CoreZone loot {def.defName} @ {intVec}: {ex.Message}");
				break;
			}
		}
		return (num > 0) ? 1 : 0;
	}

	private static IEnumerable<KeyValuePair<string, int>> RoleLoot(string role)
	{
		switch (role)
		{
		case "SupplyDepot":
			yield return new KeyValuePair<string, int>("Steel", 120);
			yield return new KeyValuePair<string, int>("MealSurvivalPack", 14);
			yield return new KeyValuePair<string, int>("MedicineIndustrial", 12);
			break;
		case "AmmoDepot":
			yield return new KeyValuePair<string, int>("Shell_HighExplosive", 14);
			yield return new KeyValuePair<string, int>("Shell_Incendiary", 8);
			yield return new KeyValuePair<string, int>("Chemfuel", 150);
			break;
		case "PrisonCamp":
			yield return new KeyValuePair<string, int>("MealSimple", 12);
			yield return new KeyValuePair<string, int>("MedicineIndustrial", 10);
			break;
		case "ResearchOutpost":
			yield return new KeyValuePair<string, int>("Neutroamine", 40);
			yield return new KeyValuePair<string, int>("ComponentSpacer", 4);
			break;
		case "NobleEstate":
			yield return new KeyValuePair<string, int>("Gold", 60);
			yield return new KeyValuePair<string, int>("Jade", 80);
			break;
		case "CommandFortress":
			yield return new KeyValuePair<string, int>("Plasteel", 80);
			yield return new KeyValuePair<string, int>("ComponentSpacer", 8);
			break;
		case "MortarBattery":
			yield return new KeyValuePair<string, int>("Shell_HighExplosive", 16);
			yield return new KeyValuePair<string, int>("Steel", 80);
			break;
		case "BlackMarketHub":
			yield return new KeyValuePair<string, int>("Gold", 50);
			yield return new KeyValuePair<string, int>("Neutroamine", 40);
			yield return new KeyValuePair<string, int>("Yayo", 30);
			break;
		case "TrainingCamp":
			yield return new KeyValuePair<string, int>("Steel", 100);
			yield return new KeyValuePair<string, int>("MedicineIndustrial", 8);
			break;
		case "MechanoidRelay":
			yield return new KeyValuePair<string, int>("ComponentSpacer", 10);
			yield return new KeyValuePair<string, int>("Plasteel", 60);
			break;
		case "CryoBarracks":
			yield return new KeyValuePair<string, int>("Neutroamine", 40);
			yield return new KeyValuePair<string, int>("MedicineIndustrial", 10);
			break;
		case "ArtilleryBase":
			yield return new KeyValuePair<string, int>("Shell_HighExplosive", 18);
			yield return new KeyValuePair<string, int>("Chemfuel", 120);
			break;
		case "PsychicShrine":
			yield return new KeyValuePair<string, int>("Jade", 60);
			yield return new KeyValuePair<string, int>("MedicineHerbal", 20);
			break;
		case "JammingStation":
			yield return new KeyValuePair<string, int>("ComponentSpacer", 8);
			yield return new KeyValuePair<string, int>("Gold", 40);
			break;
		default:
			yield return new KeyValuePair<string, int>("Steel", 80);
			break;
		}
	}
}
