using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class EmitterMarkerUtil
{
	private static readonly Dictionary<int, string> RoleCache = new Dictionary<int, string>();

	private static readonly List<Settlement> EmitterList = new List<Settlement>();

	private static int cacheTick = int.MinValue;

	private const int RefreshInterval = 60;

	public static string EmitterRoleOf(Settlement s)
	{
		if (s?.Faction == null || s.Faction == Faction.OfPlayer)
		{
			return null;
		}
		RefreshIfStale();
		if (!RoleCache.TryGetValue(s.ID, out var value))
		{
			return null;
		}
		return value;
	}

	public static List<Settlement> CachedEmitters()
	{
		RefreshIfStale();
		return EmitterList;
	}

	public static void Invalidate()
	{
		cacheTick = int.MinValue;
	}

	private static void RefreshIfStale()
	{
		int num = Find.TickManager?.TicksGame ?? 0;
		if (cacheTick != int.MinValue && num - cacheTick < 60 && num >= cacheTick)
		{
			return;
		}
		cacheTick = num;
		RoleCache.Clear();
		EmitterList.Clear();
		WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
		if (instance == null)
		{
			return;
		}
		foreach (KeyValuePair<int, StrongholdData> allDataPair in instance.AllDataPairs)
		{
			string text = allDataPair.Value?.roleDefName;
			if (ThreatEmitterUtility.IsEmitterRole(text))
			{
				RoleCache[allDataPair.Key] = text;
			}
		}
		if (RoleCache.Count == 0)
		{
			return;
		}
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (settlement?.Faction != null && !settlement.Faction.IsPlayer && RoleCache.ContainsKey(settlement.ID))
			{
				EmitterList.Add(settlement);
			}
		}
	}
}
