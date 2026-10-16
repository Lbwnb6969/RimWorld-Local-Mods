using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace EvolvingEnemyStrongholds;

public static class Patch_KCSG_CleanRect
{
	public static void TryApply(Harmony harmony)
	{
		try
		{
			Type type = AccessTools.TypeByName("KCSG.LayoutUtils");
			if (type == null)
			{
				return;
			}
			MethodInfo[] array = (from m in type.GetMethods(AccessTools.all)
				where m.Name == "CleanRect"
				select m).ToArray();
			if (array.Length == 0)
			{
				EESLog.Warning("KCSG.LayoutUtils 는 있으나 CleanRect 메서드를 못 찾음(KCSG 버전 변경?) → CleanRect 가드 미적용.");
				return;
			}
			MethodInfo[] array2 = array;
			foreach (MethodInfo original in array2)
			{
				harmony.Patch(original, null, null, null, new HarmonyMethod(typeof(Patch_KCSG_CleanRect), "CleanRectFinalizer"));
			}
			EESLog.Message($"KCSG 감지: LayoutUtils.CleanRect 오버로드 {array.Length}개에 IndexOutOfRange 가드(Finalizer) 적용 — 세력기지 rect↔구조물 roofGrid 불일치 크래시 흡수.");
		}
		catch (Exception ex)
		{
			EESLog.Error("KCSG CleanRect 가드 부착 실패(스킵, EES 본 기능에는 영향 없음): " + ex);
		}
	}

	public static Exception CleanRectFinalizer(Exception __exception)
	{
		if (__exception is IndexOutOfRangeException && Patch_BaseGenPostSpawnGuard.Active)
		{
			EESLog.Message("[KCSG호환] CleanRect IndexOutOfRange 흡수 — KCSG 정착지 pre-clean 버그(rect↔roofGrid 불일치). 생성 계속(BaseGen).");
			return null;
		}
		return __exception;
	}
}
