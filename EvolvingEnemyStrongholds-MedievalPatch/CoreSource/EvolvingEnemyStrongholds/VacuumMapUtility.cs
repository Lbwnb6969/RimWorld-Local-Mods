using RimWorld;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class VacuumMapUtility
{
	public const float VacuumBaseScale = 0.9f;

	public static bool IsVacuum(Map map)
	{
		if (map != null && map.Biome != null)
		{
			return map.Biome.inVacuum;
		}
		return false;
	}

	public static bool IsSpaceMap(Map map)
	{
		if (map == null)
		{
			return false;
		}
		if (map.Tile.Valid && map.Tile.LayerDef != null && map.Tile.LayerDef.isSpace)
		{
			return true;
		}
		return IsVacuum(map);
	}

	public static bool IsBuildableGround(Map map, IntVec3 c)
	{
		if (map == null || !c.InBounds(map))
		{
			return false;
		}
		if (!IsVacuum(map))
		{
			return true;
		}
		TerrainDef terrain = c.GetTerrain(map);
		if (terrain == null || terrain == TerrainDefOf.Space)
		{
			return false;
		}
		return terrain.passability != Traversability.Impassable;
	}

	public static CellRect ShrinkForVacuum(Map map, CellRect rect, float factor = 0.9f)
	{
		if (!IsVacuum(map) || rect.Width < 6 || rect.Height < 6)
		{
			return rect;
		}
		int num = Mathf.Max(5, Mathf.RoundToInt((float)rect.Width * factor));
		int num2 = Mathf.Max(5, Mathf.RoundToInt((float)rect.Height * factor));
		IntVec3 centerCell = rect.CenterCell;
		return new CellRect(centerCell.x - num / 2, centerCell.z - num2 / 2, num, num2).ClipInsideMap(map);
	}
}
