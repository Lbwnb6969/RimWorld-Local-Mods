using System.Collections.Generic;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class StrongholdLootFilter
{
	private static readonly Dictionary<string, int> SkipCount = new Dictionary<string, int>();

	private static int skipTotal;

	public static bool IsDroppableLoot(ThingDef def)
	{
		if (def != null)
		{
			return !def.destroyOnDrop;
		}
		return false;
	}

	public static bool IsDroppableLoot(ThingDef def, string context)
	{
		if (def == null)
		{
			return false;
		}
		if (!def.destroyOnDrop)
		{
			return true;
		}
		SkipCount.TryGetValue(def.defName, out var value);
		value++;
		SkipCount[def.defName] = value;
		skipTotal++;
		EESLog.Message("Loot skip [" + context + "] " + def.defName + ": destroyOnDrop=true (CE 미주입 탄약 등 — 주우면 소멸·거래 불가라 깔지 않는다). " + $"이 def {value}회 / 전체 {skipTotal}회 / {SkipCount.Count}종.");
		return false;
	}
}
