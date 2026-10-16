using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public class GameComponent_Ambush : GameComponent
{
	private const int ScanInterval = 60;

	private HashSet<int> evaluatedMaps = new HashSet<int>();

	public GameComponent_Ambush(Game game)
	{
	}

	public override void GameComponentTick()
	{
		if (Find.TickManager.TicksGame % 60 == 0 && (StrongholdMod.Settings == null || StrongholdMod.Settings.enableScouting))
		{
			List<Map> maps = Find.Maps;
			for (int i = 0; i < maps.Count; i++)
			{
				Evaluate(maps[i]);
			}
		}
	}

	private void Evaluate(Map map)
	{
		if (map == null || !(map.Parent is Settlement { Faction: { } faction } settlement) || faction == Faction.OfPlayer || RimPactsCompat.NotAPlayerAssault(settlement) || evaluatedMaps.Contains(map.uniqueID) || !AnyPlayerPawn(map))
		{
			return;
		}
		evaluatedMaps.Add(map.uniqueID);
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
		if (strongholdData == null)
		{
			return;
		}
		if (FakeOutpostUtility.IsFake(strongholdData.roleDefName))
		{
			EESLog.Message("Ambush eval " + settlement.LabelCap + " [" + faction.Name + "]: FAKE outpost — 기습·증원 차단(설계 #2).");
			return;
		}
		if (BattlefieldUtility.IsBattlefield(strongholdData.roleDefName))
		{
			EESLog.Message("Ambush eval " + settlement.LabelCap + " [" + faction.Name + "]: BATTLEFIELD — 기습·중력부양선 증원 차단(자체 4파전).");
			return;
		}
		bool flag = GravshipReinforceUtility.IsGravshipAttack(map);
		bool flag2 = ScoutUtility.IsScouted(strongholdData);
		EESLog.Message($"Ambush eval {settlement.LabelCap} [{faction.Name}]: odyssey={ModsConfig.OdysseyActive} gravship={flag} scouted={flag2} " + "-> path=" + ((flag && flag2) ? "GRAV+scouted(둘레보강만)" : (flag ? "GRAV-증원표식" : (flag2 ? "scouted-매복없음" : "AMBUSH"))) + ".");
		if (flag && !VacuumMapUtility.IsVacuum(map) && !EESCustomBaseDetector.IsDedicatedOutpostFaction(map.ParentFaction))
		{
			CellRect baseRect = map.GetComponent<MapComponent_StrongholdSiege>()?.eesBaseRect ?? default(CellRect);
			GravshipReinforceUtility.PatchPerimeter(map, strongholdData, baseRect);
		}
		if (flag2)
		{
			return;
		}
		if (flag)
		{
			MapComponent_StrongholdSiege component = map.GetComponent<MapComponent_StrongholdSiege>();
			if (component != null)
			{
				component.MarkGravshipAttack();
				EESLog.Message("Gravship attack " + settlement.LabelCap + " [" + settlement.Faction?.Name + "]: 매복 억제 → 증원 표식(MapComponent가 처리).");
			}
		}
		else
		{
			AmbushUtility.SpawnAmbush(map, settlement, strongholdData);
		}
	}

	private static bool AnyPlayerPawn(Map map)
	{
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn != null && !pawn.Dead && pawn.Faction == Faction.OfPlayer)
			{
				return true;
			}
		}
		return false;
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Collections.Look(ref evaluatedMaps, "evaluatedMaps", LookMode.Value);
		if (Scribe.mode == LoadSaveMode.PostLoadInit && evaluatedMaps == null)
		{
			evaluatedMaps = new HashSet<int>();
		}
	}
}
