using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class PrisonerMarkerUtil
{
	private static readonly Dictionary<int, int> HeldCache = new Dictionary<int, int>();

	private static int cacheTick = int.MinValue;

	private const int RefreshInterval = 60;

	public static int HeldPrisoners(Settlement s)
	{
		if (s?.Faction == null || s.Faction == Faction.OfPlayer)
		{
			return 0;
		}
		RefreshIfStale();
		if (!HeldCache.TryGetValue(s.ID, out var value))
		{
			return 0;
		}
		return value;
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
		HeldCache.Clear();
		WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
		if (instance == null)
		{
			return;
		}
		foreach (KeyValuePair<int, StrongholdData> allDataPair in instance.AllDataPairs)
		{
			List<Pawn> list = allDataPair.Value?.capturedPrisoners;
			if (list == null || list.Count == 0)
			{
				continue;
			}
			int num2 = 0;
			for (int i = 0; i < list.Count; i++)
			{
				Pawn pawn = list[i];
				if (pawn != null && !pawn.Dead && !pawn.Destroyed && pawn.Faction != Faction.OfPlayer)
				{
					num2++;
				}
			}
			if (num2 > 0)
			{
				HeldCache[allDataPair.Key] = num2;
			}
		}
	}
}
