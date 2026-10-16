using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class EESHostility
{
	public static bool SystemsActive(Map map, Faction owner)
	{
		if (owner == null || owner.IsPlayer)
		{
			return false;
		}
		if (!owner.HostileTo(Faction.OfPlayer))
		{
			return AnyEnemyOf(map, owner);
		}
		return true;
	}

	public static bool SystemsActive(Map map, Settlement s)
	{
		return SystemsActive(map, s?.Faction);
	}

	public static Faction OwnerOf(Map map)
	{
		return (map?.Parent as Settlement)?.Faction;
	}

	public static List<Pawn> EnemyPawnsOf(Map map, Faction owner, bool combatOnly = true)
	{
		List<Pawn> list = new List<Pawn>();
		if (map == null || owner == null)
		{
			return list;
		}
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn != null && pawn.Faction != null && pawn.Faction != owner && (!combatOnly || (!pawn.Dead && !pawn.Downed)) && pawn.HostileTo(owner))
			{
				list.Add(pawn);
			}
		}
		return list;
	}

	public static bool AnyEnemyOf(Map map, Faction owner)
	{
		if (map == null || owner == null)
		{
			return false;
		}
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn != null && !pawn.Dead && !pawn.Downed && pawn.Faction != null && pawn.Faction != owner && pawn.HostileTo(owner))
			{
				return true;
			}
		}
		return false;
	}

	public static bool TryEnemyCentroid(Map map, Faction owner, out IntVec3 center)
	{
		center = map?.Center ?? IntVec3.Invalid;
		List<Pawn> list = EnemyPawnsOf(map, owner);
		if (list.Count == 0)
		{
			return false;
		}
		long num = 0L;
		long num2 = 0L;
		for (int i = 0; i < list.Count; i++)
		{
			num += list[i].Position.x;
			num2 += list[i].Position.z;
		}
		center = new IntVec3((int)(num / list.Count), 0, (int)(num2 / list.Count));
		return true;
	}

	public static bool AnyEnemyMech(Map map, Faction owner)
	{
		if (map == null || owner == null)
		{
			return false;
		}
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn?.RaceProps != null && pawn.RaceProps.IsMechanoid && pawn.Faction != null && pawn.Faction != owner && pawn.HostileTo(owner))
			{
				return true;
			}
		}
		return false;
	}

	public static bool IsFriendlyOutpost(Faction owner)
	{
		if (owner != null && !owner.IsPlayer)
		{
			return !owner.HostileTo(Faction.OfPlayer);
		}
		return false;
	}
}
