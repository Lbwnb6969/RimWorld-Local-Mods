using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public static class AssaultGroupUtility
{
	private const float OutpostDropChance = 0.85f;

	private const float SettlementDropChance = 0.12f;

	private const int DropDelayMinTicks = 5000;

	private const int DropDelayMaxTicks = 10000;

	private const int TargetWaitTicks = 15000;

	private const int TargetRecheckTicks = 250;

	private const int BoardTimeoutTicks = 7500;

	private const int ModeOutpost = 0;

	private const int ModeSettlement = 1;

	private const int ModeWalkIn = 2;

	public const int PhaseBoarding = 0;

	public const int PhaseInFlight = 1;

	public const int PhaseDropped = 2;

	public static void BeginBoarding(Map map, Faction faction, StrongholdData data, List<Pawn> squad)
	{
		if (map != null && faction != null && data != null && squad != null && squad.Count != 0)
		{
			WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
			if (instance == null)
			{
				EESLog.Warning("Assault BeginBoarding: WorldComponent 없음.");
				return;
			}
			AssaultOp assaultOp = new AssaultOp
			{
				phase = 0,
				faction = faction,
				settlementId = data.settlementId,
				sourceTile = map.Tile,
				isEmpire = (Faction.OfEmpire != null && faction == Faction.OfEmpire),
				squad = new List<Pawn>(squad),
				startTick = Find.TickManager.TicksGame,
				forcedMode = data.forcedAssaultMode
			};
			instance.assaultOps.Add(assaultOp);
			EESLog.Message($"Assault BeginBoarding [{faction.Name} T{data.tier}]: squad={squad.Count} empire={assaultOp.isEmpire} tile={map.Tile}.");
		}
	}

	public static void DebugForceDrop(Map map, Faction faction, StrongholdData data, int dropMode, int count = 6)
	{
		WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
		if (instance == null || map == null || faction == null || data == null)
		{
			return;
		}
		AssaultOp assaultOp = new AssaultOp
		{
			phase = 1,
			faction = faction,
			settlementId = data.settlementId,
			sourceTile = map.Tile,
			isEmpire = (Faction.OfEmpire != null && faction == Faction.OfEmpire),
			shuttleResolved = true,
			dropMode = dropMode,
			dropTick = Find.TickManager.TicksGame
		};
		for (int i = 0; i < count; i++)
		{
			try
			{
				Pawn item = PawnGenerator.GeneratePawn(new PawnGenerationRequest(faction.RandomPawnKind(), faction, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true, allowDead: false, allowDowned: false, canGeneratePawnRelations: true, mustBeCapableOfViolence: true, 1f, forceAddFreeWarmLayerIfNeeded: false, allowGay: true, allowPregnant: false, allowFood: true, allowAddictions: true, inhabitant: true));
				assaultOp.innerContainer.TryAdd(item, canMergeWithExistingStacks: false);
			}
			catch (Exception ex)
			{
				EESLog.Warning("[DEBUG] Assault force-drop pawn gen 실패: " + ex.Message);
			}
		}
		if (assaultOp.innerContainer.Count != 0)
		{
			if (dropMode == 1)
			{
				Messages.Message(EESText.T("EES.Assault.SettlementWarning", faction.Name), MessageTypeDefOf.ThreatBig, historical: false);
			}
			instance.assaultOps.Add(assaultOp);
			EESLog.Message($"[DEBUG] Assault FORCE-DROP queued: mode={ModeName(dropMode)} count={assaultOp.innerContainer.Count} [{faction.Name}].");
		}
	}

	public static bool TickOp(AssaultOp op, int now)
	{
		if (op == null)
		{
			return false;
		}
		try
		{
			return op.phase switch
			{
				0 => TickBoarding(op, now),
				1 => TickInFlight(op, now),
				2 => TickDropped(op, now),
				_ => false,
			};
		}
		catch (Exception arg)
		{
			EESLog.Error($"Assault TickOp(phase {op.phase}) 실패: {arg}");
			ReturnAssault(op, $"TickOp 예외(phase {op.phase})");
			return false;
		}
	}

	private static bool TickBoarding(AssaultOp op, int now)
	{
		PruneDeadSquad(op);
		Map map = SourceMap(op);
		if (map == null)
		{
			ReturnAssault(op, "탑승 중 전초기지 맵 소멸");
			return false;
		}
		if (!op.shuttleResolved)
		{
			ResolveShuttle(op, map);
			if (!op.shuttleResolved)
			{
				return true;
			}
		}
		if (op.abstractMode)
		{
			LaunchOff(op, map, now);
			return true;
		}
		Thing shuttle = op.shuttle;
		if (shuttle == null || shuttle.Destroyed || !shuttle.Spawned)
		{
			op.abstractMode = true;
			LaunchOff(op, map, now);
			return true;
		}
		ExtractFromTransporter(op, shuttle);
		CellRect cellRect = shuttle.OccupiedRect().ExpandedBy(2);
		int num = 0;
		int num2 = 0;
		for (int i = 0; i < op.squad.Count; i++)
		{
			Pawn pawn = op.squad[i];
			if (pawn == null || pawn.Dead)
			{
				continue;
			}
			if (!pawn.Spawned)
			{
				if (!op.innerContainer.Contains(pawn))
				{
					num2++;
				}
			}
			else if (cellRect.Contains(pawn.Position) || (pawn.Position - shuttle.Position).LengthHorizontal <= 5f)
			{
				GrabIntoContainer(op, pawn);
			}
			else if (IsNearMapEdge(pawn.Position, map, 12))
			{
				EESLog.Message($"Assault [{op.faction?.Name}] {pawn.LabelShort} @ {pawn.Position} 맵 가장자리 근접 → ★강제 흡수(이탈 차단).");
				GrabIntoContainer(op, pawn);
			}
			else
			{
				num++;
			}
		}
		int count = op.innerContainer.Count;
		if (now % 480 < 60 || (num2 > 0 && now % 120 < 60))
		{
			LogBoardingState(op, shuttle, now, $"onMap={num} aboard={count} vanished={num2}");
		}
		if (num == 0 && count > 0)
		{
			EESLog.Message($"Assault [{op.faction?.Name}] onMap=0 aboard={count} vanished={num2} → 발진. ★vanished>0이면 흡수 전에 맵 밖으로 이탈한 것.");
			LaunchOff(op, map, now);
			return true;
		}
		if (num == 0 && count == 0)
		{
			EESLog.Message($"Assault [{op.faction?.Name}] onMap=0 aboard=0 vanished={num2} → 취소. ★vanished>0이면 전원 맵 이탈.");
			CancelAssault(op, "탑승 인원 0(전원 맵 이탈/전멸)");
			return false;
		}
		if (now - op.startTick > 7500)
		{
			EESLog.Message($"Assault boarding 타임아웃 [{op.faction?.Name}] aboard={count} onMap={num} vanished={num2} → 강제 발진.");
			LaunchOff(op, map, now);
			return true;
		}
		return true;
	}

	private static void ResolveShuttle(AssaultOp op, Map map)
	{
		Thing thing = null;
		if (op.isEmpire)
		{
			thing = FindFactionShuttleOnMap(map, op.faction);
			if (thing == null && Find.TickManager.TicksGame - op.startTick < 1250)
			{
				return;
			}
		}
		op.shuttleResolved = true;
		if (thing == null && ThingDefOf.Shuttle != null && TransportShipDefOf.Ship_Shuttle != null)
		{
			IntVec3 reachFrom = map.Center;
			for (int i = 0; i < op.squad.Count; i++)
			{
				Pawn pawn = op.squad[i];
				if (pawn != null && !pawn.Dead && pawn.Spawned)
				{
					reachFrom = pawn.Position;
					break;
				}
			}
			thing = SpawnShuttle(map, op.faction, reachFrom);
		}
		if (thing == null)
		{
			op.abstractMode = true;
			EESLog.Message("Assault shuttle [" + op.faction?.Name + "]: 셔틀 없음(Royalty 미설치?) → 추상 이탈.");
			return;
		}
		op.shuttle = thing;
		FindTransportShipFor(thing)?.ForceJob(ShipJobMaker.MakeShipJob(ShipJobDefOf.WaitForever));
		PunchRoofForShuttle(map, thing);
		List<Pawn> list = op.squad.Where((Pawn p) => p != null && !p.Dead && p.Spawned).ToList();
		CompShuttle compShuttle = thing.TryGetComp<CompShuttle>();
		CompTransporter compTransporter = thing.TryGetComp<CompTransporter>();
		if (compShuttle != null)
		{
			foreach (Pawn item in list)
			{
				if (!compShuttle.requiredPawns.Contains(item))
				{
					compShuttle.requiredPawns.Add(item);
				}
			}
		}
		if (compTransporter != null && !compTransporter.LoadingInProgressOrReadyToLaunch)
		{
			TransporterUtility.InitiateLoading(Gen.YieldSingle(compTransporter));
		}
		if (list.Count > 0)
		{
			foreach (Pawn item2 in list)
			{
				item2.GetLord()?.RemovePawn(item2);
			}
			LordMaker.MakeNewLord(op.faction, new LordJob_EESBoardShuttle(thing), map, list);
		}
		int num = 0;
		for (int num2 = 0; num2 < list.Count; num2++)
		{
			if (list[num2].CanReach(thing, PathEndMode.Touch, Danger.Deadly))
			{
				num++;
			}
		}
		EESLog.Message(string.Format("Assault shuttle [{0}]: {1} @ {2}, ", op.faction?.Name, op.isEmpire ? "제국 왕복선 인수" : "셔틀 스폰", thing.Position) + string.Format("탑승듀티 {0}명 (도달가능 {1}/{2}, 거리 {3}).", list.Count, num, list.Count, (list.Count > 0) ? (list[0].Position - thing.Position).LengthHorizontal.ToString("0") : "-"));
		LogBoardingState(op, thing, Find.TickManager.TicksGame, "RESOLVE 직후");
	}

	private static void LaunchOff(AssaultOp op, Map map, int now)
	{
		Thing shuttle = op.shuttle;
		foreach (Pawn item in op.squad)
		{
			if (item != null && !item.Dead && item.Spawned)
			{
				GrabIntoContainer(op, item);
			}
		}
		op.squad.Clear();
		if (op.innerContainer.Count == 0)
		{
			CancelAssault(op, "발진 시 보관 폰 0명");
			return;
		}
		if (shuttle != null && !shuttle.Destroyed)
		{
			shuttle.TryGetComp<CompShuttle>()?.requiredPawns.Clear();
			TransportShip transportShip = FindTransportShipFor(shuttle);
			if (transportShip != null)
			{
				transportShip.ForceJob(ShipJobMaker.MakeShipJob(ShipJobDefOf.FlyAway));
			}
			else if (shuttle.Spawned)
			{
				shuttle.Destroy();
			}
		}
		if (EESHostility.IsFriendlyOutpost(op.faction))
		{
			op.dropMode = 0;
		}
		else if (op.forcedMode >= 0)
		{
			op.dropMode = op.forcedMode;
		}
		else
		{
			float value = Rand.Value;
			op.dropMode = ((!(value < 0.85f)) ? ((value < 0.97f) ? 1 : 2) : 0);
		}
		op.dropTick = now + Rand.RangeInclusive(5000, 10000);
		op.phase = 1;
		if (op.dropMode == 1)
		{
			Messages.Message(EESText.T("EES.Assault.SettlementWarning", op.faction?.Name), MessageTypeDefOf.ThreatBig, historical: false);
		}
		EESLog.Message($"Assault LAUNCH [{op.faction?.Name}]: {op.innerContainer.Count}명 이탈, dropMode={ModeName(op.dropMode)} dropIn={op.dropTick - now}t.");
	}

	private static bool TickInFlight(AssaultOp op, int now)
	{
		if (now < op.dropTick)
		{
			return true;
		}
		return ExecuteDrop(op, now);
	}

	private static bool ExecuteDrop(AssaultOp op, int now)
	{
		List<Pawn> list = (from p in op.innerContainer.OfType<Pawn>()
			where p != null && !p.Dead
			select p).ToList();
		if (list.Count == 0)
		{
			CancelAssault(op, "보관 폰 0명(전멸)");
			return false;
		}
		Map map = SourceMap(op);
		switch (op.dropMode)
		{
		case 0:
		{
			if (map == null)
			{
				ReturnAssault(op, "전초기지 맵 무효");
				return false;
			}
			if (!DropCentroid(op, map, out var center3))
			{
				return WaitForTarget(op, now, map, "전초기지 강하");
			}
			DoPodDrop(op, map, center3, list, null);
			break;
		}
		case 1:
		{
			Map map2 = BestPlayerHomeMap();
			if (map2 == null)
			{
				ReturnAssault(op, "플레이어 정착지 없음");
				return false;
			}
			IntVec3 center2 = SettlementDropCenter(map2);
			DoPodDrop(op, map2, center2, list, null);
			break;
		}
		default:
		{
			if (map == null)
			{
				ReturnAssault(op, "전초기지 맵 무효");
				return false;
			}
			if (!DropCentroid(op, map, out var _))
			{
				return WaitForTarget(op, now, map, "도보 진입");
			}
			DoWalkIn(op, map, list);
			Messages.Message(EESText.T("EES.Assault.WalkInLate", op.faction?.Name), new TargetInfo(list[0].Position, map), MessageTypeDefOf.ThreatBig, historical: false);
			break;
		}
		}
		return op.phase == 2;
	}

	private static bool WaitForTarget(AssaultOp op, int now, Map map, string what)
	{
		if (op.waitDeadlineTick < 0)
		{
			op.waitDeadlineTick = now + 15000;
			EESLog.Message($"Assault WAIT [{op.faction?.Name}]: {what} 표적 없음(맵 {map}) → 대기 시작. " + $"마감 {op.waitDeadlineTick}t(+{15000}t=6시간), 재확인 {250}t 간격, " + $"보관 {op.innerContainer.Count}명.");
		}
		if (now < op.waitDeadlineTick)
		{
			op.dropTick = now + 250;
			return true;
		}
		ReturnAssault(op, what + " 표적 미도착 — 대기 6시간 만료");
		return false;
	}

	private static void DoPodDrop(AssaultOp op, Map map, IntVec3 center, List<Pawn> pawns, Action onDone)
	{
		foreach (Pawn pawn in pawns)
		{
			op.innerContainer.Remove(pawn);
			pawn.GetLord()?.RemovePawn(pawn);
		}
		if (!DropCellFinder.TryFindDropSpotNear(center, map, out var result, allowFogged: false, canRoofPunch: true))
		{
			result = center;
		}
		Lord lord = LordMaker.MakeNewLord(op.faction, new LordJob_EESAssaultGroup(op.faction, GarrisonGroupType.Assault, 0.2f, pawns.Count), map, pawns);
		DropPodUtility.DropThingsNear(result, map, pawns.Cast<Thing>(), 110, canInstaDropDuringInit: false, leaveSlag: false, canRoofPunch: true, forbid: true, allowFogged: true, op.faction);
		op.lord = lord;
		op.dropCount = pawns.Count;
		op.phase = 2;
		EESLog.Message($"Assault DROP [{op.faction?.Name}]: {pawns.Count}명 포드 강하 @ {result} ({ModeName(op.dropMode)}, {map}).");
	}

	private static void DoWalkIn(AssaultOp op, Map map, List<Pawn> pawns)
	{
		foreach (Pawn pawn in pawns)
		{
			op.innerContainer.Remove(pawn);
			pawn.GetLord()?.RemovePawn(pawn);
		}
		Lord lord = LordMaker.MakeNewLord(op.faction, new LordJob_EESAssaultGroup(op.faction, GarrisonGroupType.Assault, 0.2f, pawns.Count), map, pawns);
		foreach (Pawn pawn2 in pawns)
		{
			if (!CellFinder.TryFindRandomEdgeCellWith((IntVec3 c) => c.Standable(map) && !c.Fogged(map) && map.reachability.CanReachColony(c), map, 0.3f, out var result))
			{
				result = CellFinder.RandomEdgeCell(map);
			}
			GenSpawn.Spawn(pawn2, result, map);
		}
		op.lord = lord;
		op.dropCount = pawns.Count;
		op.phase = 2;
		EESLog.Message($"Assault WALK-IN [{op.faction?.Name}]: {pawns.Count}명 가장자리 도보 진입 ({map}).");
	}

	private static bool TickDropped(AssaultOp op, int now)
	{
		Lord lord = op.lord;
		if (lord == null)
		{
			return false;
		}
		if (lord.ownedPawns.Count((Pawn p) => p != null && !p.Dead) == 0)
		{
			return false;
		}
		int num = lord.ownedPawns.Count((Pawn p) => p != null && !p.Dead && !p.Spawned);
		int num2 = AliveInLord(lord);
		if (num2 > op.peakAlive)
		{
			op.peakAlive = num2;
		}
		if (num > 0 && now - op.dropTick < 600)
		{
			return true;
		}
		if (num2 == 0)
		{
			return true;
		}
		int num3 = ((op.peakAlive > 0) ? op.peakAlive : ((op.dropCount > 0) ? op.dropCount : lord.ownedPawns.Count));
		if (!op.fled && num2 < num3 && num2 <= Mathf.CeilToInt(0.2f * (float)Mathf.Max(1, num3)))
		{
			if (RimPactsCompat.IsSpectateBattle(lord.Map))
			{
				if (!op.fleeSuppressLogged)
				{
					op.fleeSuppressLogged = true;
					EESLog.Message("Assault flee 억제 [" + op.faction?.Name + "]: 관전 전투라 도주 안 함 — " + $"aliveSpawned={num2} peak={op.peakAlive} basis={num3} " + $"thr=ceil(0.2*{num3})={Mathf.CeilToInt(0.2f * (float)Mathf.Max(1, num3))} → 끝까지 교전.");
				}
			}
			else
			{
				EESLog.Message($"Assault flee 판정 [{op.faction?.Name}]: aliveSpawned={num2} peak={op.peakAlive} inTransit={num} " + $"dropCount={op.dropCount} since-drop={now - op.dropTick}t basis={num3} thr=ceil(0.2*{num3})={Mathf.CeilToInt(0.2f * (float)Mathf.Max(1, num3))} → 도주.");
				PanicFleeLord(lord);
				op.fled = true;
			}
		}
		return true;
	}

	public static void ReturnAssault(AssaultOp op, string reason)
	{
		try
		{
			Map map = SourceMap(op);
			List<Pawn> list = (from p in op.innerContainer?.OfType<Pawn>()
				where p != null && !p.Dead
				select p).ToList() ?? new List<Pawn>();
			if (map == null || list.Count == 0)
			{
				CancelAssault(op, reason + " · 복귀할 맵 없음");
				return;
			}
			MapComponent_StrongholdSiege component = map.GetComponent<MapComponent_StrongholdSiege>();
			IntVec3 intVec = ((component != null && component.eesBaseRect.Width > 0) ? component.eesBaseRect.CenterCell : map.Center);
			foreach (Pawn item in list)
			{
				op.innerContainer.Remove(item);
				item.GetLord()?.RemovePawn(item);
			}
			int num = 0;
			foreach (Pawn item2 in list)
			{
				IntVec3 intVec2 = CellFinder.RandomClosewalkCellNear(intVec, map, 12);
				if (!intVec2.IsValid || !intVec2.InBounds(map))
				{
					intVec2 = intVec;
				}
				try
				{
					GenSpawn.Spawn(item2, intVec2, map);
					num++;
				}
				catch (Exception ex)
				{
					EESLog.Warning("Assault 복귀 스폰 실패 " + item2?.LabelShort + ": " + ex.Message);
				}
			}
			List<Pawn> list2 = list.Where((Pawn p) => p != null && p.Spawned && !p.Dead).ToList();
			if (list2.Count > 0)
			{
				LordMaker.MakeNewLord(op.faction, new LordJob_EESDefendGroup(op.faction, intVec, GarrisonGroupType.Assault, 0.2f, list2.Count), map, list2);
			}
			op.innerContainer?.ClearAndDestroyContents();
			EESLog.Message($"Assault RETURN [{op.faction?.Name}]: {num}/{list.Count}명 거점 복귀 @ {intVec} " + $"→ 기지 방어 재편성({list2.Count}명). 사유={reason}.");
		}
		catch (Exception ex2)
		{
			EESLog.Warning("Assault ReturnAssault 실패: " + ex2.Message);
		}
	}

	public static void CancelAssault(AssaultOp op, string reason = null)
	{
		try
		{
			int num = op.innerContainer?.Count ?? 0;
			op.innerContainer?.ClearAndDestroyContents();
			EESLog.Message(string.Format("Assault CANCEL [{0}]: 보관 폰 {1}명 폐기. 사유={2}.", op.faction?.Name, num, reason ?? "대상 맵 무효/전멸"));
		}
		catch (Exception ex)
		{
			EESLog.Warning("Assault CancelAssault 실패: " + ex.Message);
		}
	}

	private static void PruneDeadSquad(AssaultOp op)
	{
		op.squad.RemoveAll((Pawn p) => p?.Dead ?? true);
	}

	private static Map SourceMap(AssaultOp op)
	{
		return (WorldComponent_Strongholds.Instance?.GetSettlement(op.settlementId))?.Map;
	}

	private static void GrabIntoContainer(AssaultOp op, Pawn p)
	{
		if (p != null)
		{
			string text = p.GetLord()?.LordJob?.GetType().Name ?? "no-lord";
			IntVec3 intVec = (p.Spawned ? p.Position : IntVec3.Invalid);
			p.GetLord()?.RemovePawn(p);
			if (p.Spawned)
			{
				p.DeSpawn();
			}
			int count = op.innerContainer.Count;
			bool flag = op.innerContainer.TryAdd(p, canMergeWithExistingStacks: false);
			EESLog.Message(string.Format("Assault GRAB [{0}] {1} @ {2} (직전lord={3}): TryAdd={4} 컨테이너 {5}→{6}{7}.", op.faction?.Name, p.LabelShort, intVec, text, flag, count, op.innerContainer.Count, flag ? "" : " ★흡수 실패!"));
		}
	}

	private static bool IsNearMapEdge(IntVec3 c, Map map, int margin)
	{
		if (map == null)
		{
			return false;
		}
		IntVec3 size = map.Size;
		if (c.x >= margin && c.z >= margin && c.x < size.x - margin)
		{
			return c.z >= size.z - margin;
		}
		return true;
	}

	private static void ExtractFromTransporter(AssaultOp op, Thing shuttle)
	{
		CompTransporter compTransporter = shuttle?.TryGetComp<CompTransporter>();
		if (compTransporter == null || compTransporter.innerContainer.Count == 0)
		{
			return;
		}
		for (int num = compTransporter.innerContainer.Count - 1; num >= 0; num--)
		{
			if (compTransporter.innerContainer[num] is Pawn pawn && op.squad.Contains(pawn))
			{
				pawn.GetLord()?.RemovePawn(pawn);
				compTransporter.innerContainer.Remove(pawn);
				op.innerContainer.TryAdd(pawn, canMergeWithExistingStacks: false);
				EESLog.Message($"Assault GRAB(셔틀 탑승완료) [{op.faction?.Name}] {pawn.LabelShort} → op 컨테이너 {op.innerContainer.Count}.");
			}
		}
	}

	private static void LogBoardingState(AssaultOp op, Thing shuttle, int now, string tag)
	{
		try
		{
			StringBuilder stringBuilder = new StringBuilder();
			TransportShip transportShip = FindTransportShipFor(shuttle);
			string text = ((transportShip?.curJob != null) ? $"{transportShip.curJob.def?.defName}/{transportShip.curJob.GetType().Name}(state={transportShip.curJob.jobState})" : "null");
			CompShuttle compShuttle = shuttle?.TryGetComp<CompShuttle>();
			CellRect cellRect = shuttle?.OccupiedRect().ExpandedBy(2) ?? default(CellRect);
			stringBuilder.Append($"Assault 탑승진단[{op.faction?.Name} | {tag}] now={now} 셔틀@{shuttle?.Position} spawned={shuttle?.Spawned ?? false} shipJob={text} reqPawns={compShuttle?.requiredPawns?.Count ?? (-1)}:");
			for (int i = 0; i < op.squad.Count; i++)
			{
				Pawn pawn = op.squad[i];
				if (pawn == null)
				{
					stringBuilder.Append($"\n   [{i}] null");
					continue;
				}
				string text2 = pawn.GetLord()?.LordJob?.GetType().Name ?? "no-lord";
				string text3 = pawn.CurJob?.def?.defName ?? "no-job";
				string text4 = pawn.mindState?.duty?.def?.defName ?? "no-duty";
				bool flag = op.innerContainer.Contains(pawn);
				float num = ((shuttle != null && pawn.Spawned) ? (pawn.Position - shuttle.Position).LengthHorizontal : (-1f));
				bool flag2 = shuttle != null && pawn.Spawned && cellRect.Contains(pawn.Position);
				stringBuilder.Append(string.Format("\n   [{0}] {1} pos={2} dead={3} downed={4} dist={5:0} inZone={6} lord={7} duty={8} job={9}", i, pawn.LabelShort, pawn.Spawned ? pawn.Position.ToString() : (flag ? "(흡수됨)" : "(despawn-이탈?)"), pawn.Dead, pawn.Downed, num, flag2, text2, text4, text3));
			}
			EESLog.Message(stringBuilder.ToString());
		}
		catch (Exception ex)
		{
			EESLog.Warning("LogBoardingState 실패: " + ex.Message);
		}
	}

	private static void PunchRoofForShuttle(Map map, Thing shuttle)
	{
		if (map == null || shuttle == null)
		{
			return;
		}
		try
		{
			foreach (IntVec3 item in shuttle.OccupiedRect())
			{
				if (item.InBounds(map) && item.Roofed(map))
				{
					map.roofGrid.SetRoof(item, null);
				}
			}
		}
		catch (Exception ex)
		{
			EESLog.Warning("PunchRoofForShuttle 실패: " + ex.Message);
		}
	}

	private static Thing FindFactionShuttleOnMap(Map map, Faction faction)
	{
		List<TransportShip> list = Find.TransportShipManager?.AllTransportShips;
		if (list == null)
		{
			return null;
		}
		for (int i = 0; i < list.Count; i++)
		{
			Thing thing = list[i]?.shipThing;
			if (thing != null && thing.Spawned && thing.Map == map && thing.Faction == faction && thing.TryGetComp<CompShuttle>() != null)
			{
				return thing;
			}
		}
		return null;
	}

	private static TransportShip FindTransportShipFor(Thing shuttle)
	{
		List<TransportShip> list = Find.TransportShipManager?.AllTransportShips;
		if (list == null || shuttle == null)
		{
			return null;
		}
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i]?.shipThing == shuttle)
			{
				return list[i];
			}
		}
		return null;
	}

	private static Thing SpawnShuttle(Map map, Faction faction, IntVec3 reachFrom)
	{
		try
		{
			Thing thing = ThingMaker.MakeThing(ThingDefOf.Shuttle);
			thing.SetFaction(faction);
			CompShuttle compShuttle = thing.TryGetComp<CompShuttle>();
			if (compShuttle != null)
			{
				compShuttle.permitShuttle = true;
			}
			IntVec3 intVec = FindShuttleSpawnCell(map, faction, thing.def.size, reachFrom);
			GenSpawn.Spawn(thing, intVec, map, Rot4.North);
			TransportShip transportShip = TransportShipMaker.MakeTransportShip(TransportShipDefOf.Ship_Shuttle, null, thing);
			transportShip.Start();
			transportShip.AddJobs(ShipJobDefOf.WaitForever);
			EESLog.Message($"Assault shuttle 직접 스폰 [{faction?.Name}] @ {intVec} (닿을수있는 빈공간, size {thing.def.size}, reachFrom {reachFrom}).");
			return thing;
		}
		catch (Exception ex)
		{
			EESLog.Warning("Assault SpawnShuttle 실패(추상으로 폴백): " + ex.Message);
			return null;
		}
	}

	private static IntVec3 FindShuttleSpawnCell(Map map, Faction faction, IntVec2 size, IntVec3 reachFrom)
	{
		TraverseParms tp = TraverseParms.For(TraverseMode.PassDoors);
		bool reachOk = reachFrom.IsValid && reachFrom.InBounds(map);
		CellRect cellRect = StrongholdGarrisonUtility.ComputeBaseRect(map, faction);
		if (cellRect.Width <= 0)
		{
			cellRect = CellRect.CenteredOn(reachOk ? reachFrom : map.Center, 20).ClipInsideMap(map);
		}
		IntVec3 centerCell = cellRect.CenterCell;
		for (int i = 2; i <= 22; i++)
		{
			foreach (IntVec3 edgeCell in cellRect.ExpandedBy(i).ClipInsideMap(map).EdgeCells)
			{
				if (!cellRect.Contains(edgeCell) && FreeReach(edgeCell))
				{
					return edgeCell;
				}
			}
		}
		if (CellFinder.TryFindRandomCellInsideWith(cellRect.ContractedBy(2), FreeReach, out var result))
		{
			return result;
		}
		if (reachOk)
		{
			foreach (IntVec3 item in GenRadial.RadialCellsAround(reachFrom, 60f, useCenter: true))
			{
				if (FreeReach(item))
				{
					return item;
				}
			}
		}
		if (!reachOk)
		{
			return centerCell;
		}
		return reachFrom;
		bool FootprintFree(IntVec3 c)
		{
			foreach (IntVec3 item2 in GenAdj.OccupiedRect(c, Rot4.North, size))
			{
				if (!item2.InBounds(map) || item2.GetEdifice(map) != null || !item2.Standable(map))
				{
					return false;
				}
			}
			return true;
		}
		bool FreeReach(IntVec3 c)
		{
			if (c.InBounds(map) && FootprintFree(c))
			{
				if (reachOk)
				{
					return map.reachability.CanReach(reachFrom, c, PathEndMode.Touch, tp);
				}
				return true;
			}
			return false;
		}
	}

	private static bool DropCentroid(AssaultOp op, Map map, out IntVec3 center)
	{
		if (EESHostility.TryEnemyCentroid(map, op?.faction, out center))
		{
			return true;
		}
		return PlayerCentroid(map, out center);
	}

	private static bool PlayerCentroid(Map map, out IntVec3 center)
	{
		center = map.Center;
		List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
		int num = 0;
		long num2 = 0L;
		long num3 = 0L;
		for (int i = 0; i < list.Count; i++)
		{
			Pawn pawn = list[i];
			if (pawn != null && !pawn.Dead)
			{
				num2 += pawn.Position.x;
				num3 += pawn.Position.z;
				num++;
			}
		}
		if (num == 0)
		{
			return false;
		}
		center = new IntVec3((int)(num2 / num), 0, (int)(num3 / num));
		return true;
	}

	private static Map BestPlayerHomeMap()
	{
		Map result = null;
		float num = -1f;
		List<Map> maps = Find.Maps;
		for (int i = 0; i < maps.Count; i++)
		{
			Map map = maps[i];
			if (map != null && map.IsPlayerHome)
			{
				float num2 = map.wealthWatcher?.WealthTotal ?? 0f;
				if (num2 > num)
				{
					num = num2;
					result = map;
				}
			}
		}
		return result;
	}

	private static IntVec3 SettlementDropCenter(Map map)
	{
		List<Building> allBuildingsColonist = map.listerBuildings.allBuildingsColonist;
		for (int i = 0; i < allBuildingsColonist.Count; i++)
		{
			string text = allBuildingsColonist[i]?.def?.defName;
			if (text != null && (text.IndexOf("GravEngine", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Gravship", StringComparison.OrdinalIgnoreCase) >= 0))
			{
				return allBuildingsColonist[i].Position;
			}
		}
		return map.Center;
	}

	private static int AliveInLord(Lord l)
	{
		int num = 0;
		for (int i = 0; i < l.ownedPawns.Count; i++)
		{
			Pawn pawn = l.ownedPawns[i];
			if (pawn != null && pawn.Spawned && !pawn.Dead && !pawn.Downed)
			{
				num++;
			}
		}
		return num;
	}

	private static void PanicFleeLord(Lord l)
	{
		MentalStateDef panicFlee = MentalStateDefOf.PanicFlee;
		if (panicFlee == null)
		{
			return;
		}
		int num = 0;
		foreach (Pawn item in new List<Pawn>(l.ownedPawns))
		{
			if (item != null && !item.Dead && !item.Downed && !item.InMentalState && item.mindState?.mentalStateHandler != null && item.mindState.mentalStateHandler.TryStartMentalState(panicFlee, "EES assault flee"))
			{
				num++;
			}
		}
		if (num > 0)
		{
			EESLog.Message($"Assault flee [{l.faction?.Name}]: {num}명 PanicFlee(임계 {0.2f:P0}).");
			Messages.Message(EESText.T("EES.Group.GroupFlee", l.faction?.Name), (l.ownedPawns.Count > 0 && l.ownedPawns[0].Spawned) ? new TargetInfo(l.ownedPawns[0].Position, l.ownedPawns[0].Map) : TargetInfo.Invalid, MessageTypeDefOf.NeutralEvent, historical: false);
		}
	}

	private static string ModeName(int m)
	{
		return m switch
		{
			1 => "12%정착지",
			0 => "85%전초기지",
			_ => "3%도보",
		};
	}
}
