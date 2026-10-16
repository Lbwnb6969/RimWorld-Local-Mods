using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class HVTQuestUtility
{
	private const float TierRewardScale = 0.08f;

	private const int SpawnMinDist = 6;

	private const int SpawnMaxDist = 10;

	private const float MinSettlementGap = 4f;

	private const float NearbySettlementRadius = 10f;

	private const int NearbySettlementCap = 5;

	public const int TruceDays = 3;

	public static bool Enabled
	{
		get
		{
			if (StrongholdMod.Settings != null)
			{
				return StrongholdMod.Settings.enableHVTQuests;
			}
			return true;
		}
	}

	public static QuestScriptDef QuestDef => DefDatabase<QuestScriptDef>.GetNamedSilentFail("EES_HVTBounty");

	public static float BaseReward(CommanderType t)
	{
		switch (t)
		{
		case CommanderType.Warlord:
			return 1100f;
		case CommanderType.Fanatic:
		case CommanderType.Security:
			return 900f;
		case CommanderType.Comms:
		case CommanderType.Research:
			return 800f;
		case CommanderType.Marksman:
		case CommanderType.Defense:
		case CommanderType.Assault:
		case CommanderType.Engineer:
		case CommanderType.Observer:
			return 700f;
		default:
			return 500f;
		}
	}

	public static float VariantRewardMul(HVTVariant v)
	{
		return v switch
		{
			HVTVariant.Insider => 0.9f,
			HVTVariant.HighAlert => 1.3f,
			_ => 1f,
		};
	}

	public static bool TryOffer()
	{
		if (RimPactsCompat.StrategyMode)
		{
			EESLog.Message("HVT: 전략 모드라 발주 skip — 표적 스폰이 총량 고정 사양을 깬다.");
			return false;
		}
		if (QuestDef == null)
		{
			EESLog.Warning("HVT: QuestScriptDef EES_HVTBounty 미로드 → skip.");
			return false;
		}
		float num = StrongholdScoreUtility.TechProgress01();
		StrongholdScoreUtility.BountyBandKind bountyBandKind = StrongholdScoreUtility.BountyBand();
		if (bountyBandKind == StrongholdScoreUtility.BountyBandKind.None)
		{
			EESLog.Message("HVT: 발주 skip — 산업 시대 미도달(관문=" + StrongholdScoreUtility.EraGateName() + " " + $"prog={num:0.00} < 해금 {0.2f:0.00}=Electricity). " + $"산업 진입 후부터 T{3}~T{6}, " + $"스페이서 진입 후부터 T{7}~T{12} 가 발주된다.");
			return false;
		}
		bool flag = bountyBandKind == StrongholdScoreUtility.BountyBandKind.Industrial;
		int num2 = (flag ? 3 : 7);
		int num3 = (flag ? 6 : 12);
		string text = StrongholdScoreUtility.BountyBandName(bountyBandKind);
		Map anyPlayerHomeMap = Find.AnyPlayerHomeMap;
		if (anyPlayerHomeMap == null)
		{
			EESLog.Message("HVT: 플레이어 본거지 맵 없음(보상 드랍포드 불가) → skip.");
			return false;
		}
		int num4 = CountForeignSettlementsNear(anyPlayerHomeMap.Tile, 10f);
		if (num4 >= 5)
		{
			EESLog.Message($"HVT: 본거지 {10f:0}타일 내 타 세력 정착지 {num4}개(상한 {5}) → 발주 skip(포화).");
			return false;
		}
		List<Faction> allFactionsListForReading = Find.FactionManager.AllFactionsListForReading;
		List<Faction> list = new List<Faction>();
		List<int> list2 = new List<int>();
		List<Faction> list3 = new List<Faction>();
		List<string> list4 = new List<string>();
		float num5 = 0f;
		List<float> list5 = new List<float>();
		for (int i = 0; i < allFactionsListForReading.Count; i++)
		{
			Faction faction = allFactionsListForReading[i];
			if (!EligibleTargetFaction(faction))
			{
				continue;
			}
			int lo;
			int hi;
			float center;
			string dist;
			int num6 = StrongholdScoreUtility.RollTierForBounty(faction, out lo, out hi, out center, out dist);
			if (num6 < 0)
			{
				EESLog.Message($"HVT: 후보 제외 {faction.Name} — 발주 가능 구간 없음(밴드={text} 시대={faction.def.techLevel} 구간 T{lo}~T{hi} · " + $"하한 T{num2} · 상한 T{num3} · " + $"설정 maxTier=T{StrongholdMod.Settings?.maxTier ?? 13}).");
				continue;
			}
			string item = $"밴드={text} 시대={faction.def.techLevel} 구간 T{lo}~T{hi} 중심 {center:0.0}" + $"(관문={StrongholdScoreUtility.EraGateName()} prog={StrongholdScoreUtility.TechProgress01():0.00}) 분포[{dist}]";
			Faction faction2 = PickIssuer(faction);
			if (faction2 == null)
			{
				EESLog.Message("HVT: 후보 제외 " + faction.Name + " — 발주 가능 세력 없음(플레이어 비적대·표적과 적대·리더 보유).");
				continue;
			}
			list.Add(faction);
			list2.Add(num6);
			list4.Add(item);
			list3.Add(faction2);
			float num7 = (faction.HostileTo(Faction.OfPlayer) ? 3f : 1f);
			list5.Add(num7);
			num5 += num7;
		}
		if (list.Count == 0)
		{
			EESLog.Message($"HVT: 표적 세력 0 → skip(밴드={text} 구간 T{num2}~T{num3} 안에 드는 세력이 없거나 발주자가 없다).");
			return false;
		}
		int index = list.Count - 1;
		float num8 = Rand.Value * num5;
		for (int j = 0; j < list5.Count; j++)
		{
			num8 -= list5[j];
			if (num8 <= 0f)
			{
				index = j;
				break;
			}
		}
		Faction faction3 = list[index];
		int num9 = list2[index];
		Faction faction4 = list3[index];
		CommanderType commanderType = (flag ? CommanderType.Commander : RollCommanderType());
		HVTVariant hVTVariant = (HVTVariant)Rand.RangeInclusive(0, 2);
		float num10 = BaseReward(commanderType) * (1f + 0.08f * (float)num9) * VariantRewardMul(hVTVariant);
		bool flag2 = !faction3.HostileTo(Faction.OfPlayer);
		bool permanentEnemy = faction4.def.permanentEnemy;
		bool var = faction4.HostileTo(Faction.OfPlayer);
		float num11 = 1f;
		int num12 = 0;
		if (flag2)
		{
			num11 = Rand.Range(1.5f, 2f);
			num10 *= num11;
		}
		else if (!permanentEnemy)
		{
			num12 = Rand.RangeInclusive(8, 12);
		}
		Slate slate = new Slate();
		slate.Set("targetFaction", faction3);
		slate.Set("issuerFaction", faction4);
		slate.Set("asker", faction4.leader);
		slate.Set("map", anyPlayerHomeMap);
		slate.Set("rewardValue", num10);
		slate.Set("hvtTier", num9);
		slate.Set("hvtCommanderType", (int)commanderType);
		slate.Set("hvtVariant", (int)hVTVariant);
		slate.Set("hvtDirty", flag2);
		slate.Set("hvtGoodwillBonus", num12);
		slate.Set("hvtTruceIssuer", permanentEnemy);
		slate.Set("hvtHostileIssuer", var);
		string text2 = EESText.T("EES.HVT.Name", faction3.Name);
		string text3 = EESText.T("EES.HVT.Desc", faction4.Name, faction3.Name, commanderType.Nickname(), num9.ToString(), EESText.T("EES.HVT.Variant." + hVTVariant));
		if (flag2)
		{
			text3 += EESText.T("EES.HVT.DirtyWarn", faction3.Name);
		}
		else if (num12 > 0)
		{
			text3 += EESText.T("EES.HVT.GoodwillBonus", faction4.Name, num12.ToString());
		}
		if (permanentEnemy)
		{
			text3 += EESText.T("EES.HVT.TruceNotice", faction4.Name, 3.ToString());
		}
		slate.Set("resolvedQuestName", text2);
		slate.Set("resolvedQuestDescription", text3);
		Quest quest;
		try
		{
			quest = QuestUtility.GenerateQuestAndMakeAvailable(QuestDef, slate);
		}
		catch (Exception ex)
		{
			EESLog.Error("HVT: 퀘스트 생성 실패: " + ex);
			return false;
		}
		if (quest == null)
		{
			return false;
		}
		quest.name = text2;
		quest.description = text3;
		QuestUtility.SendLetterQuestAvailable(quest);
		EESLog.Message($"HVT: 발주 issuer={faction4.Name} targetFaction={faction3.Name} T{num9} " + list4[index] + " " + string.Format("cmd={0}{1} variant={2} reward={3:F0}", commanderType, flag ? "(산업 밴드 고정)" : "", hVTVariant, num10) + (flag2 ? $" DIRTY(×{num11:F2})" : $" goodwill+{num12}") + (permanentEnemy ? $" TRUCE({3}일)" : "") + " (표적은 수락 시 스폰).");
		return true;
	}

	private static bool AnySettlementWithin(PlanetTile tile, float radius)
	{
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (settlement.Tile.Valid && settlement.Tile.Layer == tile.Layer && Find.WorldGrid.ApproxDistanceInTiles(settlement.Tile, tile) <= radius)
			{
				return true;
			}
		}
		return false;
	}

	private static int CountForeignSettlementsNear(PlanetTile home, float radius)
	{
		int num = 0;
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (settlement.Faction != null && !settlement.Faction.IsPlayer && settlement.Tile.Valid && settlement.Tile.Layer == home.Layer && Find.WorldGrid.ApproxDistanceInTiles(settlement.Tile, home) <= radius)
			{
				num++;
			}
		}
		return num;
	}

	private static bool EligibleTargetFaction(Faction f)
	{
		if (f == null || f.IsPlayer || f.temporary || f.defeated || f.Hidden)
		{
			return false;
		}
		if (f.def == null || !f.def.humanlikeFaction || EESFactionKind.IsInsect(f))
		{
			return false;
		}
		if (EESFactionBlacklist.IsBlocked(f))
		{
			return false;
		}
		return true;
	}

	private static Faction PickIssuer(Faction targetFaction)
	{
		List<Faction> allFactionsListForReading = Find.FactionManager.AllFactionsListForReading;
		List<Faction> list = new List<Faction>();
		for (int i = 0; i < allFactionsListForReading.Count; i++)
		{
			Faction faction = allFactionsListForReading[i];
			if (!faction.IsPlayer && !faction.temporary && !faction.defeated && !faction.Hidden && faction.def != null && faction.def.humanlikeFaction && faction != targetFaction && faction.HostileTo(targetFaction) && faction.leader != null)
			{
				list.Add(faction);
			}
		}
		if (list.Count <= 0)
		{
			return null;
		}
		return list.RandomElement();
	}

	private static CommanderType RollCommanderType()
	{
		int length = Enum.GetValues(typeof(CommanderType)).Length;
		return (CommanderType)Rand.RangeInclusive(1, length - 1);
	}

	public static void SpawnAcceptedTarget(Quest quest, QuestPart_EESHVTSpawnTarget part)
	{
		if (RimPactsCompat.StrategyMode)
		{
			EESLog.Message($"HVT: 전략 모드라 수락 시 표적 스폰 불가 → 퀘스트 회수. quest#{quest.id}");
			quest.End(QuestEndOutcome.InvalidPreAcceptance, sendLetter: false, playSound: false);
			return;
		}
		Faction faction = CounterScoutUtility.FactionByLoadId(part.targetFactionLoadId);
		if (faction == null || faction.defeated || (!part.dirtyJob && !faction.HostileTo(Faction.OfPlayer)))
		{
			EESLog.Warning($"HVT: 수락 시 표적 세력 무효(소멸/비적대) → 퀘스트 회수. quest#{quest.id}");
			quest.End(QuestEndOutcome.InvalidPreAcceptance, sendLetter: false, playSound: false);
			return;
		}
		Predicate<PlanetTile> noObject = (PlanetTile t) => !Find.WorldObjects.AnyWorldObjectAt(t);
		Predicate<PlanetTile> validator = (PlanetTile t) => noObject(t) && !AnySettlementWithin(t, 4f);
		if (!TileFinder.TryFindNewSiteTile(out var tile, 6, 10, allowCaravans: false, null, 0.5f, canSelectComboLandmarks: true, TileFinderMode.Near, exitOnFirstTileFound: false, canBeSpace: false, null, validator) && !TileFinder.TryFindNewSiteTile(out tile, 5, 30, allowCaravans: false, null, 0.5f, canSelectComboLandmarks: true, TileFinderMode.Near, exitOnFirstTileFound: false, canBeSpace: false, null, validator) && !TileFinder.TryFindNewSiteTile(out tile, 5, 30, allowCaravans: false, null, 0.5f, canSelectComboLandmarks: true, TileFinderMode.Near, exitOnFirstTileFound: false, canBeSpace: false, null, noObject))
		{
			tile = TileFinder.RandomSettlementTileFor(faction);
		}
		if (!tile.Valid)
		{
			EESLog.Error($"HVT: 표적 스폰 타일 탐색 실패(전 폴백 소진) → 퀘스트 회수. quest#{quest.id}");
			quest.End(QuestEndOutcome.InvalidPreAcceptance, sendLetter: false, playSound: false);
			return;
		}
		Settlement settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
		settlement.SetFaction(faction);
		settlement.Tile = tile;
		settlement.Name = SettlementNameGenerator.GenerateSettlementName(settlement);
		Find.WorldObjects.Add(settlement);
		CommanderType commanderType = (CommanderType)part.commanderType;
		HVTVariant variant = (HVTVariant)part.variant;
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
		if (strongholdData != null)
		{
			strongholdData.tier = part.tier;
			strongholdData.tierSource = "hvt";
			strongholdData.roleDefName = "CommandFortress";
			strongholdData.hasCommander = true;
			strongholdData.commanderType = commanderType;
			strongholdData.commanderForced = true;
			CommanderUtility.ForceCommanderKind(strongholdData, settlement);
			StrongholdFeatureUtility.EnsureAssigned(strongholdData, settlement, WorldComponent_Strongholds.Instance);
			StrongholdScoreUtility.ApplyDerived(strongholdData, settlement);
			StrongholdScoreUtility.DecideFacilities(strongholdData, settlement);
			GarrisonGroupUtility.DecidePlan(strongholdData, settlement);
			int ticksGame = Find.TickManager.TicksGame;
			switch (variant)
			{
			case HVTVariant.Scouted:
				strongholdData.intelLevel = 1;
				strongholdData.lastScoutedTick = ticksGame;
				break;
			case HVTVariant.Insider:
				strongholdData.intelLevel = 1;
				strongholdData.lastScoutedTick = ticksGame;
				ScoutUtility.ApplySabotageSuccess(settlement, strongholdData, showMessage: false, "HVT insider");
				break;
			case HVTVariant.HighAlert:
				AlertUtility.RaiseAlert(strongholdData, settlement, 3, "HVT high alert");
				break;
			}
		}
		else
		{
			EESLog.Warning("HVT: 스폰 거점 " + settlement.LabelCap + " StrongholdData 생성 실패(블랙리스트?) — 거점 파괴 판정만 유효.");
		}
		List<QuestPart> partsListForReading = quest.PartsListForReading;
		for (int num = 0; num < partsListForReading.Count; num++)
		{
			if (partsListForReading[num] is QuestPart_EESHVTTarget questPart_EESHVTTarget)
			{
				questPart_EESHVTTarget.settlement = settlement;
				questPart_EESHVTTarget.settlementId = settlement.ID;
				questPart_EESHVTTarget.factionLoadId = faction.loadID;
				break;
			}
		}
		Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_HVTTargetSpawned"), EESText.T("EES.HVT.SpawnLetter.Text", settlement.LabelCap, commanderType.Nickname()), EESLetterDefOf.EES_HVTTargetSpawned, settlement);
		EESLog.Message($"HVT: 표적 스폰 {settlement.LabelCap} [{faction.Name}] tile={tile} T{part.tier} " + $"cmd={commanderType} variant={variant} role=CommandFortress.");
	}

	public static void Notify_StrongholdDestroyed(int settlementId)
	{
		SendOutcome(settlementId, success: true, "거점 파괴");
	}

	public static void Notify_TargetInvalid(int settlementId, string reason)
	{
		SendOutcome(settlementId, success: false, reason);
	}

	private static void SendOutcome(int settlementId, bool success, string reason)
	{
		List<Quest> list = Find.QuestManager?.QuestsListForReading;
		if (list == null || QuestDef == null)
		{
			return;
		}
		for (int i = 0; i < list.Count; i++)
		{
			Quest quest = list[i];
			if (quest.root == QuestDef)
			{
				QuestPart_EESHVTTarget questPart_EESHVTTarget = FindTargetPart(quest, settlementId);
				if (questPart_EESHVTTarget != null && quest.State == QuestState.Ongoing)
				{
					string tag = (success ? questPart_EESHVTTarget.outSignalKilled : questPart_EESHVTTarget.outSignalInvalid);
					Find.SignalManager.SendSignal(new Signal(tag));
					EESLog.Message(string.Format("HVT: {0} 시그널({1}) → quest#{2}.", success ? "성공" : "무효", reason, quest.id));
				}
			}
		}
	}

	private static QuestPart_EESHVTTarget FindTargetPart(Quest q, int settlementId)
	{
		if (settlementId < 0)
		{
			return null;
		}
		List<QuestPart> partsListForReading = q.PartsListForReading;
		for (int i = 0; i < partsListForReading.Count; i++)
		{
			if (partsListForReading[i] is QuestPart_EESHVTTarget questPart_EESHVTTarget && questPart_EESHVTTarget.settlementId == settlementId)
			{
				return questPart_EESHVTTarget;
			}
		}
		return null;
	}

	public static void ValidateLiveQuests()
	{
		List<Quest> list = Find.QuestManager?.QuestsListForReading;
		if (list == null || QuestDef == null)
		{
			return;
		}
		_ = WorldComponent_Strongholds.Instance;
		for (int i = 0; i < list.Count; i++)
		{
			Quest quest = list[i];
			if (quest.root != QuestDef || (quest.State != QuestState.Ongoing && quest.State != QuestState.NotYetAccepted))
			{
				continue;
			}
			QuestPart_EESHVTTarget questPart_EESHVTTarget = null;
			QuestPart_EESHVTSpawnTarget questPart_EESHVTSpawnTarget = null;
			List<QuestPart> partsListForReading = quest.PartsListForReading;
			for (int j = 0; j < partsListForReading.Count; j++)
			{
				if (partsListForReading[j] is QuestPart_EESHVTTarget questPart_EESHVTTarget2)
				{
					questPart_EESHVTTarget = questPart_EESHVTTarget2;
				}
				else if (partsListForReading[j] is QuestPart_EESHVTSpawnTarget questPart_EESHVTSpawnTarget2)
				{
					questPart_EESHVTSpawnTarget = questPart_EESHVTSpawnTarget2;
				}
			}
			if (questPart_EESHVTTarget == null)
			{
				continue;
			}
			bool flag = questPart_EESHVTSpawnTarget?.dirtyJob ?? false;
			if (quest.State == QuestState.NotYetAccepted)
			{
				Faction faction = CounterScoutUtility.FactionByLoadId(questPart_EESHVTSpawnTarget?.targetFactionLoadId ?? questPart_EESHVTTarget.factionLoadId);
				if (faction == null || faction.defeated || (!flag && !faction.HostileTo(Faction.OfPlayer)))
				{
					quest.End(QuestEndOutcome.InvalidPreAcceptance, sendLetter: false, playSound: false);
					EESLog.Message($"HVT: 미수락 퀘스트 회수(표적 세력 소멸/비적대) → quest#{quest.id}.");
				}
				continue;
			}
			if (questPart_EESHVTTarget.settlementId < 0)
			{
				Find.SignalManager.SendSignal(new Signal(questPart_EESHVTTarget.outSignalInvalid));
				EESLog.Warning($"HVT: Ongoing인데 표적 미스폰 → 무효 종료. quest#{quest.id}");
				continue;
			}
			Settlement settlement = questPart_EESHVTTarget.settlement;
			if (settlement == null || settlement.Destroyed)
			{
				Notify_TargetInvalid(questPart_EESHVTTarget.settlementId, "거점 소실(안전망)");
			}
			else if (!settlement.HasMap)
			{
				if (settlement.Faction == null || settlement.Faction.loadID != questPart_EESHVTTarget.factionLoadId)
				{
					Notify_TargetInvalid(questPart_EESHVTTarget.settlementId, "세력 교체");
				}
				else if (!flag && !settlement.Faction.HostileTo(Faction.OfPlayer))
				{
					Notify_TargetInvalid(questPart_EESHVTTarget.settlementId, "표적 세력 비적대 전환");
				}
			}
		}
	}
}
