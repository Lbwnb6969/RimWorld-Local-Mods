using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(WorldObject), "GetInspectTabs")]
public static class Patch_WorldObject_GetInspectTabs
{
	public static void Postfix(WorldObject __instance, ref IEnumerable<InspectTabBase> __result)
	{
		if (__instance is Settlement { Faction: not null } settlement && !settlement.Faction.IsPlayer && (__result == null || !__result.Contains(WITab_Scouting.Instance)))
		{
			__result = (__result ?? Enumerable.Empty<InspectTabBase>()).Concat(new InspectTabBase[1] { WITab_Scouting.Instance });
		}
	}
}
