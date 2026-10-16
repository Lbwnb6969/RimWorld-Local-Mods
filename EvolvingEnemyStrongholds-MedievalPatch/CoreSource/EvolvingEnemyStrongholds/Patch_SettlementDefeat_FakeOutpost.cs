using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(SettlementDefeatUtility), "IsDefeated")]
public static class Patch_SettlementDefeat_FakeOutpost
{
	public static void Postfix(Map map, ref bool __result)
	{
		try
		{
			if (__result && map != null && map.Parent is Settlement settlement)
			{
				StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
				if (strongholdData != null && FakeOutpostUtility.IsFake(strongholdData.roleDefName) && AnyHostileAlive(map))
				{
					__result = false;
				}
			}
		}
		catch
		{
		}
	}

	private static bool AnyHostileAlive(Map map)
	{
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn != null && !pawn.Dead)
			{
				Faction faction = pawn.Faction;
				if (faction != null && faction != Faction.OfPlayer && faction.HostileTo(Faction.OfPlayer))
				{
					return true;
				}
			}
		}
		List<Building> list = map.listerBuildings?.allBuildingsNonColonist;
		if (list != null)
		{
			for (int j = 0; j < list.Count; j++)
			{
				Building building = list[j];
				if (building != null && !building.Destroyed && building.Faction == Faction.OfMechanoids)
				{
					return true;
				}
			}
		}
		return false;
	}
}
