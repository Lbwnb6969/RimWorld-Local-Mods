using System;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class ScoutConfirmUtility
{
	public static void RunWithConfirms(Settlement settlement, StrongholdData d, Caravan caravan, Action proceed, string source)
	{
		if (d == null)
		{
			proceed();
			return;
		}
		float chance = ScoutUtility.RollChance(d, caravan);
		int humans = ScoutUtility.ScoutHumanlikeCount(caravan);
		Chain(settlement, d, chance, humans, proceed, source);
	}

	public static void RunWithConfirmsPods(Settlement settlement, StrongholdData d, Action proceed)
	{
		if (d == null)
		{
			proceed();
		}
		else
		{
			Chain(settlement, d, ScoutUtility.SuccessChance(d), 0, proceed, "포드 발사");
		}
	}

	private static void Chain(Settlement settlement, StrongholdData d, float chance, int humans, Action proceed, string source)
	{
		bool flag = ScoutUtility.IsHopeless(chance);
		bool flag2 = humans >= 2;
		EESLog.Message($"Scout confirm [{settlement?.LabelCap}] ({source}): T{d.tier} {ScoutUtility.CurveDebug(d.tier)} " + $"chance={chance:0.000} 0%경고={flag} 다인원경고={flag2}(정찰병 {humans}명) " + $"playerLvl={StrongholdScoreUtility.PlayerLevel01():0.00} alert={d.alertLevel}");
		Action action = proceed;
		if (flag2)
		{
			Action innerParty = action;
			action = delegate
			{
				Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(EESText.T("EES.Scout.PartyWarn", chance.ToStringPercent()), innerParty));
			};
		}
		if (flag)
		{
			Action innerHopeless = action;
			action = delegate
			{
				Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(EESText.T("EES.Scout.HopelessWarn", d.tier, chance.ToStringPercent()), innerHopeless));
			};
		}
		action();
	}
}
