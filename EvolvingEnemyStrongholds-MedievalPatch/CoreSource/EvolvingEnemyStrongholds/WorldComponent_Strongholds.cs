using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public partial class WorldComponent_Strongholds : WorldComponent
{
	private Dictionary<int, StrongholdData> dataById = new Dictionary<int, StrongholdData>();

	private List<FinalStandClusterRecord> finalStandClusters = new List<FinalStandClusterRecord>();

	private int nextFinalStandClusterId = 1;

	// Explicit cross-location replacement history. This is written only by the
	// lineage API; no world scan is allowed to infer these relationships.
	private Dictionary<string, string> finalStandLineage = new Dictionary<string, string>();

	private int lastAlertDecayTick = -1;

	public List<AssaultOp> assaultOps = new List<AssaultOp>();

	private int lastAssaultTick = -1;

	private List<int> tmpKeys;

	private List<StrongholdData> tmpVals;

	private List<string> lineageKeys;

	private List<string> lineageValues;

	private const int GrowthCheckInterval = 2500;

	public const int MinGeneralTier = 1;

	public const float GeneralTierRecoverMul = 0.4f;

	public const int MinPuppetTier = 1;

	public static WorldComponent_Strongholds Instance => Find.World?.GetComponent<WorldComponent_Strongholds>();

	public IEnumerable<KeyValuePair<int, StrongholdData>> AllDataPairs => dataById;

	public WorldComponent_Strongholds(World world)
		: base(world)
	{
	}

	public StrongholdData GetOrCreate(Settlement settlement)
	{
		if (settlement == null)
		{
			return null;
		}
		if (EESFactionBlacklist.IsBlocked(settlement.Faction) || EESCustomBaseDetector.IsForeignSettlement(settlement))
		{
			return null;
		}
		if (dataById.TryGetValue(settlement.ID, out var value))
		{
			StrongholdFeatureUtility.EnsureAssigned(value, settlement, this);
			MaybeReleaseEmitterRole(value, settlement);
			MaybeGrowTier(value, settlement);
			MaybeFixCommanderTech(value, settlement);
			MaybeFixStrategyRole(value, settlement);
			return value;
		}
		StrongholdData strongholdData = new StrongholdData(settlement.ID);
		strongholdData.lastGrowthTick = Find.TickManager.TicksGame;
		if (EESPuppetUtility.IsPuppetOutpost(settlement))
		{
			strongholdData.tierClockTick = strongholdData.lastGrowthTick;
			strongholdData.lastCap = EESPuppetUtility.CapFor(settlement);
			strongholdData.intelLevel = 1;
		}
		strongholdData.tier = StrongholdScoreUtility.ComputeTier(settlement);
		strongholdData.roleDefName = StrongholdScoreUtility.ComputeRole(settlement, strongholdData.tier);
		StrongholdRoleDiversityUtility.ApplyOnCreation(strongholdData, settlement, this);
		StrongholdFeatureUtility.ApplyOnCreation(strongholdData, settlement, this);
		StrongholdScoreUtility.ApplyDerived(strongholdData, settlement);
		CommanderUtility.DecideCommander(strongholdData, settlement);
		StrongholdScoreUtility.DecideFacilities(strongholdData, settlement);
		GarrisonGroupUtility.DecidePlan(strongholdData, settlement);
		dataById[settlement.ID] = strongholdData;
		EESLog.Message($"Created data for {settlement.LabelCap} [{settlement.Faction?.Name}] → {strongholdData} | {StrongholdScoreUtility.DebugDescribe(settlement)}");
		if (WorldDominationCompat.Active)
		{
			bool flag = WorldDominationCompat.IsWdSettlement(settlement);
			bool flag2 = WorldDominationCompat.TakesOver(settlement);
			EESLog.Message($"[WD진단] {settlement.LabelCap}: variant={WorldDominationCompat.Variant} WD거점={flag} takeover={flag2} strength={WorldDominationCompat.GetStrengthRaw(settlement):0} " + string.Format("→ 티어 T{0} (출처={1}).", strongholdData.tier, flag2 ? "WD strength" : "EES 진행도"));
		}
		return strongholdData;
	}

	private void MaybeFixCommanderTech(StrongholdData d, Settlement settlement)
	{
		if (d != null && settlement != null && d.commanderType != CommanderType.None && !CommanderTypeUtil.AllowedForFactionTech(d.commanderType, settlement.Faction))
		{
			CommanderType commanderType = d.commanderType;
			TechLevel valueOrDefault = (settlement.Faction?.def?.techLevel).GetValueOrDefault();
			if (settlement.HasMap)
			{
				EESLog.Message($"[지휘관 기술게이트] {settlement.LabelCap} [{settlement.Faction?.Name}]: {commanderType} 은 " + $"{valueOrDefault} 세력에 부적합하지만 ★맵이 열려 있어 재산정 보류(폰이 이미 스폰됨).");
				return;
			}
			d.commanderForced = false;
			d.commanderKindDefName = null;
			CommanderUtility.DecideCommander(d, settlement);
			EESLog.Message("[지휘관 기술게이트] " + settlement.LabelCap + " [" + settlement.Faction?.Name + "]: " + $"{commanderType} → {d.commanderType} 재산정(세력 {valueOrDefault} < 요구 {CommanderTypeUtil.RequiredTech(commanderType)}, " + $"hasCommander={d.hasCommander}).");
		}
	}

	private void MaybeFixStrategyRole(StrongholdData d, Settlement settlement)
	{
		if (d == null || settlement == null || !RimPactsCompat.StrategyMode)
		{
			return;
		}
		bool num = FakeOutpostUtility.IsFake(d.roleDefName);
		bool flag = BattlefieldUtility.IsBattlefield(d.roleDefName);
		if (num || flag)
		{
			string roleDefName = d.roleDefName;
			if (settlement.HasMap)
			{
				EESLog.Message("[전략 모드 역할 재배정] " + settlement.LabelCap + " [" + settlement.Faction?.Name + "]: '" + roleDefName + "' 은 정복 표적에서 제외되지만 ★맵이 열려 있어 보류(실물이 이미 지어져 있다).");
			}
			else
			{
				d.roleForced = false;
				d.roleDefName = StrongholdScoreUtility.ComputeRole(settlement, d.tier);
				StrongholdFeatureUtility.EnsureAssigned(d, settlement, this);
				StrongholdScoreUtility.ApplyDerived(d, settlement);
				CommanderUtility.DecideCommander(d, settlement);
				StrongholdScoreUtility.DecideFacilities(d, settlement);
				GarrisonGroupUtility.DecidePlan(d, settlement);
				EESLog.Message("[전략 모드 역할 재배정] " + settlement.LabelCap + " [" + settlement.Faction?.Name + "]: " + $"'{roleDefName}' → '{d.roleDefName}' (T{d.tier}) — 총량 고정 사양이라 영구 면역 표적을 없앤다.");
			}
		}
	}

	private void MaybeReleaseEmitterRole(StrongholdData d, Settlement settlement)
	{
		if (d == null || settlement?.Faction == null || !ThreatEmitterUtility.IsEmitterRole(d.roleDefName))
		{
			return;
		}
		Faction faction = settlement.Faction;
		TechLevel techLevel = faction.def?.techLevel ?? TechLevel.Undefined;
		string text;
		if (d.emitterSpawnFactionLoadId >= 0)
		{
			if (d.emitterSpawnFactionLoadId == faction.loadID)
			{
				return;
			}
			text = "스폰 당시 '" + d.emitterSpawnFactionName + "' → 현재 '" + faction.Name + "'";
		}
		else
		{
			if (ThreatEmitterUtility.TechOkFor(d.roleDefName, techLevel))
			{
				return;
			}
			text = "스폰 당시 기록 없음(구 세이브)인데 세력 조건 불일치 — " + ThreatEmitterUtility.RoleLabelKo(d.roleDefName) + " 은 " + string.Format("{0} 여야 하는데 현재 {1}", (d.roleDefName == "PsychicShrine") ? "신석기 이하" : "산업+", techLevel);
		}
		string roleDefName = d.roleDefName;
		if (settlement.HasMap)
		{
			EESLog.Message("[위협 발신 역할 해제] " + settlement.LabelCap + " [" + faction.Name + "]: '" + roleDefName + "' 소유권 변경 감지(" + text + ") — ★맵이 열려 있어 보류(실물이 이미 지어져 있다). 맵이 닫히면 다음 조회에서 해제한다.");
			return;
		}
		d.tierSource = "auto";
		d.emitterSpawnFactionLoadId = -1;
		d.emitterSpawnFactionName = null;
		d.emitterWarnedTick = -1;
		d.emitterStartNotified = false;
		d.artilleryNextVolleyTick = -1;
		d.artillerySilenced = false;
		d.artillerySawMortars = false;
		d.roleForced = false;
		int tier = d.tier;
		d.tier = StrongholdScoreUtility.ComputeTier(settlement);
		d.roleDefName = StrongholdScoreUtility.ComputeRole(settlement, d.tier);
		StrongholdFeatureUtility.EnsureAssigned(d, settlement, this);
		StrongholdScoreUtility.ApplyDerived(d, settlement);
		CommanderUtility.DecideCommander(d, settlement);
		StrongholdScoreUtility.DecideFacilities(d, settlement);
		GarrisonGroupUtility.DecidePlan(d, settlement);
		EESLog.Message($"[위협 발신 역할 해제] {settlement.LabelCap} [{faction.Name}/{techLevel}]: " + $"'{roleDefName}' → '{d.roleDefName}' T{tier}(emitter) → T{d.tier}(auto) — 소유권 변경({text}). " + "슬롯이 비어 다음 주기에 새 위협 발신 거점이 나온다.");
	}

	private void MaybeGrowTier(StrongholdData d, Settlement settlement)
	{
		if (d == null || settlement == null || d.tierSource != "auto")
		{
			return;
		}
		int num = settlement.Faction?.loadID ?? (-1);
		bool flag = d.lastFactionLoadId != -1 && d.lastFactionLoadId != num;
		d.lastFactionLoadId = num;
		if (EESPuppetUtility.IsPuppetOutpost(settlement))
		{
			if (flag)
			{
				ResetTierOnPuppetCapture(d, settlement);
			}
			GrowPuppetTier(d, settlement);
			return;
		}
		int ticksGame = Find.TickManager.TicksGame;
		if (!flag && d.lastGrowthTick >= 0 && ticksGame - d.lastGrowthTick < 2500)
		{
			return;
		}
		d.lastGrowthTick = ticksGame;
		int penaltyBeforeRecovery = d.warTierPenalty;
		TickGeneralTierPenalty(d, settlement);
		int num2 = StrongholdScoreUtility.ComputeTier(settlement);
		bool flag2 = WorldDominationCompat.TakesOver(settlement);
		int num3 = ((flag2 || flag) ? num2 : Mathf.Max(num2, d.tier + penaltyBeforeRecovery));
		int num4 = ((d.warTierPenalty > 0) ? Mathf.Max(1, num3 - d.warTierPenalty) : num3);
		if (!flag && num4 == d.tier)
		{
			EESLog.Message(string.Format("[성장체크] {0} | 현재 T{1} vs 계산 T{2} → 변화 없음{3}", StrongholdScoreUtility.TierBreakdown(settlement), d.tier, num2, flag2 ? "(WD 양방향)" : "") + ((d.warTierPenalty > 0) ? $" [전쟁 페널티 -{d.warTierPenalty} · 기준 T{num3} · 다음 회복 {RecoverDaysLeft(d)}일]" : ""));
			return;
		}
		int tier = d.tier;
		d.tier = num4;
		if (d.roleForced && !EESPuppetUtility.RoleStillValid(settlement, d.tier, d.roleDefName))
		{
			EESLog.Message($"[역할] {settlement.LabelCap}: 지정 역할 '{d.roleDefName}' 이(가) T{d.tier} 에서 성립 불가 → 지정 해제·재산정.");
			d.roleForced = false;
		}
		if (!FakeOutpostUtility.IsFake(d.roleDefName) && !d.roleForced && EESPuppetUtility.ShouldRerollAutoRole(settlement, d.roleDefName))
		{
			d.roleDefName = StrongholdScoreUtility.ComputeRole(settlement, d.tier);
		}
		StrongholdFeatureUtility.EnsureAssigned(d, settlement, this);
		StrongholdScoreUtility.ApplyDerived(d, settlement);
		CommanderUtility.DecideCommander(d, settlement);
		StrongholdScoreUtility.DecideFacilities(d, settlement);
		GarrisonGroupUtility.DecidePlan(d, settlement);
		EESLog.Message($"[티어변경] {StrongholdScoreUtility.TierBreakdown(settlement)} | T{tier}→T{d.tier} (역할·지휘관·시설·그룹 재결정)" + (flag ? string.Format(" [세력변경 · 새 소유 세력 기준 계산 T{0} 적용{1}]", num2, (d.tier < tier) ? "(하락)" : ((d.tier > tier) ? "(상승)" : "(동률)")) : "") + (flag2 ? " [WD]" : "") + ((d.warTierPenalty > 0) ? $" [전쟁 페널티 -{d.warTierPenalty} · 기준 T{num3} · 하한 T{1} · 다음 회복 {RecoverDaysLeft(d)}일]" : ""));
	}

	public static int GeneralRecoverTicks(int tier)
	{
		return Mathf.Max(1, Mathf.RoundToInt((float)EESPuppetUtility.DaysToNextTier(tier) * 0.4f)) * 60000;
	}

	private static int RecoverDaysLeft(StrongholdData d)
	{
		if (d == null || d.warTierPenalty <= 0 || d.warTierRecoverTick < 0)
		{
			return -1;
		}
		int num = d.warTierRecoverTick - Find.TickManager.TicksGame;
		if (num > 0)
		{
			return num / 60000;
		}
		return 0;
	}

	private void TickGeneralTierPenalty(StrongholdData d, Settlement settlement)
	{
		if (d == null || d.warTierPenalty <= 0)
		{
			return;
		}
		int ticksGame = Find.TickManager.TicksGame;
		if (d.warTierRecoverTick < 0)
		{
			d.warTierRecoverTick = ticksGame + GeneralRecoverTicks(d.tier);
			EESLog.Message("[전초 전쟁페널티] " + settlement?.LabelCap + ": 회복 시계 미설정 → 지금부터 " + $"{GeneralRecoverTicks(d.tier) / 60000}일(남은 페널티 -{d.warTierPenalty}).");
			return;
		}
		int warTierPenalty = d.warTierPenalty;
		int num = 0;
		while (d.warTierPenalty > 0 && ticksGame >= d.warTierRecoverTick)
		{
			d.warTierPenalty--;
			num++;
			if (d.warTierPenalty <= 0)
			{
				d.warTierRecoverTick = -1;
				break;
			}
			d.warTierRecoverTick += GeneralRecoverTicks(d.tier + num);
		}
		if (num > 0)
		{
			EESLog.Message("[전초 전쟁페널티] " + settlement?.LabelCap + " [" + settlement?.Faction?.Name + "]: " + $"회복 {num}단계(-{warTierPenalty} → -{d.warTierPenalty}) | 현재 T{d.tier} | " + "다음 회복 " + ((d.warTierPenalty > 0) ? (RecoverDaysLeft(d) + "일") : "없음(전량 회복)") + " | " + $"가속 x{0.4f}");
		}
	}

	public int LowerGeneralTier(StrongholdData d, Settlement settlement, int steps)
	{
		if (d == null || settlement == null)
		{
			return 0;
		}
		steps = Mathf.Max(1, steps);
		int tier = d.tier;
		int num = Mathf.Min(steps, tier - 1);
		if (num <= 0)
		{
			return 0;
		}
		d.tier = tier - num;
		d.warTierPenalty += num;
		d.warTierRecoverTick = Find.TickManager.TicksGame + GeneralRecoverTicks(d.tier);
		if (d.roleForced && !EESPuppetUtility.RoleStillValid(settlement, d.tier, d.roleDefName))
		{
			EESLog.Message($"[전초 전쟁페널티] {settlement.LabelCap}: 지정 역할 '{d.roleDefName}' 이(가) T{d.tier} 에서 성립 불가 → 지정 해제·재산정.");
			d.roleForced = false;
		}
		if (!FakeOutpostUtility.IsFake(d.roleDefName) && !d.roleForced && EESPuppetUtility.ShouldRerollAutoRole(settlement, d.roleDefName))
		{
			d.roleDefName = StrongholdScoreUtility.ComputeRole(settlement, d.tier);
		}
		StrongholdFeatureUtility.EnsureAssigned(d, settlement, this);
		StrongholdScoreUtility.ApplyDerived(d, settlement);
		CommanderUtility.DecideCommander(d, settlement);
		StrongholdScoreUtility.DecideFacilities(d, settlement);
		GarrisonGroupUtility.DecidePlan(d, settlement);
		return num;
	}

	public int ClearGeneralTierPenalty(StrongholdData d)
	{
		if (d == null || d.warTierPenalty <= 0)
		{
			return 0;
		}
		int warTierPenalty = d.warTierPenalty;
		d.warTierPenalty = 0;
		d.warTierRecoverTick = -1;
		d.lastGrowthTick = -1;
		return warTierPenalty;
	}

	private void GrowPuppetTier(StrongholdData d, Settlement settlement)
	{
		int ticksGame = Find.TickManager.TicksGame;
		int num = EESPuppetUtility.CapFor(settlement);
		if (d.intelLevel < 1)
		{
			d.intelLevel = 1;
			EESLog.Message("[괴뢰국] " + settlement.LabelCap + ": 우리 전초기지 → 정보 전량 공개(정찰 불요).");
		}
		if (d.lastCap < 0)
		{
			d.lastCap = num;
		}
		else
		{
			if (num > d.lastCap)
			{
				d.lastCap = num;
				d.tierClockTick = ticksGame;
				EESLog.Message($"[괴뢰국] {settlement.LabelCap}: 시대 승격 감지 → 천장 T{num}. 성장 시계 재시작 " + $"(다음 T{d.tier + 1}까지 {EESPuppetUtility.DaysToNextTier(d.tier)}일).");
				return;
			}
			if (num < d.lastCap)
			{
				EESLog.Message($"[괴뢰국] {settlement.LabelCap}: 천장 하향 T{d.lastCap}→T{num}(티어는 유지 T{d.tier}).");
				d.lastCap = num;
			}
		}
		if (d.tierClockTick < 0)
		{
			d.tierClockTick = ticksGame;
		}
		else
		{
			if (d.tier >= num)
			{
				return;
			}
			int num2 = 0;
			int tier = d.tier;
			while (d.tier < num)
			{
				int num3 = EESPuppetUtility.TicksToNextTier(d.tier);
				if (ticksGame - d.tierClockTick < num3)
				{
					break;
				}
				d.tierClockTick += num3;
				d.tier++;
				num2++;
			}
			if (num2 != 0)
			{
				if (d.tier >= num)
				{
					d.tierClockTick = ticksGame;
				}
				ApplyPuppetTierChange(d, settlement);
				EESLog.Message($"[괴뢰국 티어변경] {settlement.LabelCap}: T{tier}→T{d.tier} (천장 T{num}) " + "| 다음 " + ((d.tier >= num) ? "없음(천장·시계 정지)" : (EESPuppetUtility.DaysToNextTier(d.tier) + "일")));
			}
		}
	}

	public void RaisePuppetTierPaid(StrongholdData d, Settlement settlement)
	{
		if (d != null && settlement != null)
		{
			int num = EESPuppetUtility.CapFor(settlement);
			if (d.tier < num)
			{
				int tier = d.tier;
				d.tier++;
				d.tierClockTick = Find.TickManager.TicksGame;
				d.lastCap = num;
				ApplyPuppetTierChange(d, settlement);
				EESLog.Message($"[괴뢰국 티어구매] {settlement.LabelCap}: T{tier}→T{d.tier} (천장 T{num}, 은 {EESPuppetUtility.UpgradeCost(tier)}).");
			}
		}
	}

	private void ResetTierOnPuppetCapture(StrongholdData d, Settlement settlement)
	{
		int tier = d.tier;
		int ticksGame = Find.TickManager.TicksGame;
		d.tier = 1;
		d.tierClockTick = ticksGame;
		d.lastCap = EESPuppetUtility.CapFor(settlement);
		ApplyPuppetTierChange(d, settlement, d.tier < tier);
		EESLog.Message("[괴뢰국 접수] " + settlement.LabelCap + " [" + settlement.Faction?.Name + "]: 소유권 변경 감지 → " + $"T{tier}→T{d.tier} · 성장 시계 0 부터(유예 없음) · 천장 T{d.lastCap} · " + $"다음 T{d.tier + 1}까지 {EESPuppetUtility.DaysToNextTier(d.tier)}일.");
	}

	public int LowerPuppetTier(StrongholdData d, Settlement settlement, int steps)
	{
		if (d == null || settlement == null)
		{
			return 0;
		}
		steps = Mathf.Max(1, steps);
		int tier = d.tier;
		int num = Mathf.Min(steps, tier - 1);
		if (num <= 0)
		{
			return 0;
		}
		d.tier = tier - num;
		int ticksGame = Find.TickManager.TicksGame;
		int num2 = num * EESPuppetUtility.TicksToNextTier(d.tier);
		d.tierClockTick = ticksGame + num2;
		d.lastCap = EESPuppetUtility.CapFor(settlement);
		ApplyPuppetTierChange(d, settlement, downgraded: true);
		EESLog.Message($"[괴뢰국 티어하향] {settlement.LabelCap}: T{tier}→T{d.tier} " + $"(요청 {steps}단계 · 실제 {num}단계 · 하한 T{1}) | " + $"성장 시계 재시작 + 유예 {num2 / 60000}일 → " + $"자동 복구까지 총 {(num2 + EESPuppetUtility.TicksToNextTier(d.tier)) / 60000}일 " + $"(공칭 {EESPuppetUtility.DaysToNextTier(d.tier)}일) | 은 환불 없음");
		return num;
	}

	private void ApplyPuppetTierChange(StrongholdData d, Settlement settlement, bool downgraded = false)
	{
		if (d.roleForced && !EESPuppetUtility.RoleStillValid(settlement, d.tier, d.roleDefName))
		{
			EESLog.Message($"[괴뢰국] {settlement.LabelCap}: 지정 역할 '{d.roleDefName}' 이(가) T{d.tier} 에서 성립 불가 → 지정 해제·재산정.");
			d.roleForced = false;
		}
		bool flag = downgraded && !d.roleForced && !FakeOutpostUtility.IsFake(d.roleDefName) && EESPuppetUtility.RoleBelowMinTier(d.tier, d.roleDefName);
		if (flag)
		{
			EESLog.Message($"[괴뢰국] {settlement.LabelCap}: 자동 역할 '{d.roleDefName}' 이(가) T{d.tier} 하한 미달 → 재산정(하향).");
		}
		if (!d.roleForced && !FakeOutpostUtility.IsFake(d.roleDefName) && (flag || EESPuppetUtility.ShouldRerollAutoRole(settlement, d.roleDefName)))
		{
			string roleDefName = d.roleDefName;
			d.roleDefName = StrongholdScoreUtility.ComputeRole(settlement, d.tier);
			if (roleDefName != d.roleDefName)
			{
				EESLog.Message("[괴뢰국] " + settlement.LabelCap + ": 역할 재산정 '" + roleDefName + "' → '" + d.roleDefName + "'.");
			}
		}
		StrongholdFeatureUtility.EnsureAssigned(d, settlement, this);
		StrongholdScoreUtility.ApplyDerived(d, settlement);
		CommanderUtility.DecideCommander(d, settlement);
		StrongholdScoreUtility.DecideFacilities(d, settlement);
		GarrisonGroupUtility.DecidePlan(d, settlement);
	}

	public void ForceGrowCheck(StrongholdData d, Settlement settlement)
	{
		if (d != null)
		{
			d.lastGrowthTick = -1;
			MaybeGrowTier(d, settlement);
		}
	}

	public StrongholdData GetExisting(int settlementId)
	{
		if (!dataById.TryGetValue(settlementId, out var value))
		{
			return null;
		}
		return value;
	}

	public Settlement GetSettlement(int settlementId)
	{
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			if (settlements[i].ID == settlementId)
			{
				return settlements[i];
			}
		}
		return null;
	}

	public override void WorldComponentTick()
	{
		base.WorldComponentTick();
		int ticksGame = Find.TickManager.TicksGame;
		if (assaultOps.Count > 0 && (lastAssaultTick < 0 || ticksGame - lastAssaultTick >= 60))
		{
			lastAssaultTick = ticksGame;
			for (int num = assaultOps.Count - 1; num >= 0; num--)
			{
				if (!AssaultGroupUtility.TickOp(assaultOps[num], ticksGame))
				{
					assaultOps.RemoveAt(num);
				}
			}
		}
		if (dataById.Count == 0 || (lastAlertDecayTick >= 0 && ticksGame - lastAlertDecayTick < 60000))
		{
			return;
		}
		lastAlertDecayTick = ticksGame;
		foreach (StrongholdData value in dataById.Values)
		{
			AlertUtility.DecayTick(value, ticksGame);
		}
	}

	public void Remove(int settlementId)
	{
		if (dataById.TryGetValue(settlementId, out var removedData)) removedData.finalStandActive = false;
		if (dataById.Remove(settlementId))
		{
			EESLog.Message($"Removed data for settlement id={settlementId}");
		}
	}

	public int ClearAll()
	{
		int count = dataById.Count;
		dataById.Clear();
		finalStandClusters.Clear();
		ClearFinalStandOutcomes();
		finalStandLineage.Clear();
		nextFinalStandClusterId = 1;
		return count;
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref lastAlertDecayTick, "lastAlertDecayTick", -1);
		Scribe_Values.Look(ref lastAssaultTick, "lastAssaultTick", -1);
		Scribe_Values.Look(ref nextFinalStandClusterId, "nextFinalStandClusterId", 1);
		Scribe_Collections.Look(ref dataById, "dataById", LookMode.Value, LookMode.Deep, ref tmpKeys, ref tmpVals);
		Scribe_Collections.Look(ref assaultOps, "assaultOps", LookMode.Deep);
		Scribe_Collections.Look(ref finalStandClusters, "finalStandClusters", LookMode.Deep);
		ExposeFinalStandState();
		Scribe_Collections.Look(ref finalStandLineage, "finalStandLineage", LookMode.Value, LookMode.Value, ref lineageKeys, ref lineageValues);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (dataById == null)
			{
				dataById = new Dictionary<int, StrongholdData>();
			}
			if (assaultOps == null)
			{
				assaultOps = new List<AssaultOp>();
			}
			if (finalStandClusters == null)
			{
				finalStandClusters = new List<FinalStandClusterRecord>();
			}
			if (finalStandLineage == null)
			{
				finalStandLineage = new Dictionary<string, string>();
			}
			if (nextFinalStandClusterId < 1)
			{
				nextFinalStandClusterId = finalStandClusters.Count + 1;
			}
		}
	}
}
