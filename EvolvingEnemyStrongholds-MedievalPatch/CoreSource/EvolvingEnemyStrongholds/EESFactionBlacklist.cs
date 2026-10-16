using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class EESFactionBlacklist
{
	public static bool IsBlocked(Faction f)
	{
		if (f != null)
		{
			return IsBlocked(f.def);
		}
		return false;
	}

	public static bool IsBlocked(FactionDef def)
	{
		if (def == null)
		{
			return false;
		}
		HashSet<string> hashSet = StrongholdMod.Settings?.blacklistedFactionDefs;
		if (hashSet != null && hashSet.Count > 0)
		{
			return hashSet.Contains(def.defName);
		}
		return false;
	}

	public static List<FactionDef> ConfigurableFactionDefs()
	{
		return (from d in DefDatabase<FactionDef>.AllDefs
			where d != null && !d.isPlayer && !d.hidden && !d.settlementTexturePath.NullOrEmpty()
			orderby (d.modContentPack == null) ? int.MaxValue : d.modContentPack.loadOrder, d.label ?? d.defName
			select d).ToList();
	}
}
