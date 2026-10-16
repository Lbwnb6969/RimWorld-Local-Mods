using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;

namespace EvolvingEnemyStrongholds;

public static class IgnoranceCompat
{
	private const string AsmName = "IgnoranceIsBliss";

	private const string TypeFullName = "DIgnoranceIsBliss.Core_Patches.IgnoranceBase";

	private const string MethodName = "FactionInEligibleTechRange";

	private static bool _inited;

	private static bool _failed;

	private static Func<Faction, bool> _eligible;

	public static bool Active
	{
		get
		{
			EnsureInit();
			if (_eligible != null)
			{
				return !_failed;
			}
			return false;
		}
	}

	private static void EnsureInit()
	{
		if (_inited)
		{
			return;
		}
		_inited = true;
		try
		{
			Assembly assembly = null;
			Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
			for (int i = 0; i < assemblies.Length; i++)
			{
				if (assemblies[i]?.GetName()?.Name == "IgnoranceIsBliss")
				{
					assembly = assemblies[i];
					break;
				}
			}
			if (assembly == null)
			{
				return;
			}
			Type type = assembly.GetType("DIgnoranceIsBliss.Core_Patches.IgnoranceBase");
			if (type == null)
			{
				EESLog.Error("Ignorance Is Bliss 감지했으나 DIgnoranceIsBliss.Core_Patches.IgnoranceBase 타입 없음 — 연동 비활성(EES는 기존대로 동작).");
				return;
			}
			MethodInfo methodInfo = AccessTools.Method(type, "FactionInEligibleTechRange", new Type[1] { typeof(Faction) });
			if (methodInfo == null || !methodInfo.IsStatic || methodInfo.ReturnType != typeof(bool))
			{
				EESLog.Error("Ignorance Is Bliss 감지했으나 FactionInEligibleTechRange(Faction) 시그니처 불일치 — 연동 비활성(EES는 기존대로 동작).");
				return;
			}
			_eligible = (Func<Faction, bool>)Delegate.CreateDelegate(typeof(Func<Faction, bool>), methodInfo);
			EESLog.Message("Ignorance Is Bliss 감지 — 역정찰·보급 상단·EES 습격에 IIB 테크 제한을 그대로 적용한다.");
		}
		catch (Exception ex)
		{
			_eligible = null;
			EESLog.Error("Ignorance Is Bliss 연동 초기화 실패(무시하고 기존대로 동작): " + ex);
		}
	}

	public static bool FactionAllowed(Faction f)
	{
		if (f == null)
		{
			return true;
		}
		EnsureInit();
		if (_eligible == null || _failed)
		{
			return true;
		}
		try
		{
			return _eligible(f);
		}
		catch (Exception ex)
		{
			_failed = true;
			EESLog.Error("Ignorance Is Bliss 판정 호출 실패 — 연동 영구 비활성(EES는 기존대로 동작): " + ex);
			return true;
		}
	}
}
