using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class Patch_VEF_SpawnGuard
{
	public static void TryApply(Harmony harmony)
	{
		int num = 0;
		num += GuardMethod(harmony, "VEF.Apparels.CompShieldField+SpawnSetup_Patch", "Postfix");
		num += GuardMethod(harmony, "VEF.Hediffs.PhasingPatches", "CheckPhasing");
		if (num > 0)
		{
			EESLog.Message($"VEF 감지: 폰 스폰 Postfix {num}개에 NRE 가드(Finalizer) 적용 — KCSG 정착지 생성 중 보호막/phasing NRE로 기지 일부생성 방지.");
		}
	}

	private static int GuardMethod(Harmony harmony, string typeName, string methodName)
	{
		try
		{
			Type type = AccessTools.TypeByName(typeName);
			if (type == null)
			{
				return 0;
			}
			MethodInfo[] array = (from m in type.GetMethods(AccessTools.all)
				where m.Name == methodName
				select m).ToArray();
			if (array.Length == 0)
			{
				EESLog.Warning("VEF " + typeName + "." + methodName + " 못 찾음(버전 변경?) → 가드 미적용.");
				return 0;
			}
			int num = 0;
			MethodInfo[] array2 = array;
			foreach (MethodInfo original in array2)
			{
				harmony.Patch(original, null, null, null, new HarmonyMethod(typeof(Patch_VEF_SpawnGuard), "SwallowFinalizer"));
				num++;
			}
			return num;
		}
		catch (Exception ex)
		{
			EESLog.Error("VEF " + typeName + "." + methodName + " 가드 부착 실패(스킵, EES 본 기능에는 영향 없음): " + ex);
			return 0;
		}
	}

	public static Exception SwallowFinalizer(Exception __exception, MethodBase __originalMethod)
	{
		if (__exception is NullReferenceException && Patch_BaseGenPostSpawnGuard.Active &&
			MapGenerator.mapBeingGenerated?.Parent is Settlement settlement &&
			!EESFactionBlacklist.IsBlocked(settlement.Faction) && !EESCustomBaseDetector.IsForeignSettlement(settlement))
		{
			EESLog.Message("[VEF호환] " + __originalMethod?.DeclaringType?.Name + "." + __originalMethod?.Name + " 예외 흡수: " + __exception.GetType().Name + " (폰 스폰 계속 — 보호막/phasing은 tick에서 재초기화).");
			return null;
		}
		return __exception;
	}
}
