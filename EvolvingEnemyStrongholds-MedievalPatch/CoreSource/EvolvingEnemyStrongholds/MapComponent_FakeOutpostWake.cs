using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class MapComponent_FakeOutpostWake : MapComponent
{
	private bool armed;

	private bool woken;

	private bool nocioWoken;

	private int initialWallCount = -1;

	private int rewardWallCount = -1;

	private int bx0;

	private int bz0;

	private int bx1;

	private int bz1;

	private int rx0;

	private int rz0;

	private int rx1;

	private int rz1;

	private List<int> monsterIds = new List<int>();

	private List<int> nocioIds = new List<int>();

	private List<int> hiveIds = new List<int>();

	private int harbingerPending;

	private bool isFakeMap;

	private const int CheckInterval = 30;

	private const int ExitDiagInterval = 120;

	private CellRect BaseRect => CellRect.FromLimits(bx0, bz0, bx1, bz1);

	private CellRect RewardRoom => CellRect.FromLimits(rx0, rz0, rx1, rz1);

	public MapComponent_FakeOutpostWake(Map map)
		: base(map)
	{
	}

	public void Arm(CellRect baseRect, CellRect rewardRoom, List<int> monsters, List<int> nocios, List<int> hives, int harbingerCount)
	{
		bx0 = baseRect.minX;
		bz0 = baseRect.minZ;
		bx1 = baseRect.maxX;
		bz1 = baseRect.maxZ;
		rx0 = rewardRoom.minX;
		rz0 = rewardRoom.minZ;
		rx1 = rewardRoom.maxX;
		rz1 = rewardRoom.maxZ;
		monsterIds = monsters ?? new List<int>();
		nocioIds = nocios ?? new List<int>();
		hiveIds = hives ?? new List<int>();
		harbingerPending = harbingerCount;
		initialWallCount = CountEdgeWalls(baseRect);
		rewardWallCount = CountEdgeWalls(rewardRoom);
		armed = true;
		woken = false;
		nocioWoken = nocioIds.Count == 0;
		isFakeMap = true;
		EESLog.Message($"FakeWake armed [{map.Parent?.LabelCap}]: monsters={monsterIds.Count} nocio={nocioIds.Count} " + $"harbinger(pending)={harbingerPending} baseRect={baseRect.Width}x{baseRect.Height} edgeWalls={initialWallCount}.");
	}

	private void DebugLogExitState()
	{
		try
		{
			if (Find.TickManager.TicksGame % 120 != 0)
			{
				return;
			}
			Settlement settlement = map.Parent as Settlement;
			bool anyPawnBlockingMapRemoval = map.mapPawns.AnyPawnBlockingMapRemoval;
			bool flag = false;
			bool alsoRemoveWorldObject = false;
			MapParent parent = map.Parent;
			if (parent != null)
			{
				try
				{
					flag = parent.ShouldRemoveMapNow(out alsoRemoveWorldObject);
				}
				catch
				{
				}
			}
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			int num5 = 0;
			int num6 = 0;
			Faction faction = settlement?.Faction;
			IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
			for (int i = 0; i < allPawnsSpawned.Count; i++)
			{
				Pawn pawn = allPawnsSpawned[i];
				if (pawn != null && !pawn.Dead)
				{
					Faction faction2 = pawn.Faction;
					if (faction2 == Faction.OfPlayer)
					{
						num++;
					}
					else if (faction2 == Faction.OfMechanoids)
					{
						num2++;
					}
					else if (faction2 == Faction.OfInsects)
					{
						num3++;
					}
					else if (Faction.OfEntities != null && faction2 == Faction.OfEntities)
					{
						num4++;
					}
					else if (faction2 == faction)
					{
						num5++;
					}
					else if (faction2 != null && faction2.HostileTo(Faction.OfPlayer))
					{
						num6++;
					}
				}
			}
			EESLog.Message($"FakeExitDiag [{settlement?.LabelCap}]: settlementAlive={settlement != null && !settlement.Destroyed} defeated={faction?.defeated} " + $"shouldRemoveMap={flag}(alsoRemoveWO={alsoRemoveWorldObject}) blockingRemoval={anyPawnBlockingMapRemoval} | " + $"pawns player={num} mech={num2} insect={num3} entity={num4} owner={num5} otherHostile={num6}.");
		}
		catch (Exception ex)
		{
			EESLog.Warning("FakeExitDiag failed: " + ex.Message);
		}
	}

	public override void MapComponentTick()
	{
		if (isFakeMap)
		{
			DebugLogExitState();
		}
		if (armed && Find.TickManager.TicksGame % 30 == 0)
		{
			if (harbingerPending > 0)
			{
				int num = SpawnHarbingerTrees(harbingerPending);
				EESLog.Message($"FakeWake [{map.Parent?.LabelCap}]: 전조나무 {num}/{harbingerPending} 지연 식수(맵 활성 후).");
				harbingerPending = 0;
			}
			if (!woken && WallBreached(BaseRect, initialWallCount))
			{
				WakeMonsters("외벽 돌파");
				woken = true;
			}
			if (!nocioWoken && nocioIds.Count > 0 && (RewardDoorOpened() || WallBreached(RewardRoom, rewardWallCount) || AnyPlayerInside(RewardRoom.ContractedBy(1))))
			{
				ActivateNociospheres();
				nocioWoken = true;
			}
			if (nocioWoken)
			{
				MaintainNociospheres();
			}
			if (woken && nocioWoken && !AnyNocioAlive())
			{
				armed = false;
			}
		}
	}

	private bool WallBreached(CellRect rect, int initial)
	{
		if (initial < 0)
		{
			return false;
		}
		return CountEdgeWalls(rect) < initial;
	}

	private int CountEdgeWalls(CellRect r)
	{
		int num = 0;
		foreach (IntVec3 edgeCell in r.EdgeCells)
		{
			if (edgeCell.InBounds(map))
			{
				Building edifice = edgeCell.GetEdifice(map);
				if (edifice != null && !edifice.Destroyed && edifice.def.passability == Traversability.Impassable)
				{
					num++;
				}
			}
		}
		return num;
	}

	private bool RewardDoorOpened()
	{
		bool flag = false;
		foreach (IntVec3 edgeCell in RewardRoom.EdgeCells)
		{
			if (!edgeCell.InBounds(map))
			{
				continue;
			}
			Building edifice = edgeCell.GetEdifice(map);
			if (edifice != null && !edifice.Destroyed && edifice.def.IsDoor)
			{
				flag = true;
				CompHackable compHackable = edifice.TryGetComp<CompHackable>();
				if (compHackable != null && compHackable.IsHacked)
				{
					return true;
				}
				if (edifice is Building_Door { Open: not false })
				{
					return true;
				}
			}
		}
		return !flag;
	}

	private int SpawnHarbingerTrees(int count)
	{
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_TreeHarbinger");
		if (namedSilentFail == null || count <= 0)
		{
			return 0;
		}
		CellRect cellRect = BaseRect.ContractedBy(2);
		CellRect cellRect2 = RewardRoom.ExpandedBy(2);
		List<IntVec3> list = new List<IntVec3>();
		foreach (IntVec3 item in cellRect)
		{
			if (item.InBounds(map) && !cellRect2.Contains(item) && item.Standable(map) && item.GetEdifice(map) == null && item.GetFirstPawn(map) == null && item.GetPlant(map) == null && namedSilentFail.CanEverPlantAt(item, map))
			{
				list.Add(item);
			}
		}
		if (list.Count == 0)
		{
			return 0;
		}
		Rand.PushState(Gen.HashCombineInt(map.uniqueID, 30702));
		list.Shuffle();
		Rand.PopState();
		int num = 0;
		for (int i = 0; i < list.Count; i++)
		{
			if (num >= count)
			{
				break;
			}
			try
			{
				GenSpawn.Spawn(namedSilentFail, list[i], map);
				num++;
			}
			catch
			{
			}
		}
		return num;
	}

	private bool AnyPlayerInside(CellRect r)
	{
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn != null && !pawn.Dead && pawn.Faction == Faction.OfPlayer && r.Contains(pawn.Position))
			{
				return true;
			}
		}
		return false;
	}

	private void WakeMonsters(string reason)
	{
		int num = 0;
		Faction faction = null;
		List<Pawn> list = new List<Pawn>();
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn != null && !pawn.Dead && monsterIds.Contains(pawn.thingIDNumber))
			{
				try
				{
					pawn.GetComp<CompCanBeDormant>()?.WakeUp();
				}
				catch
				{
				}
				list.Add(pawn);
				if (faction == null)
				{
					faction = pawn.Faction;
				}
				num++;
			}
		}
		if (list.Count > 0 && faction != null)
		{
			try
			{
				for (int j = 0; j < list.Count; j++)
				{
					list[j].GetLord()?.RemovePawn(list[j]);
				}
				LordMaker.MakeNewLord(faction, new LordJob_AssaultColony_NoFlee(faction, canKidnap: false, canTimeoutOrFlee: false, sappers: false), map, list);
			}
			catch (Exception ex)
			{
				EESLog.Warning("FakeWake assault lord failed: " + ex.Message);
			}
		}
		int num2 = 0;
		if (hiveIds.Count > 0)
		{
			List<Building> allBuildingsNonColonist = map.listerBuildings.allBuildingsNonColonist;
			for (int k = 0; k < allBuildingsNonColonist.Count; k++)
			{
				Building building = allBuildingsNonColonist[k];
				if (building != null && !building.Destroyed && hiveIds.Contains(building.thingIDNumber))
				{
					try
					{
						building.TryGetComp<CompCanBeDormant>()?.WakeUp();
						num2++;
					}
					catch
					{
					}
				}
			}
		}
		EESLog.Message($"FakeWake [{map.Parent?.LabelCap}]: {reason} → 몬스터 {num}체 + 벌집 {num2}개 각성.");
	}

	private void ActivateNociospheres()
	{
		int num = 0;
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn != null && !pawn.Dead && nocioIds.Contains(pawn.thingIDNumber))
			{
				try
				{
					pawn.GetComp<CompActivity>()?.EnterActiveState();
					num++;
				}
				catch
				{
				}
			}
		}
		EESLog.Message($"FakeWake [{map.Parent?.LabelCap}]: 연구문 개방 → 고통구체 {num}기 활성화(EnterActiveState).");
	}

	private void MaintainNociospheres()
	{
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn == null || pawn.Dead || !nocioIds.Contains(pawn.thingIDNumber))
			{
				continue;
			}
			CompActivity comp = pawn.GetComp<CompActivity>();
			if (comp != null && !comp.IsActive)
			{
				try
				{
					comp.EnterActiveState();
				}
				catch
				{
				}
			}
		}
	}

	private bool AnyNocioAlive()
	{
		if (nocioIds.Count == 0)
		{
			return false;
		}
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn != null && !pawn.Dead && nocioIds.Contains(pawn.thingIDNumber))
			{
				return true;
			}
		}
		return false;
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref armed, "ees_fakeArmed", defaultValue: false);
		Scribe_Values.Look(ref woken, "ees_fakeWoken", defaultValue: false);
		Scribe_Values.Look(ref nocioWoken, "ees_fakeNocioWoken", defaultValue: false);
		Scribe_Values.Look(ref initialWallCount, "ees_fakeWallCount", -1);
		Scribe_Values.Look(ref rewardWallCount, "ees_fakeRewardWallCount", -1);
		Scribe_Values.Look(ref harbingerPending, "ees_fakeHarbingerPending", 0);
		Scribe_Values.Look(ref isFakeMap, "ees_fakeIsFakeMap", defaultValue: false);
		Scribe_Values.Look(ref bx0, "ees_fakeBx0", 0);
		Scribe_Values.Look(ref bz0, "ees_fakeBz0", 0);
		Scribe_Values.Look(ref bx1, "ees_fakeBx1", 0);
		Scribe_Values.Look(ref bz1, "ees_fakeBz1", 0);
		Scribe_Values.Look(ref rx0, "ees_fakeRx0", 0);
		Scribe_Values.Look(ref rz0, "ees_fakeRz0", 0);
		Scribe_Values.Look(ref rx1, "ees_fakeRx1", 0);
		Scribe_Values.Look(ref rz1, "ees_fakeRz1", 0);
		Scribe_Collections.Look(ref monsterIds, "ees_fakeMonsterIds", LookMode.Value);
		Scribe_Collections.Look(ref nocioIds, "ees_fakeNocioIds", LookMode.Value);
		Scribe_Collections.Look(ref hiveIds, "ees_fakeHiveIds", LookMode.Value);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (monsterIds == null)
			{
				monsterIds = new List<int>();
			}
			if (nocioIds == null)
			{
				nocioIds = new List<int>();
			}
			if (hiveIds == null)
			{
				hiveIds = new List<int>();
			}
		}
	}
}
