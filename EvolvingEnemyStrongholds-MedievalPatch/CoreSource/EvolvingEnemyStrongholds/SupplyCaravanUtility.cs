using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public static class SupplyCaravanUtility
{
	public const int CutTurrets = 2;

	public const int CutMortars = 1;

	public const float CutMilFactor = 0.75f;

	public const float CutDays = 10f;

	public const int TicksPerTile = 4700;

	public const int MaxLifetimeTicks = 480000;

	private const int SourceSearchRadius = 20;

	private static readonly int[] AnchorRadii = new int[3] { 15, 25, 40 };

	private static int _techBlockedDests;

	private static int _puppetBlockedDests;

	private static int _worldWarBlockedDests;

	private static string _worldWarBlockDebug;

	private static ThingDef gravAnchorDef;

	private static bool gravAnchorResolved;

	public static bool Enabled
	{
		get
		{
			if (StrongholdMod.Settings != null)
			{
				return StrongholdMod.Settings.enableSupplyCaravans;
			}
			return true;
		}
	}

	public static WorldObjectDef Def => DefDatabase<WorldObjectDef>.GetNamed("EES_SupplyCaravan", errorOnFail: false);

	public static WorldObjectDef EncounterDef => DefDatabase<WorldObjectDef>.GetNamed("EES_SupplyEncounter", errorOnFail: false);

	public static bool IsMobileGravshipMap(Map map)
	{
		if (map == null || !ModsConfig.OdysseyActive)
		{
			return false;
		}
		if (GravshipUtility.GetPlayerGravEngine_NewTemp(map) == null)
		{
			return false;
		}
		if (!gravAnchorResolved)
		{
			gravAnchorDef = DefDatabase<ThingDef>.GetNamedSilentFail("GravAnchor");
			gravAnchorResolved = true;
		}
		if (gravAnchorDef != null && map.listerThings.ThingsOfDef(gravAnchorDef).Count > 0)
		{
			return false;
		}
		return true;
	}

	public static WorldObject_EESSupplyCaravan FindSupplyInterceptedBy(Caravan caravan)
	{
		List<WorldObject> allWorldObjects = Find.WorldObjects.AllWorldObjects;
		for (int i = 0; i < allWorldObjects.Count; i++)
		{
			if (allWorldObjects[i] is WorldObject_EESSupplyCaravan worldObject_EESSupplyCaravan && worldObject_EESSupplyCaravan.IsInterceptor(caravan))
			{
				return worldObject_EESSupplyCaravan;
			}
		}
		return null;
	}

	public static bool SupplyCutActive(StrongholdData data)
	{
		if (data != null && data.supplyCutUntilTick > 0 && Find.TickManager != null)
		{
			return Find.TickManager.TicksGame < data.supplyCutUntilTick;
		}
		return false;
	}

	public static bool SupplyCutCombatActive(StrongholdData data)
	{
		if (SupplyCutActive(data))
		{
			return !FakeOutpostUtility.IsFake(data.roleDefName);
		}
		return false;
	}

	public static int ActiveCount()
	{
		int num = 0;
		List<WorldObject> allWorldObjects = Find.WorldObjects.AllWorldObjects;
		for (int i = 0; i < allWorldObjects.Count; i++)
		{
			if (allWorldObjects[i] is WorldObject_EESSupplyCaravan)
			{
				num++;
			}
		}
		return num;
	}

	private static HashSet<int> ActiveFactionIds()
	{
		HashSet<int> hashSet = new HashSet<int>();
		List<WorldObject> allWorldObjects = Find.WorldObjects.AllWorldObjects;
		for (int i = 0; i < allWorldObjects.Count; i++)
		{
			if (allWorldObjects[i] is WorldObject_EESSupplyCaravan { Faction: not null } worldObject_EESSupplyCaravan)
			{
				hashSet.Add(worldObject_EESSupplyCaravan.Faction.loadID);
			}
		}
		return hashSet;
	}

	public static bool TrySpawn()
	{
		WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
		if (instance == null || Def == null)
		{
			return false;
		}
		List<PlanetTile> list = CollectPlayerAnchors();
		if (list.Count == 0)
		{
			EESLog.Message("Supply: 플레이어 앵커(정착지/캐러밴) 없음 → skip.");
			return false;
		}
		List<Settlement> list2 = new List<Settlement>();
		int num = 0;
		_techBlockedDests = 0;
		_puppetBlockedDests = 0;
		_worldWarBlockedDests = 0;
		_worldWarBlockDebug = null;
		for (int i = 0; i < AnchorRadii.Length; i++)
		{
			if (list2.Count != 0)
			{
				break;
			}
			num = AnchorRadii[i];
			CollectCandidates(list, num, list2);
		}
		if (_techBlockedDests > 0)
		{
			EESLog.Message($"Supply: 목적지 후보 {_techBlockedDests}곳 제외 — Ignorance Is Bliss 테크 제한(잔여 후보 {list2.Count}곳).");
		}
		if (_puppetBlockedDests > 0)
		{
			EESLog.Message($"Supply: 목적지 후보 {_puppetBlockedDests}곳 제외 — RimPacts 괴뢰국(잔여 후보 {list2.Count}곳).");
		}
		if (_worldWarBlockedDests > 0)
		{
			EESLog.Message($"Supply: 목적지 후보 {_worldWarBlockedDests}곳 제외 — {_worldWarBlockDebug}" + $"(잔여 후보 {list2.Count}곳).");
		}
		if (list2.Count == 0)
		{
			EESLog.Message("Supply: 반경 40타일 내 대상 거점 0 → 이번 주기 skip.");
			return false;
		}
		HashSet<int> hashSet = ActiveFactionIds();
		if (hashSet.Count > 0)
		{
			List<Settlement> list3 = new List<Settlement>();
			for (int j = 0; j < list2.Count; j++)
			{
				if (list2[j].Faction != null && !hashSet.Contains(list2[j].Faction.loadID))
				{
					list3.Add(list2[j]);
				}
			}
			if (list3.Count > 0)
			{
				list2 = list3;
			}
		}
		for (int k = 0; k < 4; k++)
		{
			if (list2.Count <= 0)
			{
				break;
			}
			Settlement settlement = list2.RandomElement();
			list2.Remove(settlement);
			StrongholdData orCreate = instance.GetOrCreate(settlement);
			if (orCreate == null)
			{
				continue;
			}
			try
			{
				if (TrySpawnFor(settlement, orCreate, num))
				{
					return true;
				}
			}
			catch (Exception ex)
			{
				EESLog.Warning("Supply: 후보 " + settlement.LabelCap + " 스폰 예외 → 폐기: " + ex.Message);
			}
		}
		return false;
	}

	private static bool TrySpawnFor(Settlement dest, StrongholdData data, int usedRadius)
	{
		PlanetTile result = PlanetTile.Invalid;
		Settlement settlement = NearestSameFactionSettlement(dest);
		if (settlement != null)
		{
			result = settlement.Tile;
		}
		else if (!TileFinder.TryFindPassableTileWithTraversalDistance(dest.Tile, 6, 10, out result, (PlanetTile t) => Find.WorldReachability.CanReach(t, dest.Tile)))
		{
			EESLog.Message("Supply: " + dest.LabelCap + " 가상 출발점 실패 → 후보 폐기.");
			return false;
		}
		List<PlanetTile> list = BuildPath(result, dest.Tile);
		if (list == null || list.Count == 0)
		{
			EESLog.Message("Supply: " + dest.LabelCap + " 경로 없음(분단?) → 후보 폐기.");
			return false;
		}
		WorldObject_EESSupplyCaravan worldObject_EESSupplyCaravan = (WorldObject_EESSupplyCaravan)WorldObjectMaker.MakeWorldObject(Def);
		worldObject_EESSupplyCaravan.Tile = result;
		worldObject_EESSupplyCaravan.SetFaction(dest.Faction);
		worldObject_EESSupplyCaravan.destSettlementId = dest.ID;
		worldObject_EESSupplyCaravan.destTile = dest.Tile;
		worldObject_EESSupplyCaravan.spawnTick = Find.TickManager.TicksGame;
		Find.WorldObjects.Add(worldObject_EESSupplyCaravan);
		worldObject_EESSupplyCaravan.SetPath(list);
		EESLog.Message($"Supply: 스폰 [{dest.Faction?.Name}] {result} → {dest.LabelCap} " + $"(경로 {list.Count}타일, 탐색반경 {usedRadius}).");
		if (StrongholdMod.Settings == null || StrongholdMod.Settings.supplySpawnLetter)
		{
			if (StrongholdMod.Settings == null || StrongholdMod.Settings.scoutLetterNotify)
			{
				Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_SupplyCaravan"), EESText.T("EES.Supply.Letter.Text", dest.Faction?.Name ?? "?", dest.LabelCap), EESLetterDefOf.EES_SupplyCaravan, worldObject_EESSupplyCaravan);
			}
			else
			{
				Messages.Message(EESText.T("EES.Supply.Letter.Text", dest.Faction?.Name ?? "?", dest.LabelCap), worldObject_EESSupplyCaravan, MessageTypeDefOf.NeutralEvent, historical: false);
			}
		}
		return true;
	}

	private static List<PlanetTile> CollectPlayerAnchors()
	{
		List<PlanetTile> list = new List<PlanetTile>();
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			if (settlements[i].Faction == Faction.OfPlayer && settlements[i].Tile.Valid && settlements[i].Tile.Layer.IsRootSurface)
			{
				list.Add(settlements[i].Tile);
			}
		}
		List<Caravan> caravans = Find.WorldObjects.Caravans;
		for (int j = 0; j < caravans.Count; j++)
		{
			if (caravans[j].IsPlayerControlled && caravans[j].Tile.Valid && caravans[j].Tile.Layer.IsRootSurface)
			{
				list.Add(caravans[j].Tile);
			}
		}
		return list;
	}

	private static void CollectCandidates(List<PlanetTile> anchors, int radius, List<Settlement> outList)
	{
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (!EligibleDest(settlement))
			{
				continue;
			}
			for (int j = 0; j < anchors.Count; j++)
			{
				if (Find.WorldGrid.ApproxDistanceInTiles(anchors[j], settlement.Tile) <= (float)radius)
				{
					outList.Add(settlement);
					break;
				}
			}
		}
	}

	private static bool EligibleDest(Settlement s)
	{
		Faction faction = s?.Faction;
		if (faction == null || faction.IsPlayer)
		{
			return false;
		}
		if (!s.Tile.Valid || !s.Tile.Layer.IsRootSurface)
		{
			return false;
		}
		if (faction.def == null || !faction.def.humanlikeFaction || EESFactionKind.IsInsect(faction))
		{
			return false;
		}
		if (EESFactionBlacklist.IsBlocked(faction) || EESCustomBaseDetector.IsForeignSettlement(s))
		{
			return false;
		}
		if (RimPactsCompat.IsPuppet(faction))
		{
			_puppetBlockedDests++;
			return false;
		}
		if (RimPactsCompat.WorldWarBlocked(faction))
		{
			_worldWarBlockedDests++;
			if (_worldWarBlockDebug == null)
			{
				_worldWarBlockDebug = RimPactsCompat.WorldWarBlockDebug(faction);
			}
			return false;
		}
		if (!IgnoranceCompat.FactionAllowed(faction))
		{
			_techBlockedDests++;
			return false;
		}
		return !s.HasMap;
	}

	private static Settlement NearestSameFactionSettlement(Settlement dest)
	{
		Settlement result = null;
		float num = float.MaxValue;
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (settlement != dest && settlement.Faction == dest.Faction)
			{
				float num2 = Find.WorldGrid.ApproxDistanceInTiles(settlement.Tile, dest.Tile);
				if (num2 <= 20f && num2 < num && Find.WorldReachability.CanReach(settlement.Tile, dest.Tile))
				{
					result = settlement;
					num = num2;
				}
			}
		}
		return result;
	}

	public static List<PlanetTile> BuildPath(PlanetTile from, PlanetTile to)
	{
		if (!from.Valid || !to.Valid)
		{
			return null;
		}
		WorldPath worldPath = from.Layer.Pather.FindPath(from, to, null);
		if (worldPath == null || !worldPath.Found)
		{
			return null;
		}
		List<PlanetTile> list = new List<PlanetTile>();
		while (worldPath.NodesLeftCount > 1)
		{
			list.Add(worldPath.ConsumeNextNode());
		}
		worldPath.ReleaseToPool();
		return list;
	}

	public static int DestTier(int destSettlementId)
	{
		return (WorldComponent_Strongholds.Instance?.GetExisting(destSettlementId))?.tier ?? 3;
	}

	public static List<Pawn> GenerateEscort(Faction faction, int tier, int seed)
	{
		List<Pawn> list = new List<Pawn>();
		float num = StrongholdMod.Settings?.militaryPowerMul ?? 1f;
		float points = Mathf.Clamp(200f + (float)tier * 130f, 300f, 2600f) * num;
		PawnGroupMakerParms parms = new PawnGroupMakerParms
		{
			groupKind = PawnGroupKindDefOf.Combat,
			faction = faction,
			points = points,
			inhabitants = false,
			seed = Gen.HashCombineInt(seed, 6048273)
		};
		try
		{
			foreach (Pawn item2 in PawnGroupMakerUtility.GeneratePawns(parms, warnOnZeroResults: false))
			{
				list.Add(item2);
			}
		}
		catch (Exception ex)
		{
			EESLog.Error("Supply escort gen: " + ex.Message);
		}
		PawnKindDef namedSilentFail = DefDatabase<PawnKindDef>.GetNamedSilentFail("Muffalo");
		if (namedSilentFail != null)
		{
			int num2 = Mathf.Clamp(2 + tier / 5, 2, 4);
			for (int i = 0; i < num2; i++)
			{
				try
				{
					Pawn item = PawnGenerator.GeneratePawn(new PawnGenerationRequest(namedSilentFail, faction));
					list.Add(item);
				}
				catch (Exception ex2)
				{
					EESLog.Warning("Supply pack animal gen: " + ex2.Message);
				}
			}
		}
		return list;
	}

	public static List<Thing> MakeCargo(Faction faction, int tier)
	{
		List<Thing> list = new List<Thing>();
		float num = StrongholdMod.Settings?.lootMultiplier ?? 1f;
		AddStacks(list, ThingDefOf.Silver, Mathf.RoundToInt((float)(200 + tier * 60) * num));
		AddStacks(list, DefDatabase<ThingDef>.GetNamedSilentFail("MealSurvivalPack"), Mathf.RoundToInt((float)(10 + tier * 2) * num));
		if (faction?.def != null && (int)faction.def.techLevel >= 4)
		{
			AddStacks(list, ThingDefOf.Steel, Mathf.RoundToInt((float)(75 + tier * 15) * num));
			AddStacks(list, ThingDefOf.ComponentIndustrial, Mathf.RoundToInt((2f + (float)tier / 2f) * num));
			AddStacks(list, ThingDefOf.MedicineIndustrial, Mathf.RoundToInt((1f + (float)tier / 3f) * num));
		}
		else
		{
			AddStacks(list, ThingDefOf.WoodLog, Mathf.RoundToInt((float)(120 + tier * 10) * num));
			AddStacks(list, DefDatabase<ThingDef>.GetNamedSilentFail("MedicineHerbal"), Mathf.RoundToInt((2f + (float)tier / 3f) * num));
		}
		if (CECompat.Active)
		{
			List<ThingDef> list2 = CECompat.PickLootAmmo(faction, 2);
			for (int i = 0; i < list2.Count; i++)
			{
				AddStacks(list, list2[i], Mathf.RoundToInt((float)(60 + tier * 15) * num));
			}
		}
		return list;
	}

	public static void SpawnCargo(Map map, IntVec3 near, Faction faction, int tier)
	{
		List<Thing> list = MakeCargo(faction, tier);
		for (int i = 0; i < list.Count; i++)
		{
			GenPlace.TryPlaceThing(list[i], near, map, ThingPlaceMode.Near);
		}
	}

	public static void LoadCargoIntoGroup(List<Pawn> group, Faction faction, int tier)
	{
		List<Pawn> list = new List<Pawn>();
		for (int i = 0; i < group.Count; i++)
		{
			if (group[i].RaceProps != null && group[i].RaceProps.Animal)
			{
				list.Add(group[i]);
			}
		}
		if (list.Count == 0)
		{
			list = group;
		}
		if (list.Count == 0)
		{
			return;
		}
		List<Thing> list2 = MakeCargo(faction, tier);
		for (int j = 0; j < list2.Count; j++)
		{
			if (!list[j % list.Count].inventory.innerContainer.TryAdd(list2[j]))
			{
				list2[j].Destroy();
			}
		}
	}

	private static void AddStacks(List<Thing> outList, ThingDef def, int total)
	{
		if (def != null && total > 0)
		{
			int b = Mathf.Max(1, def.stackLimit);
			while (total > 0)
			{
				int num = Mathf.Min(total, b);
				total -= num;
				Thing thing = ThingMaker.MakeThing(def);
				thing.stackCount = num;
				outList.Add(thing);
			}
		}
	}

	public static void RegisterBattleFor(Map map, int destSettlementId, Faction faction, WorldObject_EESSupplyCaravan convoy = null, List<Pawn> group = null)
	{
		WorldComponent_EESSupply.Instance?.RegisterBattle(map.Tile, destSettlementId, faction, convoy, group);
	}

	public static void FinalizeBattleMap(Map map, Faction faction, List<Pawn> escorts, IntVec3 cargoCenter, int destSettlementId, int tier, WorldObject_EESSupplyCaravan convoy = null)
	{
		if (escorts.Count > 0)
		{
			LordMaker.MakeNewLord(faction, new LordJob_DefendPoint(cargoCenter), map, escorts);
		}
		SpawnCargo(map, cargoCenter, faction, tier);
		WorldComponent_EESSupply.Instance?.RegisterBattle(map.Tile, destSettlementId, faction, convoy);
		Find.TickManager.Notify_GeneratedPotentiallyHostileMap();
	}
}
