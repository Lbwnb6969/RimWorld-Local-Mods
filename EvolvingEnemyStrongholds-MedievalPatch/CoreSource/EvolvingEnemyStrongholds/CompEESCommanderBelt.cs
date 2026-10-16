using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

public class CompEESCommanderBelt : ThingComp
{
	public bool legit;

	public override void PostExposeData()
	{
		base.PostExposeData();
		Scribe_Values.Look(ref legit, "legit", defaultValue: false);
	}

	public override void CompTick()
	{
		Apparel apparel = parent as Apparel;
		Pawn pawn = apparel?.Wearer;
		if (pawn != null)
		{
			bool flag = pawn.Faction == null || pawn.Faction.IsPlayer || pawn.IsPrisonerOfColony;
			if ((!legit || flag) && !parent.Destroyed)
			{
				if (EESLog.Verbose)
				{
					EESLog.Message("Commander shield belt self-removed from " + pawn.LabelShortCap + " " + $"(legit={legit}, playerFaction={pawn.Faction != null && pawn.Faction.IsPlayer}, prisoner={pawn.IsPrisonerOfColony}).");
				}
				pawn.apparel.Remove(apparel);
				if (!apparel.Destroyed)
				{
					apparel.Destroy();
				}
			}
		}
		else if (parent.Spawned && !parent.Destroyed)
		{
			parent.Destroy();
		}
	}
}
