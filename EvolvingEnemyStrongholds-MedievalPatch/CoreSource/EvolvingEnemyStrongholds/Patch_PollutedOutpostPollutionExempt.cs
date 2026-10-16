using System;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(PollutionInfo), "MapRemoved")]
public static class Patch_PollutedOutpostPollutionExempt
{
	private static int exemptCount;

	private static readonly FieldInfo PollutionAtGenField = AccessTools.Field(typeof(PollutionInfo), "pollutionAtGen");

	public static bool Prefix(PollutionInfo __instance)
	{
		try
		{
			Map map = __instance?.map;
			if (map == null)
			{
				return true;
			}
			MapComponent_StrongholdSiege component = map.GetComponent<MapComponent_StrongholdSiege>();
			if (component == null || !component.eesStrongholdMap || !component.isPolluted)
			{
				return true;
			}
			exemptCount++;
			float num = -1f;
			try
			{
				if (PollutionAtGenField != null)
				{
					num = (float)PollutionAtGenField.GetValue(__instance);
				}
			}
			catch
			{
			}
			int num2 = map.pollutionGrid?.TotalPollution ?? (-1);
			EESLog.Message(string.Format("오염 적립 면제: 타일 {0} ({1}) — ", map.Tile, map.Parent?.Label ?? "부모 없음") + "EES 거점 맵이라 「기지 오염」 판정을 내지 않는다 " + $"(오염 전초기지 {component.isPolluted} · 총오염 {num2} · 생성시점 {num:0} · " + "잘린 증가분 " + ((num2 >= 0 && num >= 0f) ? ((float)num2 - num).ToString("0") : "계측실패") + $" · 이번 세션 누적 {exemptCount}건)");
			return false;
		}
		catch (Exception ex)
		{
			EESLog.Warning("오염 적립 면제 판정 실패(바닐라 그대로 진행): " + ex.Message);
			return true;
		}
	}
}
