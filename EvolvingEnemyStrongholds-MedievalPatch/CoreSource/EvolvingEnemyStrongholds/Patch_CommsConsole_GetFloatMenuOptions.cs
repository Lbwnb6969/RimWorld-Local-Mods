using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Building_CommsConsole), "GetFloatMenuOptions")]
public static class Patch_CommsConsole_GetFloatMenuOptions
{
	public static IEnumerable<FloatMenuOption> Postfix(IEnumerable<FloatMenuOption> values, Building_CommsConsole __instance, Pawn myPawn)
	{
		if (ScoutUtility.Enabled)
		{
			yield return MakeEESOption(__instance, myPawn, EESText.T("EES.Intercept.MenuLabel"), 2, delegate(Pawn p)
			{
				InterceptUtility.BeginInterceptTargeting(p, __instance);
			});
			yield return MakeEESOption(__instance, myPawn, EESText.T("EES.Strike.MenuLabel"), 1, delegate(Pawn p)
			{
				StrikeUtility.BeginStrikeTargeting(p, __instance);
			});
		}
		foreach (FloatMenuOption value in values)
		{
			yield return value;
		}
	}

	private static FloatMenuOption MakeEESOption(Building_CommsConsole console, Pawn myPawn, string label, int order, Action<Pawn> begin)
	{
		if (!console.CanUseCommsNow)
		{
			return new FloatMenuOption(label + " (" + EESText.T("EES.Intercept.NoPower") + ")", null);
		}
		if (myPawn == null || !myPawn.CanReach(console, PathEndMode.InteractionCell, Danger.Some) || !myPawn.CanReserve(console))
		{
			return new FloatMenuOption(label + " (" + EESText.T("EES.Intercept.Unreachable") + ")", null);
		}
		return new FloatMenuOption(label, delegate
		{
			begin(myPawn);
		}, MenuOptionPriority.SummonThreat)
		{
			orderInPriority = order
		};
	}
}
