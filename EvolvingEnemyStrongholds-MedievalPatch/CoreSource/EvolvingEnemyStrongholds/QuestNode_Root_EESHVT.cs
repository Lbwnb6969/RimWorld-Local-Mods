using RimWorld;
using RimWorld.QuestGen;
using Verse;

namespace EvolvingEnemyStrongholds;

public class QuestNode_Root_EESHVT : QuestNode
{
	public const int TimeoutTicks = 900000;

	protected override bool TestRunInt(Slate slate)
	{
		if (RimPactsCompat.StrategyMode)
		{
			return false;
		}
		if (slate.Get<Faction>("targetFaction") != null && slate.Get<Faction>("issuerFaction") != null && slate.Get<Pawn>("asker") != null)
		{
			return slate.Get<Map>("map") != null;
		}
		return false;
	}

	protected override void RunInt()
	{
		Slate slate = QuestGen.slate;
		Quest quest = QuestGen.quest;
		Faction faction = slate.Get<Faction>("targetFaction");
		Faction faction2 = slate.Get<Faction>("issuerFaction");
		Pawn pawn = slate.Get<Pawn>("asker");
		float rewardValue = slate.Get("rewardValue", 1000f);
		int tier = slate.Get("hvtTier", 7);
		int commanderType = slate.Get("hvtCommanderType", 1);
		int variant = slate.Get("hvtVariant", 0);
		bool dirtyJob = slate.Get("hvtDirty", defaultValue: false);
		int num = slate.Get("hvtGoodwillBonus", 0);
		bool flag = slate.Get("hvtTruceIssuer", defaultValue: false);
		bool flag2 = slate.Get("hvtHostileIssuer", defaultValue: false);
		QuestPart_EESHVTTarget part = new QuestPart_EESHVTTarget
		{
			settlement = null,
			settlementId = -1,
			factionLoadId = faction.loadID,
			outSignalKilled = QuestGenUtility.HardcodedSignalWithQuestID("hvt.Killed"),
			outSignalInvalid = QuestGenUtility.HardcodedSignalWithQuestID("hvt.Invalid")
		};
		quest.AddPart(part);
		quest.AddPart(new QuestPart_EESHVTSpawnTarget
		{
			targetFactionLoadId = faction.loadID,
			tier = tier,
			commanderType = commanderType,
			variant = variant,
			dirtyJob = dirtyJob
		});
		quest.GiveRewards(new RewardsGeneratorParams
		{
			rewardValue = rewardValue,
			giverFaction = faction2,
			allowGoodwill = !flag2,
			allowRoyalFavor = !flag2
		}, "hvt.Killed", asker: pawn, customLetterLabel: EESText.T("EES_Letter_HVTReward"), customLetterText: EESText.T("EES.HVT.RewardLetter.Text", faction2.Name));
		if (flag)
		{
			quest.AddPart(new QuestPart_EESHVTRaidTruce
			{
				inSignal = QuestGenUtility.HardcodedSignalWithQuestID("hvt.Killed"),
				issuerFactionLoadId = faction2.loadID
			});
		}
		quest.End(QuestEndOutcome.Success, num, (num > 0) ? faction2 : null, "hvt.Killed", QuestPart.SignalListenMode.OngoingOnly, sendStandardLetter: true, playSound: true);
		quest.End(QuestEndOutcome.Unknown, 0, null, "hvt.Invalid");
		quest.Delay(900000, delegate
		{
			quest.End(QuestEndOutcome.Fail, 0, null, null, QuestPart.SignalListenMode.OngoingOnly, sendStandardLetter: true);
		}, null, null, null, reactivatable: false, null, null, isQuestTimeout: true, null, null, "EES HVT timeout");
	}
}
