using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public class WorldObject_EESRebuildConvoy : WorldObject_EESSupplyCaravan
{
	public int rebuildTier = 1;

	public int originTier = 1;

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref rebuildTier, "rebuildTier", 1);
		Scribe_Values.Look(ref originTier, "originTier", 1);
	}

	public override int ConvoyTier()
	{
		return Mathf.Max(rebuildTier, 3);
	}

	protected override string DestLabel()
	{
		return EESText.T("EES.Rebuild.DestLabel");
	}

	protected override bool DestStillValid()
	{
		if (base.Faction == null || base.Faction.defeated)
		{
			return false;
		}
		if (WorldComponent_EESRebuild.RuinTileBlocked(destTile, this))
		{
			return false;
		}
		return AnyFactionSettlement(base.Faction);
	}

	protected override void Arrive()
	{
		WorldComponent_EESRebuild.Instance?.Notify_ConvoyArrived(this);
		DisappearInternal();
	}

	private static bool AnyFactionSettlement(Faction f)
	{
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			if (settlements[i].Faction == f)
			{
				return true;
			}
		}
		return false;
	}
}
