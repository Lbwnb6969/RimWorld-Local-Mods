using HarmonyLib;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Thing), "MaxHitPoints", MethodType.Getter)]
public static class Patch_Thing_MaxHitPoints_Security
{
	[HarmonyPostfix]
	public static void Postfix(Thing __instance, ref int __result)
	{
		if (!MapComponent_StrongholdSiege.AnySecurityActive || !(__instance is Building) || !__instance.Spawned || __instance.Map == null)
		{
			return;
		}
		Faction faction = __instance.Faction;
		if (faction != null && !faction.IsPlayer)
		{
			MapComponent_StrongholdSiege component = __instance.Map.GetComponent<MapComponent_StrongholdSiege>();
			if (component != null && component.SecurityBuffActive(__instance))
			{
				__result *= 5;
			}
		}
	}
}
