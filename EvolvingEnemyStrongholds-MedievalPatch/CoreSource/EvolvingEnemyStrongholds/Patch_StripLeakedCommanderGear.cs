using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(PawnApparelGenerator), "GenerateStartingApparelFor")]
public static class Patch_StripLeakedCommanderGear
{
	private static ThingDef beltDef;

	private static bool resolved;

	public static void Postfix(Pawn pawn)
	{
		if (pawn?.apparel == null)
		{
			return;
		}
		if (!resolved)
		{
			beltDef = DefDatabase<ThingDef>.GetNamedSilentFail("EES_CommanderShieldBelt");
			resolved = true;
		}
		if (beltDef == null)
		{
			return;
		}
		List<Apparel> wornApparel = pawn.apparel.WornApparel;
		for (int num = wornApparel.Count - 1; num >= 0; num--)
		{
			Apparel apparel = wornApparel[num];
			if (apparel != null && apparel.def == beltDef)
			{
				pawn.apparel.Remove(apparel);
				if (!apparel.Destroyed)
				{
					apparel.Destroy();
				}
				EESLog.Message("Stripped leaked commander shield belt from " + pawn.LabelShortCap + " at generation (mod apparel-gen conflict guard).");
			}
		}
	}
}
