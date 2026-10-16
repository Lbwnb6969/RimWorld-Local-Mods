using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public class WorldComponent_EESRebuild : WorldComponent
{
	public static WorldComponent_EESRebuild Instance;

	private const int TickInterval = 2500;

	private const float RebuildTierFactor = 0.6f;

	private List<EESRuinRecord> ruins = new List<EESRuinRecord>();

	private WorldObject_EESRebuildConvoy activeConvoy;

	public static bool FeatureEnabled
	{
		get
		{
			StrongholdSettings settings = StrongholdMod.Settings;
			if (settings != null && !settings.enableRebuildConvoys)
			{
				return false;
			}
			if (WorldDominationCompat.Active && (settings == null || !settings.rebuildWithWD))
			{
				return false;
			}
			return true;
		}
	}

	public WorldComponent_EESRebuild(World world)
		: base(world)
	{
		Instance = this;
	}

	public void Notify_StrongholdDestroyed(Settlement s, StrongholdData d)
	{
		if (!FeatureEnabled || s?.Faction?.def == null || d == null)
		{
			return;
		}
		if (RimPactsCompat.StrategyMode)
		{
			EESLog.Message("Rebuild: 전략 모드라 폐허 기록 생략 [" + s.Faction.Name + "] " + s.LabelCap + " — 총량 고정 사양.");
		}
		else if (d.rebuiltOnce)
		{
			EESLog.Message("Rebuild: " + s.LabelCap + "=재건된 거점 재파괴 — 재재건 없음.");
		}
		else if (s.Faction.def.humanlikeFaction && !EESFactionBlacklist.IsBlocked(s.Faction) && !FakeOutpostUtility.IsFake(d.roleDefName))
		{
			Map map = s.Map;
			if (map != null && map.mapPawns.AnyColonistSpawned)
			{
				float num = (StrongholdMod.Settings?.rebuildDelayDays ?? 12f) * Rand.Range(0.7f, 1.3f);
				EESRuinRecord item = new EESRuinRecord
				{
					tile = s.Tile,
					factionLoadId = s.Faction.loadID,
					tier = d.tier,
					dueTick = Find.TickManager.TicksGame + Mathf.RoundToInt(num * 60000f)
				};
				ruins.Add(item);
				EESLog.Message($"Rebuild: 폐허 기록 [{s.Faction.Name}] {s.LabelCap} T{d.tier} @ {s.Tile} — {num:F1}일 후 발차 예정.");
			}
		}
	}

	public override void WorldComponentTick()
	{
		base.WorldComponentTick();
		if (Find.TickManager.TicksGame % 2500 != 0 || ruins.Count == 0)
		{
			return;
		}
		int ticksGame = Find.TickManager.TicksGame;
		for (int num = ruins.Count - 1; num >= 0; num--)
		{
			if (ruins[num].launched && (activeConvoy == null || activeConvoy.Destroyed))
			{
				EESLog.Message($"Rebuild: 호송대 소멸(도착 실패) — 폐허 @ {ruins[num].tile} 재건 ★영구 취소.");
				Messages.Message(EESText.T("EES.Rebuild.CancelMsg"), MessageTypeDefOf.PositiveEvent, historical: false);
				ruins.RemoveAt(num);
				activeConvoy = null;
			}
		}
		TryLaunchDue(ticksGame);
	}

	private bool TryLaunchDue(int now)
	{
		if (activeConvoy != null && !activeConvoy.Destroyed)
		{
			return false;
		}
		for (int num = ruins.Count - 1; num >= 0; num--)
		{
			EESRuinRecord eESRuinRecord = ruins[num];
			if (!eESRuinRecord.launched && now >= eESRuinRecord.dueTick)
			{
				if (!FeatureEnabled)
				{
					ruins.RemoveAt(num);
				}
				else
				{
					Faction faction = CounterScoutUtility.FactionByLoadId(eESRuinRecord.factionLoadId);
					if (faction == null || faction.defeated || faction.def == null)
					{
						ruins.RemoveAt(num);
					}
					else if (Find.WorldObjects.AnyWorldObjectAt(eESRuinRecord.tile))
					{
						EESLog.Message($"Rebuild: 폐허 타일 @ {eESRuinRecord.tile} 점유됨(플레이어 정착 등) — 조용히 취소.");
						ruins.RemoveAt(num);
					}
					else
					{
						Settlement settlement = NearestFactionSettlement(faction, eESRuinRecord.tile);
						if (settlement == null)
						{
							ruins.RemoveAt(num);
						}
						else
						{
							List<PlanetTile> list = SupplyCaravanUtility.BuildPath(settlement.Tile, eESRuinRecord.tile);
							if (list != null)
							{
								LaunchConvoy(eESRuinRecord, faction, settlement, list);
								return true;
							}
							EESLog.Warning($"Rebuild: 경로 없음 {settlement.Tile}→{eESRuinRecord.tile} — 취소.");
							ruins.RemoveAt(num);
						}
					}
				}
			}
		}
		return false;
	}

	private void LaunchConvoy(EESRuinRecord rec, Faction fac, Settlement origin, List<PlanetTile> path)
	{
		WorldObjectDef named = DefDatabase<WorldObjectDef>.GetNamed("EES_RebuildConvoy", errorOnFail: false);
		if (named == null)
		{
			EESLog.Error("Rebuild: EES_RebuildConvoy WorldObjectDef 없음 — 기능 비활성.");
			ruins.Remove(rec);
			return;
		}
		WorldObject_EESRebuildConvoy worldObject_EESRebuildConvoy = (WorldObject_EESRebuildConvoy)WorldObjectMaker.MakeWorldObject(named);
		worldObject_EESRebuildConvoy.Tile = origin.Tile;
		worldObject_EESRebuildConvoy.SetFaction(fac);
		worldObject_EESRebuildConvoy.destSettlementId = -1;
		worldObject_EESRebuildConvoy.destTile = rec.tile;
		worldObject_EESRebuildConvoy.spawnTick = Find.TickManager.TicksGame;
		worldObject_EESRebuildConvoy.originTier = rec.tier;
		worldObject_EESRebuildConvoy.rebuildTier = Mathf.Max(1, Mathf.FloorToInt((float)rec.tier * 0.6f));
		Find.WorldObjects.Add(worldObject_EESRebuildConvoy);
		worldObject_EESRebuildConvoy.SetPath(path);
		rec.launched = true;
		activeConvoy = worldObject_EESRebuildConvoy;
		Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_RebuildDepart"), EESText.T("EES.Rebuild.DepartText", fac.Name), LetterDefOf.ThreatSmall, worldObject_EESRebuildConvoy);
		EESLog.Message($"Rebuild: 호송대 발차 [{fac.Name}] {origin.LabelCap} → 폐허 @ {rec.tile} (재건 T{worldObject_EESRebuildConvoy.rebuildTier}, 원 T{rec.tier}).");
	}

	public void Notify_ConvoyArrived(WorldObject_EESRebuildConvoy convoy)
	{
		for (int num = ruins.Count - 1; num >= 0; num--)
		{
			if (ruins[num].launched && ruins[num].tile == convoy.destTile)
			{
				ruins.RemoveAt(num);
			}
		}
		activeConvoy = null;
		Faction faction = convoy.Faction;
		if (faction == null || faction.defeated || RuinTileBlocked(convoy.destTile, convoy))
		{
			EESLog.Message($"Rebuild: 도착 시점 무효 — 재건 취소 @ {convoy.destTile} " + "(세력=" + ((faction == null) ? "null" : (faction.defeated ? "소멸" : "생존")) + " " + $"타일점유={RuinTileBlocked(convoy.destTile, convoy)}).");
			return;
		}
		Settlement settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
		settlement.SetFaction(faction);
		settlement.Tile = convoy.destTile;
		settlement.Name = SettlementNameGenerator.GenerateSettlementName(settlement);
		Find.WorldObjects.Add(settlement);
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
		if (strongholdData != null)
		{
			strongholdData.tier = convoy.rebuildTier;
			strongholdData.tierSource = "auto";
			strongholdData.rebuiltOnce = true;
			StrongholdScoreUtility.ApplyDerived(strongholdData, settlement);
			StrongholdScoreUtility.DecideFacilities(strongholdData, settlement);
			GarrisonGroupUtility.DecidePlan(strongholdData, settlement);
		}
		Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_RebuildDone"), EESText.T("EES.Rebuild.DoneText", faction.Name, settlement.LabelCap), LetterDefOf.NegativeEvent, settlement);
		EESLog.Message($"Rebuild: 거점 재건 [{faction.Name}] {settlement.LabelCap} T{strongholdData?.tier} @ {settlement.Tile}.");
	}

	public static bool RuinTileBlocked(PlanetTile tile, WorldObject ignore)
	{
		foreach (WorldObject item in Find.WorldObjects.ObjectsAt(tile))
		{
			if (item != ignore)
			{
				return true;
			}
		}
		return false;
	}

	private static Settlement NearestFactionSettlement(Faction f, PlanetTile target)
	{
		Settlement result = null;
		float num = float.MaxValue;
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (settlement.Faction == f && settlement.Tile.Valid && settlement.Tile.Layer == target.Layer)
			{
				float num2 = Find.WorldGrid.ApproxDistanceInTiles(settlement.Tile, target);
				if (num2 < num)
				{
					result = settlement;
					num = num2;
				}
			}
		}
		return result;
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Collections.Look(ref ruins, "ees_ruins", LookMode.Deep);
		Scribe_References.Look(ref activeConvoy, "ees_rebuildConvoy");
		if (Scribe.mode == LoadSaveMode.PostLoadInit && ruins == null)
		{
			ruins = new List<EESRuinRecord>();
		}
	}
}
