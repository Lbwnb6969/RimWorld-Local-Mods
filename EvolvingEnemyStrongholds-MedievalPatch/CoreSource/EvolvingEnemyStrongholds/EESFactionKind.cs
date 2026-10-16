using System.Collections.Generic;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class EESFactionKind
{
	private static readonly Dictionary<string, bool> _cache = new Dictionary<string, bool>();

	private static readonly string[] FleshInsect = new string[1] { "Insectoid" };

	private static readonly string[] FleshMech = new string[1] { "Mechanoid" };

	private static readonly string[] FleshEntity = new string[3] { "EntityFlesh", "EntityMechanical", "Fleshbeast" };

	public static bool IsInsect(Faction f)
	{
		if (f != null)
		{
			return IsInsect(f.def);
		}
		return false;
	}

	public static bool IsInsect(FactionDef def)
	{
		return Match(def, "insect", FleshInsect);
	}

	public static bool IsMech(Faction f)
	{
		if (f != null)
		{
			return IsMech(f.def);
		}
		return false;
	}

	public static bool IsMech(FactionDef def)
	{
		return Match(def, "mech", FleshMech);
	}

	public static bool IsEntity(Faction f)
	{
		if (f != null)
		{
			return IsEntity(f.def);
		}
		return false;
	}

	public static bool IsEntity(FactionDef def)
	{
		return Match(def, "entity", FleshEntity);
	}

	private static bool Match(FactionDef def, string kind, string[] fleshNames)
	{
		if (def == null || def.isPlayer)
		{
			return false;
		}
		if (def.humanlikeFaction)
		{
			return false;
		}
		string key = def.defName + "|" + kind;
		if (_cache.TryGetValue(key, out var value))
		{
			return value;
		}
		bool flag = Compute(def, fleshNames);
		_cache[key] = flag;
		return flag;
	}

	private static bool Compute(FactionDef def, string[] fleshNames)
	{
		int num = 0;
		int num2 = 0;
		List<PawnGroupMaker> pawnGroupMakers = def.pawnGroupMakers;
		if (pawnGroupMakers != null)
		{
			for (int i = 0; i < pawnGroupMakers.Count; i++)
			{
				List<PawnGenOption> list = pawnGroupMakers[i]?.options;
				if (list == null)
				{
					continue;
				}
				for (int j = 0; j < list.Count; j++)
				{
					RaceProperties raceProperties = list[j]?.kind?.race?.race;
					if (raceProperties?.FleshType != null)
					{
						num2++;
						if (NameIn(fleshNames, raceProperties.FleshType.defName))
						{
							num++;
						}
					}
				}
			}
		}
		if (num2 == 0)
		{
			RaceProperties raceProperties2 = def.basicMemberKind?.race?.race;
			if (raceProperties2?.FleshType != null)
			{
				return NameIn(fleshNames, raceProperties2.FleshType.defName);
			}
			return false;
		}
		return num * 2 > num2;
	}

	private static bool NameIn(string[] arr, string s)
	{
		for (int i = 0; i < arr.Length; i++)
		{
			if (arr[i] == s)
			{
				return true;
			}
		}
		return false;
	}
}
