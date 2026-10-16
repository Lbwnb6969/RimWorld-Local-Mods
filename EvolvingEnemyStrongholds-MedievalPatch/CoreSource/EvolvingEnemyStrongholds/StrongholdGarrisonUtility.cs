using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public static class StrongholdGarrisonUtility
{
	private static readonly int[] PopulationBudgetByTier = new int[14]
	{
		0, 10, 15, 19, 24, 30, 36, 42, 48, 54, 60, 66, 72, 78
	};

	private static int activePopulationBudget = -1;


	public static int PopulationLimit(int tier)
	{
		int clamped = Mathf.Clamp(tier, 1, 16);
		if (clamped >= 14) return clamped == 14 ? 96 : clamped == 15 ? 116 : 138;
		return PopulationBudgetByTier[clamped];
	}

	private static int activePopulationUsed;

	private static int activePopulationReserved;

	private static int activePopulationNormalLimit;

	private struct TurretCand
	{
		public ThingDef def;

		public float strength;

		public float norm;
	}

	private const float RationDays = 5f;

	private const float HumanHungerPerDay = 1.6f;

	private const int PwHopelessThreshold = 20;

	private static readonly Dictionary<ThingDef, int> _pwRejects = new Dictionary<ThingDef, int>();

	private static readonly HashSet<ThingDef> _pwHopeless = new HashSet<ThingDef>();

	private static HashSet<ModContentPack> _identityMods;

	private static Dictionary<ModContentPack, string> _modRaceLabels;

	private static readonly int[] DirectCountPerTier = new int[14]
	{
		0, 0, 2, 3, 4, 6, 8, 10, 13, 15,
		17, 19, 21, 25
	};

	private static readonly int[] MortarCountPerTier = new int[14]
	{
		0, 0, 0, 0, 0, 0, 1, 1, 2, 2,
		3, 3, 4, 5
	};

	private const int RocketMinTier = 10;

	private const int ForcedMortarTier = 12;

	public const float OverlayTurretMul = 1.4f;

	private const float DirectLowFrac = 0.45f;

	private const float DirectHighFrac = 0.2f;

	private const float MortarLowFrac = 0.5f;

	private const float MortarHighFrac = 0.2f;

	private static bool kcsgGenExtChecked;

	private static Type kcsgGenExtType;

	private static readonly Dictionary<string, IntRange> TurretTierOverride = new Dictionary<string, IntRange>
	{
		{
			"CE_Turret_OrganGun",
			new IntRange(2, 6)
		},
		{
			"CE_Turret_TwelvePounder",
			new IntRange(2, 6)
		},
		{
			"CE_Turret_12PounderBombard",
			new IntRange(2, 6)
		},
		{
			"CE_Turret_GatlingGun",
			new IntRange(5, 9)
		},
		{
			"CE_Turret_ShotgunTurret",
			new IntRange(5, 9)
		},
		{
			"Turret_Medium",
			new IntRange(5, 9)
		},
		{
			"CE_Turret_Vickers",
			new IntRange(6, 10)
		},
		{
			"Turret_MiniTurret",
			new IntRange(6, 10)
		},
		{
			"CE_Turret_M1919Browning",
			new IntRange(7, 11)
		},
		{
			"Turret_M240B",
			new IntRange(7, 11)
		},
		{
			"CE_Turret_PKM",
			new IntRange(8, 12)
		},
		{
			"Turret_Autocannon",
			new IntRange(8, 12)
		},
		{
			"Turret_KPV",
			new IntRange(9, 13)
		},
		{
			"CE_Turret_M2HB",
			new IntRange(9, 13)
		},
		{
			"Turret_Heavy",
			new IntRange(9, 13)
		},
		{
			"CE_Turret_MkNineteenGL",
			new IntRange(9, 13)
		},
		{
			"Turret_AGSThirty",
			new IntRange(9, 13)
		},
		{
			"Turret_Flak",
			new IntRange(9, 13)
		},
		{
			"CE_Turret_SPGNine",
			new IntRange(10, 13)
		},
		{
			"Turret_Sniper",
			new IntRange(10, 13)
		},
		{
			"Turret_Blaster",
			new IntRange(11, 13)
		},
		{
			"Turret_Rocketswarm",
			new IntRange(12, 13)
		},
		{
			"CE_Turret_PortableMortar",
			new IntRange(6, 10)
		}
	};

	private const int ElectricMinTier = 7;

	private const float StrengthFavor = 1.5f;

	private const int FuelReloadStock = 1;

	private static readonly string[] ShipInfraCompClassNames = new string[2] { "SaveOurShip2.CompShipHeat", "SaveOurShip2.CompSpinalMount" };

	private const string RimatomicsEnergyWeaponClassName = "Rimatomics.Building_EnergyWeapon";

	private static List<ThingDef> _configurableTurrets;

	private static bool MortarsAllowed
	{
		get
		{
			if (StrongholdMod.Settings != null)
			{
				return StrongholdMod.Settings.allowMortars;
			}
			return true;
		}
	}

	private static Type KcsgGenExtType
	{
		get
		{
			if (!kcsgGenExtChecked)
			{
				kcsgGenExtChecked = true;
				kcsgGenExtType = GenTypes.GetTypeInAnyAssembly("KCSG.CustomGenOption");
			}
			return kcsgGenExtType;
		}
	}

	public static void ReinforceGarrison(Map map, StrongholdData data, CellRect rectOverride = default(CellRect), bool mapStructures = true)
	{
		if (map == null || data == null)
		{
			return;
		}
		Faction parentFaction = map.ParentFaction;
		if (parentFaction == null || parentFaction == Faction.OfPlayer)
		{
			return;
		}
		CellRect cellRect = ((rectOverride.Width > 0) ? rectOverride : GetSettlementRect(map));
		if (cellRect.Width <= 0)
		{
			EESLog.Warning("Garrison: no SettlementRect; skip.");
			return;
		}
		var previousPawnIds = new HashSet<int>(StrongholdPawnOwnership.Population(map, parentFaction).Select(p => p.thingIDNumber));
		BeginPopulationBudget(map, parentFaction, data, mapStructures);
		try
		{
			// Allocate the actual specialist positions before ordinary defenders.
			// Estimated crew reservations undercount overlay turrets and loaders;
			// filling infantry first otherwise leaves generated defenses unmanned.
			int num2 = MechRelayUtility.SpawnRelayMechs(map, parentFaction, data, cellRect);
			int num3 = CryoBarracksUtility.FillReserve(map, parentFaction, data, cellRect);
			Pawn pawn = CommanderUtility.SpawnCommander(map, data);
			int num4 = 0;
			int num5 = 0;
			if (mapStructures)
			{
				num5 = RemoveVanillaEdgeTurrets(map, parentFaction, data);
				num4 = AddTurrets(map, parentFaction, data, cellRect);
			}
			activePopulationReserved = 0;
			activePopulationNormalLimit = activePopulationBudget;
			int num = AddReinforcements(map, parentFaction, data, cellRect);
			EESLog.Message($"Garrison: {parentFaction.Name} T{data.tier} mil={data.militaryPower} -> " + string.Format("extraPawns={0} mechs={1} cryoReserve={2} turrets={3} commander={4} ", num, num2, num3, num4, (pawn != null) ? "yes" : "no") + $"(removedVanillaMini={num5}, mapStructures={mapStructures}).");
			CECompat.ApplyCeGarrison(map, parentFaction, data, pawn);
			StrongholdFeatureEffects.ApplyToNewGarrison(map, parentFaction, data, previousPawnIds);
			if (mapStructures)
			{
				EvictPawnsFromSealedRooms(map, parentFaction, data, cellRect, "생성");
			}
		}
		finally
		{
			EndPopulationBudget(parentFaction, data);
		}
	}

	private static void BeginPopulationBudget(Map map, Faction faction, StrongholdData data, bool mapStructures)
	{
		int tier = Mathf.Clamp(data?.tier ?? 1, 1, 16);
		int turretTier = Mathf.Min(tier, 13);
		activePopulationBudget = PopulationLimit(tier) + FinalStandUtility.PopulationBonus(data);
		// Native defenders and other active hostile groups consume the same cap.
		// Preserve them even when a third-party template already exceeds it.
		activePopulationUsed = StrongholdPawnOwnership.Population(map, faction, FinalStandUtility.Active(data)).Count;
		int commander = data?.hasCommander == true ? 1 : 0;
		int mech = 0;
		if (map?.Parent is Settlement settlement && data?.roleDefName == "MechanoidRelay")
		{
			mech = MechRelayUtility.EstimateMechCount(settlement, data);
		}
		int cryo = data?.roleDefName == "CryoBarracks" ? Mathf.Clamp(Mathf.RoundToInt((data.militaryPower * 0.35f) / 140f), 0, 12) : 0;
		int turretTarget = mapStructures ? Mathf.RoundToInt(DirectCountPerTier[turretTier] * (StrongholdMod.Settings?.turretDensity ?? 1f) * 0.75f * FinalStandUtility.TurretMultiplier(data)) : 0;
		activePopulationReserved = Mathf.Min(Mathf.Max(0, activePopulationBudget - activePopulationUsed), commander + mech + cryo + turretTarget);
		activePopulationNormalLimit = Mathf.Max(activePopulationUsed, activePopulationBudget - activePopulationReserved);
		EESLog.Message($"Garrison population budget [{faction?.Name} T{tier}]: cap={activePopulationBudget}, normal={activePopulationNormalLimit}, reserved={activePopulationReserved} (commander={commander}, mech={mech}, cryo={cryo}, turretCrew={turretTarget}).");
	}

	private static void EndPopulationBudget(Faction faction, StrongholdData data)
	{
		if (activePopulationBudget >= 0)
		{
			EESLog.Message($"Garrison population used [{faction?.Name} T{data?.tier}]: {activePopulationUsed}/{activePopulationBudget} (unused={activePopulationBudget - activePopulationUsed}).");
		}
		activePopulationBudget = -1;
		activePopulationUsed = 0;
		activePopulationReserved = 0;
		activePopulationNormalLimit = 0;
	}

	internal static bool TryConsumePopulationSlot(bool special) => TryConsumePopulationSlots(1, special);

	private static bool TryConsumePopulationSlots(int count, bool special)
	{
		if (activePopulationBudget < 0)
		{
			return true;
		}
		int limit = special ? activePopulationBudget : activePopulationNormalLimit;
		if (activePopulationUsed + count > limit)
		{
			return false;
		}
		activePopulationUsed += count;
		return true;
	}

	internal static void ReleasePopulationSlot()
	{
		if (activePopulationBudget >= 0 && activePopulationUsed > 0)
		{
			activePopulationUsed--;
		}
	}

	public static (int inside, int moved) EvictPawnsFromSealedRooms(Map map, Faction faction, StrongholdData data, CellRect baseRect, string phase)
	{
		if (map == null || faction == null || data?.sealedRects == null || data.sealedRects.Count == 0)
		{
			return (inside: 0, moved: 0);
		}
		int num = 0;
		int num2 = 0;
		for (int i = 0; i < data.sealedRects.Count; i++)
		{
			string label = ((data.sealedRectKinds != null && i < data.sealedRectKinds.Count) ? data.sealedRectKinds[i] : "봉인방");
			(int inside, int moved) tuple = EvictFromRect(map, faction, data.sealedRects[i], baseRect, label, phase);
			int item = tuple.inside;
			int item2 = tuple.moved;
			num += item;
			num2 += item2;
		}
		return (inside: num, moved: num2);
	}

	public static (int trapped, int moved) FreeTrappedFactionPawns(Map map, Faction faction)
	{
		if (map == null || faction == null)
		{
			return (trapped: 0, moved: 0);
		}
		List<Pawn> list = new List<Pawn>();
		foreach (Pawn item in map.mapPawns.SpawnedPawnsInFaction(faction))
		{
			if (item != null && !item.Dead && !item.IsPrisoner)
			{
				Room room = item.Position.GetRoom(map);
				if (room != null && !room.TouchesMapEdge)
				{
					list.Add(item);
				}
			}
		}
		int num = 0;
		foreach (Pawn item2 in list)
		{
			if (CellFinder.TryFindRandomCellInsideWith(CellRect.WholeMap(map), (IntVec3 c) => c.InBounds(map) && c.Standable(map) && !c.Fogged(map) && c.Walkable(map) && c.GetRoom(map) != null && c.GetRoom(map).TouchesMapEdge, out var result))
			{
				item2.Position = result;
				item2.Notify_Teleported(endCurrentJob: false);
				num++;
			}
		}
		if (list.Count > 0)
		{
			EESLog.Message($"Garrison free-trapped [{faction.Name}]: 갇힘 {list.Count}명 중 {num}명 구제(맵 가장자리 연결 방으로).");
		}
		return (trapped: list.Count, moved: num);
	}

	private static (int inside, int moved) EvictFromRect(Map map, Faction faction, CellRect v, CellRect baseRect, string label, string phase)
	{
		if (v.Width <= 0 || v.Height <= 0)
		{
			return (inside: 0, moved: 0);
		}
		List<Pawn> list = new List<Pawn>();
		foreach (Pawn item in map.mapPawns.SpawnedPawnsInFaction(faction))
		{
			if (item != null && !item.Dead && v.Contains(item.Position) && !item.IsPrisoner)
			{
				list.Add(item);
			}
		}
		int num = 0;
		foreach (Pawn item2 in list)
		{
			if (CellFinder.TryFindRandomCellInsideWith(baseRect, (IntVec3 c) => c.InBounds(map) && !v.Contains(c) && c.Standable(map) && !c.Fogged(map) && c.Walkable(map), out var result))
			{
				item2.Position = result;
				item2.Notify_Teleported(endCurrentJob: false);
				num++;
			}
		}
		if (list.Count > 0)
		{
			EESLog.Message("Garrison seal-evict[" + phase + "] [" + faction.Name + "] " + label + ": " + $"{num}/{list.Count} 명 봉인 방 {v} 밖으로 재배치(실패 {list.Count - num}명).");
		}
		return (inside: list.Count, moved: num);
	}

	private static int AddReinforcements(Map map, Faction faction, StrongholdData data, CellRect rect)
	{
		if (data.militaryPower <= 0)
		{
			return 0;
		}
		float num = (SupplyCaravanUtility.SupplyCutCombatActive(data) ? 0.75f : 1f);
		float num2 = (CryoBarracksUtility.IsCryo(data.roleDefName) ? 0.65f : 1f);
		PawnGroupMakerParms parms = new PawnGroupMakerParms
		{
			tile = map.Tile,
			faction = faction,
			groupKind = PawnGroupKindDefOf.Combat,
			points = (float)data.militaryPower * num * num2,
			inhabitants = true,
			seed = Gen.HashCombineInt(data.settlementId, 111288656)
		};
		List<Pawn> list;
		try
		{
			list = PawnGroupMakerUtility.GeneratePawns(parms, warnOnZeroResults: false).ToList();
		}
		catch (Exception ex)
		{
			EESLog.Error("Garrison GeneratePawns failed: " + ex.Message);
			return 0;
		}
		if (list.Count == 0)
		{
			return 0;
		}
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		List<Pawn> list2 = new List<Pawn>();
		foreach (Pawn item in list)
		{
			int people = StrongholdPawnOwnership.GroupSize(item);
			if (!TryConsumePopulationSlots(people, special: false))
			{
				DiscardUnspawnedPawn(item);
				continue;
			}
			if (!TryFindSpawnCell(map, rect, out var cell))
			{
				for (int slot = 0; slot < people; slot++) ReleasePopulationSlot();
				DiscardUnspawnedPawn(item);
				continue;
			}
			try
			{
				GenSpawn.Spawn(item, cell, map);
			}
			catch (Exception ex)
			{
				for (int slot = 0; slot < people; slot++) ReleasePopulationSlot();
				DiscardUnspawnedPawn(item);
				EESLog.Warning("Garrison pawn spawn failed: " + ex.Message);
				continue;
			}
			foreach (var member in StrongholdPawnOwnership.Group(item))
			{
				int num6 = GiveSiegeRations(member);
				if (num6 > 0) { num4 += num6; num5++; }
			}
			list2.Add(item);
			num3++;
		}
		EESLog.Message($"Garrison rations [{faction.Name}]: {num5}/{list2.Sum(StrongholdPawnOwnership.GroupSize)}명에 비상식량 총 {num4}개 지급 " + $"(목표 {Mathf.Max(1, Mathf.RoundToInt(8.888889f))}개/명, 별개로 바닐라 랜덤식량도 보유).");
		if (list2.Count > 0)
		{
			int assaultDelay = (BattlefieldUtility.IsBattlefield(data.roleDefName) ? 2000000000 : 25000);
			if (data.groupPlanDecided && GarrisonGroupUtility.IsApplicable(faction, data))
			{
				GarrisonGroupUtility.ApplyGroups(map, faction, data, rect, list2);
			}
			else
			{
				AssignDispersedDefense(map, faction, rect, list2, assaultDelay);
			}
		}
		return num3;
	}

	internal static int GiveSiegeRations(Pawn p)
	{
		if (p?.inventory == null || p.RaceProps == null || !p.RaceProps.Humanlike)
		{
			return 0;
		}
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("MealSurvivalPack");
		if (namedSilentFail == null || namedSilentFail.GetStatValueAbstract(StatDefOf.Nutrition) <= 0f)
		{
			namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("Pemmican");
		}
		if (namedSilentFail == null || namedSilentFail.GetStatValueAbstract(StatDefOf.Nutrition) <= 0f)
		{
			return 0;
		}
		float statValueAbstract = namedSilentFail.GetStatValueAbstract(StatDefOf.Nutrition);
		int num = Mathf.Max(1, Mathf.RoundToInt(8f / statValueAbstract));
		int num2 = 0;
		int num3 = 0;
		while (num > 0 && num3++ < 32)
		{
			int num4 = Mathf.Min(num, namedSilentFail.stackLimit);
			Thing thing = ThingMaker.MakeThing(namedSilentFail);
			thing.stackCount = num4;
			if (!p.inventory.innerContainer.TryAdd(thing))
			{
				if (!thing.Destroyed)
				{
					thing.Destroy();
				}
				break;
			}
			num2 += num4;
			num -= num4;
		}
		return num2;
	}

	public static int RelordDefendersNoSally(Map map, Faction faction, CellRect rect)
	{
		if (map == null || faction == null)
		{
			return 0;
		}
		List<Pawn> list = new List<Pawn>();
		List<Pawn> list2 = new List<Pawn>();
		foreach (Pawn item in map.mapPawns.SpawnedPawnsInFaction(faction))
		{
			if (item == null || item.Dead || !item.RaceProps.Humanlike)
			{
				continue;
			}
			Lord lord = item.GetLord();
			if (lord != null && lord.LordJob is LordJob_ManTurrets)
			{
				if (!(lord.LordJob is LordJob_ManTurrets_NoFlee))
				{
					list2.Add(item);
				}
			}
			else
			{
				list.Add(item);
			}
		}
		if (list2.Count > 0)
		{
			for (int i = 0; i < list2.Count; i++)
			{
				list2[i].GetLord()?.RemovePawn(list2[i]);
			}
			LordMaker.MakeNewLord(faction, new LordJob_ManTurrets_NoFlee(), map, list2);
		}
		if (list.Count > 0)
		{
			for (int j = 0; j < list.Count; j++)
			{
				list[j].GetLord()?.RemovePawn(list[j]);
			}
			AssignDispersedDefense(map, faction, rect, list, 2000000000, noFlee: true);
		}
		if (EESLog.Verbose)
		{
			LogGunnerState(map, faction, "RelordDefendersNoSally 직후");
		}
		return list.Count;
	}

	public static void LogGunnerState(Map map, Faction faction, string context)
	{
		if (map == null || faction == null)
		{
			return;
		}
		try
		{
			List<Thing> list = map.listerThings.ThingsInGroup(ThingRequestGroup.Shell);
			List<Pawn> list2 = new List<Pawn>();
			foreach (Pawn item in map.mapPawns.SpawnedPawnsInFaction(faction))
			{
				if (item != null && !item.Dead && item.GetLord()?.LordJob is LordJob_ManTurrets)
				{
					list2.Add(item);
				}
			}
			int num = 0;
			int num2 = 0;
			StringBuilder stringBuilder = new StringBuilder();
			foreach (Building item2 in map.listerBuildings.allBuildingsNonColonist)
			{
				if (item2 == null || item2.Destroyed || item2.Faction != faction || !item2.def.hasInteractionCell || !HasCompOrSubclass(item2.def, typeof(CompMannable)))
				{
					continue;
				}
				num++;
				bool flag = item2.TryGetComp<CompMannable>()?.MannedNow ?? false;
				if (flag)
				{
					num2++;
				}
				IntVec3 interactionCell = item2.InteractionCell;
				Building_TurretGun building_TurretGun = item2 as Building_TurretGun;
				bool flag2 = item2.def.HasComp(typeof(CompMannable));
				Building building = (interactionCell.InBounds(map) ? interactionCell.GetEdifice(map) : null);
				string text = ((!interactionCell.InBounds(map)) ? "OOB" : ((building != null) ? ((building == item2) ? "자기footprint" : (building.def.defName ?? "")) : (interactionCell.Standable(map) ? "빈칸standable" : "비standable지형")));
				CompRefuelable compRefuelable = item2.TryGetComp<CompRefuelable>();
				string text2 = ((compRefuelable == null) ? "none" : (compRefuelable.HasFuel ? $"{compRefuelable.Fuel:0}/{compRefuelable.Props.fuelCapacity:0}" : "EMPTY"));
				int num3 = 0;
				for (int i = 0; i < list.Count; i++)
				{
					if ((list[i].Position - item2.Position).LengthHorizontal <= 40f)
					{
						num3++;
					}
				}
				Pawn pawn = null;
				float num4 = float.MaxValue;
				for (int j = 0; j < list2.Count; j++)
				{
					float num5 = (list2[j].Position - item2.Position).LengthHorizontalSquared;
					if (num5 < num4)
					{
						num4 = num5;
						pawn = list2[j];
					}
				}
				string text3;
				if (pawn == null)
				{
					text3 = "사수0명";
				}
				else
				{
					bool flag3 = map.reservationManager.CanReserve(pawn, item2);
					bool flag4 = map.reachability.CanReach(pawn.Position, item2, PathEndMode.InteractionCell, TraverseParms.For(pawn));
					bool flag5 = building_TurretGun != null && JobDriver_ManTurret.FindAmmoForTurret(pawn, building_TurretGun) != null;
					bool flag6 = compRefuelable == null || compRefuelable.HasFuel || (building_TurretGun != null && JobDriver_ManTurret.FindFuelForTurret(pawn, building_TurretGun) != null);
					bool flag7 = flag2 && flag3 && flag4 && flag5 && flag6;
					text3 = string.Format("VALIDATOR={0} [근접사수 {1} d{2:0} canReserve={3} reachIC={4} ammoOk={5} fuelOk={6}]", flag7 ? "PASS" : "FAIL", pawn.LabelShort, Mathf.Sqrt(num4), flag3, flag4, flag5, flag6);
				}
				stringBuilder.Append($"\n   - {item2.def.defName} @{item2.Position} rot={item2.Rotation.ToStringHuman()} manned={flag} | IC{interactionCell}={text} compMannableExact={flag2} fuel={text2} shells40={num3} | {text3}");
			}
			StringBuilder stringBuilder2 = new StringBuilder();
			for (int k = 0; k < list2.Count; k++)
			{
				Pawn pawn2 = list2[k];
				string text4 = pawn2.CurJob?.def?.defName ?? "none";
				string text5 = pawn2.mindState?.duty?.def?.defName ?? "none";
				string text6 = ((pawn2.mindState?.duty != null) ? pawn2.mindState.duty.focus.Cell.ToString() : "-");
				string text7 = ((pawn2.CurJob?.targetA.Thing != null) ? $"{pawn2.CurJob.targetA.Thing.def.defName}@{pawn2.CurJob.targetA.Thing.Position}" : "-");
				stringBuilder2.Append($"\n   - {pawn2.LabelShort} @{pawn2.Position} lord={pawn2.GetLord()?.LordJob?.GetType().Name} duty={text5} focus={text6} job={text4} target={text7}");
			}
			EESLog.Message($"[사수진단:{context}] {faction.Name}: 사수포탑 {num2}/{num} manned, 사수폰 {list2.Count}명, 맵Shell총 {list.Count}개.{stringBuilder}{stringBuilder2}");
		}
		catch (Exception arg)
		{
			EESLog.Warning($"LogGunnerState 실패: {arg}");
		}
	}

	internal static void AssignDispersedDefense(Map map, Faction faction, CellRect rect, List<Pawn> pawns, int assaultDelay = 25000, bool noFlee = false)
	{
		List<IntVec3> list = DefendPoints(rect);
		int count = list.Count;
		List<List<Pawn>> list2 = new List<List<Pawn>>();
		for (int i = 0; i < count; i++)
		{
			list2.Add(new List<Pawn>());
		}
		for (int j = 0; j < pawns.Count; j++)
		{
			list2[j % count].Add(pawns[j]);
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int k = 0; k < count; k++)
		{
			if (list2[k].Count != 0)
			{
				LordJob_DefendBase lordJob = (noFlee ? ((LordJob_DefendBase)new LordJob_DefendBase_NoFlee(faction, list[k], assaultDelay)) : ((LordJob_DefendBase)new LordJob_EESDefendBaseWide(faction, list[k], assaultDelay)));
				LordMaker.MakeNewLord(faction, lordJob, map, list2[k]);
				stringBuilder.Append($"({list[k].x},{list[k].z})={list2[k].Count} ");
			}
		}
		EESLog.Message($"Garrison dispersed [{faction.Name}]: {pawns.Count}명 → {count}거점(rect {rect.Width}x{rect.Height} @ {rect.CenterCell}). 거점별: {stringBuilder}");
	}

	private static List<IntVec3> DefendPoints(CellRect rect)
	{
		int num = Mathf.Clamp(Mathf.Min(rect.Width, rect.Height) / 8, 2, 6);
		int x = rect.CenterCell.x;
		int z = rect.CenterCell.z;
		int newX = rect.minX + num;
		int newX2 = rect.maxX - num;
		int newZ = rect.minZ + num;
		int newZ2 = rect.maxZ - num;
		return new List<IntVec3>
		{
			rect.CenterCell,
			new IntVec3(x, 0, newZ2),
			new IntVec3(x, 0, newZ),
			new IntVec3(newX2, 0, z),
			new IntVec3(newX, 0, z),
			new IntVec3(newX2, 0, newZ2),
			new IntVec3(newX, 0, newZ2),
			new IntVec3(newX2, 0, newZ),
			new IntVec3(newX, 0, newZ)
		};
	}

	internal static bool TryFindSpawnCell(Map map, CellRect rect, out IntVec3 cell)
	{
		bool vac = VacuumMapUtility.IsVacuum(map);
		return CellFinder.TryFindRandomCellInsideWith(rect, (IntVec3 c) => c.InBounds(map) && c.Standable(map) && (vac || !c.Fogged(map)), out cell);
	}

	public static void DiscardUnspawnedPawn(Pawn p)
	{
		StrongholdPawnOwnership.ReturnUnused(p);
	}

	private static bool IsMortarTurret(ThingDef d)
	{
		if (d.building != null)
		{
			return d.building.IsMortar;
		}
		return false;
	}

	private static bool IsModFaction(Faction faction)
	{
		ModContentPack modContentPack = faction?.def?.modContentPack;
		if (modContentPack != null)
		{
			return !modContentPack.IsOfficialMod;
		}
		return false;
	}

	private static bool IsCEMod(ModContentPack m)
	{
		if (m == null)
		{
			return false;
		}
		string text = (m.Name ?? "").ToLowerInvariant();
		string text2 = (m.PackageId ?? "").ToLowerInvariant();
		if (!text.Contains("combat extended") && !text.Contains("ce guns") && !text.Contains("ce armory") && !text.Contains("ce melee") && !text2.Contains("ceteam"))
		{
			return text2.Contains("combatextended");
		}
		return true;
	}

	private static bool IsEmpOnlyTurret(ThingDef turretDef)
	{
		ThingDef thingDef = turretDef.building?.turretGunDef;
		if (thingDef?.Verbs == null)
		{
			return false;
		}
		for (int i = 0; i < thingDef.Verbs.Count; i++)
		{
			DamageDef damageDef = (thingDef.Verbs[i]?.defaultProjectile)?.projectile?.damageDef;
			if (damageDef != null && damageDef != DamageDefOf.EMP)
			{
				return false;
			}
		}
		for (int j = 0; j < thingDef.Verbs.Count; j++)
		{
			if (thingDef.Verbs[j]?.defaultProjectile?.projectile?.damageDef == DamageDefOf.EMP)
			{
				return true;
			}
		}
		return false;
	}

	private static bool AttackerHasMech(Map map)
	{
		List<Caravan> caravans = Find.WorldObjects.Caravans;
		for (int i = 0; i < caravans.Count; i++)
		{
			Caravan caravan = caravans[i];
			if (caravan == null || caravan.Faction != Faction.OfPlayer || caravan.Tile != map.Tile)
			{
				continue;
			}
			List<Pawn> pawnsListForReading = caravan.PawnsListForReading;
			for (int j = 0; j < pawnsListForReading.Count; j++)
			{
				Pawn pawn = pawnsListForReading[j];
				if (pawn?.RaceProps != null && pawn.RaceProps.IsMechanoid)
				{
					return true;
				}
			}
		}
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int k = 0; k < allPawnsSpawned.Count; k++)
		{
			Pawn pawn2 = allPawnsSpawned[k];
			if (pawn2?.RaceProps != null && pawn2.RaceProps.IsMechanoid && pawn2.Faction == Faction.OfPlayer)
			{
				return true;
			}
		}
		return false;
	}

	internal static void ResetPlaceWorkerCache()
	{
		_pwRejects.Clear();
		_pwHopeless.Clear();
	}

	private static bool PlaceWorkersAllow(ThingDef def, IntVec3 c, Map map)
	{
		if (def == null || map == null)
		{
			return true;
		}
		ModContentPack modContentPack = def.modContentPack;
		if (modContentPack == null || modContentPack.IsOfficialMod)
		{
			return true;
		}
		List<PlaceWorker> placeWorkers;
		try
		{
			placeWorkers = def.PlaceWorkers;
		}
		catch
		{
			return true;
		}
		if (placeWorkers == null || placeWorkers.Count == 0)
		{
			return true;
		}
		if (_pwHopeless.Contains(def))
		{
			return false;
		}
		for (int i = 0; i < placeWorkers.Count; i++)
		{
			AcceptanceReport acceptanceReport;
			try
			{
				acceptanceReport = placeWorkers[i].AllowsPlacing(def, c, Rot4.North, map);
			}
			catch (Exception ex)
			{
				EESLog.Warning("PlaceWorker 예외 무시: " + def.defName + " / " + placeWorkers[i].GetType().Name + " → " + ex.GetType().Name);
				continue;
			}
			if (!acceptanceReport.Accepted)
			{
				_pwRejects.TryGetValue(def, out var value);
				value++;
				_pwRejects[def] = value;
				if (value == 20)
				{
					_pwHopeless.Add(def);
					EESLog.Message("포탑 배치 제외(PlaceWorker 전역 거부): " + def.defName + " [" + modContentPack.Name + "] worker=" + placeWorkers[i].GetType().Name + " 사유='" + acceptanceReport.Reason + "' " + $"— {20}회 연속 거부라 이 맵에서 후보에서 뺍니다.");
				}
				return false;
			}
		}
		return true;
	}

	private static bool TryFindTurretCell(Map map, CellRect rect, bool inner, int side, ThingDef def, IntVec3 baseCenter, int outerOffset, out IntVec3 cell)
	{
		IntVec2 size = def.size;
		int val = Math.Max(size.x, size.z);
		int dist = Mathf.Clamp(Math.Max(Mathf.Min(rect.Width, rect.Height) / 4, val), 1, Math.Max(4, val));
		CellRect innerRect = rect.ContractedBy(dist);
		if (inner)
		{
			if (CellFinder.TryFindRandomCellInsideWith(rect, (IntVec3 c) => Base(c) && innerRect.Contains(c), out cell))
			{
				return true;
			}
		}
		else
		{
			CellRect cellRect = rect.ExpandedBy(outerOffset - 1);
			if (side >= 0 && CellFinder.TryFindRandomCellInsideWith(cellRect, (IntVec3 c) => Base(c) && !rect.Contains(c) && OnSide(c, rect, side), out cell))
			{
				return true;
			}
			if (CellFinder.TryFindRandomCellInsideWith(cellRect, (IntVec3 c) => Base(c) && !rect.Contains(c), out cell))
			{
				return true;
			}
			if (CellFinder.TryFindRandomCellInsideWith(rect, (IntVec3 c) => Base(c) && !innerRect.Contains(c), out cell))
			{
				return true;
			}
		}
		return CellFinder.TryFindRandomCellInsideWith(rect, Base, out cell);
		bool Base(IntVec3 c)
		{
			if (TurretFootprintFree(map, c, size) && !RootRoofed(map, c, size) && MannableSpotOk(map, c, def, baseCenter))
			{
				return PlaceWorkersAllow(def, c, map);
			}
			return false;
		}
	}

	private static bool OnSide(IntVec3 c, CellRect rect, int side)
	{
		bool flag = c.z >= rect.maxZ;
		bool flag2 = c.z <= rect.minZ;
		bool flag3 = c.x >= rect.maxX;
		bool flag4 = c.x <= rect.minX;
		return side switch
		{
			0 => flag,
			1 => flag2,
			2 => flag3,
			3 => flag4,
			4 => flag && flag3,
			5 => flag && flag4,
			6 => flag2 && flag3,
			7 => flag2 && flag4,
			_ => true,
		};
	}

	private static bool TurretFootprintFree(Map map, IntVec3 center, IntVec2 size)
	{
		foreach (IntVec3 item in GenAdj.OccupiedRect(center, Rot4.North, size))
		{
			if (!item.InBounds(map) || item.GetEdifice(map) != null || !item.Standable(map))
			{
				return false;
			}
		}
		return true;
	}

	private static bool RootRoofed(Map map, IntVec3 center, IntVec2 size)
	{
		foreach (IntVec3 item in GenAdj.OccupiedRect(center, Rot4.North, size))
		{
			if (item.InBounds(map) && item.Roofed(map))
			{
				return true;
			}
		}
		return false;
	}

	private static int RemoveVanillaEdgeTurrets(Map map, Faction faction, StrongholdData data)
	{
		List<Thing> list = new List<Thing>();
		ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("Turret_MiniTurret");
		if (namedSilentFail != null)
		{
			foreach (Thing item in map.listerThings.ThingsOfDef(namedSilentFail))
			{
				if (item.Faction == faction)
				{
					list.Add(item);
				}
			}
		}
		int count = list.Count;
		List<Building> allBuildingsNonColonist = map.listerBuildings.allBuildingsNonColonist;
		for (int i = 0; i < allBuildingsNonColonist.Count; i++)
		{
			Building building = allBuildingsNonColonist[i];
			if (building != null && !building.Destroyed && building.Faction == faction && IsMortarTurret(building.def))
			{
				list.Add(building);
			}
		}
		int num = list.Count - count;
		foreach (Thing item2 in list)
		{
			try
			{
				if (!item2.Destroyed)
				{
					item2.Destroy();
				}
			}
			catch
			{
			}
		}
		EESLog.Message($"RemoveVanilla [{faction.Name}]: 미니포탑 {count} + 바닐라 박격포 {num} 제거 " + $"(hasMortarBattery={data?.hasMortarBattery} → 박격포는 이때만 AddTurrets가 재배치).");
		return list.Count;
	}

	internal static int AddTurrets(Map map, Faction faction, StrongholdData data, CellRect rect, bool inert = false)
	{
		if (EESFactionKind.IsInsect(faction))
		{
			return 0;
		}
		ResetPlaceWorkerCache();
		TechLevel tech = (TechLevel)Mathf.Max((int)(faction.def?.techLevel ?? TechLevel.Industrial), (int)MaxTechForTier(data.tier));
		BuildTurretPool(faction, tech, out var ownPool, out var officialPool);
		if (ownPool.Count == 0 && officialPool.Count == 0)
		{
			return 0;
		}
		bool flag = AttackerHasMech(map);
		int num = ownPool.Count((TurretCand c) => IsEmpOnlyTurret(c.def)) + officialPool.Count((TurretCand c) => IsEmpOnlyTurret(c.def));
		if (!flag && num > 0)
		{
			ownPool = ownPool.Where((TurretCand c) => !IsEmpOnlyTurret(c.def)).ToList();
			officialPool = officialPool.Where((TurretCand c) => !IsEmpOnlyTurret(c.def)).ToList();
		}
		EESLog.Message($"Garrison EMP filter [{faction.Name}]: attackerHasMech={flag} empTurretsInPool={num} " + string.Format("-> {0}. ownAfter={1} officialAfter={2}", flag ? "kept(공격측 메카)" : "removed(공격측 메카없음)", ownPool.Count, officialPool.Count));
		if (ownPool.Count == 0 && officialPool.Count == 0)
		{
			return 0;
		}
		List<TurretCand> list = ownPool.Where((TurretCand c) => IsMortarTurret(c.def)).ToList();
		List<TurretCand> list2 = ownPool.Where((TurretCand c) => !IsMortarTurret(c.def)).ToList();
		List<TurretCand> list3 = officialPool.Where((TurretCand c) => IsMortarTurret(c.def)).ToList();
		List<TurretCand> list4 = officialPool.Where((TurretCand c) => !IsMortarTurret(c.def)).ToList();
		bool flag2 = list.Count > 0;
		bool flag3 = list2.Count > 0;
		bool flag4 = ownPool.Count > 0 || IsModFaction(faction);
		ThingDef mortar105 = Identify105mm(list3);
		List<TurretCand> pool = (flag2 ? list : (flag4 ? list3.Where((TurretCand c) => c.def != mortar105).ToList() : list3));
		List<TurretCand> source = (flag3 ? list2 : list4);
		int tier = data.tier;
		pool = FilterByTierWindow(pool, tier, 0.5f, 0.2f, !flag2);
		ThingDef thingDef = ((!flag4 && tier >= 12 && mortar105 != null && pool.Any((TurretCand c) => c.def == mortar105)) ? mortar105 : null);
		if (!data.hasMortarBattery || !MortarsAllowed)
		{
			pool = new List<TurretCand>();
			thingDef = null;
		}
		List<TurretCand> collection = source.Where((TurretCand c) => HasCompOrSubclass(c.def, typeof(CompInteractable))).ToList();
		List<TurretCand> collection2 = FilterByTierWindow(source.Where((TurretCand c) => !HasCompOrSubclass(c.def, typeof(CompInteractable))).ToList(), tier, 0.45f, 0.2f, !flag3);
		if (!flag3 && tier < 10)
		{
			collection = new List<TurretCand>();
		}
		source = new List<TurretCand>(collection2);
		source.AddRange(collection);
		EESLog.Message($"Garrison turret cats [{faction.Name} T{data.tier}]: " + string.Format("direct={0}({1})[{2}] ", source.Count, flag3 ? "own" : "vanilla", string.Join(",", source.Select((TurretCand c) => c.def.defName))) + string.Format("mortar={0}({1})[{2}]", pool.Count, flag2 ? "own" : (flag4 ? "vanilla-no105" : "vanilla"), string.Join(",", pool.Select((TurretCand c) => c.def.defName))) + ((thingDef != null) ? (" forced105=" + thingDef.defName) : ((mortar105 != null) ? (" 105=" + mortar105.defName) : "")) + ".");
		float num2 = (StrongholdMod.Settings?.turretDensity ?? 1f) * (data.isOverlayBase ? 1.4f : 1f);
		num2 *= FinalStandUtility.TurretMultiplier(data);
		int num3 = Mathf.RoundToInt((float)DirectCountPerTier[Mathf.Clamp(tier, 1, 13)] * num2);
		int num4 = ((pool.Count > 0) ? Mathf.RoundToInt((float)MortarCountPerTier[Mathf.Clamp(tier, 1, 13)] * num2) : 0);
		if (BattlefieldUtility.IsBattlefield(data.roleDefName))
		{
			num3 = Mathf.Max(0, num3 - 2);
		}
		if (data.sabotagedTurrets > 0 || data.sabotagedMortars > 0)
		{
			float num5 = (data.isOverlayBase ? 1.4f : 1f);
			int num6 = Mathf.Min(Mathf.RoundToInt((float)data.sabotagedTurrets * num5), num3);
			int num7 = Mathf.Min(Mathf.RoundToInt((float)data.sabotagedMortars * num5), num4);
			num3 = Mathf.Max(0, num3 - num6);
			num4 = Mathf.Max(0, num4 - num7);
			EESLog.Message($"Garrison sabotage [{faction.Name} T{data.tier}]: direct-{num6} mortar-{num7} (요청 직사{data.sabotagedTurrets}/박격포{data.sabotagedMortars}, overlayMul={num5:0.0}).");
		}
		if (SupplyCaravanUtility.SupplyCutCombatActive(data))
		{
			num3 = Mathf.Max(0, num3 - 2);
			num4 = Mathf.Max(0, num4 - 1);
			EESLog.Message($"Garrison supply-cut [{faction.Name} T{data.tier}]: direct-{2} mortar-{1} (만료틱 {data.supplyCutUntilTick}).");
		}
		if (source.Count == 0)
		{
			num4 = Mathf.Max(num4, num3);
			num3 = 0;
		}
		int num8 = num3 + num4;
		if (num8 <= 0)
		{
			return 0;
		}
		int num9 = num4;
		float tierBias = Mathf.Clamp01(((float)tier - 1f) / 12f);
		NormalizeStrength(pool);
		NormalizeStrength(source);
		Rand.PushState(Gen.HashCombineInt(data.settlementId, 8266471));
		int num10 = 0;
		Dictionary<ThingDef, int> dictionary = new Dictionary<ThingDef, int>();
		List<Thing> list5 = new List<Thing>();
		try
		{
			int num11 = 0;
			IntVec3 centerCell = rect.CenterCell;
			int outerOffset = StrongholdAreaUtility.OuterRingOffsetFor(data);
			bool flag5 = false;
			for (int num12 = 0; num12 < num8; num12++)
			{
				bool flag6 = num12 < num9 && pool.Count > 0;
				List<TurretCand> list6 = (flag6 ? pool : ((source.Count > 0) ? source : pool));
				if (list6.Count == 0)
				{
					break;
				}
				ThingDef thingDef2;
				if (flag6 && !flag5 && thingDef != null)
				{
					thingDef2 = thingDef;
					flag5 = true;
				}
				else
				{
					thingDef2 = PickWeightedCapped(list6, tierBias, mortar105, dictionary);
				}
				if (thingDef2 == null)
				{
					continue;
				}
				int num13 = (flag6 ? (-1) : (num11++ % 8));
				if (!TryFindTurretCell(map, rect, flag6, num13, thingDef2, centerCell, outerOffset, out var cell))
				{
					continue;
				}
				try
				{
					ThingDef stuff = (thingDef2.MadeFromStuff ? GenStuff.DefaultStuffFor(thingDef2) : null);
					Thing thing = ThingMaker.MakeThing(thingDef2, stuff);
					thing.SetFactionDirect(faction);
					Rot4 rot = InteriorFacingRotation(thingDef2, cell, centerCell, map, faction);
					GenSpawn.Spawn(thing, cell, map, rot);
					StrongholdFeatureEffects.RegisterGeneratedTurret(thing);
					if (inert)
					{
						SetTurretPowered(thing, on: false);
					}
					else
					{
						ActivateTurret(thing, centerCell, data.tier);
						if (HasCompOrSubclass(thing.def, typeof(CompMannable)))
						{
							ClearManningCell(thing, map, faction);
							list5.Add(thing);
						}
					}
					if (thingDef2.size.x > 1 || thingDef2.size.z > 1)
					{
						IntVec3 intVec = (thingDef2.hasInteractionCell ? thing.InteractionCell : IntVec3.Invalid);
						string text = ((!intVec.IsValid || !intVec.InBounds(map)) ? "none" : (intVec.Standable(map) ? "standable" : "BLOCKED"));
						EESLog.Message($"Turret place [{thingDef2.defName} {thingDef2.size.x}x{thingDef2.size.z}] @ {cell} rot={rot} side={num13} | " + string.Format("manned={0} interactCell={1}({2}) | 위치={3}", HasCompOrSubclass(thing.def, typeof(CompMannable)), intVec, text, rect.Contains(cell) ? "내부" : "외곽"));
					}
					num10++;
					dictionary.TryGetValue(thingDef2, out var value);
					dictionary[thingDef2] = value + 1;
				}
				catch (Exception ex)
				{
					EESLog.Error("Garrison turret spawn " + thingDef2.defName + ": " + ex.Message);
					break;
				}
			}
			int num14 = ((!inert) ? SpawnGunners(map, faction, list5, centerCell) : 0);
			if (list5.Count > 0)
			{
				EESLog.Message($"Garrison gunners [{faction.Name}]: {num14} for {list5.Count} manned turret(s).");
			}
		}
		finally
		{
			Rand.PopState();
		}
		if (dictionary.Count > 0)
		{
			string text2 = string.Join(", ", dictionary.Select((KeyValuePair<ThingDef, int> kv) => $"{kv.Key.defName}×{kv.Value}"));
			EESLog.Message($"Garrison turrets [{faction.Name} T{data.tier} target direct={num3} mortar={num4}]: {text2}");
		}
		return num10;
	}

	private static int SpawnGunners(Map map, Faction faction, List<Thing> mannableTurrets, IntVec3 baseCenter)
	{
		if (mannableTurrets.Count == 0)
		{
			return 0;
		}
		List<Pawn> list = new List<Pawn>();
		foreach (Thing mannableTurret in mannableTurrets)
		{
			if (!TryFindManningCell(mannableTurret, map, baseCenter, out var cell))
			{
				EESLog.Warning($"Garrison: no gunner cell for {mannableTurret.def.defName} @ {mannableTurret.Position}");
				continue;
			}
			if (!TryConsumePopulationSlot(special: true))
			{
				EESLog.Warning($"Garrison: population budget exhausted; no gunner for {mannableTurret.def.defName} @ {mannableTurret.Position}");
				continue;
			}
			try
			{
				Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(faction.RandomPawnKind(), faction, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: false, allowDead: false, allowDowned: false, canGeneratePawnRelations: true, mustBeCapableOfViolence: true, 1f, forceAddFreeWarmLayerIfNeeded: false, allowGay: true, allowPregnant: false, allowFood: true, allowAddictions: true, inhabitant: true));
				GenSpawn.Spawn(pawn, cell, map);
				list.Add(pawn);
			}
			catch (Exception ex)
			{
				ReleasePopulationSlot();
				EESLog.Error("Garrison gunner spawn failed: " + ex);
			}
		}
		if (list.Count > 0)
		{
			LordMaker.MakeNewLord(faction, new LordJob_ManTurrets(), map, list);
		}
		return list.Count;
	}

	private static void SpawnTurretLoader(Thing turret, IntVec3 baseCenter)
	{
		Faction faction = turret.Faction;
		Map map = turret.Map;
		if (faction == null || map == null)
		{
			return;
		}
		if (!TryFindManningCell(turret, map, baseCenter, out var cell))
		{
			EESLog.Warning($"Garrison loader [{faction.Name}] for {turret.def.defName} @ {turret.Position}: 배치 셀 없음 → 사수 없음.");
			return;
		}
		if (!TryConsumePopulationSlot(special: true))
		{
			EESLog.Warning($"Garrison loader [{faction.Name}] budget exhausted for {turret.def.defName} @ {turret.Position}.");
			return;
		}
		try
		{
			Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(faction.RandomPawnKind(), faction, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: false, allowDead: false, allowDowned: false, canGeneratePawnRelations: true, mustBeCapableOfViolence: true, 1f, forceAddFreeWarmLayerIfNeeded: false, allowGay: true, allowPregnant: false, allowFood: true, allowAddictions: true, inhabitant: true));
			GenSpawn.Spawn(pawn, cell, map);
			Current.Game?.GetComponent<GameComponent_TurretAI>()?.RegisterLoader(pawn, turret);
			EESLog.Message($"Garrison loader [{faction.Name}] {pawn.LabelShort} @ {cell} for {turret.def.defName} " + $"(turret@{turret.Position} base@{baseCenter} 장전병 상주).");
		}
		catch (Exception ex)
		{
			ReleasePopulationSlot();
			EESLog.Error("Garrison loader spawn failed: " + ex);
		}
	}

	private static bool TryFindManningCell(Thing turret, Map map, IntVec3 baseCenter, out IntVec3 cell)
	{
		if (turret.def.hasInteractionCell)
		{
			IntVec3 interactionCell = turret.InteractionCell;
			if (interactionCell.InBounds(map) && interactionCell.Standable(map) && interactionCell.GetFirstPawn(map) == null)
			{
				cell = interactionCell;
				return true;
			}
		}
		return TryFindTurretSideCell(turret, baseCenter, avoidEdifice: false, out cell);
	}

	public static int ComputeMaxTurretFootprint(Faction faction, StrongholdData data)
	{
		if (faction == null || data == null)
		{
			return 2;
		}
		try
		{
			TechLevel tech = (TechLevel)Mathf.Max((int)(faction.def?.techLevel ?? TechLevel.Industrial), (int)MaxTechForTier(data.tier));
			BuildTurretPool(faction, tech, out var ownPool, out var officialPool);
			int num = 2;
			foreach (TurretCand item in ownPool)
			{
				if (item.def != null)
				{
					num = Math.Max(num, Math.Max(item.def.size.x, item.def.size.z));
				}
			}
			foreach (TurretCand item2 in officialPool)
			{
				if (item2.def != null)
				{
					num = Math.Max(num, Math.Max(item2.def.size.x, item2.def.size.z));
				}
			}
			return num;
		}
		catch (Exception ex)
		{
			EESLog.Warning("ComputeMaxTurretFootprint failed: " + ex.Message);
			return 2;
		}
	}

	private static void BuildTurretPool(Faction faction, TechLevel tech, out List<TurretCand> ownPool, out List<TurretCand> officialPool)
	{
		HashSet<ModContentPack> hashSet = FactionModSet(faction);
		HashSet<ModContentPack> hashSet2 = IdentityMods();
		List<TurretCand> list = new List<TurretCand>();
		List<TurretCand> list2 = new List<TurretCand>();
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		List<ThingDef> allDefsListForReading = DefDatabase<ThingDef>.AllDefsListForReading;
		for (int i = 0; i < allDefsListForReading.Count; i++)
		{
			ThingDef thingDef = allDefsListForReading[i];
			if (EESTurretBlacklist.IsBlocked(thingDef))
			{
				continue;
			}
			ModContentPack modContentPack = thingDef.modContentPack;
			bool flag = CECompat.Active && (CECompat.IsCeAmmoTurretDef(thingDef) || IsCEMod(modContentPack)) && thingDef.thingClass != null && typeof(Building_Turret).IsAssignableFrom(thingDef.thingClass);
			if (!IsAutoTurret(thingDef, tech))
			{
				if (flag)
				{
					num3++;
				}
				continue;
			}
			TurretCand item = new TurretCand
			{
				def = thingDef,
				strength = TurretStrength(thingDef)
			};
			ModContentPack modContentPack2 = thingDef.modContentPack;
			bool flag2 = modContentPack2?.IsOfficialMod ?? true;
			if (!flag2 && hashSet.Contains(modContentPack2))
			{
				list.Add(item);
			}
			else if (MedievalOverhaulCompat.IsEmpireOwnTurret(faction, modContentPack2))
			{
				list.Add(item);
			}
			else if (flag2)
			{
				list2.Add(item);
			}
			else if (!hashSet2.Contains(modContentPack2))
			{
				list2.Add(item);
				if (CECompat.Active && (CECompat.IsCeAmmoTurretDef(thingDef) || IsCEMod(modContentPack2)))
				{
					num++;
				}
				else
				{
					num2++;
				}
			}
		}
		string arg = ((hashSet.Count > 0) ? string.Join("+", hashSet.Select((ModContentPack x) => x.Name)) : "none");
		ownPool = list;
		officialPool = list2;
		EESLog.Message($"Garrison turret pool [{faction.Name} mods={arg} tech={tech}]: " + string.Format("own={0}({1}) ", list.Count, string.Join(",", list.Select((TurretCand c) => c.def.defName))) + $"official={list2.Count} (ceArsenalAdded={num}, modAdded={num2}(CE외 포탑모드 자동티어링), ceExcludedByAuto={num3} ←techLevel>{tech}/건설불가 등).");
	}

	public static void DumpTurretTiers(Faction faction)
	{
		if (faction != null)
		{
			TechLevel techLevel = faction.def?.techLevel ?? TechLevel.Industrial;
			TechLevel techLevel2 = (TechLevel)Mathf.Max((int)techLevel, (int)MaxTechForTier(13));
			BuildTurretPool(faction, techLevel2, out var ownPool, out var officialPool);
			EESLog.Message($"===== Turret tier table: {faction.Name} [poolTechCeil={techLevel2}, factionTech={techLevel}] (표시 티어 T1~T13) =====");
			DumpPoolTiers("OWN", ownPool, own: true);
			DumpPoolTiers("OFFICIAL+CE", officialPool, own: false);
			EESLog.Message($"(표시 티어 T1~13 개별 실제 필터 시뮬. 로켓탄막=T{10}+ / 박격포=MortarCountPerTier>0 티어. 종족 own 은 tech하한·윈도우 면제. 직사수/박격포수=Per-Tier 표.)");
		}
	}

	private static void DumpPoolTiers(string label, List<TurretCand> pool, bool own)
	{
		if (pool == null || pool.Count == 0)
		{
			EESLog.Message("[" + label + "] (none)");
			return;
		}
		List<TurretCand> source = pool.Where((TurretCand c) => !IsMortarTurret(c.def)).ToList();
		List<TurretCand> pool2 = pool.Where((TurretCand c) => IsMortarTurret(c.def)).ToList();
		HashSet<ThingDef> ai = new HashSet<ThingDef>(from c in source
			where HasCompOrSubclass(c.def, typeof(CompInteractable))
			select c.def);
		Dictionary<ThingDef, List<int>> appear = new Dictionary<ThingDef, List<int>>();
		for (int num = 1; num <= 13; num++)
		{
			foreach (TurretCand item in FilterByTierWindow(source.Where((TurretCand c) => !ai.Contains(c.def)).ToList(), num, 0.45f, 0.2f, !own))
			{
				Mark(item.def, num);
			}
			if (own || num >= 10)
			{
				foreach (ThingDef item2 in ai)
				{
					Mark(item2, num);
				}
			}
			if (MortarCountPerTier[num] <= 0)
			{
				continue;
			}
			foreach (TurretCand item3 in FilterByTierWindow(pool2, num, 0.5f, 0.2f, !own))
			{
				Mark(item3.def, num);
			}
		}
		foreach (TurretCand item4 in pool.OrderByDescending((TurretCand x) => x.strength))
		{
			appear.TryGetValue(item4.def, out var value);
			string text = ((value != null && value.Count > 0) ? $"T{value.Min()}~T{value.Max()}" : "안나옴");
			string text2 = (IsMortarTurret(item4.def) ? "박격포" : (ai.Contains(item4.def) ? "로켓/AI" : "직사"));
			EESLog.Message($"[{label}] {item4.def.defName} ({text2}) tech={item4.def.techLevel} str={item4.strength:0} -> {text}");
		}
		void Mark(ThingDef d, int t)
		{
			if (!appear.TryGetValue(d, out var value2))
			{
				value2 = new List<int>();
				appear[d] = value2;
			}
			value2.Add(t);
		}
	}

	private static HashSet<ModContentPack> IdentityMods()
	{
		if (_identityMods != null)
		{
			return _identityMods;
		}
		HashSet<ModContentPack> hashSet = new HashSet<ModContentPack>();
		try
		{
			foreach (FactionDef item in DefDatabase<FactionDef>.AllDefsListForReading)
			{
				ModContentPack modContentPack = item.modContentPack;
				if (modContentPack != null && !modContentPack.IsOfficialMod)
				{
					hashSet.Add(modContentPack);
				}
			}
			List<ThingDef> allDefsListForReading = DefDatabase<ThingDef>.AllDefsListForReading;
			for (int i = 0; i < allDefsListForReading.Count; i++)
			{
				ThingDef thingDef = allDefsListForReading[i];
				if (thingDef.race != null && thingDef.race.Humanlike)
				{
					ModContentPack modContentPack2 = thingDef.modContentPack;
					if (modContentPack2 != null && !modContentPack2.IsOfficialMod)
					{
						hashSet.Add(modContentPack2);
					}
				}
			}
		}
		catch (Exception ex)
		{
			EESLog.Warning("IdentityMods build failed: " + ex.Message);
		}
		_identityMods = hashSet;
		return hashSet;
	}

	public static HashSet<ModContentPack> GetIdentityMods()
	{
		return IdentityMods();
	}

	public static Dictionary<ModContentPack, string> ModRaceLabels()
	{
		if (_modRaceLabels != null)
		{
			return _modRaceLabels;
		}
		Dictionary<ModContentPack, List<string>> map = new Dictionary<ModContentPack, List<string>>();
		try
		{
			List<ThingDef> allDefsListForReading = DefDatabase<ThingDef>.AllDefsListForReading;
			for (int i = 0; i < allDefsListForReading.Count; i++)
			{
				ThingDef thingDef = allDefsListForReading[i];
				if (thingDef.category == ThingCategory.Pawn && thingDef.race != null && thingDef.race.Humanlike)
				{
					AddLabel(thingDef.modContentPack, thingDef.label.NullOrEmpty() ? thingDef.defName : thingDef.LabelCap.ToString());
				}
			}
			foreach (XenotypeDef item in DefDatabase<XenotypeDef>.AllDefsListForReading)
			{
				AddLabel(item.modContentPack, item.label.NullOrEmpty() ? item.defName : item.LabelCap.ToString());
			}
		}
		catch (Exception ex)
		{
			EESLog.Warning("ModRaceLabels build failed: " + ex.Message);
		}
		Dictionary<ModContentPack, string> dictionary = new Dictionary<ModContentPack, string>();
		foreach (KeyValuePair<ModContentPack, List<string>> item2 in map)
		{
			dictionary[item2.Key] = string.Join(", ", item2.Value);
		}
		_modRaceLabels = dictionary;
		return dictionary;
		void AddLabel(ModContentPack m, string lbl)
		{
			if (m != null && !m.IsOfficialMod && !lbl.NullOrEmpty())
			{
				if (!map.TryGetValue(m, out var value))
				{
					value = new List<string>();
					map[m] = value;
				}
				if (!value.Contains(lbl))
				{
					value.Add(lbl);
				}
			}
		}
	}

	private static HashSet<ModContentPack> FactionModSet(Faction faction)
	{
		return FactionModSetForDef(faction?.def);
	}

	public static HashSet<ModContentPack> FactionModSetForDef(FactionDef fd)
	{
		HashSet<ModContentPack> mods = new HashSet<ModContentPack>();
		if (fd == null)
		{
			return mods;
		}
		Add(fd.modContentPack);
		Add(fd.basicMemberKind?.race?.modContentPack);
		return mods;
		void Add(ModContentPack m)
		{
			if (m != null && !m.IsOfficialMod)
			{
				mods.Add(m);
			}
		}
	}

	private static float TurretStrength(ThingDef d)
	{
		return (d.building?.combatPower ?? 0f) + d.BaseMarketValue * 0.1f;
	}

	public static void EstimateGarrison(int tier, bool overlay, out int direct, out int mortarMax)
	{
		float num = (StrongholdMod.Settings?.turretDensity ?? 1f) * (overlay ? 1.4f : 1f);
		int num2 = Mathf.Clamp(tier, 1, 13);
		direct = Mathf.RoundToInt((float)DirectCountPerTier[num2] * num);
		mortarMax = Mathf.RoundToInt((float)MortarCountPerTier[num2] * num);
	}

	public static void EstimateGarrison(StrongholdData data, Settlement settlement, out int direct, out int mortarMax)
	{
		if (settlement != null && EESFactionKind.IsInsect(settlement.Faction))
		{
			direct = 0;
			mortarMax = 0;
			return;
		}
		bool flag = (data != null && data.isOverlayBase) || WillBeOverlayBase(settlement);
		EstimateGarrison(data?.tier ?? 1, flag, out direct, out mortarMax);
		if (!MortarsAllowed)
		{
			mortarMax = 0;
		}
		if (data != null && BattlefieldUtility.IsBattlefield(data.roleDefName))
		{
			direct = Mathf.Max(0, direct - 2);
		}
		float num = (flag ? 1.4f : 1f);
		direct = Mathf.Max(0, direct - Mathf.RoundToInt((float)(data?.sabotagedTurrets ?? 0) * num));
		mortarMax = Mathf.Max(0, mortarMax - Mathf.RoundToInt((float)(data?.sabotagedMortars ?? 0) * num));
		if (SupplyCaravanUtility.SupplyCutActive(data))
		{
			direct = Mathf.Max(0, direct - 2);
			mortarMax = Mathf.Max(0, mortarMax - 1);
		}
	}

	private static bool DefHasKcsgGenExt(Def def)
	{
		Type type = KcsgGenExtType;
		if (type == null || def?.modExtensions == null)
		{
			return false;
		}
		for (int i = 0; i < def.modExtensions.Count; i++)
		{
			if (type.IsInstanceOfType(def.modExtensions[i]))
			{
				return true;
			}
		}
		return false;
	}

	public static bool WillBeOverlayBase(Settlement s)
	{
		if (s?.Faction == null || s.Faction == Faction.OfPlayer)
		{
			return false;
		}
		if (WorldDominationCompat.TakesOver(s))
		{
			return false;
		}
		if (DefHasKcsgGenExt(s.Faction.def))
		{
			return true;
		}
		List<WorldObject> list = Find.World?.worldObjects?.AllWorldObjects;
		if (list != null)
		{
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].Tile == s.Tile && DefHasKcsgGenExt(list[i].def))
				{
					return true;
				}
			}
		}
		return false;
	}

	private static bool RequiresPower(ThingDef d)
	{
		return HasCompOrSubclass(d, typeof(CompPowerTrader));
	}

	private static List<TurretCand> FilterByTierWindow(List<TurretCand> pool, int tier, float lowFrac, float highFrac, bool aggressive)
	{
		if (tier < 7)
		{
			List<TurretCand> list = pool.Where((TurretCand c) => !RequiresPower(c.def)).ToList();
			if (list.Count > 0)
			{
				pool = list;
			}
		}
		List<TurretCand> list2 = null;
		if (pool.Any((TurretCand c) => TurretTierOverride.ContainsKey(c.def.defName)))
		{
			list2 = new List<TurretCand>();
			List<TurretCand> list3 = new List<TurretCand>();
			foreach (TurretCand item in pool)
			{
				if (TurretTierOverride.TryGetValue(item.def.defName, out var value))
				{
					if (tier >= value.min && tier <= value.max)
					{
						list2.Add(item);
					}
				}
				else
				{
					list3.Add(item);
				}
			}
			pool = list3;
		}
		List<TurretCand> list5;
		if (aggressive)
		{
			List<TurretCand> list4 = new List<TurretCand>();
			foreach (TurretCand item2 in pool)
			{
				IntRange intRange = AutoTierRange(item2.def);
				if (tier >= intRange.min && tier <= intRange.max)
				{
					list4.Add(item2);
				}
			}
			list5 = ((list4.Count > 0) ? list4 : FilterByTierWindowCore(pool, tier, lowFrac, highFrac, aggressive: true));
		}
		else
		{
			list5 = FilterByTierWindowCore(pool, tier, lowFrac, highFrac, aggressive: false);
		}
		if (list2 != null && list2.Count > 0)
		{
			list5.AddRange(list2);
		}
		return list5;
	}

	public static TechLevel TurretEffectiveTechLevel(ThingDef d)
	{
		return EffectiveTechLevel(d);
	}

	private static TechLevel EffectiveTechLevel(ThingDef d)
	{
		TechLevel techLevel = d.techLevel;
		if (d.researchPrerequisites != null)
		{
			for (int i = 0; i < d.researchPrerequisites.Count; i++)
			{
				if (d.researchPrerequisites[i] != null && (int)d.researchPrerequisites[i].techLevel > (int)techLevel)
				{
					techLevel = d.researchPrerequisites[i].techLevel;
				}
			}
		}
		if ((int)techLevel < 3)
		{
			techLevel = TechLevel.Industrial;
		}
		return techLevel;
	}

	private static void TechEraBand(TechLevel t, out int bmin, out int bmax)
	{
		switch (t)
		{
		case TechLevel.Animal:
		case TechLevel.Neolithic:
			bmin = 1;
			bmax = 4;
			break;
		case TechLevel.Medieval:
			bmin = 2;
			bmax = 7;
			break;
		case TechLevel.Industrial:
			bmin = 5;
			bmax = 12;
			break;
		case TechLevel.Spacer:
			bmin = 10;
			bmax = 13;
			break;
		case TechLevel.Ultra:
			bmin = 11;
			bmax = 13;
			break;
		case TechLevel.Archotech:
			bmin = 12;
			bmax = 13;
			break;
		default:
			bmin = 5;
			bmax = 12;
			break;
		}
	}

	private static IntRange AutoTierRange(ThingDef d)
	{
		TechLevel techLevel = EffectiveTechLevel(d);
		TechEraBand(techLevel, out var bmin, out var bmax);
		float num = TurretStrength(d);
		int num2 = ((num >= 1500f) ? 4 : ((num >= 900f) ? 3 : ((num >= 500f) ? 2 : ((num >= 250f) ? 1 : 0))));
		int num3 = Mathf.Clamp(bmin + num2, bmin, Mathf.Max(bmin, bmax - 4));
		if (RequiresPower(d) && (int)techLevel <= 4)
		{
			num3 = Mathf.Max(num3, 7);
		}
		int max = Mathf.Min(num3 + 4, bmax);
		return new IntRange(num3, max);
	}

	private static List<TurretCand> FilterByTierWindowCore(List<TurretCand> pool, int tier, float lowFrac, float highFrac, bool aggressive)
	{
		if (pool.Count <= 2)
		{
			return new List<TurretCand>(pool);
		}
		if (!aggressive)
		{
			int a = ((tier >= 11) ? 2 : ((tier >= 7) ? 1 : 0));
			a = Mathf.Min(a, Mathf.Max(0, pool.Count - 2));
			if (a > 0)
			{
				return pool.OrderBy((TurretCand c) => c.strength).Skip(a).ToList();
			}
			return new List<TurretCand>(pool);
		}
		int minTech = (int)MinTechForTier(tier);
		int maxTech = (int)MaxTechForTier(tier);
		List<TurretCand> list = pool.Where((TurretCand c) => (int)c.def.techLevel >= minTech && (int)c.def.techLevel <= maxTech).ToList();
		if (list.Count >= 1)
		{
			pool = list;
		}
		if (pool.Count <= 2)
		{
			return new List<TurretCand>(pool);
		}
		List<TurretCand> list2 = pool.OrderBy((TurretCand c) => c.strength).ToList();
		int count = list2.Count;
		float num = ((float)tier - 1f) / 12f;
		List<TurretCand> list3 = new List<TurretCand>();
		for (int num2 = 0; num2 < count; num2++)
		{
			float num3 = ((count == 1) ? 0.5f : ((float)num2 / (float)(count - 1)));
			if (num3 >= num - lowFrac && num3 <= num + highFrac)
			{
				list3.Add(list2[num2]);
			}
		}
		if (list3.Count >= 1)
		{
			return list3;
		}
		int index = Mathf.Clamp(Mathf.FloorToInt((num + highFrac) * (float)(count - 1)), 0, count - 1);
		return new List<TurretCand> { list2[index] };
	}

	private static TechLevel MinTechForTier(int tier)
	{
		if (tier >= 7)
		{
			return TechLevel.Industrial;
		}
		if (tier >= 5)
		{
			return TechLevel.Medieval;
		}
		return TechLevel.Neolithic;
	}

	private static TechLevel MaxTechForTier(int tier)
	{
		if (tier >= 5)
		{
			return TechLevel.Archotech;
		}
		return TechLevel.Medieval;
	}

	private static void NormalizeStrength(List<TurretCand> pool)
	{
		float num = float.MaxValue;
		float num2 = float.MinValue;
		for (int i = 0; i < pool.Count; i++)
		{
			num = Mathf.Min(num, pool[i].strength);
			num2 = Mathf.Max(num2, pool[i].strength);
		}
		float num3 = num2 - num;
		for (int j = 0; j < pool.Count; j++)
		{
			TurretCand value = pool[j];
			value.norm = ((num3 > 0.0001f) ? ((value.strength - num) / num3) : 0.5f);
			pool[j] = value;
		}
	}

	private static float PickWeight(TurretCand c, float tierBias)
	{
		return 1f / (1f + Mathf.Abs(c.norm - tierBias) * 4f) * (1f + c.norm * 1.5f);
	}

	private static ThingDef Identify105mm(List<TurretCand> offMortar)
	{
		if (offMortar == null || offMortar.Count == 0)
		{
			return null;
		}
		foreach (TurretCand item in offMortar)
		{
			string defName = item.def.defName;
			if (defName.IndexOf("105", StringComparison.OrdinalIgnoreCase) >= 0 || defName.IndexOf("Howitzer", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return item.def;
			}
		}
		return null;
	}

	private static ThingDef PickWeightedCapped(List<TurretCand> pool, float tierBias, ThingDef capped, Dictionary<ThingDef, int> placed)
	{
		if (pool == null || pool.Count == 0)
		{
			return null;
		}
		List<TurretCand> list = pool;
		if (capped != null && placed.TryGetValue(capped, out var value) && value >= 1)
		{
			list = pool.Where((TurretCand c) => c.def != capped).ToList();
		}
		if (list.Count != 0)
		{
			return PickWeighted(list, tierBias);
		}
		return null;
	}

	private static ThingDef PickWeighted(List<TurretCand> pool, float tierBias)
	{
		float num = 0f;
		for (int i = 0; i < pool.Count; i++)
		{
			num += PickWeight(pool[i], tierBias);
		}
		if (num <= 0f)
		{
			return pool[0].def;
		}
		float num2 = Rand.Value * num;
		for (int j = 0; j < pool.Count; j++)
		{
			num2 -= PickWeight(pool[j], tierBias);
			if (num2 <= 0f)
			{
				return pool[j].def;
			}
		}
		return pool[pool.Count - 1].def;
	}

	private static void ActivateTurret(Thing turret, IntVec3 baseCenter, int tier = 1)
	{
		CompPowerTrader compPowerTrader = turret.TryGetComp<CompPowerTrader>();
		if (compPowerTrader != null)
		{
			compPowerTrader.PowerOutput = 2000f;
			compPowerTrader.PowerOn = true;
		}
		CompRefuelable compRefuelable = turret.TryGetComp<CompRefuelable>();
		compRefuelable?.Refuel(compRefuelable.Props.fuelCapacity);
		turret.TryGetComp<CompCanBeDormant>()?.WakeUp();
		SpawnTurretAmmo(turret, baseCenter);
		EnsureManningTokenShell(turret, baseCenter);
		if (compRefuelable != null && turret.TryGetComp<CompInteractable>() != null)
		{
			SpawnTurretFuel(turret, compRefuelable, baseCenter);
			SpawnTurretLoader(turret, baseCenter);
		}
		else if (compRefuelable != null && !compRefuelable.Props.fuelIsMortarBarrel && Patch_JobGiver_FixedProjectileManning.Eligible(turret))
		{
			// The original gunner uses native fuel hauling; reserve one real refill.
			SpawnTurretFuel(turret, compRefuelable, baseCenter);
		}
		CECompat.LoadTurretAmmo(turret, baseCenter, tier);
	}

	internal static void SetTurretPowered(Thing turret, bool on)
	{
		CompPowerTrader compPowerTrader = turret.TryGetComp<CompPowerTrader>();
		if (compPowerTrader != null)
		{
			if (on)
			{
				compPowerTrader.PowerOutput = 2000f;
				compPowerTrader.PowerOn = true;
			}
			else
			{
				float num = ((compPowerTrader.Props != null) ? Mathf.Max(1f, compPowerTrader.Props.PowerConsumption) : 200f);
				compPowerTrader.PowerOutput = 0f - num;
				compPowerTrader.PowerOn = false;
			}
		}
	}

	private static bool IsClearableCover(Map map, IntVec3 c, Faction faction)
	{
		Building edifice = c.GetEdifice(map);
		if (edifice != null && !edifice.Destroyed && edifice.Faction == faction && edifice.def.fillPercent < 1f && (edifice.def.building == null || !edifice.def.building.IsTurret) && !edifice.def.IsDoor)
		{
			if (edifice.def.building != null)
			{
				return !edifice.def.building.isNaturalRock;
			}
			return true;
		}
		return false;
	}

	private static bool ClearManningCell(Thing turret, Map map, Faction faction)
	{
		if (!turret.def.hasInteractionCell)
		{
			return false;
		}
		IntVec3 interactionCell = turret.InteractionCell;
		if (!interactionCell.InBounds(map) || interactionCell.Standable(map))
		{
			return false;
		}
		if (!IsClearableCover(map, interactionCell, faction))
		{
			return false;
		}
		try
		{
			interactionCell.GetEdifice(map)?.Destroy();
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static Rot4 InteriorFacingRotation(ThingDef def, IntVec3 cell, IntVec3 baseCenter, Map map, Faction faction)
	{
		if (!def.rotatable)
		{
			return Rot4.North;
		}
		if (def.hasInteractionCell)
		{
			Rot4 result = Rot4.North;
			float num = float.MaxValue;
			int num2 = -1;
			for (int i = 0; i < 4; i++)
			{
				Rot4 rot = new Rot4(i);
				if (!TurretFootprintFreeRot(map, cell, def.size, rot))
				{
					continue;
				}
				IntVec3 intVec = ThingUtility.InteractionCellWhenAt(def, cell, rot, map);
				if (!intVec.InBounds(map))
				{
					continue;
				}
				int num3 = (intVec.Standable(map) ? 2 : (IsClearableCover(map, intVec, faction) ? 1 : (-1)));
				if (num3 >= 0)
				{
					float num4 = (intVec - baseCenter).LengthHorizontalSquared;
					if (num3 > num2 || (num3 == num2 && num4 < num))
					{
						num2 = num3;
						num = num4;
						result = rot;
					}
				}
			}
			return result;
		}
		IntVec3 intVec2 = cell - baseCenter;
		Rot4 rot2 = ((Mathf.Abs(intVec2.x) < Mathf.Abs(intVec2.z)) ? ((intVec2.z >= 0) ? Rot4.North : Rot4.South) : ((intVec2.x >= 0) ? Rot4.East : Rot4.West));
		if (!TurretFootprintFreeRot(map, cell, def.size, rot2))
		{
			return Rot4.North;
		}
		return rot2;
	}

	private static bool TurretFootprintFreeRot(Map map, IntVec3 center, IntVec2 size, Rot4 rot)
	{
		foreach (IntVec3 item in GenAdj.OccupiedRect(center, rot, size))
		{
			if (!item.InBounds(map) || item.GetEdifice(map) != null || !item.Standable(map))
			{
				return false;
			}
		}
		return true;
	}

	private static bool MannableSpotOk(Map map, IntVec3 center, ThingDef def, IntVec3 baseCenter)
	{
		if (!def.hasInteractionCell)
		{
			return true;
		}
		float num = (center - baseCenter).LengthHorizontalSquared;
		int num2 = ((!def.rotatable) ? 1 : 4);
		for (int i = 0; i < num2; i++)
		{
			Rot4 rot = new Rot4(i);
			if (TurretFootprintFreeRot(map, center, def.size, rot))
			{
				IntVec3 intVec = ThingUtility.InteractionCellWhenAt(def, center, rot, map);
				if (intVec.InBounds(map) && intVec.Standable(map) && intVec.GetEdifice(map) == null && (float)(intVec - baseCenter).LengthHorizontalSquared < num)
				{
					return true;
				}
			}
		}
		return false;
	}

	private static bool TryFindInteriorItemCell(Thing turret, IntVec3 baseCenter, float radius, out IntVec3 best, bool avoidEdifice = false)
	{
		Map map = turret.Map;
		best = IntVec3.Invalid;
		float num = float.MaxValue;
		foreach (IntVec3 item in GenRadial.RadialCellsAround(turret.Position, radius, useCenter: false))
		{
			if (item.InBounds(map) && item.Standable(map) && item.GetFirstItem(map) == null && (!avoidEdifice || item.GetEdifice(map) == null))
			{
				float num2 = (item - baseCenter).LengthHorizontalSquared;
				if (num2 < num)
				{
					num = num2;
					best = item;
				}
			}
		}
		return best.IsValid;
	}

	private static bool TryFindTurretSideCell(Thing turret, IntVec3 baseCenter, bool avoidEdifice, out IntVec3 cell)
	{
		Map map = turret.Map;
		cell = IntVec3.Invalid;
		float num = float.MaxValue;
		foreach (IntVec3 item in GenAdj.CellsAdjacent8Way(turret))
		{
			if (item.InBounds(map) && item.Standable(map) && item.GetFirstItem(map) == null && item.GetFirstPawn(map) == null && (!avoidEdifice || item.GetEdifice(map) == null))
			{
				float num2 = (item - baseCenter).LengthHorizontalSquared;
				if (num2 < num)
				{
					num = num2;
					cell = item;
				}
			}
		}
		if (cell.IsValid)
		{
			return true;
		}
		Region region = turret.Position.GetRegion(map);
		foreach (IntVec3 item2 in GenRadial.RadialCellsAround(turret.Position, 3.5f, useCenter: false))
		{
			if (item2.InBounds(map) && item2.Standable(map) && item2.GetFirstItem(map) == null && item2.GetFirstPawn(map) == null && (!avoidEdifice || item2.GetEdifice(map) == null) && (region == null || item2.GetRegion(map) == region))
			{
				float num3 = (item2 - turret.Position).LengthHorizontalSquared;
				if (num3 < num)
				{
					num = num3;
					cell = item2;
				}
			}
		}
		return cell.IsValid;
	}

	private static void SpawnTurretFuel(Thing turret, CompRefuelable refuel, IntVec3 baseCenter)
	{
		ThingFilter thingFilter = refuel?.Props?.fuelFilter;
		if (thingFilter == null)
		{
			return;
		}
		HashSet<ModContentPack> hashSet = FactionModSet(turret.Faction);
		// A foreign culture may operate this turret: accept its own mod's fuel
		// only when the actual CompRefuelable filter accepts that definition.
		if (!refuel.Props.fuelIsMortarBarrel && Patch_JobGiver_FixedProjectileManning.Eligible(turret) && turret.def.modContentPack != null)
			hashSet.Add(turret.def.modContentPack);
		ThingDef thingDef = null;
		List<ThingDef> allDefsListForReading = DefDatabase<ThingDef>.AllDefsListForReading;
		for (int i = 0; i < allDefsListForReading.Count; i++)
		{
			ThingDef thingDef2 = allDefsListForReading[i];
			if (thingDef2.category == ThingCategory.Item && thingFilter.Allows(thingDef2))
			{
				ModContentPack modContentPack = thingDef2.modContentPack;
				if ((modContentPack == null || modContentPack.IsOfficialMod || hashSet.Contains(modContentPack)) && (thingDef == null || thingDef2.BaseMarketValue < thingDef.BaseMarketValue))
				{
					thingDef = thingDef2;
				}
			}
		}
		if (thingDef == null)
		{
			return;
		}
		Map map = turret.Map;
		try
		{
			int num = Mathf.Max(1, Mathf.CeilToInt(refuel.Props.fuelCapacity / refuel.Props.FuelMultiplierCurrentDifficulty));
			int num2 = 0;
			int num3 = num;
			int num4 = 0;
			IntVec3 intVec = IntVec3.Invalid;
			IntVec3 cell;
			while (num3 > 0 && num4++ < 8 && TryFindTurretSideCell(turret, baseCenter, avoidEdifice: true, out cell))
			{
				if (!intVec.IsValid)
				{
					intVec = cell;
				}
				int num5 = Mathf.Min(num3, Mathf.Max(1, thingDef.stackLimit));
				Thing thing = ThingMaker.MakeThing(thingDef);
				thing.stackCount = num5;
				thing.SetForbidden(value: false, warnOnFail: false);
				GenSpawn.Spawn(thing, cell, map);
				num3 -= num5;
				num2 += num5;
			}
			EESLog.Message($"Garrison fuel [{turret.Faction?.Name}] {thingDef.defName}×{num2} for {turret.def.defName} " + $"@ {intVec} (turret@{turret.Position} base@{baseCenter} 가득1회={num} 미배치={num3}).");
		}
		catch (Exception ex)
		{
			EESLog.Error("Garrison fuel spawn " + thingDef.defName + ": " + ex.Message);
		}
	}

	private static void SpawnTurretAmmo(Thing turret, IntVec3 baseCenter)
	{
		if (!(turret is Building_TurretGun { gun: not null } building_TurretGun))
		{
			return;
		}
		CompChangeableProjectile compChangeableProjectile = building_TurretGun.gun.TryGetComp<CompChangeableProjectile>();
		if (compChangeableProjectile == null)
		{
			return;
		}
		ThingFilter thingFilter = building_TurretGun.gun.def.building?.fixedStorageSettings?.filter ?? building_TurretGun.gun.def.building?.defaultStorageSettings?.filter ?? compChangeableProjectile.allowedShellsSettings?.filter;
		if (thingFilter == null)
		{
			return;
		}
		HashSet<ModContentPack> hashSet = FactionModSet(turret.Faction);
		ThingDef thingDef = null;
		List<ThingDef> allDefsListForReading = DefDatabase<ThingDef>.AllDefsListForReading;
		for (int i = 0; i < allDefsListForReading.Count; i++)
		{
			ThingDef thingDef2 = allDefsListForReading[i];
			if (thingDef2.category == ThingCategory.Item && thingDef2.projectileWhenLoaded != null && thingFilter.Allows(thingDef2))
			{
				ModContentPack modContentPack = thingDef2.modContentPack;
				if ((modContentPack == null || modContentPack.IsOfficialMod || hashSet.Contains(modContentPack)) && (thingDef == null || thingDef2.BaseMarketValue < thingDef.BaseMarketValue))
				{
					thingDef = thingDef2;
				}
			}
		}
		if (thingDef == null)
		{
			return;
		}
		int num = Mathf.Clamp(thingDef.stackLimit, 5, 25);
		compChangeableProjectile.LoadShell(thingDef, num);
		Map map = turret.Map;
		if (TryFindInteriorItemCell(turret, baseCenter, 3f, out var best))
		{
			try
			{
				Thing thing = ThingMaker.MakeThing(thingDef);
				thing.stackCount = Mathf.Min(Mathf.Max(thingDef.stackLimit, 1), 25);
				thing.SetForbidden(value: false, warnOnFail: false);
				GenPlace.TryPlaceThing(thing, best, map, ThingPlaceMode.Near);
			}
			catch (Exception ex)
			{
				EESLog.Error("Garrison ammo spawn " + thingDef.defName + ": " + ex.Message);
			}
		}
		EESLog.Message($"Garrison ammo [{turret.def.defName}]: loaded {thingDef.defName}×{num} + spare.");
	}

	private static void EnsureManningTokenShell(Thing turret, IntVec3 baseCenter)
	{
		// Fixed-projectile guns use their real fuel, not mortar shells. The
		// scoped native-job fallback handles this without creating loot tokens.
		// Keep the previous CE path until its own ammo integration is verified.
		if (!CECompat.Active) return;
		try
		{
			if (!(turret is Building_TurretGun { gun: not null } building_TurretGun) || !HasCompOrSubclass(turret.def, typeof(CompMannable)) || building_TurretGun.gun.TryGetComp<CompChangeableProjectile>() != null)
			{
				return;
			}
			Map map = turret.Map;
			if (map == null)
			{
				return;
			}
			List<Thing> list = map.listerThings.ThingsInGroup(ThingRequestGroup.Shell);
			for (int i = 0; i < list.Count; i++)
			{
				if ((list[i].Position - turret.Position).LengthHorizontal <= 40f)
				{
					return;
				}
			}
			HashSet<ModContentPack> hashSet = FactionModSet(turret.Faction);
			ThingDef thingDef = null;
			List<ThingDef> allDefsListForReading = DefDatabase<ThingDef>.AllDefsListForReading;
			for (int j = 0; j < allDefsListForReading.Count; j++)
			{
				ThingDef thingDef2 = allDefsListForReading[j];
				if (thingDef2.category != ThingCategory.Item || thingDef2.projectileWhenLoaded?.projectile == null)
				{
					continue;
				}
				DamageDef damageDef = thingDef2.projectileWhenLoaded.projectile.damageDef;
				if (damageDef != null && damageDef.harmsHealth)
				{
					ModContentPack modContentPack = thingDef2.modContentPack;
					if ((modContentPack == null || modContentPack.IsOfficialMod || hashSet.Contains(modContentPack)) && (thingDef == null || thingDef2.BaseMarketValue < thingDef.BaseMarketValue))
					{
						thingDef = thingDef2;
					}
				}
			}
			if (thingDef == null)
			{
				EESLog.Warning("ManningToken [" + turret.def.defName + "]: 토큰 포탄 후보 없음.");
				return;
			}
			if (!TryFindInteriorItemCell(turret, baseCenter, 3f, out var best))
			{
				best = turret.InteractionCell;
			}
			Thing thing = ThingMaker.MakeThing(thingDef);
			thing.stackCount = 1;
			thing.SetForbidden(value: false, warnOnFail: false);
			GenPlace.TryPlaceThing(thing, best, map, ThingPlaceMode.Near);
			EESLog.Message($"ManningToken [{turret.def.defName}@{turret.Position}]: {thingDef.defName}×1 @ {best} (manning 허가용, 미소비).");
		}
		catch (Exception ex)
		{
			EESLog.Error("EnsureManningTokenShell " + turret?.def?.defName + ": " + ex.Message);
		}
	}

	private static bool IsAutoTurret(ThingDef d, TechLevel tech)
	{
		if (d == null || (int)d.techLevel > (int)tech)
		{
			return false;
		}
		return IsConfigurableTurretDef(d);
	}

	public static bool IsConfigurableTurretDef(ThingDef d)
	{
		if (d == null || d.thingClass == null || !typeof(Building_Turret).IsAssignableFrom(d.thingClass))
		{
			return false;
		}
		if (HasCompOrSubclass(d, typeof(CompInteractable)) && (!typeof(Building_TurretGun).IsAssignableFrom(d.thingClass) || d.building == null || !d.building.ai_combatDangerous))
		{
			return false;
		}
		if (d.building == null || d.category != ThingCategory.Building)
		{
			return false;
		}
		if (HasCompOrSubclass(d, typeof(CompMannable)) && !d.hasInteractionCell)
		{
			return false;
		}
		if (d.designationCategory == null)
		{
			return false;
		}
		if (!d.building.claimable)
		{
			return false;
		}
		if (HasCompOrSubclass(d, typeof(CompSpawnerPawn)) || HasCompOrSubclass(d, typeof(CompMaintainable)))
		{
			return false;
		}
		if (HasShipInfrastructureComp(d))
		{
			return false;
		}
		if (IsRimatomicsEnergyWeapon(d))
		{
			return false;
		}
		return true;
	}

	private static bool IsRimatomicsEnergyWeapon(ThingDef d)
	{
		Type type = d.thingClass;
		while (type != null)
		{
			if (type.FullName == "Rimatomics.Building_EnergyWeapon")
			{
				return true;
			}
			type = type.BaseType;
		}
		return false;
	}

	private static bool HasShipInfrastructureComp(ThingDef d)
	{
		if (d.comps == null)
		{
			return false;
		}
		for (int i = 0; i < d.comps.Count; i++)
		{
			Type type = d.comps[i]?.compClass;
			if (type == null)
			{
				continue;
			}
			string fullName = type.FullName;
			if (fullName == null)
			{
				continue;
			}
			for (int j = 0; j < ShipInfraCompClassNames.Length; j++)
			{
				if (fullName == ShipInfraCompClassNames[j])
				{
					return true;
				}
			}
		}
		return false;
	}

	public static List<ThingDef> AllConfigurableTurretDefs()
	{
		if (_configurableTurrets != null)
		{
			return _configurableTurrets;
		}
		List<ThingDef> list = new List<ThingDef>();
		List<ThingDef> allDefsListForReading = DefDatabase<ThingDef>.AllDefsListForReading;
		for (int i = 0; i < allDefsListForReading.Count; i++)
		{
			if (IsConfigurableTurretDef(allDefsListForReading[i]))
			{
				list.Add(allDefsListForReading[i]);
			}
		}
		_configurableTurrets = list;
		return list;
	}

	private static bool HasCompOrSubclass(ThingDef d, Type baseCompType)
	{
		if (d.comps == null)
		{
			return false;
		}
		for (int i = 0; i < d.comps.Count; i++)
		{
			Type compClass = d.comps[i].compClass;
			if (compClass != null && baseCompType.IsAssignableFrom(compClass))
			{
				return true;
			}
		}
		return false;
	}

	private static CellRect GetSettlementRect(Map map)
	{
		if (!MapGenerator.TryGetVar<CellRect>("SettlementRect", out var var))
		{
			return CellRect.Empty;
		}
		return var;
	}

	public static CellRect ComputeBaseRect(Map map, Faction faction)
	{
		if (map?.listerBuildings == null || faction == null)
		{
			return CellRect.Empty;
		}
		int num = int.MaxValue;
		int num2 = int.MaxValue;
		int num3 = int.MinValue;
		int num4 = int.MinValue;
		int num5 = 0;
		List<Building> allBuildingsNonColonist = map.listerBuildings.allBuildingsNonColonist;
		for (int i = 0; i < allBuildingsNonColonist.Count; i++)
		{
			Building building = allBuildingsNonColonist[i];
			if (building != null && !building.Destroyed && building.Faction == faction)
			{
				IntVec3 position = building.Position;
				if (position.x < num)
				{
					num = position.x;
				}
				if (position.x > num3)
				{
					num3 = position.x;
				}
				if (position.z < num2)
				{
					num2 = position.z;
				}
				if (position.z > num4)
				{
					num4 = position.z;
				}
				num5++;
			}
		}
		if (num5 == 0)
		{
			return CellRect.Empty;
		}
		CellRect result = CellRect.FromLimits(num, num2, num3, num4).ExpandedBy(3).ClipInsideMap(map);
		if (num5 < 4 && (result.Width > 110 || result.Height > 110))
		{
			return CellRect.Empty;
		}
		return result;
	}
}
