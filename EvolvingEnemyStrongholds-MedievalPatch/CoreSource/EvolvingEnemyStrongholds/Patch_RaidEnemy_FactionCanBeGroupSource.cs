using System;
using HarmonyLib;
using RimWorld;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(IncidentWorker_RaidEnemy), "FactionCanBeGroupSource", new Type[]
{
	typeof(Faction),
	typeof(IncidentParms),
	typeof(bool)
})]
public static class Patch_RaidEnemy_FactionCanBeGroupSource
{
	public static void Postfix(Faction f, ref bool __result)
	{
		if (__result)
		{
			WorldComponent_EESHVT instance = WorldComponent_EESHVT.Instance;
			if (instance != null && instance.TruceActive(f))
			{
				__result = false;
			}
		}
	}
}
