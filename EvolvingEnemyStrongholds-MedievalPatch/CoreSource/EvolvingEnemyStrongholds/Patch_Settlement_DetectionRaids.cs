using System;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Settlement), "PostMapGenerate")]
public static class Patch_Settlement_DetectionRaids
{
	public static void Postfix(Settlement __instance)
	{
		try
		{
			Map map = __instance?.Map;
			if (map == null)
			{
				return;
			}
			TimedDetectionRaids component = __instance.GetComponent<TimedDetectionRaids>();
			if (component != null && component.DetectionCountdownStarted)
			{
				bool flag = map.GetComponent<MapComponent_StrongholdSiege>()?.isBattlefield ?? false;
				StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetExisting(__instance.ID);
				bool flag2 = strongholdData != null && CommanderEffects.ReinforcementCommander(strongholdData);
				if (flag || flag2)
				{
					component.ResetCountdown();
					EESLog.Message("DetectionRaids OFF [" + __instance.LabelCap + "]: " + (flag ? "전장" : "") + ((flag && flag2) ? "+" : "") + (flag2 ? "통신장교" : "") + " → 바닐라 4일 발각 습격 비활성(EES 증원/4파전이 담당).");
				}
			}
		}
		catch (Exception ex)
		{
			EESLog.Warning("Patch_Settlement_DetectionRaids: " + ex.Message);
		}
	}
}
