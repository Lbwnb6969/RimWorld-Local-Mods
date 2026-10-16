using RimWorld;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public static class ObserverFocus
{
	private static bool loggedForce;

	private static bool loggedBreach;

	private static bool loggedPursue;

	public static Pawn ResolveTargetFor(Pawn p)
	{
		if (!MapComponent_StrongholdSiege.AnyObserverActive)
		{
			return null;
		}
		if (p == null || !p.Spawned)
		{
			return null;
		}
		Map map = p.Map;
		if (map == null)
		{
			return null;
		}
		Faction parentFaction = map.ParentFaction;
		if (parentFaction == null || parentFaction.IsPlayer || p.Faction != parentFaction)
		{
			return null;
		}
		Pawn pawn = map.GetComponent<MapComponent_StrongholdSiege>()?.ObserverTarget;
		if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed || pawn.Map != map)
		{
			return null;
		}
		if (!p.HostileTo(pawn))
		{
			return null;
		}
		Lord lord = p.GetLord();
		if (lord != null && lord.LordJob is LordJob_ManTurrets)
		{
			return null;
		}
		return pawn;
	}

	public static void NotifyForced(Pawn p, Pawn tgt)
	{
		if (!loggedForce)
		{
			loggedForce = true;
			EESLog.Message("Observer focus ACTIVE: " + p?.LabelShortCap + " → enemyTarget 강제=" + tgt?.LabelShortCap + ". 이후 생략.");
		}
	}

	public static void NotifyBreach(Pawn p, Thing blocker, Pawn tgt)
	{
		if (!loggedBreach)
		{
			loggedBreach = true;
			EESLog.Message("Observer breach ACTIVE: " + p?.LabelShortCap + "가 표적 " + tgt?.LabelShortCap + " 도달 위해 " + blocker?.LabelShortCap + "(" + blocker?.def?.defName + ") 파괴 시작. 이후 생략.");
		}
	}

	public static void NotifyPursue(Pawn p, Pawn tgt)
	{
		if (!loggedPursue)
		{
			loggedPursue = true;
			EESLog.Message("Observer pursue ACTIVE: " + p?.LabelShortCap + "가 거점 leash 무시하고 표적 " + tgt?.LabelShortCap + " 추격(Goto). 이후 생략.");
		}
	}
}
