using HarmonyLib;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(PawnUIOverlay), "DrawPawnGUIOverlay")]
public static class Patch_PawnUIOverlay_HideScoutLabel
{
	private static HediffDef cloakDef;

	private static bool cloakResolved;

	public static bool Prefix(Pawn ___pawn)
	{
		if (___pawn == null)
		{
			return true;
		}
		if (!cloakResolved)
		{
			cloakDef = DefDatabase<HediffDef>.GetNamedSilentFail("EES_ScoutCloak");
			cloakResolved = true;
		}
		if (cloakDef == null)
		{
			return true;
		}
		Pawn_HealthTracker health = ___pawn.health;
		if (health != null && health.hediffSet?.HasHediff(cloakDef) == true && ___pawn.IsPsychologicallyInvisible())
		{
			return false;
		}
		return true;
	}
}
