using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(GenStep_Settlement), "ScatterAt")]
public static class Patch_GenStep_Settlement_ScatterAt
{
	public static void Postfix(IntVec3 c, Map map)
	{
		if (map?.Parent is Settlement { Faction: var faction } settlement)
		{
			LogGenmapEnter("ScatterAt", settlement, map, faction);
			if (faction != null && faction != Faction.OfPlayer)
			{
				BuildStronghold(settlement, map, overlay: false);
			}
		}
	}

	public static void LogGenmapEnter(string hook, Settlement settlement, Map map, Faction faction)
	{
		MapComponent_StrongholdSiege mapComponent_StrongholdSiege = map?.GetComponent<MapComponent_StrongholdSiege>();
		bool flag = faction == Faction.OfPlayer;
		string arg = ((faction == null) ? "SKIP(세력 없음)" : (flag ? "SKIP(플레이어 소유 — 바닐라가 RandomEnemyFaction 폴백을 탄다)" : ((mapComponent_StrongholdSiege != null && mapComponent_StrongholdSiege.eesGenerated) ? "SKIP(이미 생성)" : "BUILD")));
		EESLog.Message($"EES genmap enter [{hook}] {settlement?.LabelCap}(타일 {settlement?.Tile}): " + string.Format("faction={0} isPlayer={1} ", faction?.Name ?? "없음", flag) + $"hostile={faction?.HostileTo(Faction.OfPlayer) ?? false} " + $"eesGenerated={mapComponent_StrongholdSiege?.eesGenerated ?? false} -> path={arg}");
	}

