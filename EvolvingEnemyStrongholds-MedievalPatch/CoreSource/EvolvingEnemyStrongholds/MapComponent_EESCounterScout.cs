using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class MapComponent_EESCounterScout : MapComponent
{
	public class ScoutMission : IExposable
	{
		public List<Pawn> scouts = new List<Pawn>();

		public List<int> states = new List<int>();

		public int sourceSettlementId = -1;

		public int factionLoadId = -1;

		public int spawnTick;

		public void ExposeData()
		{
			Scribe_Collections.Look(ref scouts, "scouts", LookMode.Reference);
			Scribe_Collections.Look(ref states, "states", LookMode.Value);
			Scribe_Values.Look(ref sourceSettlementId, "sourceSettlementId", -1);
			Scribe_Values.Look(ref factionLoadId, "factionLoadId", -1);
			Scribe_Values.Look(ref spawnTick, "spawnTick", 0);
			if (Scribe.mode != LoadSaveMode.PostLoadInit)
			{
				return;
			}
			if (scouts == null)
			{
				scouts = new List<Pawn>();
			}
			if (states == null)
			{
				states = new List<int>();
			}
			for (int num = scouts.Count - 1; num >= 0; num--)
			{
				if (scouts[num] == null)
				{
					scouts.RemoveAt(num);
					if (num < states.Count)
					{
						states.RemoveAt(num);
					}
				}
			}
			while (states.Count < scouts.Count)
			{
				states.Add(0);
			}
		}
	}

	public class PendingCapture : IExposable
	{
		public Pawn pawn;

		public int sourceSettlementId = -1;

		public int expireTick;

		public void ExposeData()
		{
			Scribe_References.Look(ref pawn, "pawn");
			Scribe_Values.Look(ref sourceSettlementId, "sourceSettlementId", -1);
			Scribe_Values.Look(ref expireTick, "expireTick", 0);
		}
	}

	private const int ScanInterval = 7;

	private const float BaseVisibleRadius = 14f;

	private const int MissionTimeoutTicks = 300000;

	private List<ScoutMission> missions = new List<ScoutMission>();

	private List<PendingCapture> pendingCaptures = new List<PendingCapture>();

	public bool AnyActiveMission => missions.Count > 0;

	public bool IsCounterScout(Pawn p)
	{
		if (p == null)
		{
			return false;
		}
		for (int i = 0; i < missions.Count; i++)
		{
			List<Pawn> list = missions[i]?.scouts;
			if (list == null)
			{
				continue;
			}
			for (int j = 0; j < list.Count; j++)
			{
				if (list[j] == p)
				{
					return true;
				}
			}
		}
		return false;
	}

	public MapComponent_EESCounterScout(Map map)
		: base(map)
	{
	}

	public void RegisterMission(List<Pawn> scouts, int sourceSettlementId, Faction faction)
	{
		ScoutMission scoutMission = new ScoutMission
		{
			scouts = new List<Pawn>(scouts),
			sourceSettlementId = sourceSettlementId,
			factionLoadId = (faction?.loadID ?? (-1)),
			spawnTick = Find.TickManager.TicksGame
		};
		for (int i = 0; i < scoutMission.scouts.Count; i++)
		{
			scoutMission.states.Add(0);
		}
		missions.Add(scoutMission);
		EESLog.Message($"CScout: 임무 등록 map={map.uniqueID} src={sourceSettlementId} fac={faction?.Name} scouts={scouts.Count}.");
	}

	public override void MapComponentTick()
	{
		if (missions.Count == 0 && pendingCaptures.Count == 0)
		{
			return;
		}
		int ticksGame = Find.TickManager.TicksGame;
		if (ticksGame % 7 != 0)
		{
			return;
		}
		TickPendingCaptures(ticksGame);
		for (int num = missions.Count - 1; num >= 0; num--)
		{
			ScoutMission scoutMission = missions[num];
			if (ticksGame - scoutMission.spawnTick > 300000)
			{
				EESLog.Warning($"CScout: 임무 타임아웃(5일) 강제 정리 src={scoutMission.sourceSettlementId} " + $"미해소={CountState(scoutMission, 0)} → 페널티 없이 종료.");
				missions.RemoveAt(num);
			}
			else
			{
				TickMission(scoutMission, ticksGame);
				if (!AnyActive(scoutMission))
				{
					ResolveMission(scoutMission);
					missions.RemoveAt(num);
				}
			}
		}
	}

	private void TickMission(ScoutMission m, int now)
	{
		for (int i = 0; i < m.scouts.Count; i++)
		{
			if (m.states[i] != 0)
			{
				continue;
			}
			Pawn pawn = m.scouts[i];
			if (pawn == null || pawn.Dead)
			{
				m.states[i] = 1;
				EESLog.Message("CScout: " + (pawn?.LabelShort ?? "?") + " 사망 → 무력화.");
			}
			else if (pawn.IsPrisonerOfColony)
			{
				m.states[i] = 2;
				RemoveCloak(pawn);
				ApplyInterrogationBonus(m.sourceSettlementId, pawn);
			}
			else if (pawn.Spawned && pawn.Map == map)
			{
				if (pawn.Downed)
				{
					m.states[i] = 1;
					RemoveCloak(pawn);
					EESLog.Message("CScout: " + pawn.LabelShort + " 다운 → 무력화(이후 생포 시 심문 보너스만).");
				}
				else if (Cloaked(pawn))
				{
					TryDetect(m, pawn);
				}
			}
			else if (!(pawn.ParentHolder is Pawn_CarryTracker) && !ThingOwnerUtility.SpawnedOrAnyParentSpawned(pawn))
			{
				m.states[i] = 3;
				EESLog.Message("CScout: " + pawn.LabelShort + " 맵 이탈 → 정찰 성공(세력 정보 획득).");
			}
		}
		for (int j = 0; j < m.scouts.Count; j++)
		{
			if (m.states[j] == 1)
			{
				Pawn pawn2 = m.scouts[j];
				if (pawn2 != null && !pawn2.Dead && pawn2.IsPrisonerOfColony)
				{
					m.states[j] = 2;
					RemoveCloak(pawn2);
					ApplyInterrogationBonus(m.sourceSettlementId, pawn2);
				}
			}
		}
	}

	private void TryDetect(ScoutMission m, Pawn scout)
	{
		List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
		for (int i = 0; i < list.Count; i++)
		{
			Pawn pawn = list[i];
			if (pawn != null && !pawn.Dead && !pawn.Downed && pawn.Awake() && scout.Position.InHorDistOf(pawn.Position, SightRadius(pawn, scout)) && GenSight.LineOfSightToThing(pawn.Position, scout, map))
			{
				Reveal(m, scout, pawn);
				break;
			}
		}
	}

	private static string WatcherKind(Pawn p)
	{
		if (p?.RaceProps == null)
		{
			return "?";
		}
		if (p.RaceProps.IsMechanoid)
		{
			return "메카";
		}
		if (p.IsMutant)
		{
			return "변이체";
		}
		if (p.RaceProps.Animal)
		{
			return "동물";
		}
		return "정착민";
	}

	private float SightRadius(Pawn detector, Pawn scout)
	{
		float num = 14f;
		if (detector.genes == null || detector.genes.AffectedByDarkness)
		{
			float t = map.glowGrid.GroundGlowAt(scout.Position);
			num *= Mathf.Lerp(0.33f, 1f, t);
		}
		return num * detector.health.capacities.GetLevel(PawnCapacityDefOf.Sight);
	}

	private void Reveal(ScoutMission m, Pawn scout, Pawn detector)
	{
		scout.GetInvisibilityComp()?.BecomeVisible(instant: true);
		RemoveCloak(scout);
		scout.GetLord()?.ReceiveMemo("EESScoutFight");
		Messages.Message(EESText.T("EES.CScout.Revealed", detector.LabelShort), new TargetInfo(scout.Position, map), MessageTypeDefOf.ThreatSmall);
		EESLog.Message("CScout: " + scout.LabelShort + " 발각(by " + detector.LabelShort + "[" + WatcherKind(detector) + "], " + $"dist={(scout.Position - detector.Position).LengthHorizontal:F1}, " + $"반경={SightRadius(detector, scout):F1}) → 팀 응전 전환(도주 없음).");
	}

	private static bool Cloaked(Pawn p)
	{
		if (CounterScoutUtility.CloakDef != null)
		{
			Pawn_HealthTracker health = p.health;
			if (health == null)
			{
				return false;
			}
			return health.hediffSet?.HasHediff(CounterScoutUtility.CloakDef) == true;
		}
		return false;
	}

	private static void RemoveCloak(Pawn p)
	{
		if (CounterScoutUtility.CloakDef != null)
		{
			Hediff hediff = p.health?.hediffSet?.GetFirstHediffOfDef(CounterScoutUtility.CloakDef);
			if (hediff != null)
			{
				p.health.RemoveHediff(hediff);
			}
		}
	}

	private void ApplyInterrogationBonus(int sourceSettlementId, Pawn p)
	{
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetExisting(sourceSettlementId);
		Settlement settlement = FindSettlement(sourceSettlementId);
		if (strongholdData == null || settlement == null)
		{
			Messages.Message(EESText.T("EES.CScout.CapturedNoIntel", p.LabelShort), p, MessageTypeDefOf.PositiveEvent);
			EESLog.Message("CScout: " + p.LabelShort + " 생포 — 출신 거점 소멸/미상 → intel 보너스 없음.");
			return;
		}
		int intelLevel = strongholdData.intelLevel;
		strongholdData.intelLevel = Mathf.Min(5, strongholdData.intelLevel + 1);
		Messages.Message(EESText.T("EES.CScout.Captured", p.LabelShort, settlement.LabelCap), p, MessageTypeDefOf.PositiveEvent);
		EESLog.Message($"CScout: {p.LabelShort} 생포·심문 → {settlement.LabelCap} intel {intelLevel}→{strongholdData.intelLevel}.");
	}

	private void TickPendingCaptures(int now)
	{
		for (int num = pendingCaptures.Count - 1; num >= 0; num--)
		{
			PendingCapture pendingCapture = pendingCaptures[num];
			if (pendingCapture.pawn == null || pendingCapture.pawn.Dead || now > pendingCapture.expireTick)
			{
				pendingCaptures.RemoveAt(num);
			}
			else if (pendingCapture.pawn.IsPrisonerOfColony)
			{
				RemoveCloak(pendingCapture.pawn);
				ApplyInterrogationBonus(pendingCapture.sourceSettlementId, pendingCapture.pawn);
				pendingCaptures.RemoveAt(num);
			}
		}
	}

	private void ResolveMission(ScoutMission m)
	{
		bool flag = false;
		for (int i = 0; i < m.states.Count; i++)
		{
			if (m.states[i] == 3)
			{
				flag = true;
				break;
			}
		}
		for (int j = 0; j < m.scouts.Count; j++)
		{
			Pawn pawn = m.scouts[j];
			if (m.states[j] == 1 && pawn != null && !pawn.Dead)
			{
				pendingCaptures.Add(new PendingCapture
				{
					pawn = pawn,
					sourceSettlementId = m.sourceSettlementId,
					expireTick = m.spawnTick + 300000
				});
			}
		}
		EESLog.Message($"CScout: 임무 종결 src={m.sourceSettlementId} " + $"이탈={CountState(m, 3)} 무력화={CountState(m, 1)} 생포={CountState(m, 2)} → " + (flag ? "표적 습격 예약" : "페널티 없음"));
		if (!flag)
		{
			return;
		}
		Faction faction = CounterScoutUtility.FactionByLoadId(m.factionLoadId);
		if (faction == null || faction.defeated || !faction.HostileTo(Faction.OfPlayer))
		{
			EESLog.Message("CScout: 세력 소멸/비적대 → 습격 예약 취소.");
			return;
		}
		WorldComponent_EESCounterScout.Instance?.ScheduleTargetedRaid(faction, map);
		if (StrongholdMod.Settings == null || StrongholdMod.Settings.scoutLetterNotify)
		{
			Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_CounterScoutEscaped"), EESText.T("EES.CScout.Escaped.Text", faction.Name), EESLetterDefOf.EES_CounterScoutEscaped);
		}
		else
		{
			Messages.Message(EESText.T("EES.CScout.Escaped.Text", faction.Name), MessageTypeDefOf.NegativeEvent, historical: false);
		}
	}

	private static bool AnyActive(ScoutMission m)
	{
		for (int i = 0; i < m.states.Count; i++)
		{
			if (m.states[i] == 0)
			{
				return true;
			}
		}
		return false;
	}

	private static int CountState(ScoutMission m, int state)
	{
		int num = 0;
		for (int i = 0; i < m.states.Count; i++)
		{
			if (m.states[i] == state)
			{
				num++;
			}
		}
		return num;
	}

	private static Settlement FindSettlement(int id)
	{
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			if (settlements[i].ID == id)
			{
				return settlements[i];
			}
		}
		return null;
	}

	public override void ExposeData()
	{
		Scribe_Collections.Look(ref missions, "missions", LookMode.Deep);
		Scribe_Collections.Look(ref pendingCaptures, "pendingCaptures", LookMode.Deep);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (missions == null)
			{
				missions = new List<ScoutMission>();
			}
			if (pendingCaptures == null)
			{
				pendingCaptures = new List<PendingCapture>();
			}
			pendingCaptures.RemoveAll((PendingCapture pc) => pc == null || pc.pawn == null);
		}
	}
}
