using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public class WorldComponent_EESCounterScout : WorldComponent
{
	public class PendingRaid : IExposable
	{
		public int factionLoadId = -1;

		public int mapId = -1;

		public int fireTick;

		public float pointsMul = 1.2f;

		public int attempts;

		public void ExposeData()
		{
			Scribe_Values.Look(ref factionLoadId, "factionLoadId", -1);
			Scribe_Values.Look(ref mapId, "mapId", -1);
			Scribe_Values.Look(ref fireTick, "fireTick", 0);
			Scribe_Values.Look(ref pointsMul, "pointsMul", 1.2f);
			Scribe_Values.Look(ref attempts, "attempts", 0);
		}
	}

	private const int TickGate = 250;

	private const int RetryTicks = 60000;

	private const int MaxAttempts = 3;

	private int nextSpawnTick = -1;

	private int lastGateTick = -1;

	private List<PendingRaid> pendingRaids = new List<PendingRaid>();

	public static WorldComponent_EESCounterScout Instance => Find.World?.GetComponent<WorldComponent_EESCounterScout>();

	public WorldComponent_EESCounterScout(World world)
		: base(world)
	{
	}

	public void ScheduleTargetedRaid(Faction faction, Map map)
	{
		for (int i = 0; i < pendingRaids.Count; i++)
		{
			if (pendingRaids[i].factionLoadId == faction.loadID)
			{
				EESLog.Message("CScout: " + faction.Name + " 표적 습격 이미 예약됨 → 중복 예약 안 함.");
				return;
			}
		}
		float num = Rand.Range(2f, 7f);
		PendingRaid pendingRaid = new PendingRaid
		{
			factionLoadId = faction.loadID,
			mapId = (map?.uniqueID ?? (-1)),
			fireTick = Find.TickManager.TicksGame + Mathf.RoundToInt(num * 60000f),
			pointsMul = Rand.Range(1.15f, 1.25f)
		};
		pendingRaids.Add(pendingRaid);
		EESLog.Message($"CScout: 표적 습격 예약 fac={faction.Name} {num:F1}일 후 ×{pendingRaid.pointsMul:F2}.");
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
		ProcessPendingRaids(ticksGame);
		if (CounterScoutUtility.Enabled)
		{
			float num = StrongholdMod.Settings?.counterScoutDays ?? 30f;
			if (nextSpawnTick < 0)
			{
				nextSpawnTick = ticksGame + Mathf.RoundToInt(num * 60000f * Rand.Range(0.5f, 1.15f));
			}
			if (ticksGame >= nextSpawnTick)
			{
				nextSpawnTick = ticksGame + Mathf.RoundToInt(num * 60000f * Rand.Range(0.75f, 1.25f));
				CounterScoutUtility.TrySpawn();
			}
		}
	}

	private void ProcessPendingRaids(int now)
	{
		for (int num = pendingRaids.Count - 1; num >= 0; num--)
		{
			PendingRaid pendingRaid = pendingRaids[num];
			if (now >= pendingRaid.fireTick)
			{
				Faction faction = CounterScoutUtility.FactionByLoadId(pendingRaid.factionLoadId);
				if (faction == null || faction.defeated || !faction.HostileTo(Faction.OfPlayer))
				{
					EESLog.Message("CScout: 표적 습격 취소 — 세력 소멸/비적대.");
					pendingRaids.RemoveAt(num);
				}
				else
				{
					string note;
					Map map = FindTargetMap(pendingRaid.mapId, out note);
					if (map == null)
					{
						EESLog.Message("CScout: 표적 습격 취소 — 대상 플레이어 맵 없음 fac=" + faction.Name + " (" + note + ").");
						pendingRaids.RemoveAt(num);
					}
					else
					{
						WorldComponent_EESHVT instance = WorldComponent_EESHVT.Instance;
						if (instance != null && instance.TruceActive(faction))
						{
							pendingRaid.fireTick = now + 60000;
							EESLog.Message("CScout: 표적 습격 보류 — " + faction.Name + " 습격 중단 중(HVT 보상). 1일 후 재확인.");
						}
						else if (!IgnoranceCompat.FactionAllowed(faction))
						{
							EESLog.Message("CScout: 표적 습격 취소 — " + faction.Name + "은(는) Ignorance Is Bliss 테크 제한 대상.");
							pendingRaids.RemoveAt(num);
						}
						else
						{
							float num2 = Mathf.Max(StorytellerUtility.DefaultThreatPointsNow(map) * pendingRaid.pointsMul, 100f);
							IncidentParms incidentParms = new IncidentParms();
							incidentParms.target = map;
							incidentParms.faction = faction;
							incidentParms.points = num2;
							incidentParms.forced = true;
							incidentParms.customLetterLabel = EESText.T("EES_Letter_CounterScoutRaid");
							incidentParms.customLetterText = EESText.T("EES.CScout.Raid.Text", faction.Name);
							IncidentParms parms = incidentParms;
							bool flag = false;
							try
							{
								flag = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
							}
							catch (Exception ex)
							{
								EESLog.Error("CScout: 표적 습격 발사 예외: " + ex);
							}
							if (flag)
							{
								EESLog.Message($"CScout: 표적 습격 발사 fac={faction.Name} points={num2:F0}(×{pendingRaid.pointsMul:F2}) 맵={note}.");
								pendingRaids.RemoveAt(num);
							}
							else
							{
								pendingRaid.attempts++;
								if (pendingRaid.attempts >= 3)
								{
									EESLog.Warning($"CScout: 표적 습격 {3}회 실패 → 포기 fac={faction.Name}.");
									pendingRaids.RemoveAt(num);
								}
								else
								{
									pendingRaid.fireTick = now + 60000;
									EESLog.Warning($"CScout: 표적 습격 실패(시도 {pendingRaid.attempts}) → 1일 후 재시도 fac={faction.Name}.");
								}
							}
						}
					}
				}
			}
		}
	}

	private static Map FindTargetMap(int mapId, out string note)
	{
		List<Map> maps = Find.Maps;
		bool flag = false;
		for (int i = 0; i < maps.Count; i++)
		{
			if (maps[i].uniqueID == mapId && maps[i].IsPlayerHome)
			{
				if (!VacuumMapUtility.IsSpaceMap(maps[i]))
				{
					note = "원 맵";
					return maps[i];
				}
				flag = true;
				break;
			}
		}
		int num = 0;
		Map map = null;
		for (int j = 0; j < maps.Count; j++)
		{
			if (maps[j].IsPlayerHome)
			{
				if (VacuumMapUtility.IsSpaceMap(maps[j]))
				{
					num++;
				}
				else if (map == null)
				{
					map = maps[j];
				}
			}
		}
		note = string.Format("폴백 — 원 맵 {0} · 우주 본거지 제외 {1}곳 · ", flag ? "우주 맵이라 제외" : "소멸", num) + "대상 " + ((map == null) ? "없음" : (map.Parent?.LabelCap.ToString() ?? ("map" + map.uniqueID)));
		return map;
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref nextSpawnTick, "nextSpawnTick", -1);
		Scribe_Collections.Look(ref pendingRaids, "pendingRaids", LookMode.Deep);
		if (Scribe.mode == LoadSaveMode.PostLoadInit && pendingRaids == null)
		{
			pendingRaids = new List<PendingRaid>();
		}
	}
}
