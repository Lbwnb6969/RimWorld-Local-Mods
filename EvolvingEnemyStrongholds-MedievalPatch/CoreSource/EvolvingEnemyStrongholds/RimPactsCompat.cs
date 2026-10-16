using System;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class RimPactsCompat
{
	private static bool resolved;

	private static PropertyInfo instanceProp;

	private static MethodInfo spectateOn;

	private static MethodInfo defendTarget;

	private static bool installedResolved;

	private static bool installedCache;

	private static bool puppetResolved;

	private static MethodInfo isPuppetMethod;

	private static bool strategyResolved;

	private static Func<bool> strategyGetter;

	private static MethodInfo isTerritoryRaze;

	private static int assaultSettlementId = -1;

	private static World wwBakedWorld;

	private static Func<bool> wwActiveGetter;

	private static Func<int> wwPlayerSideGetter;

	private static Func<Faction, int> wwSideOfGetter;

	public static bool Installed
	{
		get
		{
			if (!installedResolved)
			{
				installedResolved = true;
				try
				{
					installedCache = GenTypes.GetTypeInAnyAssembly("RimPacts.RimPactsMod") != null;
				}
				catch (Exception ex)
				{
					installedCache = false;
					EESLog.Message("RimPacts 설치 감지 실패(무해·미설치 취급): " + ex.Message);
				}
				EESLog.Message("RimPacts 호환: 설치 감지 " + (installedCache ? "활성" : "비활성(미설치)") + ".");
			}
			return installedCache;
		}
	}

	public static bool StrategyMode
	{
		get
		{
			if (!strategyResolved)
			{
				strategyResolved = true;
				try
				{
					MethodInfo methodInfo = GenTypes.GetTypeInAnyAssembly("RimPacts.RimPactsMod")?.GetProperty("StrategyMode", BindingFlags.Static | BindingFlags.Public)?.GetGetMethod();
					if (methodInfo != null)
					{
						strategyGetter = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), methodInfo);
					}
					EESLog.Message("RimPacts 호환: 전략 모드 게이트 " + ((strategyGetter != null) ? "활성" : "비활성(미설치/구버전)") + ".");
				}
				catch (Exception ex)
				{
					EESLog.Message("RimPacts 전략 모드 술어 해석 실패(무해·비활성): " + ex.Message);
				}
			}
			if (strategyGetter == null)
			{
				return false;
			}
			try
			{
				return strategyGetter();
			}
			catch
			{
				return false;
			}
		}
	}

	public static bool WorldWarActive
	{
		get
		{
			ResolveWorldWar();
			if (wwActiveGetter == null)
			{
				return false;
			}
			try
			{
				return wwActiveGetter();
			}
			catch
			{
				return false;
			}
		}
	}

	private static void Resolve()
	{
		if (resolved)
		{
			return;
		}
		resolved = true;
		try
		{
			Type typeInAnyAssembly = GenTypes.GetTypeInAnyAssembly("RimPacts.WorldComponent_RimPacts");
			if (!(typeInAnyAssembly == null))
			{
				instanceProp = typeInAnyAssembly.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
				spectateOn = typeInAnyAssembly.GetMethod("SpectateOn", BindingFlags.Instance | BindingFlags.Public, null, new Type[1] { typeof(Settlement) }, null);
				defendTarget = typeInAnyAssembly.GetMethod("IsDefendMissionTarget", BindingFlags.Instance | BindingFlags.Public, null, new Type[1] { typeof(Settlement) }, null);
				EESLog.Message("RimPacts 호환: 관전 전투 게이트 " + ((spectateOn != null) ? "활성" : "비활성(메서드 없음)") + " · 방어전 표적 게이트 " + ((defendTarget != null) ? "활성" : "비활성(메서드 없음)") + ".");
			}
		}
		catch (Exception ex)
		{
			EESLog.Message("RimPacts 호환 해석 실패(무해·비활성): " + ex.Message);
		}
	}

	public static bool IsSpectateBattle(Settlement s)
	{
		if (s == null)
		{
			return false;
		}
		Resolve();
		if (instanceProp == null || spectateOn == null)
		{
			return false;
		}
		try
		{
			object value = instanceProp.GetValue(null);
			return value != null && (bool)spectateOn.Invoke(value, new object[1] { s });
		}
		catch
		{
			return false;
		}
	}

	public static bool IsSpectateBattle(Map map)
	{
		if (map != null)
		{
			return IsSpectateBattle(map.Parent as Settlement);
		}
		return false;
	}

	public static bool IsDefendBattle(Settlement s)
	{
		if (s == null)
		{
			return false;
		}
		Resolve();
		if (instanceProp == null || defendTarget == null)
		{
			return false;
		}
		try
		{
			object value = instanceProp.GetValue(null);
			return value != null && (bool)defendTarget.Invoke(value, new object[1] { s });
		}
		catch
		{
			return false;
		}
	}

	public static bool IsDefendBattle(Map map)
	{
		if (map != null)
		{
			return IsDefendBattle(map.Parent as Settlement);
		}
		return false;
	}

	public static bool IsPuppet(Faction f)
	{
		if (f == null)
		{
			return false;
		}
		if (!puppetResolved)
		{
			puppetResolved = true;
			try
			{
				isPuppetMethod = GenTypes.GetTypeInAnyAssembly("RimPacts.RptFactionUtility")?.GetMethod("IsPuppet", BindingFlags.Static | BindingFlags.Public, null, new Type[1] { typeof(Faction) }, null);
				EESLog.Message("RimPacts 호환: 괴뢰국 게이트 " + ((isPuppetMethod != null) ? "활성" : "비활성(미설치/구버전)") + ".");
			}
			catch (Exception ex)
			{
				EESLog.Message("RimPacts 괴뢰국 술어 해석 실패(무해·비활성): " + ex.Message);
			}
		}
		if (isPuppetMethod == null)
		{
			return false;
		}
		try
		{
			return (bool)isPuppetMethod.Invoke(null, new object[1] { f });
		}
		catch
		{
			return false;
		}
	}

	public static bool IsPuppet(Settlement s)
	{
		return IsPuppet(s?.Faction);
	}

	public static bool IsTerritoryRaze(Settlement s)
	{
		if (s == null)
		{
			return false;
		}
		Resolve();
		if (instanceProp == null)
		{
			return false;
		}
		try
		{
			if (isTerritoryRaze == null)
			{
				isTerritoryRaze = GenTypes.GetTypeInAnyAssembly("RimPacts.WorldComponent_RimPacts")?.GetMethod("IsTerritoryRaze", BindingFlags.Instance | BindingFlags.Public, null, new Type[1] { typeof(Settlement) }, null);
				if (isTerritoryRaze == null)
				{
					return false;
				}
			}
			object value = instanceProp.GetValue(null);
			return value != null && (bool)isTerritoryRaze.Invoke(value, new object[1] { s });
		}
		catch
		{
			return false;
		}
	}

	public static void BeginPlayerAssault(Settlement s)
	{
		assaultSettlementId = s?.ID ?? (-1);
	}

	public static void EndPlayerAssault()
	{
		assaultSettlementId = -1;
	}

	public static bool PlayerAssaultInProgress(Settlement s)
	{
		if (s != null && assaultSettlementId != -1)
		{
			return assaultSettlementId == s.ID;
		}
		return false;
	}

	public static bool NotAPlayerAssault(Settlement s)
	{
		if (s?.Faction == null)
		{
			return false;
		}
		if (IsSpectateBattle(s))
		{
			return true;
		}
		if (IsDefendBattle(s))
		{
			return true;
		}
		if (PlayerAssaultInProgress(s))
		{
			return false;
		}
		return !s.Faction.HostileTo(Faction.OfPlayer);
	}

	private static void ResolveWorldWar()
	{
		World world = Find.World;
		if (wwBakedWorld == world)
		{
			return;
		}
		wwBakedWorld = world;
		wwActiveGetter = null;
		wwPlayerSideGetter = null;
		wwSideOfGetter = null;
		if (world == null)
		{
			return;
		}
		try
		{
			Type typeInAnyAssembly = GenTypes.GetTypeInAnyAssembly("RimPacts.WorldComponent_RimPacts");
			if (typeInAnyAssembly == null)
			{
				return;
			}
			object obj = typeInAnyAssembly.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
			if (obj == null)
			{
				wwBakedWorld = null;
				return;
			}
			MethodInfo methodInfo = typeInAnyAssembly.GetProperty("WorldWarActive", BindingFlags.Instance | BindingFlags.Public)?.GetGetMethod();
			MethodInfo method = typeInAnyAssembly.GetMethod("WorldWarPlayerSideNow", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
			MethodInfo method2 = typeInAnyAssembly.GetMethod("WorldWarSideOf", BindingFlags.Instance | BindingFlags.Public, null, new Type[1] { typeof(Faction) }, null);
			if (methodInfo != null && method != null && method2 != null)
			{
				wwActiveGetter = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), obj, methodInfo);
				wwPlayerSideGetter = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), obj, method);
				wwSideOfGetter = (Func<Faction, int>)Delegate.CreateDelegate(typeof(Func<Faction, int>), obj, method2);
			}
			EESLog.Message("RimPacts 호환: 세계대전 같은진영 게이트 " + ((wwSideOfGetter != null) ? "활성" : "비활성(미설치/구버전)") + ".");
		}
		catch (Exception ex)
		{
			wwActiveGetter = null;
			wwPlayerSideGetter = null;
			wwSideOfGetter = null;
			EESLog.Message("RimPacts 세계대전 술어 해석 실패(무해·비활성): " + ex.Message);
		}
	}

	public static bool WorldWarBlocked(Faction f)
	{
		int playerSide;
		int factionSide;
		return WorldWarBlockedInfo(f, out playerSide, out factionSide);
	}

	public static bool WorldWarBlockedInfo(Faction f, out int playerSide, out int factionSide)
	{
		playerSide = 0;
		factionSide = 0;
		if (f == null)
		{
			return false;
		}
		ResolveWorldWar();
		if (wwSideOfGetter == null)
		{
			return false;
		}
		try
		{
			if (!wwActiveGetter())
			{
				return false;
			}
			playerSide = wwPlayerSideGetter();
			factionSide = wwSideOfGetter(f);
			if (playerSide == 0)
			{
				return true;
			}
			return factionSide == playerSide;
		}
		catch
		{
			return false;
		}
	}

	public static bool WorldWarBlocked(Settlement s)
	{
		return WorldWarBlocked(s?.Faction);
	}

	public static bool BlocksIntel(Settlement s, out string reason)
	{
		reason = null;
		if (!WorldWarBlockedInfo(s?.Faction, out var playerSide, out var _))
		{
			return false;
		}
		reason = EESText.T((playerSide == 0) ? "EES.CanScout.WorldWarNeutral" : "EES.CanScout.WorldWarAlly");
		return true;
	}

	public static bool BlocksSupplyIntercept(Faction f, out string reason)
	{
		reason = null;
		if (!WorldWarBlockedInfo(f, out var playerSide, out var _))
		{
			return false;
		}
		reason = EESText.T((playerSide == 0) ? "EES.Supply.Intercept.WorldWarNeutral" : "EES.Supply.Intercept.WorldWarAlly");
		return true;
	}

	public static string WorldWarBlockDebug(Faction f)
	{
		WorldWarBlockedInfo(f, out var playerSide, out var factionSide);
		return "세계대전 " + ((playerSide == 0) ? "중립국(전 세력 차단)" : "같은진영") + $"(플레이어편={playerSide} 그세력={factionSide})";
	}
}
