using System.Collections.Generic;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

public class CompEESCommandAura : ThingComp
{
	private HediffDef hediffDef;

	private bool resolved;

	private int lastApplied = -1;

	private CompProperties_EESCommandAura Props => (CompProperties_EESCommandAura)props;

	private HediffDef Hediff
	{
		get
		{
			if (!resolved)
			{
				hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(Props.hediffDefName);
				resolved = true;
			}
			return hediffDef;
		}
	}

	public override void CompTick()
	{
		if (!(parent is Pawn pawn) || !parent.IsHashIntervalTick(Props.checkInterval))
		{
			return;
		}
		HediffDef hediff = Hediff;
		if (hediff == null)
		{
			if (lastApplied != -999)
			{
				lastApplied = -999;
				EESLog.Warning("Command aura: hediff def '" + Props.hediffDefName + "' not found — aura inactive.");
			}
		}
		else
		{
			if (!pawn.Spawned || pawn.Dead || pawn.Downed || pawn.MapHeld == null)
			{
				return;
			}
			Faction faction = pawn.Faction;
			if (faction == null)
			{
				return;
			}
			int num = 0;
			List<Pawn> list = pawn.MapHeld.mapPawns.SpawnedPawnsInFaction(faction);
			for (int i = 0; i < list.Count; i++)
			{
				Pawn pawn2 = list[i];
				if (pawn2 != null && !pawn2.Dead && pawn2.health != null && pawn2.RaceProps != null && pawn2.RaceProps.Humanlike && !(pawn2.Position.DistanceTo(pawn.Position) > Props.range))
				{
					GiveOrRefresh(pawn2, hediff);
					num++;
				}
			}
			if (num != lastApplied)
			{
				lastApplied = num;
				EESLog.Message($"Command aura [{pawn.LabelShortCap} {hediff.defName}]: applied to {num} ally pawn(s) in range {Props.range}.");
			}
		}
	}

	private void GiveOrRefresh(Pawn target, HediffDef def)
	{
		Hediff hediff = target.health.hediffSet.GetFirstHediffOfDef(def);
		if (hediff == null)
		{
			hediff = target.health.AddHediff(def);
		}
		HediffComp_Disappears hediffComp_Disappears = hediff.TryGetComp<HediffComp_Disappears>();
		if (hediffComp_Disappears != null)
		{
			hediffComp_Disappears.ticksToDisappear = Props.checkInterval * 12;
		}
	}

	public override void PostDraw()
	{
		base.PostDraw();
		if (parent is Pawn { Spawned: not false, Dead: false, Downed: false } pawn)
		{
			GenDraw.DrawRadiusRing(pawn.Position, Props.range, Props.ringColor);
		}
	}
}
