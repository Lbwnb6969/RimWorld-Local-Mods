using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

public class HediffComp_EESInvisibility : HediffComp_Invisibility
{
	public override void CompPostPostAdd(DamageInfo? dinfo)
	{
		BecomeInvisible(instant: true);
		UpdateTargetCaches();
	}

	public override void CompPostPostRemoved()
	{
		UpdateTargetCaches();
	}

	private void UpdateTargetCaches()
	{
		Pawn pawn = parent.pawn;
		if (pawn.Spawned)
		{
			pawn.Map.attackTargetsCache.UpdateTarget(pawn);
		}
		if (pawn.RaceProps.Humanlike)
		{
			PortraitsCache.SetDirty(pawn);
			GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(pawn);
		}
	}
}
