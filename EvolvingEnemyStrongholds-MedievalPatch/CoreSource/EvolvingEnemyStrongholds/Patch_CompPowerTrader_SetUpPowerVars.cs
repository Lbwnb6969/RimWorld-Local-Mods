using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(CompPowerTrader), "SetUpPowerVars")]
public static class Patch_CompPowerTrader_SetUpPowerVars
{
	private const float SelfSupplyOutput = 2000f;

	public static void Postfix(CompPowerTrader __instance)
	{
		ThingWithComps thingWithComps = __instance?.parent;
		if (!(thingWithComps is Building_Turret))
		{
			return;
		}
		Faction faction = thingWithComps.Faction;
		if (faction == null || faction == Faction.OfPlayer)
		{
			return;
		}
		Map mapHeld = thingWithComps.MapHeld;
		if (mapHeld == null || !(mapHeld.Parent is Settlement settlement) || EESCustomBaseDetector.IsForeignSettlement(settlement))
		{
			return;
		}
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
		if (strongholdData == null || !FakeOutpostUtility.IsFake(strongholdData.roleDefName))
		{
			MapComponent_StrongholdSiege component = mapHeld.GetComponent<MapComponent_StrongholdSiege>();
			if (component == null || !component.turretsGridPowered)
			{
				__instance.PowerOutput = 2000f;
			}
		}
	}
}
