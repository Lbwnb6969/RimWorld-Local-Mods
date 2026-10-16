using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Settlement), "GetFloatMenuOptions")]
public static class Patch_Settlement_GetFloatMenuOptions
{
	public static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> values, Caravan caravan, Settlement __instance)
	{
		foreach (FloatMenuOption value in values)
		{
			yield return value;
		}
		if (__instance.Faction == null || __instance.Faction == Faction.OfPlayer)
		{
			yield break;
		}
		foreach (FloatMenuOption floatMenuOption in CaravanArrivalAction_Scout.GetFloatMenuOptions(caravan, __instance))
		{
			yield return floatMenuOption;
		}
		foreach (FloatMenuOption floatMenuOption2 in CaravanArrivalAction_Infiltrate.GetFloatMenuOptions(caravan, __instance))
		{
			yield return floatMenuOption2;
		}
	}
}
