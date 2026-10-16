using System.Collections.Generic;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class EscapeDeviceUtility
{
	public const string DefName = "EES_EscapeDevice";

	private static ThingDef _def;

	private static bool _resolved;

	public static ThingDef Def
	{
		get
		{
			if (!_resolved)
			{
				_def = DefDatabase<ThingDef>.GetNamedSilentFail("EES_EscapeDevice");
				_resolved = true;
			}
			return _def;
		}
	}

	public static bool WornBy(Pawn p)
	{
		ThingDef def = Def;
		if (def == null || p?.apparel == null)
		{
			return false;
		}
		List<Apparel> wornApparel = p.apparel.WornApparel;
		for (int i = 0; i < wornApparel.Count; i++)
		{
			if (wornApparel[i]?.def == def)
			{
				return true;
			}
		}
		return false;
	}

	public static bool ConsumeWornBy(Pawn p)
	{
		ThingDef def = Def;
		if (def == null || p?.apparel == null)
		{
			return false;
		}
		List<Apparel> wornApparel = p.apparel.WornApparel;
		for (int i = 0; i < wornApparel.Count; i++)
		{
			Apparel apparel = wornApparel[i];
			if (apparel?.def == def)
			{
				p.apparel.Remove(apparel);
				if (!apparel.Destroyed)
				{
					apparel.Destroy();
				}
				return true;
			}
		}
		return false;
	}
}
