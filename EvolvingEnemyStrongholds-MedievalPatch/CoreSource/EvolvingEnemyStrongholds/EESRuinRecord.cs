using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public class EESRuinRecord : IExposable
{
	public PlanetTile tile = PlanetTile.Invalid;

	public int factionLoadId = -1;

	public int tier = 1;

	public int dueTick = -1;

	public bool launched;

	public void ExposeData()
	{
		Scribe_Values.Look(ref tile, "tile");
		Scribe_Values.Look(ref factionLoadId, "factionLoadId", -1);
		Scribe_Values.Look(ref tier, "tier", 1);
		Scribe_Values.Look(ref dueTick, "dueTick", -1);
		Scribe_Values.Look(ref launched, "launched", defaultValue: false);
	}
}
