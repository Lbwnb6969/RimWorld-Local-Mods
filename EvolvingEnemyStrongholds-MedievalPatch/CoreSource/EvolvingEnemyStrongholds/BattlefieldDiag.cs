using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class BattlefieldDiag
{
	private static bool _ceChecked;

	private static Type _tSuppress;

	private static FieldInfo _fIsSuppressed;

	private static MethodInfo _pHunker;

	public static string CeSuppressionState(Pawn p)
	{
		if (!_ceChecked)
		{
			_ceChecked = true;
			_tSuppress = AccessTools.TypeByName("CombatExtended.CompSuppressable");
			if (_tSuppress != null)
			{
				_fIsSuppressed = AccessTools.Field(_tSuppress, "isSuppressed");
				_pHunker = AccessTools.PropertyGetter(_tSuppress, "IsHunkering");
			}
		}
		if (_tSuppress == null || p == null)
		{
			return "noCE";
		}
		try
		{
			List<ThingComp> allComps = p.AllComps;
			ThingComp thingComp = null;
			if (allComps != null)
			{
				for (int i = 0; i < allComps.Count; i++)
				{
					if (_tSuppress.IsInstanceOfType(allComps[i]))
					{
						thingComp = allComps[i];
						break;
					}
				}
			}
			if (thingComp == null)
			{
				return "noComp";
			}
			bool num = _fIsSuppressed != null && (bool)_fIsSuppressed.GetValue(thingComp);
			return string.Concat(str1: (_pHunker != null && (bool)_pHunker.Invoke(thingComp, null)) ? "+HUNKER" : "", str0: num ? "SUPPRESSED" : "ok");
		}
		catch
		{
			return "err";
		}
	}

	public static string ShortCaller(int skip)
	{
		try
		{
			StackTrace stackTrace = new StackTrace(skip, fNeedFileInfo: false);
			StringBuilder stringBuilder = new StringBuilder();
			int num = 0;
			for (int i = 0; i < stackTrace.FrameCount; i++)
			{
				if (num >= 3)
				{
					break;
				}
				MethodBase methodBase = stackTrace.GetFrame(i)?.GetMethod();
				if (!(methodBase == null))
				{
					string text = (methodBase.DeclaringType?.Name ?? "?") + "." + methodBase.Name;
					if (!text.Contains("BattlefieldDiag") && !text.Contains("Patch_Faction"))
					{
						stringBuilder.Append(text);
						stringBuilder.Append(' ');
						num++;
					}
				}
			}
			return stringBuilder.ToString();
		}
		catch
		{
			return "?";
		}
	}
}
