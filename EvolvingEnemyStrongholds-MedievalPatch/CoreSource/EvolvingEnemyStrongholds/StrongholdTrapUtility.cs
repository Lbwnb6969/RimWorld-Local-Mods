using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class StrongholdTrapUtility
{
	private static int TrapCount(int tier)
	{
		if (tier >= 5)
		{
			return Mathf.RoundToInt((float)(tier - 4) * 1.6f);
		}
		return 0;
	}

	public static void PlaceTraps(Map map, Faction faction, StrongholdData data, CellRect settlementRect, bool mapWide = false)
	{
		if (map == null || faction == null || faction == Faction.OfPlayer || data == null)
		{
			return;
		}
		int tier = data.tier;
		int num = (mapWide ? Mathf.Max(40, Mathf.RoundToInt((float)map.Area / 300f)) : TrapCount(tier));
		if (num <= 0)
		{
			return;
		}
		try
		{
			List<ThingDef> list = BuildTrapPool(tier);
			if (list.Count == 0)
			{
				EESLog.Message($"Traps [{faction.Name} T{tier}]: no trap defs available (skip).");
				return;
			}
			CellRect cellRect = (mapWide ? CellRect.WholeMap(map) : settlementRect.ExpandedBy(4));
			int num2 = 0;
			int num3 = 0;
			int num4 = num * 40;
			Rand.PushState(Gen.HashCombineInt(data.settlementId, 30633));
			try
			{
				while (num2 < num && num3 < num4)
				{
					num3++;
					IntVec3 randomCell = cellRect.RandomCell;
					if (randomCell.InBounds(map) && (mapWide || !settlementRect.Contains(randomCell)) && randomCell.Standable(map) && randomCell.GetEdifice(map) == null && !randomCell.Roofed(map) && randomCell.GetFirstBuilding(map) == null && randomCell.GetFirstItem(map) == null)
					{
						ThingDef thingDef = list.RandomElement();
						ThingDef stuff = (thingDef.MadeFromStuff ? ThingDefOf.Steel : null);
						Thing thing = ThingMaker.MakeThing(thingDef, stuff);
						thing.SetFactionDirect(faction);
						GenSpawn.Spawn(thing, randomCell, map);
						num2++;
					}
				}
			}
			finally
			{
				Rand.PopState();
			}
			EESLog.Message($"Traps [{faction.Name} T{tier}]: placed {num2}/{num} " + string.Format("(pool={0}: {1}).", list.Count, string.Join(",", list.ConvertAll((ThingDef d) => d.defName))));
		}
		catch (Exception ex)
		{
			EESLog.Error("PlaceTraps failed: " + ex);
		}
	}

	private static List<ThingDef> BuildTrapPool(int tier)
	{
		List<ThingDef> pool = new List<ThingDef>();
		if (tier >= 5)
		{
			Add("TrapSpike");
		}
		if (tier >= 7)
		{
			Add("TrapIED_HighExplosive");
			Add("TrapIED_Incendiary");
		}
		if (tier >= 11)
		{
			Add("HunterDroneTrap");
			Add("WaspDroneTrap");
		}
		return pool;
		void Add(string n)
		{
			ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail(n);
			if (namedSilentFail != null)
			{
				pool.Add(namedSilentFail);
			}
		}
	}
}
