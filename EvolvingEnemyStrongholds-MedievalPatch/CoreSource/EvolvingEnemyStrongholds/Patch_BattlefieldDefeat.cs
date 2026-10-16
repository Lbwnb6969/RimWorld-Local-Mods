using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(SettlementDefeatUtility), "CheckDefeated")]
public static class Patch_BattlefieldDefeat
{
	public static bool Prefix(Settlement factionBase)
	{
		Map map = factionBase?.Map;
		if (map == null)
		{
			return true;
		}
		MapComponent_StrongholdSiege component = map.GetComponent<MapComponent_StrongholdSiege>();
		if (component == null || !component.BattlefieldActive)
		{
			return true;
		}
		int battlefieldEntryTick = component.BattlefieldEntryTick;
		if (battlefieldEntryTick < 0)
		{
			return false;
		}
		if (Find.TickManager.TicksGame < battlefieldEntryTick + 45000)
		{
			return false;
		}
		return true;
	}
}
