using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Settlement), "GetInspectString")]
public static class Patch_Settlement_Inspect_Prisoner
{
	public static void Postfix(Settlement __instance, ref string __result)
	{
		if (WorldDominationCompat.TakesOver(__instance))
		{
			int num = (WorldComponent_Strongholds.Instance?.GetExisting(__instance.ID))?.tier ?? StrongholdScoreUtility.ComputeTier(__instance);
			float strengthRaw = WorldDominationCompat.GetStrengthRaw(__instance);
			if (!__result.NullOrEmpty())
			{
				__result += "\n";
			}
			__result += EESText.T("EES.WD.InspectTier", num, strengthRaw.ToString("F0"));
		}
		string text = EmitterMarkerUtil.EmitterRoleOf(__instance);
		if (text != null)
		{
			if (!__result.NullOrEmpty())
			{
				__result += "\n";
			}
			__result += EESText.T("EES.Emitter.Inspect", EESText.T("EES.Role." + text));
		}
		int num2 = PrisonerMarkerUtil.HeldPrisoners(__instance);
		if (num2 > 0)
		{
			if (!__result.NullOrEmpty())
			{
				__result += "\n";
			}
			__result += EESText.T("EES.Prisoner.Held", num2);
		}
	}
}
