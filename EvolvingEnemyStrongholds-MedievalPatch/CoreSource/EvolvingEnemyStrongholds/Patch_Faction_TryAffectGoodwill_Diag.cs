using HarmonyLib;
using RimWorld;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Faction), "TryAffectGoodwillWith")]
public static class Patch_Faction_TryAffectGoodwill_Diag
{
	public static void Prefix(Faction __instance, Faction other, int goodwillChange, HistoryEventDef reason)
	{
		if (EESLog.Verbose && goodwillChange != 0 && (other == Faction.OfPlayer || __instance == Faction.OfPlayer))
		{
			bool flag = MapComponent_StrongholdSiege.IsBattlefieldParticipant((__instance == Faction.OfPlayer) ? other : __instance);
			EESLog.Message(string.Format("[굿윌진단] {0} → {1} Δ{2} 사유={3} ", __instance?.Name, other?.Name, goodwillChange, reason?.defName ?? "(null)") + string.Format("전장참전={0}{1} | 호출={2}", flag, flag ? " ★전장중 굿윌변동" : "", BattlefieldDiag.ShortCaller(2)));
		}
	}
}
