using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public static class AmbushUtility
{
	private const float AmbushPointsFraction = 0.25f;

	private const float AmbushAlertScale = 0.2f;

	private const int AmbushMaxPoints = 3500;

	private const int AmbushMinPoints = 200;

	public static int SpawnAmbush(Map map, Settlement settlement, StrongholdData data)
	{
		Faction faction = settlement?.Faction;
		if (map == null || faction == null || faction == Faction.OfPlayer || data == null)
		{
			return 0;
		}
		if (data.militaryPower <= 0)
		{
			EESLog.Message("Ambush skip " + settlement.LabelCap + ": militaryPower=0");
			return 0;
		}
		StrongholdSettings settings = StrongholdMod.Settings;
		float num = settings?.ambushPointsFraction ?? 0.25f;
		float num2 = settings?.ambushAlertScale ?? 0.2f;
		int max = settings?.ambushMaxPoints ?? 3500;
		float num3 = num * (1f + (float)data.alertLevel * num2);
		int num4 = Mathf.Clamp(Mathf.RoundToInt((float)data.militaryPower * num3), 200, max);
		int count;
		IntVec3 intVec = PlayerPawnsCenter(map, out count);
		if (count == 0 || !intVec.IsValid)
		{
			return 0;
		}
		PawnGroupMakerParms parms = new PawnGroupMakerParms
		{
			tile = map.Tile,
			faction = faction,
			groupKind = PawnGroupKindDefOf.Combat,
			points = num4,
			inhabitants = true,
			seed = Gen.HashCombineInt(data.settlementId, 670469)
		};
		List<Pawn> list;
		try
		{
			list = PawnGroupMakerUtility.GeneratePawns(parms, warnOnZeroResults: false).ToList();
		}
		catch (Exception ex)
		{
			EESLog.Error("Ambush GeneratePawns failed: " + ex.Message);
			return 0;
		}
		if (list.Count == 0)
		{
			return 0;
		}
		int num5 = 0;
		List<Pawn> list2 = new List<Pawn>();
		foreach (Pawn item in list)
		{
			if (!TryFindAmbushCell(map, intVec, out var cell))
			{
				StrongholdGarrisonUtility.DiscardUnspawnedPawn(item);
				continue;
			}
			GenSpawn.Spawn(item, cell, map);
			list2.Add(item);
			num5++;
		}
		if (list2.Count > 0)
		{
			LordJob_DefendBase lordJob = new LordJob_DefendBase(faction, intVec, 0);
			LordMaker.MakeNewLord(faction, lordJob, map, list2);
		}
		EESLog.Message($"Ambush SPAWNED {settlement.LabelCap} [{faction.Name}]: points={num4} " + $"pawns={num5} near player@{intVec} (playerPawns={count})");
		return num5;
	}

	private static IntVec3 PlayerPawnsCenter(Map map, out int count)
	{
		count = 0;
		long num = 0L;
		long num2 = 0L;
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn != null && !pawn.Dead && pawn.Faction == Faction.OfPlayer)
			{
				num += pawn.Position.x;
				num2 += pawn.Position.z;
				count++;
			}
		}
		if (count == 0)
		{
			return IntVec3.Invalid;
		}
		return new IntVec3((int)(num / count), 0, (int)(num2 / count));
	}

	private static bool TryFindAmbushCell(Map map, IntVec3 center, out IntVec3 cell)
	{
		return CellFinder.TryFindRandomCellNear(center, map, 18, (IntVec3 c) => c.InBounds(map) && c.Standable(map) && !c.Fogged(map) && (c - center).LengthHorizontal >= 8f, out cell);
	}
}
