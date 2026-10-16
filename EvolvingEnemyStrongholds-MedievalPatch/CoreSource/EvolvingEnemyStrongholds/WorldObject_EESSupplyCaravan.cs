using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class WorldObject_EESSupplyCaravan : WorldObject
{
	private const int InterceptMarginTicks = 2500;

	private const int InterceptScanMaxTiles = 40;

	private const int ValidityGate = 250;

	public int destSettlementId = -1;

	public PlanetTile destTile = PlanetTile.Invalid;

	public int spawnTick;

	private float nextCostLeft;

	private Caravan interceptor;

	private PlanetTile interceptTile = PlanetTile.Invalid;

	private PlanetTile battleTile = PlanetTile.Invalid;

	private int battleStartTick = -1;

	private List<PlanetTile> pendingNodes;

	private PlanetTile nextTile = PlanetTile.Invalid;

	private int lastValidityTick = -1;

	private bool pathBroken;

	private string inspectCache;

	private int inspectCacheTick = int.MinValue;

	private const int InspectCacheTicks = 60;

	private static bool vfResolved;

	private static FieldInfo vfPatherField;

	private static MethodInfo vfStartPath;

	private static MethodInfo vfStopDead;

	private static PropertyInfo vfDestination;

	public bool InBattle => battleTile.Valid;

	public override Color ExpandingIconColor => base.Faction?.Color ?? base.ExpandingIconColor;

	public override Vector3 DrawPos
	{
		get
		{
			if (!nextTile.Valid)
			{
				return base.DrawPos;
			}
			float t = 1f - Mathf.Clamp01(nextCostLeft / 4700f);
			return Vector3.Slerp(Find.WorldGrid.GetTileCenter(base.Tile), Find.WorldGrid.GetTileCenter(nextTile), t);
		}
	}

	public void SetPath(List<PlanetTile> nodes)
	{
		pendingNodes = nodes;
		nextTile = PlanetTile.Invalid;
		nextCostLeft = 0f;
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref destSettlementId, "destSettlementId", -1);
		Scribe_Values.Look(ref destTile, "destTile");
		Scribe_Values.Look(ref spawnTick, "spawnTick", 0);
		Scribe_Values.Look(ref nextCostLeft, "nextCostLeft", 0f);
		Scribe_References.Look(ref interceptor, "interceptor");
		Scribe_Values.Look(ref interceptTile, "interceptTile");
		Scribe_Values.Look(ref battleTile, "battleTile");
		Scribe_Values.Look(ref battleStartTick, "battleStartTick", -1);
	}

	protected override void TickInterval(int delta)
	{
		base.TickInterval(delta);
		int ticksGame = Find.TickManager.TicksGame;
		if (battleTile.Valid)
		{
			if (WorldComponent_EESSupply.Instance != null && !WorldComponent_EESSupply.Instance.HasPendingBattleFor(this))
			{
				Notify_BattleEnded("교전 기록 없음(backstop)");
			}
			else
			{
				if (Current.Game.FindMap(battleTile) != null)
				{
					return;
				}
				Notify_BattleEnded("교전 맵 소멸(후퇴/그라브십 이륙)");
			}
		}
		if (ticksGame - spawnTick > 480000)
		{
			EESLog.Message($"Supply: 수명 초과 소멸 @ {base.Tile}.");
			Disappear();
			return;
		}
		if (lastValidityTick < 0 || ticksGame - lastValidityTick >= 250)
		{
			lastValidityTick = ticksGame;
			if (!DestStillValid())
			{
				EESLog.Message("Supply: 목적지 소실/세력변경 → 소멸.");
				Disappear();
				return;
			}
			if (!ValidateInterceptor())
			{
				return;
			}
		}
		if (CaravanNightRestUtility.RestingNowAt(base.Tile) || !EnsurePath())
		{
			return;
		}
		if (!nextTile.Valid)
		{
			if (pendingNodes.Count == 0)
			{
				Arrive();
				return;
			}
			nextTile = pendingNodes[0];
			pendingNodes.RemoveAt(0);
			nextCostLeft = 4700f;
		}
		nextCostLeft -= delta;
		if (nextCostLeft > 0f)
		{
			return;
		}
		base.Tile = nextTile;
		nextTile = PlanetTile.Invalid;
		nextCostLeft = 0f;
		if (base.Tile == destTile)
		{
			Arrive();
			return;
		}
		Map map = Current.Game.FindMap(base.Tile);
		if (map != null && map.ParentFaction == Faction.OfPlayer && base.Faction != null && base.Faction.HostileTo(Faction.OfPlayer) && SupplyCaravanUtility.IsMobileGravshipMap(map))
		{
			EngageIntoMap(map);
		}
		else if ((!InterceptorLinked() || !WorldWarCancelInterceptIfBlocked()) && InterceptorLinked() && interceptor.Tile == base.Tile && !Find.WorldObjects.AnyMapParentAt(base.Tile))
		{
			Engage(interceptor);
		}
	}

	private void EngageIntoMap(Map map)
	{
		try
		{
			int tier = ConvoyTier();
			List<Pawn> list = SupplyCaravanUtility.GenerateEscort(base.Faction, tier, destSettlementId);
			if (list.Count == 0)
			{
				Disappear();
				return;
			}
			if (!RCellFinder.TryFindRandomPawnEntryCell(out var result, map, CellFinder.EdgeRoadChance_Hostile))
			{
				result = CellFinder.RandomEdgeCell(map);
			}
			for (int i = 0; i < list.Count; i++)
			{
				IntVec3 loc = CellFinder.RandomSpawnCellForPawnNear(result, map);
				GenSpawn.Spawn(list[i], loc, map, Rot4.Random);
			}
			SupplyCaravanUtility.LoadCargoIntoGroup(list, base.Faction, tier);
			LordMaker.MakeNewLord(base.Faction, new LordJob_AssaultColony(base.Faction, canKidnap: false), map, list);
			SupplyCaravanUtility.RegisterBattleFor(map, destSettlementId, base.Faction, this, list);
			Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_SupplyInvaded"), EESText.T("EES.Supply.Invaded.Text"), EESLetterDefOf.EES_SupplyInvaded, new GlobalTargetInfo(result, map));
			EESLog.Message($"Supply: 그라브십 진입 교전 @ {base.Tile} (습격조 {list.Count}) — " + "호송대는 정지만 하고 결착(전멸/이탈/이륙)까지 기다린다.");
			Notify_BattleBegun(base.Tile);
		}
		catch (Exception ex)
		{
			EESLog.Error("Supply EngageIntoMap: " + ex);
		}
	}

	private bool EnsurePath()
	{
		if (pendingNodes != null)
		{
			return true;
		}
		if (pathBroken)
		{
			return false;
		}
		List<PlanetTile> list = SupplyCaravanUtility.BuildPath(base.Tile, destTile);
		if (list == null)
		{
			pathBroken = true;
			EESLog.Warning($"Supply: 로드 후 경로 재계산 실패 {base.Tile}→{destTile} → 소멸.");
			Disappear();
			return false;
		}
		pendingNodes = list;
		return true;
	}

	protected virtual bool DestStillValid()
	{
		Settlement settlement = WorldComponent_Strongholds.Instance?.GetSettlement(destSettlementId);
		if (settlement != null && !settlement.Destroyed)
		{
			return settlement.Faction == base.Faction;
		}
		return false;
	}

	public virtual int ConvoyTier()
	{
		return SupplyCaravanUtility.DestTier(destSettlementId);
	}

	protected virtual string DestLabel()
	{
		return (WorldComponent_Strongholds.Instance?.GetSettlement(destSettlementId))?.LabelCap ?? "?";
	}

	private bool InterceptorLinked()
	{
		if (interceptor != null && !interceptor.Destroyed && interceptor.Spawned)
		{
			return interceptor.IsPlayerControlled;
		}
		return false;
	}

	private bool WorldWarCancelInterceptIfBlocked()
	{
		if (interceptor == null || !RimPactsCompat.WorldWarBlocked(base.Faction))
		{
			return false;
		}
		EESLog.Message("Supply: 요격 자동 취소 [" + base.Faction?.Name + "] " + DestLabel() + " — " + RimPactsCompat.WorldWarBlockDebug(base.Faction) + " (캐러밴 " + interceptor.LabelCap + ").");
		ClearInterceptor(notify: true);
		return true;
	}

	private bool ValidateInterceptor()
	{
		if (interceptor == null)
		{
			return true;
		}
		if (WorldWarCancelInterceptIfBlocked())
		{
			return true;
		}
		if (!InterceptorLinked())
		{
			ClearInterceptor(notify: false);
			return true;
		}
		if (interceptor.Tile == base.Tile)
		{
			if (Find.WorldObjects.AnyMapParentAt(base.Tile))
			{
				return true;
			}
			Engage(interceptor);
			return false;
		}
		if (interceptor.Tile != interceptTile && PathDestinationOf(interceptor) != interceptTile)
		{
			ClearInterceptor(notify: false);
		}
		return true;
	}

	private void ClearInterceptor(bool notify)
	{
		if (notify && InterceptorLinked())
		{
			Messages.Message(EESText.T("EES.Supply.Missed"), interceptor, MessageTypeDefOf.NeutralEvent, historical: false);
		}
		interceptor = null;
		interceptTile = PlanetTile.Invalid;
	}

	protected virtual void Arrive()
	{
		Settlement settlement = WorldComponent_Strongholds.Instance?.GetSettlement(destSettlementId);
		if (settlement != null)
		{
			Messages.Message(EESText.T("EES.Supply.Arrived", settlement.LabelCap), settlement, MessageTypeDefOf.NeutralEvent, historical: false);
		}
		Disappear();
	}

	protected void DisappearInternal()
	{
		Disappear();
	}

	public void Notify_BattleBegun(PlanetTile mapTile)
	{
		ClearInterceptor(notify: false);
		battleTile = mapTile;
		battleStartTick = Find.TickManager.TicksGame;
	}

	public void Notify_BattleLost()
	{
		EESLog.Message($"Supply: 격파 확정 @ {battleTile} — 호송대 소멸 (목적지 {destTile}).");
		battleTile = PlanetTile.Invalid;
		battleStartTick = -1;
		Disappear();
	}

	public void Notify_BattleEnded(string reason)
	{
		if (battleTile.Valid)
		{
			spawnTick += Mathf.Max(0, Find.TickManager.TicksGame - battleStartTick);
			EESLog.Message($"Supply: 교전 결착(격파 아님: {reason}) @ {battleTile} — 호송대 이동 재개 → {destTile}.");
			battleTile = PlanetTile.Invalid;
			battleStartTick = -1;
			SetPath(null);
		}
	}

	private void Disappear()
	{
		ClearInterceptor(notify: true);
		if (!base.Destroyed)
		{
			Destroy();
		}
	}

	public bool IsInterceptor(Caravan c)
	{
		if (c != null)
		{
			return interceptor == c;
		}
		return false;
	}

	public void CancelIntercept()
	{
		ClearInterceptor(notify: false);
	}

	public override void DrawExtraSelectionOverlays()
	{
		base.DrawExtraSelectionOverlays();
		if (pendingNodes != null)
		{
			WorldGrid worldGrid = Find.WorldGrid;
			Vector3 a = Lift(DrawPos);
			if (nextTile.Valid)
			{
				Vector3 vector = Lift(worldGrid.GetTileCenter(nextTile));
				GenDraw.DrawWorldLineBetween(a, vector);
				a = vector;
			}
			for (int i = 0; i < pendingNodes.Count; i++)
			{
				Vector3 vector2 = Lift(worldGrid.GetTileCenter(pendingNodes[i]));
				GenDraw.DrawWorldLineBetween(a, vector2);
				a = vector2;
			}
		}
	}

	private static Vector3 Lift(Vector3 v)
	{
		return v + v.normalized * 0.08f;
	}

	public override string GetInspectString()
	{
		int num = Find.TickManager?.TicksGame ?? 0;
		if (inspectCache != null && num >= inspectCacheTick && num - inspectCacheTick < 60)
		{
			return inspectCache;
		}
		string result = (inspectCache = BuildInspectString());
		inspectCacheTick = num;
		return result;
	}

	private string BuildInspectString()
	{
		string text = base.GetInspectString();
		int numTicks = Mathf.RoundToInt((float)(((pendingNodes?.Count ?? 0) + (nextTile.Valid ? 1 : 0)) * 4700) + Mathf.Max(0f, nextCostLeft));
		if (!text.NullOrEmpty())
		{
			text += "\n";
		}
		text += EESText.T("EES.Supply.Inspect.Dest", DestLabel(), numTicks.ToStringTicksToPeriod());
		int num = ConvoyTier();
		return text + "\n" + EESText.T("EES.Supply.Inspect.Escort", Mathf.RoundToInt(Mathf.Clamp(200f + (float)num * 130f, 300f, 2600f) / 90f));
	}

	public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
	{
		foreach (FloatMenuOption floatMenuOption in base.GetFloatMenuOptions(caravan))
		{
			yield return floatMenuOption;
		}
		if (base.Faction == null || InBattle)
		{
			yield break;
		}
		if (interceptor == caravan)
		{
			yield return new FloatMenuOption(EESText.T("EES.Supply.Intercept.Cancel"), delegate
			{
				ClearInterceptor(notify: false);
				Messages.Message(EESText.T("EES.Supply.Intercept.Canceled"), caravan, MessageTypeDefOf.NeutralEvent, historical: false);
				EESLog.Message("Supply: 요격 취소 (캐러밴 " + caravan.LabelCap + ").");
			});
		}
		if (RimPactsCompat.BlocksSupplyIntercept(base.Faction, out var reason))
		{
			yield return new FloatMenuOption(reason, null);
			yield break;
		}
		bool flag = Find.WorldObjects.AnyMapParentAt(base.Tile);
		PlanetTile planetTile;
		int etaTicks;
		bool alreadyWaiting;
		if (caravan.Tile == base.Tile)
		{
			if (flag)
			{
				yield return new FloatMenuOption(EESText.T("EES.Supply.Intercept.Overlap"), null);
				yield break;
			}
			yield return new FloatMenuOption(EESText.T("EES.Supply.Intercept.Now"), delegate
			{
				EESLog.Message($"Supply: 지금 교전 (같은 칸 {base.Tile}, 캐러밴 {caravan.LabelCap}).");
				Engage(caravan);
			});
		}
		else if (TryFindInterceptTile(caravan, out planetTile, out etaTicks, out alreadyWaiting))
		{
			PlanetTile t = planetTile;
			string label = (alreadyWaiting ? EESText.T("EES.Supply.Intercept.Wait") : EESText.T("EES.Supply.Intercept")) + " (" + etaTicks.ToStringTicksToPeriod() + ")";
			yield return new FloatMenuOption(label, delegate
			{
				OrderIntercept(caravan, t);
			});
		}
		else
		{
			yield return new FloatMenuOption(EESText.T("EES.Supply.Intercept.No"), null);
		}
	}

	private bool TryFindInterceptTile(Caravan caravan, out PlanetTile tile, out int etaTicks, out bool alreadyWaiting)
	{
		tile = PlanetTile.Invalid;
		etaTicks = 0;
		alreadyWaiting = false;
		if (!EnsurePath() || caravan == null)
		{
			return false;
		}
		List<PlanetTile> list = new List<PlanetTile>();
		if (nextTile.Valid)
		{
			list.Add(nextTile);
		}
		for (int i = 0; i < pendingNodes.Count; i++)
		{
			if (list.Count >= 40)
			{
				break;
			}
			list.Add(pendingNodes[i]);
		}
		float num = (nextTile.Valid ? Mathf.Max(0f, nextCostLeft) : 4700f);
		for (int j = 0; j < list.Count; j++)
		{
			if (!Find.WorldObjects.AnyMapParentAt(list[j]))
			{
				int num2 = Mathf.RoundToInt(num + (float)(j * 4700));
				if (caravan.Tile == list[j])
				{
					tile = list[j];
					etaTicks = Mathf.Max(0, num2);
					alreadyWaiting = true;
					return true;
				}
				int num3;
				try
				{
					num3 = CaravanArrivalTimeEstimator.EstimatedTicksToArrive(caravan.Tile, list[j], caravan);
				}
				catch (Exception)
				{
					continue;
				}
				if (num3 > 0 && num3 <= 1800000 && num3 + 2500 < num2)
				{
					tile = list[j];
					etaTicks = num3;
					return true;
				}
			}
		}
		return false;
	}

	internal static object VehiclePatherOf(Caravan caravan)
	{
		if (!vfResolved)
		{
			vfResolved = true;
			try
			{
				Type type = AccessTools.TypeByName("Vehicles.World.VehicleCaravan");
				vfPatherField = ((type != null) ? AccessTools.Field(type, "vehiclePather") : null);
				Type type2 = vfPatherField?.FieldType;
				vfStartPath = ((type2 != null) ? AccessTools.Method(type2, "StartPath", new Type[4]
				{
					typeof(PlanetTile),
					typeof(CaravanArrivalAction),
					typeof(bool),
					typeof(bool)
				}) : null);
				vfStopDead = ((type2 != null) ? AccessTools.Method(type2, "StopDead", Type.EmptyTypes) : null);
				vfDestination = ((type2 != null) ? AccessTools.Property(type2, "Destination") : null);
				if (vfPatherField != null)
				{
					EESLog.Message($"Supply: Vehicle Framework 감지 — 차량 카라반 요격=vehiclePather 경유 (StartPath={vfStartPath != null}, Destination={vfDestination != null}).");
				}
			}
			catch (Exception ex)
			{
				EESLog.Error("VF 리플렉션 해석 실패(바닐라 경로로 폴백): " + ex);
			}
		}
		if (vfPatherField == null || caravan == null || !vfPatherField.DeclaringType.IsInstanceOfType(caravan))
		{
			return null;
		}
		return vfPatherField.GetValue(caravan);
	}

	private static void StartInterceptPath(Caravan caravan, PlanetTile tile)
	{
		object obj = VehiclePatherOf(caravan);
		if (obj != null && vfStartPath != null)
		{
			try
			{
				vfStartPath.Invoke(obj, new object[4] { tile, null, true, true });
				return;
			}
			catch (Exception ex)
			{
				EESLog.Error("VF StartPath 실패(바닐라 폴백): " + ex);
			}
		}
		caravan.pather.StartPath(tile, null, repathImmediately: true);
	}

	private static PlanetTile PathDestinationOf(Caravan caravan)
	{
		object obj = VehiclePatherOf(caravan);
		if (obj != null && vfDestination != null)
		{
			try
			{
				return (PlanetTile)vfDestination.GetValue(obj);
			}
			catch (Exception ex)
			{
				EESLog.Error("VF Destination 조회 실패(바닐라 폴백): " + ex);
			}
		}
		if (caravan.pather == null)
		{
			return PlanetTile.Invalid;
		}
		return caravan.pather.Destination;
	}

	internal static void StopPathAny(Caravan caravan)
	{
		object obj = VehiclePatherOf(caravan);
		if (obj != null && vfStopDead != null)
		{
			try
			{
				vfStopDead.Invoke(obj, null);
				return;
			}
			catch (Exception ex)
			{
				EESLog.Error("VF StopDead 실패(바닐라 폴백): " + ex);
			}
		}
		caravan.pather?.StopDead();
	}

	private void OrderIntercept(Caravan caravan, PlanetTile tile)
	{
		if (caravan != null && caravan.Spawned)
		{
			StartInterceptPath(caravan, tile);
			interceptor = caravan;
			interceptTile = tile;
			Messages.Message(EESText.T("EES.Supply.Intercept.Ordered"), caravan, MessageTypeDefOf.NeutralEvent, historical: false);
			EESLog.Message($"Supply: 요격 명령 — 차단 지점 {tile} (보급 현 위치 {base.Tile}).");
		}
	}

	public void Engage(Caravan caravan)
	{
		try
		{
			int tier = ConvoyTier();
			List<Pawn> list = SupplyCaravanUtility.GenerateEscort(base.Faction, tier, destSettlementId);
			int num = CaravanIncidentUtility.CalculateIncidentMapSize(caravan.PawnsListForReading, list);
			Map map = GetOrGenerateMapUtility.GetOrGenerateMap(caravan.Tile, new IntVec3(num, 1, num), SupplyCaravanUtility.EncounterDef);
			map.retainedCaravanData.Notify_GeneratedTempIncidentMapFor(caravan);
			MultipleCaravansCellFinder.FindStartingCellsFor2Groups(map, out var playerSpot, out var second);
			List<Pawn> list2 = new List<Pawn>(caravan.PawnsListForReading);
			CaravanEnterMapUtility.Enter(caravan, map, (Pawn x) => CellFinder.RandomSpawnCellForPawnNear(playerSpot, map), CaravanDropInventoryMode.DoNotDrop, draftColonists: true);
			IntVec3 playerCenter = playerSpot;
			IntVec3 reachTarget = playerSpot;
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			for (int num5 = 0; num5 < list2.Count; num5++)
			{
				Pawn pawn = list2[num5];
				if (pawn != null && pawn.Spawned && pawn.Map == map)
				{
					num2 += pawn.Position.x;
					num3 += pawn.Position.z;
					num4++;
					reachTarget = pawn.Position;
				}
			}
			if (num4 > 0)
			{
				playerCenter = new IntVec3(num2 / num4, 0, num3 / num4);
			}
			IntVec3 root = FindEscortSpotOppositePlayer(map, playerCenter, reachTarget, second);
			for (int num6 = 0; num6 < list.Count; num6++)
			{
				GenSpawn.Spawn(list[num6], CellFinder.RandomSpawnCellForPawnNear(root, map), map, Rot4.Random);
			}
			IntVec3 intVec = ((list.Count > 0) ? list[0].Position : map.Center);
			SupplyCaravanUtility.FinalizeBattleMap(map, base.Faction, list, intVec, destSettlementId, tier, this);
			EESLog.Message($"Supply: 지상 교전 성립 @ {map.Tile} [{base.Faction?.Name}] " + $"호위 {list.Count} (적대={base.Faction?.HostileTo(Faction.OfPlayer)}).");
			Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_SupplyEngaged"), EESText.T("EES.Supply.Engaged.Text"), EESLetterDefOf.EES_SupplyEngaged, new GlobalTargetInfo(intVec, map));
			Notify_BattleBegun(map.Tile);
		}
		catch (Exception ex)
		{
			EESLog.Error("Supply Engage: " + ex);
		}
	}

	private static IntVec3 FindEscortSpotOppositePlayer(Map map, IntVec3 playerCenter, IntVec3 reachTarget, IntVec3 fallback)
	{
		CellRect cellRect = CellRect.WholeMap(map);
		int num = Mathf.RoundToInt((float)map.Size.x * 0.2f);
		int num2 = Mathf.RoundToInt((float)map.Size.z * 0.2f);
		cellRect.minX += num;
		cellRect.minZ += num2;
		cellRect.maxX -= num;
		cellRect.maxZ -= num2;
		TraverseParms traverseParams = TraverseParms.For(TraverseMode.NoPassClosedDoors).WithFenceblocked(forceFenceblocked: true);
		IntVec3 intVec = IntVec3.Invalid;
		float num3 = -1f;
		for (int i = 0; i < 40; i++)
		{
			IntVec3 intVec2 = cellRect.EdgeCells.RandomElement();
			if (intVec2.Standable(map) && !intVec2.Fogged(map) && map.reachability.CanReach(intVec2, reachTarget, PathEndMode.OnCell, traverseParams))
			{
				float num4 = (intVec2 - playerCenter).LengthHorizontalSquared;
				if (num4 > num3)
				{
					num3 = num4;
					intVec = intVec2;
				}
			}
		}
		EESLog.Message($"Supply: 호위 스폰 — 플레이어 실측 중심 {playerCenter}, " + (intVec.IsValid ? $"반대편 {intVec}(거리 {Mathf.Sqrt(num3):F0})." : $"도달 가능 후보 0 → 폴백 {fallback}."));
		if (!intVec.IsValid)
		{
			return fallback;
		}
		return intVec;
	}

	public override IEnumerable<FloatMenuOption> GetTransportersFloatMenuOptions(IEnumerable<IThingHolder> pods, Action<PlanetTile, TransportersArrivalAction> launchAction)
	{
		foreach (FloatMenuOption transportersFloatMenuOption in base.GetTransportersFloatMenuOptions(pods, launchAction))
		{
			yield return transportersFloatMenuOption;
		}
		foreach (FloatMenuOption floatMenuOption in TransportersArrivalActionUtility.GetFloatMenuOptions(() => CanInterceptWithPods(pods), () => new TransportersArrivalAction_EESIntercept(this), EESText.T("EES.Supply.Intercept"), launchAction, base.Tile))
		{
			yield return floatMenuOption;
		}
	}

	public override IEnumerable<FloatMenuOption> GetShuttleFloatMenuOptions(IEnumerable<IThingHolder> pods, Action<PlanetTile, TransportersArrivalAction> launchAction)
	{
		foreach (FloatMenuOption shuttleFloatMenuOption in base.GetShuttleFloatMenuOptions(pods, launchAction))
		{
			yield return shuttleFloatMenuOption;
		}
		foreach (FloatMenuOption floatMenuOption in TransportersArrivalActionUtility.GetFloatMenuOptions(() => CanInterceptWithPods(pods), () => new TransportersArrivalAction_EESIntercept(this), EESText.T("EES.Supply.Intercept"), launchAction, base.Tile))
		{
			yield return floatMenuOption;
		}
	}

	private FloatMenuAcceptanceReport CanInterceptWithPods(IEnumerable<IThingHolder> pods)
	{
		if (base.Destroyed || !base.Spawned || base.Faction == null)
		{
			return false;
		}
		if (Find.WorldObjects.AnyMapParentAt(base.Tile))
		{
			return FloatMenuAcceptanceReport.WithFailReason(EESText.T("EES.Supply.Intercept.Overlap"));
		}
		if (RimPactsCompat.BlocksSupplyIntercept(base.Faction, out var reason))
		{
			return FloatMenuAcceptanceReport.WithFailReason(reason);
		}
		return TransportersArrivalActionUtility.AnyNonDownedColonist(pods);
	}
}
