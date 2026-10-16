using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class IntelItemUtility
{
	private static readonly int[] CoverageMax = new int[3] { 5, 8, 12 };

	private static ThingDef _t1;

	private static ThingDef _t2;

	private static ThingDef _t3;

	private static bool _resolved;

	public static Texture2D GizmoIcon
	{
		get
		{
			Resolve();
			return _t3?.uiIcon ?? BaseContent.BadTex;
		}
	}

	public static Color GizmoIconColor
	{
		get
		{
			Resolve();
			return _t3?.uiIconColor ?? Color.white;
		}
	}

	private static void Resolve()
	{
		if (!_resolved)
		{
			_t1 = DefDatabase<ThingDef>.GetNamedSilentFail("EES_IntelData_T1Core");
			_t2 = DefDatabase<ThingDef>.GetNamedSilentFail("EES_IntelData_T2Core");
			_t3 = DefDatabase<ThingDef>.GetNamedSilentFail("EES_IntelData_T3Core");
			_resolved = true;
		}
	}

	private static ThingDef DefForItemTier(int itemTier)
	{
		Resolve();
		return itemTier switch
		{
			1 => _t1,
			2 => _t2,
			3 => _t3,
			_ => null,
		};
	}

	public static ThingDef ChooseForTarget(int targetTier)
	{
		for (int i = 1; i <= 3; i++)
		{
			if (CoverageMax[i - 1] >= targetTier)
			{
				ThingDef thingDef = DefForItemTier(i);
				if (thingDef != null && CountAvailable(thingDef) > 0)
				{
					return thingDef;
				}
			}
		}
		return null;
	}

	public static bool TierTooHigh(int targetTier)
	{
		return targetTier > CoverageMax[2];
	}

	public static int CountAvailable(ThingDef def)
	{
		if (def == null)
		{
			return 0;
		}
		int num = 0;
		List<Map> maps = Find.Maps;
		for (int i = 0; i < maps.Count; i++)
		{
			Map map = maps[i];
			if (map != null && IsPlayerMap(map))
			{
				List<Thing> list = map.listerThings.ThingsOfDef(def);
				for (int j = 0; j < list.Count; j++)
				{
					num += list[j].stackCount;
				}
			}
		}
		List<Caravan> caravans = Find.WorldObjects.Caravans;
		for (int k = 0; k < caravans.Count; k++)
		{
			Caravan caravan = caravans[k];
			if (caravan == null || !caravan.IsPlayerControlled)
			{
				continue;
			}
			List<Thing> list2 = CaravanInventoryUtility.AllInventoryItems(caravan);
			for (int l = 0; l < list2.Count; l++)
			{
				if (list2[l].def == def)
				{
					num += list2[l].stackCount;
				}
			}
		}
		return num;
	}

	public static bool ConsumeForTarget(int targetTier, out string usedLabel)
	{
		usedLabel = null;
		ThingDef thingDef = ChooseForTarget(targetTier);
		if (thingDef == null)
		{
			return false;
		}
		if (!ConsumeOne(thingDef))
		{
			return false;
		}
		usedLabel = thingDef.label;
		return true;
	}

	private static bool ConsumeOne(ThingDef def)
	{
		List<Map> maps = Find.Maps;
		for (int i = 0; i < maps.Count; i++)
		{
			Map map = maps[i];
			if (map != null && IsPlayerMap(map))
			{
				List<Thing> list = map.listerThings.ThingsOfDef(def);
				if (list.Count > 0)
				{
					list[0].SplitOff(1).Destroy();
					return true;
				}
			}
		}
		List<Caravan> caravans = Find.WorldObjects.Caravans;
		for (int j = 0; j < caravans.Count; j++)
		{
			Caravan caravan = caravans[j];
			if (caravan == null || !caravan.IsPlayerControlled)
			{
				continue;
			}
			List<Thing> list2 = CaravanInventoryUtility.AllInventoryItems(caravan);
			for (int k = 0; k < list2.Count; k++)
			{
				if (list2[k].def == def)
				{
					list2[k].SplitOff(1).Destroy();
					return true;
				}
			}
		}
		return false;
	}

	private static bool IsPlayerMap(Map m)
	{
		if (!m.IsPlayerHome)
		{
			if (m.ParentFaction != null)
			{
				return m.ParentFaction.IsPlayer;
			}
			return false;
		}
		return true;
	}
}
