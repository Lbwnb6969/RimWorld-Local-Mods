using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Faction), "TryAffectGoodwillWith")]
public static class Patch_SuppressPollutedOutpostGoodwill
{
	public static bool Prefix(Faction __instance, Faction other, int goodwillChange, HistoryEventDef reason, GlobalTargetInfo? lookTarget, ref bool __result)
	{
		if (reason == null)
		{
			return true;
		}
		string defName = reason.defName;
		if (defName != "PollutedBase" && defName != "PollutedNearbySite")
		{
			return true;
		}
		Settlement settlement = ResolveSettlement(lookTarget);
		bool flag = settlement != null && IsEESPolluted(settlement);
		EESLog.Message($"[Goodwill-Pollution] reason={defName} {__instance?.Name}->{other?.Name} delta={goodwillChange} " + string.Format("settlement={0} byTarget={1} -> {2}", (settlement != null) ? settlement.LabelCap.ToString() : "(null)", flag, flag ? "SKIP(차단)" : "통과"));
		if (flag && goodwillChange < 0 && (__instance == Faction.OfPlayer || other == Faction.OfPlayer))
		{
			__result = false;
			return false;
		}
		return true;
	}

	private static bool FactionOwnsEESPolluted(Faction f)
	{
		if (f == null || f.IsPlayer)
		{
			return false;
		}
		WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
		if (instance == null)
		{
			return false;
		}
		List<Settlement> list = Find.WorldObjects?.Settlements;
		if (list == null)
		{
			return false;
		}
		for (int i = 0; i < list.Count; i++)
		{
			Settlement settlement = list[i];
			if (settlement != null && settlement.Faction == f)
			{
				StrongholdData existing = instance.GetExisting(settlement.ID);
				if (existing != null && PollutedOutpostUtility.IsPolluted(existing.roleDefName))
				{
					return true;
				}
			}
		}
		return false;
	}

	private static Settlement ResolveSettlement(GlobalTargetInfo? lookTarget)
	{
		if (!lookTarget.HasValue)
		{
			return null;
		}
		GlobalTargetInfo value = lookTarget.Value;
		if (value.WorldObject is Settlement result)
		{
			return result;
		}
		if (value.Map?.Parent is Settlement result2)
		{
			return result2;
		}
		return value.Tile.Valid ? Find.WorldObjects?.SettlementAt(value.Tile) : null;
	}

	private static bool IsEESPolluted(Settlement s)
	{
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetExisting(s.ID);
		if (strongholdData != null)
		{
			return PollutedOutpostUtility.IsPolluted(strongholdData.roleDefName);
		}
		return false;
	}
}
