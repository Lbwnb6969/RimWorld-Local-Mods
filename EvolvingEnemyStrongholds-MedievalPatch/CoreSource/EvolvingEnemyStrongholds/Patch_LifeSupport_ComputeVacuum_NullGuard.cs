using HarmonyLib;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Building_LifeSupportUnit), "ComputeVacuum")]
public static class Patch_LifeSupport_ComputeVacuum_NullGuard
{
	public static bool Prefix(Building_LifeSupportUnit __instance)
	{
		if (__instance == null || !__instance.Spawned || __instance.Map == null)
		{
			return false;
		}
		return __instance.GetRoom() != null;
	}
}
