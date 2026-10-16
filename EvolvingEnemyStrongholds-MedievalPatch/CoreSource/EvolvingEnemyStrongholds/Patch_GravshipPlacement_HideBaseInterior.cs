using System;
using HarmonyLib;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(GravshipPlacementUtility), "PlaceGravshipInMap")]
public static class Patch_GravshipPlacement_HideBaseInterior
{
	public static void Postfix(Map map)
	{
		try
		{
			if (!ModsConfig.OdysseyActive || map == null || EESCustomBaseDetector.IsDedicatedOutpostFaction(map.ParentFaction))
			{
				return;
			}
			if (EESFactionKind.IsInsect(map.ParentFaction))
			{
				Patch_MapGenerator_GenerateMap_Refog.RefogVault(map, null);
				return;
			}
			if (VacuumMapUtility.IsVacuum(map))
			{
				Patch_MapGenerator_GenerateMap_Refog.RefogVault(map, null);
				return;
			}
			MapComponent_StrongholdSiege component = map.GetComponent<MapComponent_StrongholdSiege>();
			if (component == null || !component.eesGenerated)
			{
				return;
			}
			if (component.openBase)
			{
				Patch_MapGenerator_GenerateMap_Refog.RefogVault(map, component);
				return;
			}
			Faction parentFaction = map.ParentFaction;
			if (parentFaction != null && !parentFaction.IsPlayer && parentFaction.HostileTo(Faction.OfPlayer))
			{
				CellRect rect = ((component.eesBaseRect.Width >= 5) ? component.eesBaseRect : CellRect.WholeMap(map));
				GravshipReinforceUtility.HideBaseInterior(map, rect);
			}
		}
		catch (Exception ex)
		{
			EESLog.Warning("Gravship hide-interior failed: " + ex.Message);
		}
	}
}
