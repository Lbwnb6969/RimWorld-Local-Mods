using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Caravan), "GetGizmos")]
public static class Patch_Caravan_Gizmos_SupplyIntercept
{
	public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Caravan __instance)
	{
		foreach (Gizmo item in __result)
		{
			yield return item;
		}
		if (!__instance.IsPlayerControlled)
		{
			yield break;
		}
		WorldObject_EESSupplyCaravan supply = SupplyCaravanUtility.FindSupplyInterceptedBy(__instance);
		if (supply != null)
		{
			Caravan c = __instance;
			yield return new Command_Action
			{
				defaultLabel = EESText.T("EES.Supply.Intercept.Cancel"),
				defaultDesc = EESText.T("EES.Supply.Intercept.Canceled"),
				icon = ContentFinder<Texture2D>.Get("UI/Designators/Cancel", reportFailure: false),
				action = delegate
				{
					supply.CancelIntercept();
					Messages.Message(EESText.T("EES.Supply.Intercept.Canceled"), c, MessageTypeDefOf.NeutralEvent, historical: false);
					EESLog.Message("Supply: 요격 취소(캐러밴 기즈모).");
				}
			};
		}
	}
}
