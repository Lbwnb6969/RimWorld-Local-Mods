using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public class WorldComponent_EESThreatEmitters : WorldComponent
{
	private struct PendingShell
	{
		public Map map;

		public int tick;
	}

	private const int SchedulerInterval = 250;

	private const int ShrineInterval = 2500;

	private const int WarningGraceTicks = 180000;

	private const int PlayerOnMapRecheck = 15000;

	private const int ShellSpacingTicks = 90;

	private const float ShellRadius = 2.9f;

	private int nextArtillerySpawnTick = -1;

	private readonly List<PendingShell> pendingShells = new List<PendingShell>();

	private int lastShrineDiagTick = int.MinValue;

	private const int ShrineDiagInterval = 60000;

	private const float SpawnJitterDays = 5f;

	private int lastMapDiagTick = int.MinValue;

	private const int MapDiagInterval = 60000;

	private static float SpawnDays => StrongholdMod.Settings?.threatEmitterSpawnDays ?? 45f;

	private static float VolleyDays => StrongholdMod.Settings?.artilleryVolleyDays ?? 1f;

	private static GameConditionDef ShrineDroneDef => DefDatabase<GameConditionDef>.GetNamedSilentFail("EES_PsychicShrineDrone");

	public WorldComponent_EESThreatEmitters(World world)
		: base(world)
	{
	}

	public override void FinalizeInit(bool fromLoad)
	{
		base.FinalizeInit(fromLoad);
		try
		{
			ThreatEmitterUtility.DiagnoseAll(fromLoad ? "세이브 불러오기 직후" : "새 게임 시작 직후");
		}
		catch (Exception arg)
		{
			EESLog.Error($"위협 발신 진단 실패(무해·진단 전용): {arg}");
		}
	}

	public override void WorldComponentTick()
	{
		base.WorldComponentTick();
		int ticksGame = Find.TickManager.TicksGame;
		if (pendingShells.Count > 0)
		{
			ProcessShells(ticksGame);
		}
		if (ticksGame % 250 == 0 && ThreatEmitterUtility.Enabled)
		{
			TickArtillery(ticksGame);
		}
		if (ticksGame % 2500 == 0)
		{
			TickShrines();
		}
	}

	private void TickArtillery(int now)
	{
		if (nextArtillerySpawnTick < 0)
		{
			nextArtillerySpawnTick = now + Mathf.RoundToInt(SpawnDays * Rand.Range(0.5f, 1f) * 60000f);
		}
		else if (now >= nextArtillerySpawnTick)
		{
			nextArtillerySpawnTick = now + Mathf.RoundToInt(Rand.Range(SpawnDays - 5f, SpawnDays + 5f) * 60000f);
			TrySpawnEmitter();
		}
		TickEmitterWarnings(now);
		TickEmitterMapDiag(now);
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (settlement.Faction == null || settlement.Faction.IsPlayer || !settlement.Spawned)
			{
				continue;
			}
			StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
			if (strongholdData == null || strongholdData.roleDefName != "ArtilleryBase" || strongholdData.artillerySilenced || !ThreatEmitterUtility.IsLiveEmitter(settlement, strongholdData) || !ThreatEmitterUtility.GraceOver(strongholdData))
			{
				continue;
			}
			if (strongholdData.artilleryNextVolleyTick < 0)
			{
				strongholdData.artilleryNextVolleyTick = now;
				strongholdData.hasMortarBattery = true;
			}
			if (settlement.Map != null)
			{
				if (CountFactionMortars(settlement.Map, settlement.Faction) > 0)
				{
					strongholdData.artillerySawMortars = true;
				}
				else if (strongholdData.artillerySawMortars)
				{
					strongholdData.artillerySilenced = true;
					Messages.Message(EESText.T("EES.Arty.SilencedMsg", settlement.LabelCap), new GlobalTargetInfo(settlement), MessageTypeDefOf.PositiveEvent, historical: false);
					EESLog.Message("Emitter: 포격 기지 침묵 [" + settlement.LabelCap + "] — 박격포 전멸.");
					continue;
				}
			}
			if (now < strongholdData.artilleryNextVolleyTick)
			{
				continue;
			}
			if (settlement.Map != null && settlement.Map.mapPawns.FreeColonistsSpawnedCount > 0)
			{
				strongholdData.artilleryNextVolleyTick = now + 15000;
				EESLog.Message("Emitter: 포격 보류 [" + settlement.LabelCap + "] — 플레이어 거점 맵 체류 중(6h 뒤 재확인).");
				continue;
			}
			Map map = FindTargetHomeMap(settlement);
			strongholdData.artilleryNextVolleyTick = now + Mathf.RoundToInt(VolleyDays * 60000f);
			if (map == null)
			{
				EESLog.Message($"Emitter: 포격 표적 없음 [{settlement.LabelCap}] — 사거리({12f}타일) 내 본거지 부재.");
			}
			else
			{
				FireVolley(now, settlement, strongholdData, map);
			}
		}
	}

	private void TickEmitterMapDiag(int now)
	{
		if (!EESLog.Verbose || now - lastMapDiagTick < 60000)
		{
			return;
		}
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
		if (instance == null)
		{
			return;
		}
		bool flag = false;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (settlement?.Faction != null && settlement.Spawned && !settlement.Faction.IsPlayer && settlement.Map != null)
			{
				StrongholdData existing = instance.GetExisting(settlement.ID);
				if (existing != null && ThreatEmitterUtility.IsEmitterRole(existing.roleDefName))
				{
					flag = true;
					EESLog.Message("[위협발신 맵진단] '" + settlement.LabelCap + "' " + ThreatEmitterUtility.RoleLabelKo(existing.roleDefName) + " " + $"소유={settlement.Faction.Name} 적대={settlement.Faction.HostileTo(Faction.OfPlayer)} | " + ThreatEmitterUtility.MapDefeatDiag(settlement));
				}
			}
		}
		if (flag)
		{
			lastMapDiagTick = now;
		}
	}

	private void TickEmitterWarnings(int now)
	{
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (settlement.Faction == null || settlement.Faction.IsPlayer || !settlement.Spawned)
			{
				continue;
			}
			StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
			if (strongholdData == null || strongholdData.roleDefName == null || !ThreatEmitterUtility.IsLiveEmitter(settlement, strongholdData))
			{
				continue;
			}
			if (strongholdData.roleDefName == "JammingStation" && !strongholdData.emitterStartNotified && ThreatEmitterUtility.GraceOver(strongholdData))
			{
				strongholdData.emitterStartNotified = true;
				Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_JamStart"), EESText.T("EES.Jam.StartText", settlement.Faction.Name), LetterDefOf.NegativeEvent, new GlobalTargetInfo(settlement));
				EESLog.Message("Emitter: 재밍 개시 [" + settlement.LabelCap + "] — 유예 종료.");
			}
			if (strongholdData.emitterWarnedTick < 0)
			{
				string roleDefName = strongholdData.roleDefName;
				if (roleDefName == "ArtilleryBase" || roleDefName == "PsychicShrine" || roleDefName == "JammingStation")
				{
					strongholdData.emitterWarnedTick = now;
					string key = ((roleDefName == "PsychicShrine") ? "EES_Letter_ShrineWarn" : ((roleDefName == "JammingStation") ? "EES_Letter_JamWarn" : "EES_Letter_ArtyWarn"));
					string key2 = ((roleDefName == "PsychicShrine") ? "EES.Shrine.WarnText" : ((roleDefName == "JammingStation") ? "EES.Jam.WarnText" : "EES.Arty.WarnText"));
					Find.LetterStack.ReceiveLetter(EESText.T(key), EESText.T(key2, settlement.LabelCap, settlement.Faction.Name), LetterDefOf.ThreatSmall, new GlobalTargetInfo(settlement));
					EESLog.Message("Emitter: " + ThreatEmitterUtility.RoleLabelKo(roleDefName) + " 경고 [" + settlement.LabelCap + "] — " + 180000.ToStringTicksToPeriod() + " 후 효과 개시.");
				}
			}
		}
	}

	private void TrySpawnEmitter()
	{
		if (Find.AnyPlayerHomeMap == null)
		{
			return;
		}
		Settlement settlement = ThreatEmitterUtility.ActiveEmitter();
		if (settlement != null)
		{
			EESLog.Message("Emitter: 이미 존재(3종 통틀어 동시 1곳) — " + settlement.LabelCap + " 살아 있어 스폰 스킵.");
			return;
		}
		List<string> list = new List<string>();
		List<Faction> list2 = new List<Faction>();
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < ThreatEmitterUtility.EmitterRoles.Length; i++)
		{
			string text = ThreatEmitterUtility.EmitterRoles[i];
			Faction faction = ThreatEmitterUtility.PickFactionFor(text);
			if (faction == null)
			{
				stringBuilder.Append(" [" + ThreatEmitterUtility.RoleLabelKo(text) + "=세력 없음]");
				continue;
			}
			list.Add(text);
			list2.Add(faction);
		}
		if (list.Count == 0)
		{
			EESLog.Message($"Emitter: 3종 전부 세력 후보 없음 — 스킵.{stringBuilder}");
			return;
		}
		int index = Rand.Range(0, list.Count);
		EESLog.Message($"Emitter: 스폰 추첨 — 가능 {list.Count}종 중 " + $"{ThreatEmitterUtility.RoleLabelKo(list[index])} 선택.{stringBuilder}");
		ThreatEmitterUtility.TrySpawnEmitterBase(list2[index], list[index]);
	}

	private void FireVolley(int now, Settlement s, StrongholdData d, Map target)
	{
		int num = Rand.RangeInclusive(2, 3);
		for (int i = 0; i < num; i++)
		{
			pendingShells.Add(new PendingShell
			{
				map = target,
				tick = now + 300 + i * 90
			});
		}
		Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_ArtyVolley"), EESText.T("EES.Arty.VolleyText", s.LabelCap), LetterDefOf.ThreatSmall, new TargetInfo(target.Center, target));
		bool flag = s.Faction != null && s.Faction.HostileTo(Faction.OfPlayer);
		EESLog.Message($"Emitter: 포격 발사 [{s.LabelCap} T{d.tier}] 소유={s.Faction?.Name} 적대={flag} " + $"괴뢰국={RimPactsCompat.IsPuppet(s)} → {target.Parent?.LabelCap} 포탄 {num}발." + (flag ? "" : " ★★비적대 세력 소유인데 발사됨(적대 미검사 경로)."));
	}

	private void ProcessShells(int now)
	{
		for (int num = pendingShells.Count - 1; num >= 0; num--)
		{
			PendingShell pendingShell = pendingShells[num];
			if (pendingShell.map == null || !Find.Maps.Contains(pendingShell.map))
			{
				pendingShells.RemoveAt(num);
			}
			else if (now >= pendingShell.tick)
			{
				pendingShells.RemoveAt(num);
				DropShell(pendingShell.map);
			}
		}
	}

	private static void DropShell(Map map)
	{
		IntVec3 intVec = PickAimAnchor(map);
		float mortarMissRadius = ThreatEmitterUtility.MortarMissRadius;
		for (int i = 0; i < 30; i++)
		{
			IntVec3 intVec2;
			if (intVec.IsValid)
			{
				Vector2 vector = Rand.InsideUnitCircle * mortarMissRadius;
				intVec2 = new IntVec3(intVec.x + Mathf.RoundToInt(vector.x), 0, intVec.z + Mathf.RoundToInt(vector.y));
			}
			else
			{
				intVec2 = CellFinder.RandomCell(map);
			}
			if (intVec2.InBounds(map) && intVec2.x >= 8 && intVec2.z >= 8 && intVec2.x <= map.Size.x - 8 && intVec2.z <= map.Size.z - 8)
			{
				RoofDef roof = intVec2.GetRoof(map);
				if (roof == null || !roof.isThickRoof)
				{
					ThreatEmitterUtility.DetonateShell(intVec2, map);
					break;
				}
			}
		}
	}

	private static IntVec3 PickAimAnchor(Map map)
	{
		List<IntVec3> list = new List<IntVec3>();
		List<Pawn> freeColonistsAndPrisonersSpawned = map.mapPawns.FreeColonistsAndPrisonersSpawned;
		for (int i = 0; i < freeColonistsAndPrisonersSpawned.Count; i++)
		{
			if (freeColonistsAndPrisonersSpawned[i].Spawned)
			{
				list.Add(freeColonistsAndPrisonersSpawned[i].Position);
			}
		}
		List<Building> allBuildingsColonist = map.listerBuildings.allBuildingsColonist;
		for (int j = 0; j < allBuildingsColonist.Count; j++)
		{
			if (allBuildingsColonist[j].Spawned)
			{
				list.Add(allBuildingsColonist[j].Position);
			}
		}
		if (list.Count <= 0)
		{
			return IntVec3.Invalid;
		}
		return list.RandomElement();
	}

	private static Map FindTargetHomeMap(Settlement artillery)
	{
		Map result = null;
		float num = float.MaxValue;
		List<Map> maps = Find.Maps;
		for (int i = 0; i < maps.Count; i++)
		{
			Map map = maps[i];
			if (map.IsPlayerHome && map.Tile.Valid && map.Tile.Layer == artillery.Tile.Layer)
			{
				float num2 = Find.WorldGrid.ApproxDistanceInTiles(map.Tile, artillery.Tile);
				if (num2 <= 12f && num2 < num)
				{
					result = map;
					num = num2;
				}
			}
		}
		return result;
	}

	private static int CountFactionMortars(Map map, Faction faction)
	{
		int num = 0;
		List<Building> allBuildingsNonColonist = map.listerBuildings.allBuildingsNonColonist;
		for (int i = 0; i < allBuildingsNonColonist.Count; i++)
		{
			Building building = allBuildingsNonColonist[i];
			if (building?.def?.building != null && building.Faction == faction && building.def.building.IsMortar)
			{
				num++;
			}
		}
		return num;
	}

	private void TickShrines()
	{
		GameConditionDef shrineDroneDef = ShrineDroneDef;
		if (shrineDroneDef == null)
		{
			if (EESLog.Verbose && Find.TickManager.TicksGame - lastShrineDiagTick >= 60000)
			{
				lastShrineDiagTick = Find.TickManager.TicksGame;
				EESLog.Message("[제단 주기] ★EES_PsychicShrineDrone GameConditionDef 없음 — 부여·해제가 영구 불가.");
			}
			return;
		}
		int ticksGame = Find.TickManager.TicksGame;
		bool flag = EESLog.Verbose && ticksGame - lastShrineDiagTick >= 60000;
		if (flag)
		{
			lastShrineDiagTick = ticksGame;
		}
		List<Map> maps = Find.Maps;
		int num = 0;
		for (int i = 0; i < maps.Count; i++)
		{
			Map map = maps[i];
			if (!map.IsPlayerHome || !map.Tile.Valid)
			{
				continue;
			}
			num++;
			Settlement settlement = (ThreatEmitterUtility.Enabled ? FindShrineInRange(map) : null);
			GameCondition activeCondition = map.gameConditionManager.GetActiveCondition(shrineDroneDef);
			if (flag)
			{
				EESLog.Message("[제단 주기] '" + map.Parent?.LabelCap + "': 컨디션=" + ((activeCondition != null) ? "ON" : "off") + " 사거리내제단=" + ((settlement != null) ? ("'" + settlement.LabelCap + "'[" + settlement.Faction?.Name + "]") : "없음") + " → " + ((settlement != null && activeCondition == null) ? "부여 예정" : ((settlement == null && activeCondition != null) ? "해제 예정" : "변화 없음")) + " | " + ThreatEmitterUtility.GateBreakdown());
			}
			if (settlement != null && activeCondition == null)
			{
				GameCondition gameCondition = GameConditionMaker.MakeConditionPermanent(shrineDroneDef);
				if (gameCondition is GameCondition_PsychicEmanation gameCondition_PsychicEmanation)
				{
					gameCondition_PsychicEmanation.level = PsychicDroneLevel.BadMedium;
					gameCondition_PsychicEmanation.gender = ((settlement.ID % 2 == 0) ? Gender.Male : Gender.Female);
				}
				map.gameConditionManager.RegisterCondition(gameCondition);
				Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_ShrineStart"), EESText.T("EES.Shrine.StartText", settlement.LabelCap, settlement.Faction?.Name), LetterDefOf.NegativeEvent, new GlobalTargetInfo(settlement));
				ThreatEmitterUtility.NearestPlayerHome(settlement.Tile, out var dist);
				EESLog.Message($"Emitter: 정신 파동 시작 [{settlement.LabelCap}] @{settlement.Tile} " + $"소유={settlement.Faction?.Name}[{settlement.Faction?.def?.techLevel}] 거리 {dist:F1}타일 → {map.Parent?.LabelCap}.");
			}
			else if (settlement == null && activeCondition != null)
			{
				activeCondition.End();
				Messages.Message(EESText.T("EES.Shrine.EndMsg"), MessageTypeDefOf.PositiveEvent, historical: false);
				EESLog.Message("Emitter: 정신 파동 종료 — " + map.Parent?.LabelCap + " | 사유=" + ShrineEndReason() + " | " + ThreatEmitterUtility.GateBreakdown());
			}
		}
		if (flag && num == 0)
		{
			EESLog.Message("[제단 주기] 플레이어 본거지 맵 0곳 — 부여·해제 판정이 아무 맵에도 닿지 않는다.");
		}
	}

	private static string ShrineEndReason()
	{
		if (!ThreatEmitterUtility.MasterEnabled)
		{
			return "설정 마스터 토글 OFF";
		}
		if (RimPactsCompat.StrategyMode)
		{
			return "RimPacts 전략 모드";
		}
		if (!ThreatEmitterUtility.Enabled)
		{
			return $"해금일 미도달(남은 {ThreatEmitterUtility.StartDayRemaining:F1}일)";
		}
		return "사거리 내 제단 소멸(파괴·비적대 전환·사거리 이탈 중 하나)";
	}

	private static Settlement FindShrineInRange(Map home)
	{
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (settlement.Faction != null && !settlement.Faction.IsPlayer && settlement.Spawned && settlement.Tile.Valid && settlement.Tile.Layer == home.Tile.Layer && !(Find.WorldGrid.ApproxDistanceInTiles(settlement.Tile, home.Tile) > 30f))
			{
				StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
				if (strongholdData != null && !(strongholdData.roleDefName != "PsychicShrine") && ThreatEmitterUtility.IsLiveEmitter(settlement, strongholdData) && ThreatEmitterUtility.GraceOver(strongholdData))
				{
					return settlement;
				}
			}
		}
		return null;
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref nextArtillerySpawnTick, "ees_nextArtillerySpawnTick", -1);
	}
}
