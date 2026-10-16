using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Settlement), "get_Material")]
public static class Patch_Settlement_Material_PrisonerTint
{
	private static readonly Color MarkerColor = new Color(0.35f, 1f, 0.9f);

	public static void Postfix(Settlement __instance, ref Material __result)
	{
		if (PrisonerMarkerUtil.HeldPrisoners(__instance) > 0)
		{
			FactionDef factionDef = __instance.Faction?.def;
			if (factionDef != null && !factionDef.settlementTexturePath.NullOrEmpty())
			{
				__result = MaterialPool.MatFrom(factionDef.settlementTexturePath, ShaderDatabase.WorldOverlayTransparentLit, MarkerColor, 3550);
			}
		}
	}
}
