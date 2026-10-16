using System;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class FactionTerritoriesCompat
{
	private static bool _resolvedTypes;

	private static MethodInfo _findForSettlement;

	private static FieldInfo _resolvedField;

	public static bool Active
	{
		get
		{
			Resolve();
			if (_findForSettlement != null)
			{
				Patch_FactionTerritories_ExpansionComplete.TryApply(new Harmony(HarmonyInit.HarmonyId));
			}
			return _findForSettlement != null;
		}
	}

	private static void Resolve()
	{
		if (_resolvedTypes)
		{
			return;
		}
		_resolvedTypes = true;
		try
		{
			Type typeInAnyAssembly = GenTypes.GetTypeInAnyAssembly("FactionTerritories.Invasions.Utility");
			Type typeInAnyAssembly2 = GenTypes.GetTypeInAnyAssembly("FactionTerritories.Invasions.Invasion");
			if (!(typeInAnyAssembly == null) && !(typeInAnyAssembly2 == null))
			{
				_findForSettlement = AccessTools.Method(typeInAnyAssembly, "FindForSettlement", new Type[1] { typeof(Settlement) });
				_resolvedField = AccessTools.Field(typeInAnyAssembly2, "resolved");
				if (_findForSettlement != null)
				{
					EESLog.Message("[FT호환] Faction Territories 감지 — 침공 중 정착지는 EES 전장(Battlefield) 제외.");
					Patch_FactionTerritories_ExpansionComplete.TryApply(new Harmony(HarmonyInit.HarmonyId));
				}
			}
		}
		catch (Exception ex)
		{
			EESLog.Warning("[FT호환] 검출 실패(비활성으로 폴백): " + ex.Message);
			_findForSettlement = null;
		}
	}

	public static bool HasActiveInvasion(Settlement s)
	{
		try
		{
			Resolve();
			if (_findForSettlement == null || s == null)
			{
				return false;
			}
			object obj = _findForSettlement.Invoke(null, new object[1] { s });
			if (obj == null)
			{
				return false;
			}
			if (obj is WorldObject { Destroyed: not false })
			{
				return false;
			}
			bool flag = default(bool);
			int num;
			if (_resolvedField != null)
			{
				object value = _resolvedField.GetValue(obj);
				if (value is bool)
				{
					flag = (bool)value;
					num = 1;
				}
				else
				{
					num = 0;
				}
			}
			else
			{
				num = 0;
			}
			if (((uint)num & (flag ? 1u : 0u)) != 0)
			{
				return false;
			}
			return true;
		}
		catch
		{
			return false;
		}
	}
}
