using Verse;

namespace EvolvingEnemyStrongholds;

public class CompProperties_EESShieldCarrier : CompProperties
{
	public string shieldDef = "EES_CommanderShield";

	public int checkInterval = 15;

	public CompProperties_EESShieldCarrier()
	{
		compClass = typeof(CompEESShieldCarrier);
	}
}
