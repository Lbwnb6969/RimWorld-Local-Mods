using System.Collections.Generic;
using System.Linq;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class EESTurretBlacklist
{
	public struct RaceTurret
	{
		public string raceLabel;

		public ModContentPack mod;

		public ThingDef turret;
	}

	public static bool IsBlocked(ThingDef d)
	{
		if (d == null)
		{
			return false;
		}
		HashSet<string> hashSet = StrongholdMod.Settings?.blacklistedTurretDefs;
		if (hashSet != null && hashSet.Count > 0)
		{
			return hashSet.Contains(d.defName);
		}
		return false;
	}

	public static List<RaceTurret> RaceLinkedTurrets()
	{
		HashSet<ModContentPack> identity = StrongholdGarrisonUtility.GetIdentityMods();
		Dictionary<ModContentPack, string> dictionary = StrongholdGarrisonUtility.ModRaceLabels();
		List<RaceTurret> list = new List<RaceTurret>();
		foreach (ThingDef item in from t in StrongholdGarrisonUtility.AllConfigurableTurretDefs()
			where t.modContentPack != null && !t.modContentPack.IsOfficialMod && identity.Contains(t.modContentPack)
			orderby t.modContentPack.loadOrder, t.label ?? t.defName
			select t)
		{
			dictionary.TryGetValue(item.modContentPack, out var value);
			list.Add(new RaceTurret
			{
				raceLabel = (value ?? ""),
				mod = item.modContentPack,
				turret = item
			});
		}
		return list;
	}

	public static List<ThingDef> ModTurrets()
	{
		HashSet<ModContentPack> identity = StrongholdGarrisonUtility.GetIdentityMods();
		return (from d in StrongholdGarrisonUtility.AllConfigurableTurretDefs().Where(delegate(ThingDef d)
			{
				ModContentPack modContentPack = d.modContentPack;
				return modContentPack != null && !modContentPack.IsOfficialMod && !identity.Contains(modContentPack);
			})
			orderby d.modContentPack.loadOrder, d.label ?? d.defName
			select d).ToList();
	}
}