	public static void BuildStronghold(Settlement settlement, Map map, bool overlay)
	{
		Faction faction = settlement.Faction;
		if (EESFactionBlacklist.IsBlocked(faction))
		{
			EESLog.Message("BuildStronghold SKIP [" + faction?.Name + "]: 블랙리스트 세력 — EES 미적용.");
			return;
		}
		if (EESCustomBaseDetector.IsForeignSettlement(settlement))
		{
			EESLog.Message("BuildStronghold SKIP [" + settlement?.def?.defName + "]: 커스텀 정착지 객체(RimCities류) — EES 미적용.");
			return;
		}
		MapComponent_StrongholdSiege component = map.GetComponent<MapComponent_StrongholdSiege>();
		if (component != null)
		{
			if (component.eesGenerated)
			{
				return;
			}
			component.eesGenerated = true;
			component.eesStrongholdMap = true;
			component.eesOverlay = overlay;
		}
		WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
		if (instance == null)
		{
			EESLog.Warning("WorldComponent_Strongholds not found during settlement generation.");
			return;
		}
		StrongholdData orCreate = instance.GetOrCreate(settlement);
		orCreate.lastAttackedTick = Find.TickManager.TicksGame;
		orCreate.isOverlayBase = overlay;
		if (RimPactsCompat.PlayerAssaultInProgress(settlement))
		{
			instance.PrepareFinalStandCandidate(settlement);
		}
		if (orCreate.tierSource == "auto" && !FakeOutpostUtility.IsFake(orCreate.roleDefName) && !EESPuppetUtility.IsPuppetOutpost(settlement))
		{
			float mapWealth = StrongholdScoreUtility.MapWealth01(map);
			int num = StrongholdScoreUtility.ComputeTier(settlement, mapWealth);
			if (num != orCreate.tier)
			{
				orCreate.tier = num;
				if (!FakeOutpostUtility.IsFake(orCreate.roleDefName) && !orCreate.roleForced)
				{
					orCreate.roleDefName = StrongholdScoreUtility.ComputeRole(settlement, orCreate.tier);
				}
			}
			StrongholdFeatureUtility.EnsureAssigned(orCreate, settlement, instance);
			StrongholdScoreUtility.ApplyDerived(orCreate, settlement);
			CommanderUtility.DecideCommander(orCreate, settlement);
			GarrisonGroupUtility.DecidePlan(orCreate, settlement);
		}
		if (orCreate.tierSource == "auto" && BattlefieldUtility.IsBattlefield(orCreate.roleDefName) && FactionTerritoriesCompat.HasActiveInvasion(settlement))
		{
			string roleDefName = orCreate.roleDefName;
			orCreate.roleDefName = StrongholdScoreUtility.ComputeRole(settlement, orCreate.tier);
			StrongholdFeatureUtility.EnsureAssigned(orCreate, settlement, instance);
			StrongholdScoreUtility.ApplyDerived(orCreate, settlement);
			StrongholdScoreUtility.DecideFacilities(orCreate, settlement);
			CommanderUtility.DecideCommander(orCreate, settlement);
			GarrisonGroupUtility.DecidePlan(orCreate, settlement);
			EESLog.Message("[FT호환] " + settlement.LabelCap + ": FT 침공 진행 중 → 전장 역할 해제 (" + roleDefName + " → " + orCreate.roleDefName + ") — 4파전 미발동, FT warzone만 진행.");
		}
		AlertUtility.OnAttacked(orCreate, settlement);
		ScoutUtility.ApplyEntryAlertModifier(settlement, orCreate);
		FinalStandUtility.ApplyBeforeGeneration(orCreate, settlement);
		// Create the plan after tier/role resolution, then snapshot native recoverables
		// before any layout or garrison step. FinalizeRewardBudget reuses this set.
		StrongholdZoneUtility.CaptureNativeItemsForLedger(map, StrongholdGenerationPlan.GetOrCreate(map, orCreate));
		EESLog.Message("Generating stronghold map for " + settlement.LabelCap + " " + $"[{faction.Name}, tech={faction.def.techLevel}] overlay={overlay} → {orCreate}");
		orCreate.maxTurretFootprint = StrongholdGarrisonUtility.ComputeMaxTurretFootprint(faction, orCreate);
		if (FakeOutpostUtility.IsFake(orCreate.roleDefName))
		{
			if (component != null)
			{
				component.fakePending = true;
			}
			return;
		}
		bool openClanSettlement = faction?.def?.defName?.StartsWith("VFEM2_Clan", StringComparison.Ordinal) == true ||
			faction?.def?.techLevel == TechLevel.Neolithic || faction?.def?.categoryTag == "Tribal";
		if (EESCustomBaseDetector.IsDedicatedOutpostFaction(faction) && !openClanSettlement)
		{
			CellRect cellRect = StrongholdGarrisonUtility.ComputeBaseRect(map, faction);
			if (cellRect.Width <= 0)
			{
				cellRect = CellRect.CenteredOn(map.Center, Mathf.Clamp(16 + orCreate.tier, 18, 30)).ClipInsideMap(map);
			}
			if (component != null)
			{
				component.eesBaseRect = cellRect;
			}
			StrongholdGarrisonUtility.ReinforceGarrison(map, orCreate, cellRect, mapStructures: false);
			EESLog.Message("Dedicated-outpost REDUCED [" + faction.Name + "]: 맵 구조물 스킵, 병력+지휘관만 (reason=" + EESCustomBaseDetector.DetectReason(faction) + ").");
			return;
		}
		if (EESFactionKind.IsInsect(faction))
		{
			CellRect cellRect2 = StrongholdGarrisonUtility.ComputeBaseRect(map, faction);
			if (cellRect2.Width <= 0)
			{
				cellRect2 = CellRect.CenteredOn(map.Center, Mathf.Clamp(16 + orCreate.tier, 18, 30)).ClipInsideMap(map);
			}
			if (component != null)
			{
				component.eesBaseRect = cellRect2;
			}
			try
			{
				StrongholdLayoutGenerator.InsertVaultOverlay(map, orCreate, cellRect2);
				orCreate.customLayout = true;
			}
			catch (Exception ex)
			{
				EESLog.Error("Insect vault gen failed: " + ex);
			}
			StrongholdGarrisonUtility.ReinforceGarrison(map, orCreate, cellRect2);
			EESLog.Message($"Insect stronghold [{faction.Name} T{orCreate.tier}]: 벽·포탑·지휘관 제거, 금고+곤충병력만 (rect {cellRect2.Width}x{cellRect2.Height}).");
			return;
		}
		CellRect cellRect3 = default(CellRect);
		if (!overlay)
		{
			try
			{
				CellRect rect = StrongholdAreaUtility.ExpandArea(map, orCreate, out var original);
				rect = VacuumMapUtility.ShrinkForVacuum(map, rect);
				cellRect3 = rect;
                bool unifiedCenter = StrongholdCenterPlanner.Eligible(map, orCreate);
                if (unifiedCenter && StrongholdLayoutGenerator.TryBuildUnifiedCenter(map, orCreate, rect))
				{
					cellRect3 = rect;
					orCreate.customLayout = true;
					EESLog.Message("Unified center path completed for " + faction.Name + "; legacy room insertion skipped.");
				}
				if (!unifiedCenter) StrongholdAreaUtility.AddInteriorBuildings(map, original, rect, orCreate);
				if (!unifiedCenter && !VacuumMapUtility.IsVacuum(map))
				{
					StrongholdAreaUtility.BuildPerimeter(map, orCreate, rect);
					StrongholdDefenseUtility.ReinforceOuterStructures(map, orCreate);
				}
				if (!unifiedCenter && ThrumboRanchUtility.IsRanch(orCreate.roleDefName))
				{
					ThrumboRanchGen.InsertRanchCenter(map, orCreate, rect);
				}
				else if (!unifiedCenter)
				{
					StrongholdLayoutGenerator.InsertCustomCenter(map, orCreate, rect);
				}
				if (!unifiedCenter)
				{
					orCreate.customLayout = true;
					StrongholdAreaUtility.ClearTreesAroundWalls(map, orCreate, rect, 7);
					StrongholdAreaUtility.BuildOuterRing(map, orCreate, rect);
					if (CommanderEffects.TrapCommander(orCreate))
					{
						StrongholdTrapUtility.PlaceTraps(map, faction, orCreate, rect, mapWide: true);
					}
					else
					{
						EESLog.Message($"Traps SKIP {faction.Name}: commander type={orCreate.commanderType} (Engineer 아님 → 덫 없음).");
					}
				}
			}
			catch (Exception ex2)
			{
				EESLog.Error("Layout/Area generation failed: " + ex2);
			}
		}
		else
		{
			try
			{
				cellRect3 = StrongholdGarrisonUtility.ComputeBaseRect(map, faction);
				if (cellRect3.Width <= 0)
				{
					int radius = Mathf.Clamp(16 + orCreate.tier, 18, 30);
					cellRect3 = CellRect.CenteredOn(map.Center, radius).ClipInsideMap(map);
					EESLog.Warning($"Overlay [{faction.Name}]: 세력 건물 0(KCSG 생성 실패 추정) → 중앙 {cellRect3.Width}x{cellRect3.Height} 기본 요새 직접 생성.");
				}
				CellRect cellRect4 = cellRect3;
				cellRect3 = VacuumMapUtility.ShrinkForVacuum(map, cellRect3);
				if (cellRect3 != cellRect4)
				{
					EESLog.Message($"Vacuum shrink [{faction.Name}]: {cellRect4.Width}x{cellRect4.Height} → {cellRect3.Width}x{cellRect3.Height} (궤도플랫폼 0.9배).");
				}
                bool unifiedOverlay = StrongholdCenterPlanner.Eligible(map, orCreate);
                if (unifiedOverlay && StrongholdLayoutGenerator.TryBuildUnifiedCenter(map, orCreate, cellRect3))
				{
					orCreate.customLayout = true;
					EESLog.Message("Unified center path completed for overlay " + faction.Name + "; legacy overlay insertion skipped.");
				}
                else if (!unifiedOverlay)
				{
				orCreate.customLayout = true;
				if (!VacuumMapUtility.IsVacuum(map))
				{
					StrongholdAreaUtility.BuildPerimeter(map, orCreate, cellRect3);
				}
				else
				{
					EESLog.Message("Vacuum [" + faction.Name + "]: 외벽(BuildPerimeter) 생략 — 궤도플랫폼은 이미 밀폐. 바리케이드만 배치.");
				}
				StrongholdAreaUtility.ClearTreesAroundWalls(map, orCreate, cellRect3, 7);
				if (ThrumboRanchUtility.IsRanch(orCreate.roleDefName))
				{
					ThrumboRanchGen.InsertRanchCenter(map, orCreate, cellRect3);
				}
				else
				{
					StrongholdLayoutGenerator.InsertVaultOverlay(map, orCreate, cellRect3);
				}
				StrongholdAreaUtility.BuildOuterRing(map, orCreate, cellRect3);
				if (CommanderEffects.TrapCommander(orCreate))
				{
					StrongholdTrapUtility.PlaceTraps(map, faction, orCreate, cellRect3, mapWide: true);
				}
				StrongholdLayoutGenerator.RemoveOrphanWallAttachments(map, cellRect3.ExpandedBy(6));
				EESLog.Message($"Overlay stronghold on custom-layout base [{faction.Name}]: baseRect {cellRect3.Width}x{cellRect3.Height} (둘레벽 보강 포함).");
				}
			}
			catch (Exception ex3)
			{
				EESLog.Error("Overlay generation failed: " + ex3);
			}
		}
		if (component != null)
		{
			cellRect3 = StrongholdCenterPlanner.IncludeCenter(cellRect3, orCreate.generationPlan).ClipInsideMap(map);
			component.eesBaseRect = cellRect3;
			component.openBase = orCreate.tier <= 7;
		}
		var generationPlan = StrongholdGenerationPlan.GetOrCreate(map, orCreate);
		generationPlan?.RecordBase(cellRect3);
        try
        {
            StrongholdLayoutGenerator.BuildTown(map, orCreate, cellRect3);
        }
        catch (Exception exception)
        {
            EESLog.Error("Town generation failed; existing buildings and items retained: " + exception);
        }
        if (generationPlan != null && !generationPlan.centerBuilt && !StrongholdCenterPlanner.Eligible(map, orCreate)) generationPlan.stage = "core-captured";
		try
		{
			if (cellRect3.Width > 0 && !VacuumMapUtility.IsVacuum(map))
			{
				StrongholdAreaUtility.ResealPerimeter(map, orCreate, cellRect3);
			}
		}
		catch (Exception ex4)
		{
			EESLog.Error("ResealPerimeter(common) failed: " + ex4);
		}
		try
		{
			StrongholdGarrisonUtility.ReinforceGarrison(map, orCreate, cellRect3);
		}
		catch (Exception ex5)
		{
			EESLog.Error("ReinforceGarrison failed: " + ex5);
		}
		if (ThrumboRanchUtility.IsRanch(orCreate.roleDefName))
		{
			try
			{
				ThrumboRanchGen.PopulateThrumbos(map, orCreate, cellRect3);
			}
			catch (Exception ex6)
			{
				EESLog.Error("ThrumboRanch PopulateThrumbos failed: " + ex6);
			}
		}
		if (BattlefieldUtility.IsBattlefield(orCreate.roleDefName))
		{
			if (component != null)
			{
				component.isBattlefield = true;
			}
			EESLog.Message($"Battlefield 표식 [{faction.Name} T{orCreate.tier}]: 진입 후 4파전 스케줄 가동 예정(C/D 선택은 진입 시).");
		}
		if (PollutedOutpostUtility.IsPolluted(orCreate.roleDefName))
		{
			if (component != null)
			{
				component.isPolluted = true;
			}
			try
			{
				PollutedOutpostUtility.ApplyPollutedEffects(map, orCreate, faction);
			}
			catch (Exception ex7)
			{
				EESLog.Error("PollutedOutpost ApplyPollutedEffects failed: " + ex7);
			}
		}
		try
		{
			CommanderEffects.ApplyGenerationEffects(map, orCreate, faction);
		}
		catch (Exception ex8)
		{
			EESLog.Error("Commander generation effects failed: " + ex8);
		}
		try
		{
			if (cellRect3.Width > 0 && !VacuumMapUtility.IsVacuum(map))
			{
				StrongholdReachability.EnsureReachable(map, orCreate, cellRect3.ExpandedBy(2), $"{faction?.Name} T{orCreate?.tier}");
			}
			else if (VacuumMapUtility.IsVacuum(map))
			{
				EESLog.Message("Reachability SKIP [" + faction?.Name + "]: 진공/궤도플랫폼(외벽 없음·이미 밀폐).");
			}
		}
		catch (Exception ex9)
		{
			EESLog.Error("Reachability check failed: " + ex9);
		}
		try
		{
			if (cellRect3.Width > 0)
			{
				StrongholdLayoutGenerator.RemoveOrphanWallAttachments(map, cellRect3.ExpandedBy(6));
			}
		}
		catch (Exception ex10)
		{
			EESLog.Error("RemoveOrphanWallAttachments(common) failed: " + ex10);
		}
		component?.RepairTurretManningCells();
		// FillVault runs while the unified center is still being assembled. Re-scan after
		// town, garrison, commander effects, and reachability have completed so role-room
		// and other baseRect items enter the same ledger before generation returns.
		if (generationPlan != null && generationPlan.vaultRect.Width > 0)
		{
			StrongholdZoneUtility.FinalizeRewardBudget(map, orCreate, generationPlan.vaultRect);
		}
	}
}
