using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class ThreatEmitterUtility
{
	public const float EmitterRangeTiles = 30f;

	public const float ArtilleryRangeTiles = 12f;

	public const int ArtilleryMinTier = 6;

	public const int ArtilleryMaxTier = 6;

	public const int ShrineMinTier = 1;

	public const int ShrineMaxTier = 3;

	public const int JammingMinTier = 4;

	public const int JammingMaxTier = 5;

	public const float JammingScoutPenalty = 0.15f;

	public static bool DevIgnoreStartDay;

	public static readonly string[] EmitterRoles = new string[3] { "ArtilleryBase", "PsychicShrine", "JammingStation" };

	public const int WarningGraceTicks = 180000;

	private const int SpawnMinDist = 6;

	private const int SpawnMaxDist = 10;

	private const float MinSettlementGap = 4f;

	private static float missRadiusCache = -1f;

	private static bool shellResolved;

	private static ThingDef shellSourceDef;

	public const float VanillaShellRadius = 2.9f;

	public static bool MasterEnabled
	{
		get
		{
			if (StrongholdMod.Settings != null)
			{
				return StrongholdMod.Settings.enableThreatEmitters;
			}
			return false;
		}
	}

	public static float StartDayRemaining => Mathf.Max(0f, (StrongholdMod.Settings?.threatEmitterStartDay ?? 600f) - DaysPassed);

	public static bool Enabled
	{
		get
		{
			if (!MasterEnabled)
			{
				return false;
			}
			if (RimPactsCompat.StrategyMode)
			{
				return false;
			}
			if (DevIgnoreStartDay)
			{
				return true;
			}
			float num = StrongholdMod.Settings?.threatEmitterStartDay ?? 600f;
			if (num > 0f && DaysPassed < num)
			{
				return false;
			}
			return true;
		}
	}

	public static float DaysPassed => (float)(Find.TickManager?.TicksGame ?? 0) / 60000f;

	public static float MortarMissRadius
	{
		get
		{
			if (missRadiusCache >= 0f)
			{
				return missRadiusCache;
			}
			missRadiusCache = 9f;
			ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("Artillery_Mortar");
			if (namedSilentFail?.Verbs != null)
			{
				for (int i = 0; i < namedSilentFail.Verbs.Count; i++)
				{
					if (namedSilentFail.Verbs[i].ForcedMissRadius > 0f)
					{
						missRadiusCache = namedSilentFail.Verbs[i].ForcedMissRadius;
						break;
					}
				}
			}
			EESLog.Message($"Emitter: 낙탄 산포 반경 = {missRadiusCache} (Artillery_Mortar forcedMissRadius).");
			return missRadiusCache;
		}
	}

	private static ThingDef ShellSource
	{
		get
		{
			if (shellResolved)
			{
				return shellSourceDef;
			}
			shellResolved = true;
			if (CECompat.Active)
			{
				shellSourceDef = DefDatabase<ThingDef>.GetNamedSilentFail("Bullet_81mmMortarShell_HE");
			}
			EESLog.Message("Emitter: 낙탄 탄종 = " + ((shellSourceDef != null) ? shellSourceDef.defName : "바닐라 폭발(CE 비활성 또는 def 부재)") + ".");
			return shellSourceDef;
		}
	}

	public static int MinTierFor(string role)
	{
		if (role == "PsychicShrine")
		{
			return 1;
		}
		if (role == "JammingStation")
		{
			return 4;
		}
		return 6;
	}

	public static int MaxTierFor(string role)
	{
		if (role == "PsychicShrine")
		{
			return 3;
		}
		if (role == "JammingStation")
		{
			return 5;
		}
		return 6;
	}

	public static int RollTierFor(string role)
	{
		return Rand.RangeInclusive(MinTierFor(role), MaxTierFor(role));
	}

	public static bool GraceOver(StrongholdData d)
	{
		if (d == null || d.emitterWarnedTick < 0)
		{
			return false;
		}
		return Find.TickManager.TicksGame >= d.emitterWarnedTick + 180000;
	}

	public static int GraceRemaining(StrongholdData d)
	{
		if (d == null || d.emitterWarnedTick < 0)
		{
			return 180000;
		}
		return Mathf.Max(0, d.emitterWarnedTick + 180000 - Find.TickManager.TicksGame);
	}

	public static bool IsEmitterRole(string role)
	{
		if (role == null)
		{
			return false;
		}
		for (int i = 0; i < EmitterRoles.Length; i++)
		{
			if (role == EmitterRoles[i])
			{
				return true;
			}
		}
		return false;
	}

	public static bool IsLiveEmitter(Settlement s, StrongholdData d)
	{
		if (s == null || !s.Spawned || s.Faction == null || s.Faction.IsPlayer)
		{
			return false;
		}
		if (d == null || !IsEmitterRole(d.roleDefName))
		{
			return false;
		}
		if (!s.Faction.HostileTo(Faction.OfPlayer))
		{
			return false;
		}
		if (d.roleDefName == "ArtilleryBase" && d.artillerySilenced)
		{
			return false;
		}
		return true;
	}

	public static Settlement ActiveEmitter()
	{
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (settlement?.Faction != null && settlement.Spawned)
			{
				StrongholdData d = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
				if (IsLiveEmitter(settlement, d))
				{
					return settlement;
				}
			}
		}
		return null;
	}

	public static void CollectEmitters(List<Settlement> outList)
	{
		outList.Clear();
		WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
		if (instance == null)
		{
			return;
		}
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (settlement?.Faction != null && settlement.Spawned && !settlement.Faction.IsPlayer)
			{
				StrongholdData existing = instance.GetExisting(settlement.ID);
				if (existing != null && IsEmitterRole(existing.roleDefName))
				{
					outList.Add(settlement);
				}
			}
		}
	}

	public static bool JammingActive(Faction faction)
	{
		if (!Enabled || faction == null)
		{
			return false;
		}
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (settlement.Faction == faction && settlement.Spawned)
			{
				StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
				if (strongholdData != null && !(strongholdData.roleDefName != "JammingStation") && IsLiveEmitter(settlement, strongholdData) && GraceOver(strongholdData) && AnyPlayerHomeWithin(settlement.Tile, 30f))
				{
					return true;
				}
			}
		}
		return false;
	}

	public static float JammingPenaltyFor(int settlementId)
	{
		if (!Enabled)
		{
			return 0f;
		}
		Settlement settlement = FindSettlementById(settlementId);
		if (settlement?.Faction == null)
		{
			return 0f;
		}
		if (!JammingActive(settlement.Faction))
		{
			return 0f;
		}
		return 0.15f;
	}

	public static Settlement FindSettlementById(int settlementId)
	{
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			if (settlements[i].ID == settlementId)
			{
				return settlements[i];
			}
		}
		return null;
	}

	public static bool AnyPlayerHomeWithin(PlanetTile tile, float radius)
	{
		List<Map> maps = Find.Maps;
		for (int i = 0; i < maps.Count; i++)
		{
			Map map = maps[i];
			if (map.IsPlayerHome && map.Tile.Valid && map.Tile.Layer == tile.Layer && Find.WorldGrid.ApproxDistanceInTiles(map.Tile, tile) <= radius)
			{
				return true;
			}
		}
		return false;
	}

	public static Faction PickFactionFor(string role)
	{
		bool installed = RimPactsCompat.Installed;
		List<Faction> allFactionsListForReading = Find.FactionManager.AllFactionsListForReading;
		List<Faction> list = new List<Faction>();
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		for (int i = 0; i < allFactionsListForReading.Count; i++)
		{
			Faction faction = allFactionsListForReading[i];
			if (!faction.IsPlayer && !faction.temporary && !faction.defeated && !faction.Hidden && faction.def != null && faction.def.humanlikeFaction)
			{
				if (installed && !faction.def.permanentEnemy)
				{
					num++;
				}
				else if (!TechOkFor(role, faction.def.techLevel))
				{
					num2++;
				}
				else if (!faction.HostileTo(Faction.OfPlayer))
				{
					num3++;
				}
				else if (EESFactionBlacklist.IsBlocked(faction))
				{
					num4++;
				}
				else
				{
					list.Add(faction);
				}
			}
		}
		if (list.Count == 0)
		{
			EESLog.Message("Emitter: " + RoleLabelKo(role) + " 세력 후보 0 — RimPacts=" + (installed ? "설치(영구 적대만)" : "미설치(적대 전체)") + " | " + $"컷 [영구적대아님 {num}·기술조건 {num2}·비적대 {num3}·블랙리스트 {num4}]");
		}
		if (list.Count <= 0)
		{
			return null;
		}
		return list.RandomElement();
	}

	public static bool TechOkFor(string role, TechLevel tech)
	{
		if (role == "PsychicShrine")
		{
			return (int)tech <= 2;
		}
		_ = role == "JammingStation";
		return (int)tech >= 4;
	}

	public static string RoleLabelKo(string role)
	{
		if (role == "PsychicShrine")
		{
			return "주술 제단";
		}
		if (role == "JammingStation")
		{
			return "재밍 기지";
		}
		return "포격 기지";
	}

	public static Settlement TrySpawnEmitterBase(Faction fac, string role)
	{
		if (fac == null || role == null)
		{
			return null;
		}
		string text = RoleLabelKo(role);
		Predicate<PlanetTile> noObject = (PlanetTile t) => !Find.WorldObjects.AnyWorldObjectAt(t);
		Predicate<PlanetTile> validator = (PlanetTile t) => noObject(t) && !AnySettlementWithin(t, 4f);
		if (!TileFinder.TryFindNewSiteTile(out var tile, 6, 10, allowCaravans: false, null, 0.5f, canSelectComboLandmarks: true, TileFinderMode.Near, exitOnFirstTileFound: false, canBeSpace: false, null, validator) && !TileFinder.TryFindNewSiteTile(out tile, 6, 10, allowCaravans: false, null, 0.5f, canSelectComboLandmarks: true, TileFinderMode.Near, exitOnFirstTileFound: false, canBeSpace: false, null, noObject))
		{
			EESLog.Message("Emitter: " + text + " 스폰 타일 없음(2일 거리 내 후보 소진) — 이번 주기 스킵.");
			return null;
		}
		if (!tile.Valid)
		{
			return null;
		}
		Settlement settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
		settlement.SetFaction(fac);
		settlement.Tile = tile;
		settlement.Name = SettlementNameGenerator.GenerateSettlementName(settlement);
		Find.WorldObjects.Add(settlement);
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
		if (strongholdData != null)
		{
			strongholdData.tier = RollTierFor(role);
			strongholdData.tierSource = "emitter";
			strongholdData.roleDefName = role;
			strongholdData.emitterSpawnFactionLoadId = fac.loadID;
			strongholdData.emitterSpawnFactionName = fac.Name;
			StrongholdFeatureUtility.EnsureAssigned(strongholdData, settlement, WorldComponent_Strongholds.Instance);
			StrongholdScoreUtility.ApplyDerived(strongholdData, settlement);
			StrongholdScoreUtility.DecideFacilities(strongholdData, settlement);
			if (role == "ArtilleryBase")
			{
				strongholdData.hasMortarBattery = true;
			}
			GarrisonGroupUtility.DecidePlan(strongholdData, settlement);
		}
		EESLog.Message($"Emitter: {text} 스폰 [{fac.Name} tech={fac.def?.techLevel}] {settlement.LabelCap} @ {tile} " + $"T{strongholdData?.tier}(범위 T{MinTierFor(role)}~T{MaxTierFor(role)}).");
		return settlement;
	}

	public static void DetonateShell(IntVec3 cell, Map map)
	{
		ProjectileProperties projectileProperties = ShellSource?.projectile;
		if (projectileProperties == null)
		{
			GenExplosion.DoExplosion(cell, map, 2.9f, DamageDefOf.Bomb, null);
			return;
		}
		float explosionRadius = projectileProperties.explosionRadius;
		DamageDef damType = projectileProperties.damageDef ?? DamageDefOf.Bomb;
		int damageAmount = projectileProperties.GetDamageAmount(null);
		float armorPenetration = projectileProperties.GetArmorPenetration();
		SoundDef soundExplode = projectileProperties.soundExplode;
		ThingDef preExplosionSpawnSingleThingDef = projectileProperties.preExplosionSpawnSingleThingDef;
		float preExplosionSpawnChance = projectileProperties.preExplosionSpawnChance;
		bool applyDamageToExplosionCellsNeighbors = projectileProperties.applyDamageToExplosionCellsNeighbors;
		GenExplosion.DoExplosion(cell, map, explosionRadius, damType, null, damageAmount, armorPenetration, soundExplode, null, null, null, null, 0f, 1, null, null, 255, applyDamageToExplosionCellsNeighbors, preExplosionSpawnSingleThingDef, preExplosionSpawnChance);
	}

	public static Map NearestPlayerHome(PlanetTile tile, out float dist)
	{
		dist = -1f;
		Map map = null;
		List<Map> maps = Find.Maps;
		for (int i = 0; i < maps.Count; i++)
		{
			Map map2 = maps[i];
			if (map2.IsPlayerHome && map2.Tile.Valid && map2.Tile.Layer == tile.Layer)
			{
				float num = Find.WorldGrid.ApproxDistanceInTiles(map2.Tile, tile);
				if (map == null || num < dist)
				{
					map = map2;
					dist = num;
				}
			}
		}
		return map;
	}

	public static string MapDefeatDiag(Settlement s)
	{
		Map map = s?.Map;
		if (map == null)
		{
			return "맵=없음(진입 전 · 파괴 판정은 맵이 열린 동안만 돈다)";
		}
		Faction faction = s.Faction;
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int num6 = 0;
		int num7 = 0;
		int num8 = 0;
		List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(faction);
		int num9 = 0;
		for (int i = 0; i < list.Count; i++)
		{
			Pawn pawn = list[i];
			if (pawn == null || pawn.Dead || !pawn.RaceProps.Humanlike)
			{
				continue;
			}
			num9++;
			if (GenHostility.IsActiveThreatToPlayer(pawn, canBeFogged: true))
			{
				num++;
				if (pawn.Fogged())
				{
					num2++;
				}
			}
			else if (pawn.Downed)
			{
				num3++;
			}
			else if (!pawn.Awake())
			{
				num4++;
			}
			else if (pawn.MentalStateDef == MentalStateDefOf.PanicFlee)
			{
				num5++;
			}
			else if (pawn.IsPrisoner)
			{
				num6++;
			}
			else
			{
				CompCanBeDormant comp = pawn.GetComp<CompCanBeDormant>();
				if (comp != null && !comp.Awake)
				{
					num7++;
				}
				else
				{
					num8++;
				}
			}
		}
		bool flag = SettlementDefeatUtility.IsDefeated(map, faction);
		bool anyPawnBlockingMapRemoval = map.mapPawns.AnyPawnBlockingMapRemoval;
		string arg = ((num <= 0) ? (flag ? "파괴 조건 충족(다음 틱에 파괴되어야 정상)" : "★차단 폰 0인데 IsDefeated=False — 다른 모드가 CheckDefeated 를 가로챈 것이다") : ((num2 > 0) ? $"★★안개 속 {num2}명이 파괴를 막고 있다(플레이어 눈엔 안 보인다)" : "★남은 폰이 파괴를 막고 있다"));
		return $"맵=열림 | 소유세력 humanlike {num9}명 중 ★파괴차단 {num}명(안개 속 {num2}) " + $"[제외 쓰러짐 {num3}·수면 {num4}·도주 {num5}·포로 {num6}·휴면 {num7}·기타 {num8}] | " + $"IsDefeated={flag} 맵제거차단={anyPawnBlockingMapRemoval} | {arg}";
	}

	public static string GateBreakdown()
	{
		float num = StrongholdMod.Settings?.threatEmitterStartDay ?? 600f;
		float daysPassed = DaysPassed;
		string arg = ((num > 0f) ? string.Format("해금일 {0:F0}일 vs 경과 {1:F1}일={2}", num, daysPassed, (daysPassed < num) ? $"★미도달(남은 {num - daysPassed:F1}일)" : "통과") : "해금일 0=즉시");
		return $"Enabled={Enabled} (마스터토글={MasterEnabled} · RimPacts전략모드={RimPactsCompat.StrategyMode} · " + $"DEV해금무시={DevIgnoreStartDay} · {arg}) · " + "세력 선정=" + (RimPactsCompat.Installed ? "★영구 적대(해적)만 — RimPacts 설치" : "적대 전체 — RimPacts 미설치");
	}

	public static void DiagnoseAll(string reason)
	{
		if (!EESLog.Verbose)
		{
			return;
		}
		EESLog.Message("[위협발신 진단] ===== " + reason + " ===== " + GateBreakdown());
		WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		int num = 0;
		Settlement settlement = ActiveEmitter();
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement2 = settlements[i];
			if (settlement2?.Faction == null || !settlement2.Spawned)
			{
				continue;
			}
			StrongholdData strongholdData = instance?.GetExisting(settlement2.ID);
			if (strongholdData == null || strongholdData.roleDefName == null)
			{
				continue;
			}
			bool flag = false;
			for (int j = 0; j < EmitterRoles.Length; j++)
			{
				if (strongholdData.roleDefName == EmitterRoles[j])
				{
					flag = true;
					break;
				}
			}
			if (flag)
			{
				num++;
				string roleDefName = strongholdData.roleDefName;
				string text = RoleLabelKo(roleDefName);
				Faction faction = settlement2.Faction;
				TechLevel techLevel = faction.def?.techLevel ?? TechLevel.Undefined;
				bool flag2 = faction.HostileTo(Faction.OfPlayer);
				bool flag3 = RimPactsCompat.IsPuppet(settlement2);
				bool flag4 = TechOkFor(roleDefName, techLevel);
				string text2 = ((strongholdData.emitterSpawnFactionLoadId < 0) ? ($"소유={faction.Name}[{techLevel}] (스폰 당시 기록 없음=구 세이브)" + (flag4 ? "" : string.Format(" ★★세력 이전 의심 — {0} 은 스폰 시 {1} 여야 하는데 현재 {2}", text, (roleDefName == "PsychicShrine") ? "신석기 이하" : "산업+", techLevel))) : ((strongholdData.emitterSpawnFactionLoadId == faction.loadID) ? $"소유={faction.Name}[{techLevel}] (스폰 때와 동일)" : $"소유={faction.Name}[{techLevel}] ★★세력 이전됨(스폰 당시={strongholdData.emitterSpawnFactionName})"));
				float dist;
				Map map = NearestPlayerHome(settlement2.Tile, out dist);
				string text3 = ((map == null) ? "본거지 없음" : $"최근접 본거지 '{map.Parent?.LabelCap}' {dist:F1}타일");
				bool flag5 = map != null && dist <= 30f;
				bool flag6 = GraceOver(strongholdData);
				string text4 = ((strongholdData.emitterWarnedTick < 0) ? "유예=경고 전(미개시)" : (flag6 ? "유예=종료(효과 개시됨)" : ("유예=진행중(남은 " + GraceRemaining(strongholdData).ToStringTicksToPeriod() + ")")));
				string arg;
				if (roleDefName == "PsychicShrine")
				{
					bool flag7 = Enabled && flag6 && flag2 && flag5;
					arg = string.Format("효과(정신 파동)={0} [Enabled={1} 유예={2} ★적대={3} 사거리30내={4}]", flag7 ? "가동" : "정지", Enabled, flag6, flag2, flag5);
				}
				else if (roleDefName == "JammingStation")
				{
					bool flag8 = Enabled && flag6 && flag5;
					arg = string.Format("효과(재밍 −{0:F0}%p)={1} ", 15.000001f, flag8 ? "가동" : "정지") + $"[Enabled={Enabled} 유예={flag6} 사거리30내={flag5}] ★적대 미검사(적대={flag2} 이어도 가동)";
				}
				else
				{
					bool flag9 = map != null && dist <= 12f;
					bool flag10 = Enabled && flag6 && !strongholdData.artillerySilenced && flag9;
					arg = "효과(포격)=" + (flag10 ? "가동" : "정지") + " " + $"[Enabled={Enabled} 유예={flag6} 침묵={strongholdData.artillerySilenced}(박격포목격={strongholdData.artillerySawMortars}) 사거리12내={flag9}] " + $"★적대 미검사(적대={flag2} 이어도 발사)";
				}
				EESLog.Message($"[위협발신 진단] #{num} '{settlement2.LabelCap}' @{settlement2.Tile} 역할={text} T{strongholdData.tier}({strongholdData.tierSource}) | " + $"{text2} 적대={flag2} 괴뢰국={flag3} 세력조건TechOk={flag4} | {text4} | {text3} | " + $"{arg} | 슬롯점유={settlement == settlement2} | {MapDefeatDiag(settlement2)}");
			}
		}
		if (settlement == null)
		{
			EESLog.Message($"[위협발신 진단] 슬롯=비어 있음(신규 스폰 가능) · 위협 발신 거점 총 {num}곳.");
		}
		else
		{
			StrongholdData strongholdData2 = instance?.GetExisting(settlement.ID);
			bool flag11 = settlement.Faction != null && settlement.Faction.HostileTo(Faction.OfPlayer);
			bool flag12 = RimPactsCompat.IsPuppet(settlement);
			string arg2 = (flag11 ? "" : " ★★★슬롯 점유자가 비적대다 — ActiveEmitter 는 적대를 검사하지 않으므로 효과가 0인 채로 슬롯만 잡고 있고, 위협 발신 3종의 신규 스폰이 영구 차단된 상태다.");
			EESLog.Message("[위협발신 진단] 슬롯=점유 '" + settlement.LabelCap + "'(" + RoleLabelKo(strongholdData2?.roleDefName) + ") " + $"소유={settlement.Faction?.Name} 적대={flag11} 괴뢰국={flag12} → 신규 스폰 차단 중 · " + $"위협 발신 거점 총 {num}곳.{arg2}");
		}
		GameConditionDef namedSilentFail = DefDatabase<GameConditionDef>.GetNamedSilentFail("EES_PsychicShrineDrone");
		if (namedSilentFail == null)
		{
			EESLog.Message("[위협발신 진단] ★EES_PsychicShrineDrone GameConditionDef 없음 — Defs 로드 실패.");
			return;
		}
		List<Map> maps = Find.Maps;
		int num2 = 0;
		for (int k = 0; k < maps.Count; k++)
		{
			Map map2 = maps[k];
			if (map2.IsPlayerHome && map2.Tile.Valid)
			{
				num2++;
				bool flag13 = map2.gameConditionManager.GetActiveCondition(namedSilentFail) != null;
				Settlement settlement3 = ShrineInRangeOf(map2);
				string text5 = ((flag13 && settlement3 == null) ? (Enabled ? "★★원인 거점 없음인데 켜져 있다 → 다음 제단 주기(2500틱=게임내 1시간)에 자동 해제되어야 한다. 1시간 넘게 지나도 안 꺼지면 해제 경로가 이 맵에 안 닿는 것이다(TickShrines 는 IsPlayerHome 맵만 순회한다)." : "★★관문이 꺼져 있는데 켜져 있다 → 다음 제단 주기(게임내 1시간)에 해제 예정.") : ((!flag13) ? ((settlement3 == null) ? "정상(원인 거점 없음)" : ("★사거리 내 제단 '" + settlement3.LabelCap + "' 이 있는데 꺼져 있다 → 다음 주기에 부여 예정.")) : ("원인='" + settlement3.LabelCap + "'[" + settlement3.Faction?.Name + "] → 정상. 이 거점을 파괴하거나 비적대로 만들어야 꺼진다.")));
				EESLog.Message(string.Format("[위협발신 진단] 본거지 '{0}' @{1}: 정신 파동 컨디션={2} | {3}", map2.Parent?.LabelCap, map2.Tile, flag13 ? "ON" : "off", text5));
			}
		}
		if (num2 == 0)
		{
			EESLog.Message("[위협발신 진단] 플레이어 본거지 맵 0곳 — 제단 판정 자체가 돌지 않는다.");
		}
	}

	private static Settlement ShrineInRangeOf(Map home)
	{
		if (!Enabled)
		{
			return null;
		}
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (settlement.Faction != null && !settlement.Faction.IsPlayer && settlement.Spawned && settlement.Tile.Valid && settlement.Tile.Layer == home.Tile.Layer && !(Find.WorldGrid.ApproxDistanceInTiles(settlement.Tile, home.Tile) > 30f))
			{
				StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
				if (strongholdData != null && !(strongholdData.roleDefName != "PsychicShrine") && IsLiveEmitter(settlement, strongholdData) && GraceOver(strongholdData))
				{
					return settlement;
				}
			}
		}
		return null;
	}

	private static bool AnySettlementWithin(PlanetTile tile, float radius)
	{
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (settlement.Tile.Valid && settlement.Tile.Layer == tile.Layer && Find.WorldGrid.ApproxDistanceInTiles(settlement.Tile, tile) <= radius)
			{
				return true;
			}
		}
		return false;
	}

	public static void FurnishShrine(Map map, Faction faction, StrongholdData data, CellRect room)
	{
		int num = 0;
		num += ((TryPlaceCenter(map, faction, room, "SculptureGrand") > 0) ? 1 : TryPlaceCenter(map, faction, room, "SculptureLarge"));
		ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail("Brazier") ?? DefDatabase<ThingDef>.GetNamedSilentFail("TorchLamp");
		if (thingDef != null)
		{
			IntVec3[] array = new IntVec3[4]
			{
				new IntVec3(room.minX, 0, room.minZ),
				new IntVec3(room.maxX, 0, room.minZ),
				new IntVec3(room.minX, 0, room.maxZ),
				new IntVec3(room.maxX, 0, room.maxZ)
			};
			foreach (IntVec3 intVec in array)
			{
				if (intVec.InBounds(map) && intVec.Standable(map) && intVec.GetFirstBuilding(map) == null)
				{
					try
					{
						Thing thing = ThingMaker.MakeThing(thingDef, thingDef.MadeFromStuff ? GenStuff.DefaultStuffFor(thingDef) : null);
						thing.SetFactionDirect(faction);
						GenSpawn.Spawn(thing, intVec, map);
						num++;
					}
					catch
					{
					}
				}
			}
		}
		EESLog.Message($"Emitter shrine furnish [{faction?.Name}]: 소품 {num}개.");
	}

	private static int TryPlaceCenter(Map map, Faction faction, CellRect room, string defName)
	{
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
		if (namedSilentFail == null)
		{
			return 0;
		}
		IntVec3 centerCell = room.CenterCell;
		if (!centerCell.InBounds(map) || !centerCell.Standable(map) || centerCell.GetFirstBuilding(map) != null)
		{
			return 0;
		}
		try
		{
			Thing thing = ThingMaker.MakeThing(namedSilentFail, namedSilentFail.MadeFromStuff ? GenStuff.DefaultStuffFor(namedSilentFail) : null);
			thing.SetFactionDirect(faction);
			GenSpawn.Spawn(thing, centerCell, map);
			return 1;
		}
		catch
		{
			return 0;
		}
	}
}
