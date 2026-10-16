using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public class WorldComponent_EESSupply : WorldComponent
{
	public class PendingSupplyBattle : IExposable
	{
		public PlanetTile tile;

		public int destSettlementId = -1;

		public int startTick;

		public int factionLoadId = -1;

		public WorldObject_EESSupplyCaravan convoy;

		public int lastRemain = -1;

		public List<Pawn> group;

		public void ExposeData()
		{
			Scribe_Values.Look(ref tile, "tile");
			Scribe_Values.Look(ref destSettlementId, "destSettlementId", -1);
			Scribe_Values.Look(ref startTick, "startTick", 0);
			Scribe_Values.Look(ref factionLoadId, "factionLoadId", -1);
			Scribe_References.Look(ref convoy, "convoy");
			Scribe_Values.Look(ref lastRemain, "lastRemain", -1);
			Scribe_Collections.Look(ref group, "group", LookMode.Reference);
		}
	}

	private const int TickGate = 250;

	private int nextSpawnCheckTick = -1;

	private List<int> slotNextTicks = new List<int>();

	private int lastGateTick = -1;

	private List<PendingSupplyBattle> pending = new List<PendingSupplyBattle>();

	public static WorldComponent_EESSupply Instance => Find.World?.GetComponent<WorldComponent_EESSupply>();

	public WorldComponent_EESSupply(World world)
		: base(world)
	{
	}

	public bool HasPendingBattleFor(WorldObject_EESSupplyCaravan convoy)
	{
		for (int i = 0; i < pending.Count; i++)
		{
			if (pending[i].convoy == convoy)
			{
				return true;
			}
		}
		return false;
	}

	public void RegisterBattle(PlanetTile tile, int destSettlementId, Faction faction, WorldObject_EESSupplyCaravan convoy = null, List<Pawn> group = null)
	{
		pending.Add(new PendingSupplyBattle
		{
			tile = tile,
			destSettlementId = destSettlementId,
			startTick = Find.TickManager.TicksGame,
			factionLoadId = (faction?.loadID ?? (-1)),
			convoy = convoy,
			group = ((group != null) ? new List<Pawn>(group) : null)
		});
		EESLog.Message($"Supply: 교전 등록 tile={tile} dest={destSettlementId} faction={faction?.Name} " + "convoy=" + ((convoy != null) ? "정지 유지(격파 시에만 소멸)" : "없음") + " group=" + ((group != null) ? (group.Count + "명 명단 추적(영속 맵)") : "없음(임시 맵=세력 카운트)") + ".");
	}

	public override void WorldComponentTick()
	{
		base.WorldComponentTick();
		int ticksGame = Find.TickManager.TicksGame;
		if (lastGateTick >= 0 && ticksGame - lastGateTick < 250)
		{
			return;
		}
		lastGateTick = ticksGame;
		TrackPodDestinations();
		ProcessPending(ticksGame);
		if (!SupplyCaravanUtility.Enabled)
		{
			return;
		}
		float num = StrongholdMod.Settings?.supplySpawnDays ?? 8f;
		int num2 = Mathf.Clamp(StrongholdMod.Settings?.supplyMaxActive ?? 1, 1, 4);
		EnsureSlots(ticksGame, num, num2);
		for (int i = 0; i < slotNextTicks.Count; i++)
		{
			if (ticksGame >= slotNextTicks[i])
			{
				slotNextTicks[i] = ticksGame + Mathf.RoundToInt(num * 60000f * Rand.Range(0.75f, 1.25f));
				if (SupplyCaravanUtility.ActiveCount() < num2)
				{
					SupplyCaravanUtility.TrySpawn();
				}
			}
		}
	}

	private void EnsureSlots(int now, float days, int max)
	{
		if (slotNextTicks.Count == 0 && nextSpawnCheckTick > 0)
		{
			slotNextTicks.Add(nextSpawnCheckTick);
			nextSpawnCheckTick = -1;
		}
		while (slotNextTicks.Count < max)
		{
			slotNextTicks.Add(now + Mathf.RoundToInt(days * 60000f * Rand.Range(0.35f, 1.15f)));
		}
		while (slotNextTicks.Count > max)
		{
			slotNextTicks.RemoveAt(slotNextTicks.Count - 1);
		}
	}

	private void TrackPodDestinations()
	{
		List<WorldObject> allWorldObjects = Find.WorldObjects.AllWorldObjects;
		for (int i = 0; i < allWorldObjects.Count; i++)
		{
			if (allWorldObjects[i] is TravellingTransporters { arrivalAction: TransportersArrivalAction_EESIntercept { Supply: { Destroyed: false, Spawned: not false } supply } } travellingTransporters && travellingTransporters.destinationTile != supply.Tile && !Find.WorldObjects.AnyMapParentAt(supply.Tile))
			{
				travellingTransporters.destinationTile = supply.Tile;
			}
		}
	}

	private void ProcessPending(int now)
	{
		for (int num = pending.Count - 1; num >= 0; num--)
		{
			PendingSupplyBattle pendingSupplyBattle = pending[num];
			Map map = Current.Game.FindMap(pendingSupplyBattle.tile);
			if (map == null)
			{
				if (now - pendingSupplyBattle.startTick >= 250)
				{
					if (pendingSupplyBattle.lastRemain == 0)
					{
						EESLog.Message($"Supply: 승리 판정(맵 소멸 후 확정) tile={pendingSupplyBattle.tile} dest={pendingSupplyBattle.destSettlementId} — 마지막 관측 잔존호위 0.");
						ApplyCut(pendingSupplyBattle.destSettlementId, now);
						pendingSupplyBattle.convoy?.Notify_BattleLost();
					}
					else
					{
						EESLog.Message($"Supply: 교전 결착 없음(맵 소멸: 후퇴 또는 그라브십 이륙) tile={pendingSupplyBattle.tile} — " + $"이륙 직전 잔존 {pendingSupplyBattle.lastRemain}. 약화 없음.");
						pendingSupplyBattle.convoy?.Notify_BattleEnded("맵 소멸(후퇴/그라브십 이륙)");
					}
					pending.RemoveAt(num);
				}
			}
			else if (pendingSupplyBattle.group != null)
			{
				ResolveGroupBattle(pendingSupplyBattle, map, now, num);
			}
			else
			{
				int num2 = (pendingSupplyBattle.lastRemain = ((pendingSupplyBattle.factionLoadId >= 0) ? CountEscortRemains(map, pendingSupplyBattle.factionLoadId) : (-1)));
				if ((pendingSupplyBattle.factionLoadId >= 0) ? (num2 == 0) : (!GenHostility.AnyHostileActiveThreatToPlayer(map)))
				{
					EESLog.Message($"Supply: 승리 판정 tile={pendingSupplyBattle.tile} dest={pendingSupplyBattle.destSettlementId} " + $"factionId={pendingSupplyBattle.factionLoadId} 잔존호위={num2} (경과 {now - pendingSupplyBattle.startTick}틱).");
					ApplyCut(pendingSupplyBattle.destSettlementId, now);
					pendingSupplyBattle.convoy?.Notify_BattleLost();
					pending.RemoveAt(num);
				}
			}
		}
	}

	private void ResolveGroupBattle(PendingSupplyBattle b, Map map, int now, int index)
	{
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		for (int i = 0; i < b.group.Count; i++)
		{
			Pawn pawn = b.group[i];
			if (pawn == null || pawn.Dead || pawn.Destroyed)
			{
				num2++;
			}
			else if (pawn.Spawned && pawn.Map == map)
			{
				if (pawn.Downed)
				{
					num2++;
				}
				else
				{
					num++;
				}
			}
			else
			{
				num3++;
			}
		}
		b.lastRemain = num;
		if (num <= 0)
		{
			if (num3 == 0)
			{
				EESLog.Message($"Supply: 그라브십 교전 ★격파 tile={b.tile} — 습격조 {b.group.Count}명 전원 무력화 " + $"(경과 {now - b.startTick}틱).");
				ApplyCut(b.destSettlementId, now);
				b.convoy?.Notify_BattleLost();
			}
			else
			{
				EESLog.Message($"Supply: 그라브십 교전 이탈 tile={b.tile} — 무력화 {num2}/{b.group.Count}, " + $"살아서 이탈 {num3} → 격파 아님. 약화 없음.");
				b.convoy?.Notify_BattleEnded($"습격조 이탈 {num3}명");
			}
			pending.RemoveAt(index);
		}
	}

	private static int CountEscortRemains(Map map, int factionLoadId)
	{
		int num = 0;
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn.Faction != null && pawn.Faction.loadID == factionLoadId && !pawn.Dead && !pawn.Downed)
			{
				num++;
			}
		}
		return num;
	}

	private void ApplyCut(int destSettlementId, int now)
	{
		WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
		StrongholdData strongholdData = instance?.GetExisting(destSettlementId);
		if (strongholdData != null)
		{
			strongholdData.supplyCutUntilTick = now + Mathf.RoundToInt(600000f);
			Settlement settlement = instance.GetSettlement(destSettlementId);
			EESLog.Message($"Supply: 보급 차단 적용 id={destSettlementId} → {strongholdData.supplyCutUntilTick}틱까지.");
			if (settlement != null)
			{
				Messages.Message(EESText.T("EES.Supply.Cut", settlement.LabelCap), settlement, MessageTypeDefOf.PositiveEvent, historical: false);
			}
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref nextSpawnCheckTick, "nextSpawnCheckTick", -1);
		Scribe_Collections.Look(ref slotNextTicks, "slotNextTicks", LookMode.Value);
		Scribe_Collections.Look(ref pending, "pending", LookMode.Deep);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (pending == null)
			{
				pending = new List<PendingSupplyBattle>();
			}
			if (slotNextTicks == null)
			{
				slotNextTicks = new List<int>();
			}
		}
	}
}
