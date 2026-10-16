using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

/// <summary>
/// Shows the already-scouted stronghold feature in RimWorld's standard world
/// object inspection text (the lower-left panel). The scouting gate is kept in
/// place so this does not reveal intelligence before the player has scouted the
/// settlement; the richer report remains available in WITab_Scouting.
/// </summary>
[HarmonyPatch(typeof(Settlement), "GetInspectString")]
public static class Patch_Settlement_Inspect_Feature
{
	public static void Postfix(Settlement __instance, ref string __result)
	{
		if (__instance == null || __instance.Faction == null || __instance.Faction.IsPlayer ||
			EESFactionBlacklist.IsBlocked(__instance.Faction) || EESCustomBaseDetector.IsForeignSettlement(__instance))
		{
			return;
		}

		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetExisting(__instance.ID);
		if (strongholdData == null || !ScoutUtility.IsScouted(strongholdData) ||
			!StrongholdFeatureUtility.IsValid(strongholdData.featureKey))
		{
			return;
		}

		string featureLine = EESText.T("EES.Scout.Feature", StrongholdFeatureUtility.Label(strongholdData.featureKey),
			StrongholdFeatureUtility.Description(strongholdData.featureKey));
		if (featureLine.NullOrEmpty())
		{
			return;
		}
		if (!__result.NullOrEmpty())
		{
			__result += "\n";
		}
		__result += featureLine;
	}
}
