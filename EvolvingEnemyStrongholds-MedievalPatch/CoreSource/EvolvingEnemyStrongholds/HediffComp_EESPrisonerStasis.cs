using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public class HediffComp_EESPrisonerStasis : HediffComp
{
	public override void CompPostTick(ref float severityAdjustment)
	{
		Pawn pawn = base.Pawn;
		if (pawn == null || !pawn.IsHashIntervalTick(60))
		{
			return;
		}
		if (pawn.IsWorldPawn())
		{
			if (pawn.health != null)
			{
				pawn.health.RemoveHediff(parent);
			}
		}
		else
		{
			if (!pawn.IsPrisonerOfColony && (pawn.Faction == null || !pawn.Faction.IsPlayer))
			{
				return;
			}
			try
			{
				if (pawn.IsPrisonerOfColony && pawn.health != null && HediffDefOf.Anesthetic != null && !pawn.health.hediffSet.HasHediff(HediffDefOf.Anesthetic))
				{
					pawn.health.AddHediff(HediffDefOf.Anesthetic);
				}
			}
			catch
			{
			}
			if (pawn.health != null)
			{
				pawn.health.RemoveHediff(parent);
			}
		}
	}
}
