using HarmonyLib;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Thing), "Print")]
public static class Patch_TrapHidden
{
	public static bool Prefix(Thing __instance)
	{
		if (__instance is Building_Trap { Faction: not null } building_Trap && building_Trap.Faction != Faction.OfPlayer && building_Trap.Faction.HostileTo(Faction.OfPlayer))
		{
			MapComponent_StrongholdSiege siege = building_Trap.Map?.GetComponent<MapComponent_StrongholdSiege>();
			return siege == null || !siege.eesStrongholdMap;
		}
		return true;
	}
}
