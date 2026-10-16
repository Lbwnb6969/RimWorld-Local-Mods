using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class WorldDominationCompat
{
	private static bool _inited;

	private static bool _active;

	private static WdVariant _variant = WdVariant.None;

	private static Type _compType;

	private static FieldInfo _fStrength;

	private static MethodInfo _pStrengthGet;

	private static float _strMin = 100f;

	private static float _strMax = 2250f;

	public static bool Active
	{
		get
		{
			if (!_inited)
			{
				Init();
			}
			return _active;
		}
	}

	public static WdVariant Variant
	{
		get
		{
			if (!_inited)
			{
				Init();
			}
			return _variant;
		}
	}

	public static bool Enabled
	{
		get
		{
			if (Active)
			{
				if (StrongholdMod.Settings != null)
				{
					return StrongholdMod.Settings.worldDominationTierSync;
				}
				return true;
			}
			return false;
		}
	}

	private static void Init()
	{
		_inited = true;
		try
		{
			_compType = GenTypes.GetTypeInAnyAssembly("WorldDomination.CompViralSpread");
			if (_compType != null)
			{
				_variant = WdVariant.Base;
			}
			else
			{
				_compType = GenTypes.GetTypeInAnyAssembly("TSA_WorldDomination.CompViralSpread");
				if (_compType != null)
				{
					_variant = WdVariant.Experimental;
				}
			}
			if (_compType == null)
			{
				_active = false;
				_variant = WdVariant.None;
				return;
			}
			_fStrength = AccessTools.Field(_compType, "strength");
			if (_fStrength == null)
			{
				_pStrengthGet = AccessTools.PropertyGetter(_compType, "strength");
			}
			ApplyVariantScale();
			_active = _fStrength != null || _pStrengthGet != null;
			if (_active)
			{
				EESLog.Message($"World Domination detected: variant={_variant} ({_compType.FullName}, " + string.Format("strength={0}, scale={1:0}~{2:0}) ", (_fStrength != null) ? "field" : "property", _strMin, _strMax) + "— EES가 WD 거점 티어+생성 takeover.");
			}
			else
			{
				EESLog.Warning("WD detected but 'strength' field/property missing — WD takeover disabled.");
			}
		}
		catch (Exception ex)
		{
			_active = false;
			_variant = WdVariant.None;
			EESLog.Warning("WorldDominationCompat init failed: " + ex.Message);
		}
	}

	private static void ApplyVariantScale()
	{
		switch (_variant)
		{
		case WdVariant.Experimental:
			_strMin = 100f;
			_strMax = 2250f;
			break;
		case WdVariant.Base:
			_strMin = 100f;
			_strMax = 2250f;
			break;
		default:
			_strMin = 100f;
			_strMax = 2250f;
			break;
		}
	}

	private static float ReadStrength(object comp)
	{
		if (comp == null)
		{
			return -1f;
		}
		if (_fStrength != null)
		{
			object value = _fStrength.GetValue(comp);
			if (value is float)
			{
				return (float)value;
			}
		}
		if (_pStrengthGet != null)
		{
			object value = _pStrengthGet.Invoke(comp, null);
			if (value is float)
			{
				return (float)value;
			}
		}
		return -1f;
	}

	public static bool IsWdSettlement(Settlement s)
	{
		if (!Active || s == null)
		{
			return false;
		}
		return FindComp(s) != null;
	}

	public static bool TakesOver(Settlement s)
	{
		if (Enabled && s?.Faction != null && !s.Faction.IsPlayer)
		{
			return IsWdSettlement(s);
		}
		return false;
	}

	public static float GetStrengthRatio(Settlement s)
	{
		if (!Active || s == null)
		{
			return -1f;
		}
		try
		{
			object obj = FindComp(s);
			if (obj == null)
			{
				return -1f;
			}
			float num = ReadStrength(obj);
			if (num >= 0f)
			{
				return Mathf.Clamp01(Mathf.InverseLerp(_strMin, _strMax, num));
			}
		}
		catch (Exception ex)
		{
			EESLog.Warning("WD GetStrengthRatio failed: " + ex.Message);
		}
		return -1f;
	}

	public static float GetStrengthRaw(Settlement s)
	{
		if (!Active || s == null)
		{
			return -1f;
		}
		try
		{
			object obj = FindComp(s);
			if (obj != null)
			{
				return ReadStrength(obj);
			}
		}
		catch
		{
		}
		return -1f;
	}

	private static object FindComp(Settlement s)
	{
		List<WorldObjectComp> allComps = s.AllComps;
		if (allComps == null)
		{
			return null;
		}
		for (int i = 0; i < allComps.Count; i++)
		{
			if (_compType.IsInstanceOfType(allComps[i]))
			{
				return allComps[i];
			}
		}
		return null;
	}
}
