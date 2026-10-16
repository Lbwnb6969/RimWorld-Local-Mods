using RimWorld;
using RimWorld.Planet;

namespace EvolvingEnemyStrongholds;

public class MapParent_EESSupplyBattle : MapParent
{
	public override bool CanReformFoggedEnemies => true;

	public override void PostMapGenerate()
	{
		base.PostMapGenerate();
		GetComponent<TimedDetectionRaids>()?.StartDetectionCountdown(240000);
	}

	public override bool ShouldRemoveMapNow(out bool alsoRemoveWorldObject)
	{
		alsoRemoveWorldObject = false;
		if (base.Map == null || base.Map.mapPawns.AnyPawnBlockingMapRemoval)
		{
			return false;
		}
		if (TransporterUtility.IncomingTransporterPreventingMapRemoval(base.Map))
		{
			return false;
		}
		alsoRemoveWorldObject = true;
		return true;
	}
}
