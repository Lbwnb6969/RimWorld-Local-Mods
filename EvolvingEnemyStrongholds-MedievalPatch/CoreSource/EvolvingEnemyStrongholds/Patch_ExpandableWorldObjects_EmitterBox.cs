using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(ExpandableWorldObjectsUtility), "ExpandableWorldObjectsOnGUI")]
public static class Patch_ExpandableWorldObjects_EmitterBox
{
	private static readonly Color BoxColor = new Color(1f, 0.34f, 0.12f);

	private const float RectFactor = 1.3f;

	private const int Thickness = 2;

	public static void Postfix()
	{
		try
		{
			if (WorldRendererUtility.WorldBackgroundNow)
			{
				return;
			}
			List<Settlement> list = EmitterMarkerUtil.CachedEmitters();
			if (list.Count == 0)
			{
				return;
			}
			Color color = GUI.color;
			for (int i = 0; i < list.Count; i++)
			{
				Settlement settlement = list[i];
				if (settlement != null && settlement.Spawned && settlement.Tile.Valid && settlement.Tile.Layer == PlanetLayer.Selected)
				{
					float num = ExpandableWorldObjectsUtility.TransitionPct(settlement);
					if (!(num <= 0f))
					{
						Rect rect = ExpandableWorldObjectsUtility.ExpandedIconScreenRect(settlement, 1.3f);
						GUI.color = new Color(BoxColor.r, BoxColor.g, BoxColor.b, num);
						Widgets.DrawBox(rect, 2);
					}
				}
			}
			GUI.color = color;
		}
		catch
		{
			GUI.color = Color.white;
		}
	}
}
