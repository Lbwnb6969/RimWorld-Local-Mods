using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public class StockGenerator_EESIntel : StockGenerator
{
	public ThingDef thingDef;

	public float appearChance = 0.5f;

	public override IEnumerable<Thing> GenerateThings(PlanetTile forTile, Faction faction = null)
	{
		if (thingDef == null || !Rand.Chance(appearChance))
		{
			yield break;
		}
		foreach (Thing item in StockGeneratorUtility.TryMakeForStock(thingDef, RandomCountOf(thingDef), faction))
		{
			yield return item;
		}
	}

	public override bool HandlesThingDef(ThingDef td)
	{
		return td == thingDef;
	}

	public override IEnumerable<string> ConfigErrors(TraderKindDef parentDef)
	{
		foreach (string item in base.ConfigErrors(parentDef))
		{
			yield return item;
		}
		if (thingDef == null)
		{
			yield return "StockGenerator_EESIntel: thingDef is null";
		}
		else if (!thingDef.tradeability.TraderCanSell())
		{
			yield return thingDef.defName + " tradeability doesn't allow traders to sell it";
		}
	}
}
