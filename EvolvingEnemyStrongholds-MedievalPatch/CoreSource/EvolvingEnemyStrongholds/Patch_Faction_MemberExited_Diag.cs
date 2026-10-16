using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Faction), "Notify_MemberExitedMap")]
public static class Patch_Faction_MemberExited_Diag
{
	public static void Prefix(Faction __instance, Pawn member, bool freed)
	{
		if (EESLog.Verbose && member != null && MapComponent_StrongholdSiege.IsBattlefieldParticipant(__instance))
		{
			Lord lord = member.GetLord();
			Job curJob = member.CurJob;
			EESLog.Message($"[전장 이탈진단] {__instance?.Name} {member.LabelShort} 맵 떠남(freed={freed}) | " + "lord=" + (lord?.LordJob?.GetType().Name ?? "none") + " duty=" + (member.mindState?.duty?.def?.defName ?? "none") + " " + string.Format("job={0} exitMapOnArrival={1} ", member.CurJobDef?.defName ?? "none", curJob?.exitMapOnArrival ?? false) + string.Format("정신={0} canFleeIndiv={1} ", (!member.InMentalState) ? "-" : member.MentalStateDef?.defName, member.mindState?.canFleeIndividual) + $"CE={BattlefieldDiag.CeSuppressionState(member)} @{member.Position}");
		}
	}
}
