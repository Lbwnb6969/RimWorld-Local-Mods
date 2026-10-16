using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Settlement), "GetCaravanGizmos")]
public static class Patch_Settlement_GetCaravanGizmos
{
	public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Caravan caravan, Settlement __instance)
	{
		foreach (Gizmo value in values)
		{
			yield return value;
		}
		if (__instance.Faction == null || __instance.Faction == Faction.OfPlayer || (StrongholdMod.Settings != null && !StrongholdMod.Settings.enableScouting))
		{
			yield break;
		}
		StrongholdData data = WorldComponent_Strongholds.Instance?.GetOrCreate(__instance);
		if (data == null)
		{
			yield break;
		}
		string reason;
		bool flag = ScoutUtility.CanScoutNow(data, out reason, __instance);
		Command_Action command_Action = new Command_Action();
		command_Action.defaultLabel = EESText.T("EES.Gizmo.ScoutLabel");
		command_Action.defaultDesc = ScoutUtility.IntelReport(__instance, data) + EESText.T("EES.Gizmo.ScoutDescSuffix", ScoutUtility.RollChance(data, caravan).ToStringPercent());
		command_Action.icon = EESTextures.ScoutIcon;
		command_Action.action = delegate
		{
			ScoutConfirmUtility.RunWithConfirms(__instance, data, caravan, delegate
			{
				ScoutUtility.TryScout(__instance, data, caravan);
			}, "캐러밴 기즈모");
		};
		Command_Action command_Action2 = command_Action;
		if (!flag)
		{
			command_Action2.Disable(reason);
		}
		else if (!ScoutUtility.PartyCanSee(caravan))
		{
			command_Action2.Disable(EESText.T("EES.CanScout.Blind"));
		}
		yield return command_Action2;
		Gizmo gizmo = CaravanArrivalAction_Infiltrate.Gizmo(caravan, __instance);
		if (gizmo != null)
		{
			yield return gizmo;
		}
	}
}
