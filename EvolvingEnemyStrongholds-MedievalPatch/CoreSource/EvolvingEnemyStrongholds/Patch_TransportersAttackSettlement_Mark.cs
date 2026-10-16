using HarmonyLib;
using RimWorld.Planet;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(TransportersArrivalAction_AttackSettlement), "Arrived")]
internal static class Patch_TransportersAttackSettlement_Mark
{
	private static void Prefix(Settlement ___settlement)
	{
		RimPactsCompat.BeginPlayerAssault(___settlement);
		WorldComponent_Strongholds.Instance?.PrepareFinalStandCandidate(___settlement);
	}

	private static void Finalizer()
	{
		RimPactsCompat.EndPlayerAssault();
	}
}
