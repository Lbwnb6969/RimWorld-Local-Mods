using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch]
internal static class Patch_SettlementAttackNow_Mark
{
	private static MethodBase Target()
	{
		return AccessTools.Method(typeof(SettlementUtility), "AttackNow");
	}

	private static bool Prepare()
	{
		return Target() != null;
	}

	private static MethodBase TargetMethod()
	{
		return Target();
	}

	private static void Prefix(Settlement settlement)
	{
		RimPactsCompat.BeginPlayerAssault(settlement);
		WorldComponent_Strongholds.Instance?.PrepareFinalStandCandidate(settlement);
	}

	private static void Finalizer()
	{
		RimPactsCompat.EndPlayerAssault();
	}
}
