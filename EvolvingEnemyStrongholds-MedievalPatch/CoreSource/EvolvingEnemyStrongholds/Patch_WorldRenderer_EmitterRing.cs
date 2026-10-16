using System.Collections.Generic;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

[StaticConstructorOnStartup]
[HarmonyPatch(typeof(WorldRenderer), "DrawWorldLayers")]
public static class Patch_WorldRenderer_EmitterRing
{
	private static readonly Color RingColor = new Color(1f, 0.34f, 0.12f);

	private const int RingRenderQueue = 3555;

	private const float RingSizeMul = 1.15f;

	private const float RingAltOffset = 0.015f;

	private static Material ringMat;

	private static int lastDrawnFrame = -1;

	private static Material RingMat
	{
		get
		{
			if (ringMat == null)
			{
				ringMat = MaterialPool.MatFrom("World/SelectedTile", ShaderDatabase.WorldOverlayAdditive, RingColor, 3555);
			}
			return ringMat;
		}
	}

	public static void Postfix()
	{
		try
		{
			if (WorldRendererUtility.WorldBackgroundNow || lastDrawnFrame == Time.frameCount)
			{
				return;
			}
			lastDrawnFrame = Time.frameCount;
			List<Settlement> list = EmitterMarkerUtil.CachedEmitters();
			for (int i = 0; i < list.Count; i++)
			{
				Settlement settlement = list[i];
				if (settlement != null && settlement.Spawned && settlement.Tile.Valid && settlement.Tile.LayerDef != null && !settlement.Tile.LayerDef.isSpace && settlement.Tile.Layer == PlanetLayer.Selected)
				{
					WorldRendererUtility.DrawQuadTangentialToPlanet(settlement.DrawPos, 1.15f * settlement.Tile.Layer.AverageTileSize, settlement.DrawAltitude + 0.015f, RingMat);
				}
			}
		}
		catch
		{
		}
	}
}
