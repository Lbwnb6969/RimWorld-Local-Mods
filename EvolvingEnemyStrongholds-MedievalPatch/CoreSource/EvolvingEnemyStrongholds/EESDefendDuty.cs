using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public static class EESDefendDuty
{
	private static DutyDef _duty;

	private static bool _logged;

	public static DutyDef Duty
	{
		get
		{
			if (_duty == null)
			{
				_duty = DefDatabase<DutyDef>.GetNamedSilentFail("EES_DefendBase");
			}
			DutyDef dutyDef = _duty ?? DutyDefOf.DefendBase;
			if (!_logged && EESLog.Verbose)
			{
				_logged = true;
				EESLog.Message("수비대 방어 듀티 = " + dutyDef.defName + ((_duty == null) ? "(★EES_DefendBase 미발견 → 바닐라 폴백)" : "") + " 표적 획득=" + DescribeRadius(dutyDef, acquire: true) + " 유지=" + DescribeRadius(dutyDef, acquire: false) + " (바닐라 DefendBase=" + DescribeRadius(DutyDefOf.DefendBase, acquire: true) + "/" + DescribeRadius(DutyDefOf.DefendBase, acquire: false) + ").");
			}
			return dutyDef;
		}
	}

	private static string DescribeRadius(DutyDef d, bool acquire)
	{
		try
		{
			ThinkNode thinkNode = FindFightNode(d?.thinkNode);
			if (thinkNode == null)
			{
				return "?";
			}
			FieldInfo field = typeof(JobGiver_AIFightEnemy).GetField(acquire ? "targetAcquireRadius" : "targetKeepRadius", BindingFlags.Instance | BindingFlags.NonPublic);
			if (field == null)
			{
				return "?";
			}
			return ((float)field.GetValue(thinkNode)).ToString("0.#");
		}
		catch
		{
			return "?";
		}
	}

	private static ThinkNode FindFightNode(ThinkNode n)
	{
		if (n == null)
		{
			return null;
		}
		if (n is JobGiver_AIFightEnemy)
		{
			return n;
		}
		if (n.subNodes == null)
		{
			return null;
		}
		for (int i = 0; i < n.subNodes.Count; i++)
		{
			ThinkNode thinkNode = FindFightNode(n.subNodes[i]);
			if (thinkNode != null)
			{
				return thinkNode;
			}
		}
		return null;
	}

	public static StateGraph Widen(StateGraph g)
	{
		if (g == null || g.lordToils == null)
		{
			return g;
		}
		for (int i = 0; i < g.lordToils.Count; i++)
		{
			LordToil lordToil = g.lordToils[i];
			if (lordToil is LordToil_EESDefendBase || !(lordToil is LordToil_DefendBase lordToil_DefendBase))
			{
				continue;
			}
			LordToil lordToil2 = new LordToil_EESDefendBase(lordToil_DefendBase.baseCenter);
			g.lordToils[i] = lordToil2;
			if (g.transitions == null)
			{
				continue;
			}
			for (int j = 0; j < g.transitions.Count; j++)
			{
				Transition transition = g.transitions[j];
				if (transition == null)
				{
					continue;
				}
				if (transition.target == lordToil)
				{
					transition.target = lordToil2;
				}
				List<LordToil> sources = transition.sources;
				if (sources == null)
				{
					continue;
				}
				for (int k = 0; k < sources.Count; k++)
				{
					if (sources[k] == lordToil)
					{
						sources[k] = lordToil2;
					}
				}
			}
		}
		return g;
	}
}
