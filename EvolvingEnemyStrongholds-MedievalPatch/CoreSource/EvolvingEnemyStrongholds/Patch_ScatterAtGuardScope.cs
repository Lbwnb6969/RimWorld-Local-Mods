using HarmonyLib;
using RimWorld;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(GenStep_Settlement), "ScatterAt")]
internal static class Patch_ScatterAtGuardScope
{
	private static void Prefix()
	{
		Patch_BaseGenPostSpawnGuard.Begin();
	}

	private static void Finalizer()
	{
		Patch_BaseGenPostSpawnGuard.End();
	}
}
