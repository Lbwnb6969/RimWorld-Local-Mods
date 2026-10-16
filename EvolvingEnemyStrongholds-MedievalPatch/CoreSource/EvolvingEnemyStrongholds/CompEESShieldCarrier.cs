using System;
using Verse;

namespace EvolvingEnemyStrongholds;

public class CompEESShieldCarrier : ThingComp
{
	private Thing dome;

	private bool warnedNoDef;

	private CompProperties_EESShieldCarrier Props => (CompProperties_EESShieldCarrier)props;

	public override void CompTick()
	{
		if (!parent.IsHashIntervalTick(Props.checkInterval))
		{
			return;
		}
		Pawn pawn = parent as Pawn;
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail(Props.shieldDef);
		if (namedSilentFail == null)
		{
			if (!warnedNoDef)
			{
				warnedNoDef = true;
				EESLog.Warning("Shield carrier: dome def '" + Props.shieldDef + "' not found — no shield.");
			}
			return;
		}
		Map map = pawn?.MapHeld;
		if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed || map == null)
		{
			ClearDome();
		}
		else if (dome == null || dome.Destroyed || !dome.Spawned || dome.Map != map)
		{
			dome = FindOrSpawn(map, pawn, namedSilentFail);
		}
		else if (dome.Position.DistanceTo(pawn.Position) > 2f)
		{
			dome.Destroy();
			dome = FindOrSpawn(map, pawn, namedSilentFail);
		}
	}

	private Thing FindOrSpawn(Map map, Pawn owner, ThingDef sd)
	{
		foreach (Thing item in map.listerThings.ThingsOfDef(sd))
		{
			if (item.Spawned && item.Faction == owner.Faction && item.Position.DistanceTo(owner.Position) <= 3f)
			{
				return item;
			}
		}
		try
		{
			Thing thing = ThingMaker.MakeThing(sd);
			thing.SetFactionDirect(owner.Faction);
			GenSpawn.Spawn(thing, owner.Position, map);
			EESLog.Message($"Commander shield dome spawned @ {owner.Position} (def-based interceptor, follows {owner.LabelShortCap}).");
			return thing;
		}
		catch (Exception ex)
		{
			EESLog.Warning("Commander shield dome spawn: " + ex.Message);
			return null;
		}
	}

	public void ClearDome()
	{
		if (dome != null && !dome.Destroyed && dome.Spawned)
		{
			dome.Destroy();
		}
		dome = null;
	}
}
