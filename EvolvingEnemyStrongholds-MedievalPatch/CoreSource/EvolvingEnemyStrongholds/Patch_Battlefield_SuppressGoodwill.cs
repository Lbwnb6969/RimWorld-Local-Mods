using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Faction), "TryAffectGoodwillWith")]
public static class Patch_Battlefield_SuppressGoodwill
{
	public static bool Prefix(Faction __instance, Faction other, int goodwillChange, HistoryEventDef reason, ref bool __result)
	{
		// Retain the original entry point for callers without a map target.
		return true;
	}

	[HarmonyPrefix]
	public static bool TargetedPrefix(Faction __instance, Faction other, int goodwillChange, HistoryEventDef reason, GlobalTargetInfo? lookTarget, ref bool __result)
	{
		if (goodwillChange >= 0)
		{
			return true;
		}
		if (other != Faction.OfPlayer && __instance != Faction.OfPlayer)
		{
			return true;
		}
		Faction faction = ((__instance == Faction.OfPlayer) ? other : __instance);
		Map map = lookTarget?.Map ?? (lookTarget?.WorldObject as MapParent)?.Map;
		if (map?.GetComponent<MapComponent_StrongholdSiege>()?.HasActiveBattlefieldFaction(faction) != true)
		{
			return true;
		}
		EESLog.Message(string.Format("[전장 굿윌차단] {0} 굿윌손실 Δ{1} 사유={2} → SKIP(전장 진행 중, 4파전 혼전).", faction.Name, goodwillChange, reason?.defName ?? "(null)"));
		__result = false;
		return false;
	}
}
