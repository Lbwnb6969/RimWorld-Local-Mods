using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Settlement), "GetShuttleFloatMenuOptions")]
public static class Patch_Settlement_GetShuttleFloatMenuOptions
{
	public static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> values, IEnumerable<IThingHolder> pods, Action<PlanetTile, TransportersArrivalAction> launchAction, Settlement __instance)
	{
		foreach (FloatMenuOption value in values)
		{
			yield return value;
		}
		if (__instance.Faction == null || __instance.Faction == Faction.OfPlayer)
		{
			yield break;
		}
		foreach (FloatMenuOption floatMenuOption in TransportersArrivalAction_Scout.GetFloatMenuOptions(launchAction, pods, __instance, isShuttle: true))
		{
			yield return floatMenuOption;
		}
		foreach (FloatMenuOption floatMenuOption2 in TransportersArrivalAction_Infiltrate.GetFloatMenuOptions(launchAction, pods, __instance, isShuttle: true))
		{
			yield return floatMenuOption2;
		}
	}
}
