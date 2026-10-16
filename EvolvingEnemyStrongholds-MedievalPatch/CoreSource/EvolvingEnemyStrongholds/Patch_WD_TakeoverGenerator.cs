using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Settlement), "MapGeneratorDef", MethodType.Getter)]
[HarmonyAfter(new string[] { "TSA.WorldDomination.KCSG_Final_Solution" })]
public static class Patch_WD_TakeoverGenerator
{
	public static void Postfix(Settlement __instance, ref MapGeneratorDef __result)
	{
		if (WorldDominationCompat.TakesOver(__instance))
		{
			MapGeneratorDef base_Faction = MapGeneratorDefOf.Base_Faction;
			if (base_Faction != null && __result != base_Faction)
			{
				__result = base_Faction;
			}
		}
	}
}
