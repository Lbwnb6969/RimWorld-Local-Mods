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

[StaticConstructorOnStartup]
public class MapComponent_StrongholdSiege : MapComponent
{
	private int nextReinforceTick = -1;
	public int finalStandWavesSpawned;
	public int nextFinalStandWaveTick = -1;
	public string finalStandWaveReason;

	private Dictionary<int, IntVec3> turretManningCells = new Dictionary<int, IntVec3>();

	private List<int> turretManningKeys;

	private List<IntVec3> turretManningValues;

	private bool gravshipAttack;

	private bool reinforceActive;

	private bool lastCommsAlive = true;

	private bool initialized;

	private bool announcedActive;

	private bool announcedBlocked;

	private Pawn commander;

	private bool commanderRegistered;

	private bool commanderDead;

	private int commanderRegisteredTick = -1;

	private int offMapSinceTick = -1;

	private const int CommanderSpawnGraceTicks = 1000;

	private const int CommanderOffMapConfirmTicks = 600;

	private string lastCmdDiagSig = "";

	private StrongholdData cachedData;

	private CommanderType cachedType;

	private int nextFanaticTick = -1;

	private string lastResurrectDiag = "";

	private bool securityActive;

	private HashSet<Thing> securityStructures = new HashSet<Thing>();

	internal static bool AnySecurityActive;

	internal static bool AnyObserverActive;

	internal static readonly HashSet<int> BattlefieldParticipants = new HashSet<int>();

	private const int FanaticIntervalTicks = 15000;

	private const int FanaticBattlefieldIntervalTicks = 5000;

	private const int FanaticRetryTicks = 300;

	private Pawn observerTarget;

	private bool observerStarted;

	private int observerNextPickTick = -1;

	private const int ObserverFirstDelayTicks = 25000;

	private const int ObserverIntervalTicks = 25000;

	private const int CheckInterval = 250;

	private const int GravFixedDelayTicks = 15000;

	public bool eesGenerated;

	public bool centerBuilding;

	public bool eesStrongholdMap;

	public bool fakePending;

	public bool isFake;

	public bool eesOverlay;

	public CellRect eesBaseRect;

	public CellRect eesVaultRect = CellRect.Empty;

	public bool openBase;

	public bool isPolluted;

	public bool isBattlefield;

	private bool bfSelected;

	private bool bfActive;

	private int bfFactionC = -1;

	private int bfFactionD = -1;

	private int bfEntryTick = -1;

	private int bfNextEvent;

	private static readonly int[] BfTick = new int[14]
	{
		2500, 5000, 8750, 12500, 16250, 20000, 23750, 27500, 31250, 35000,
		38750, 42500, 42500, 45000
	};

	private static readonly char[] BfActor = new char[14]
	{
		'C', 'D', 'B', 'C', 'D', 'C', 'D', 'B', 'C', 'D',
		'B', 'C', 'D', 'B'
	};

	private static readonly BattlefieldWave[] BfWave = new BattlefieldWave[14]
	{
		BattlefieldWave.AssaultCenter,
		BattlefieldWave.HoldOutside,
		BattlefieldWave.ReinforceCenter,
		BattlefieldWave.AssaultCenter,
		BattlefieldWave.AssaultCenter,
		BattlefieldWave.AssaultCenter,
		BattlefieldWave.AssaultCenter,
		BattlefieldWave.ReinforceCenter,
		BattlefieldWave.AssaultCenter,
		BattlefieldWave.AssaultCenter,
		BattlefieldWave.ReinforceCenter,
		BattlefieldWave.AssaultCenter,
		BattlefieldWave.AssaultCenter,
		BattlefieldWave.ReinforceCenter
	};

	private static readonly bool[] BfNear = new bool[14]
	{
		false, true, false, true, true, true, true, false, true, true,
		false, true, true, false
	};

	public const int BattlefieldDefeatGuardTicks = 45000;

	private int groupInitGarrison = -1;

	private bool heraldActive;

	private bool heraldResolved;

	private bool heraldDropTriggered;

	private List<Pawn> heraldRetreaters = new List<Pawn>();

	private bool sawHostiles;

	private bool trapsNeutralized;

	public bool turretsGridPowered;

	public List<Thing> cryoCaskets = new List<Thing>();

	public bool cryoArmed;

	public bool cryoWoken;

	public int cryoRoomMinX = -1;

	public int cryoRoomMinZ = -1;

	public int cryoRoomW;

	public int cryoRoomH;

	public int cryoEdgeBaseline = -1;

	public int cryoInitialGarrison = -1;

	private bool defendDutyFixDone;

	private bool defendDutyFixLogged;

	private const int VaultSweepHeartbeatTicks = 2500;

	private int lastVaultSweepBeatTick = -1;

	private const float HeraldDropFraction = 0.4f;

	private string lastObserverDiag = "";

	private static Material observerTargetMat;

	public Pawn ObserverTarget => observerTarget;

	public int ObserverTicksRemaining
	{
		get
		{
			if (cachedType != CommanderType.Observer || !commanderRegistered || commanderDead || observerNextPickTick <= 0)
			{
				return -1;
			}
			return Mathf.Max(0, observerNextPickTick - Find.TickManager.TicksGame);
		}
	}

	public bool ObserverActive
	{
		get
		{
			if (cachedType == CommanderType.Observer && commanderRegistered && !commanderDead && commander != null && !commander.Dead)
			{
				return !EESHostility.IsFriendlyOutpost(EESHostility.OwnerOf(map));
			}
			return false;
		}
	}

	private static int ReinforceMinTicks => Mathf.RoundToInt((StrongholdMod.Settings?.reinforceMinHours ?? 8f) * 2500f);

	private static int ReinforceMaxTicks => Mathf.RoundToInt((StrongholdMod.Settings?.reinforceMaxHours ?? 48f) * 2500f);

	public bool BattlefieldActive
	{
		get
		{
			if (isBattlefield)
			{
				return bfActive;
			}
			return false;
		}
	}

	public int BattlefieldEntryTick => bfEntryTick;

	public bool BattlefieldHasNext
	{
		get
		{
			if (isBattlefield && bfActive && bfEntryTick >= 0)
			{
				return bfNextEvent < BfTick.Length;
			}
			return false;
		}
	}

	public int BattlefieldNextRemaining
	{
		get
		{
			if (!BattlefieldHasNext)
			{
				return -1;
			}
			return Mathf.Max(0, bfEntryTick + BfTick[bfNextEvent] - Find.TickManager.TicksGame);
		}
	}

	public string BattlefieldNextActorKey
	{
		get
		{
			if (!BattlefieldHasNext)
			{
				return null;
			}
			return BfActor[bfNextEvent].ToString();
		}
	}

	private bool Enabled
	{
		get
		{
			if (StrongholdMod.Settings != null)
			{
				return StrongholdMod.Settings.enableReinforcements;
			}
			return true;
		}
	}

	public bool ReinforcementActive
	{
		get
		{
			if (Enabled && reinforceActive && nextReinforceTick >= 0)
			{
				return !EESHostility.IsFriendlyOutpost(EESHostility.OwnerOf(map));
			}
			return false;
		}
	}

	public int ReinforcementTicksRemaining => Mathf.Max(0, nextReinforceTick - Find.TickManager.TicksGame);

	private static Material ObserverTargetMat
	{
		get
		{
			if (observerTargetMat == null)
			{
				observerTargetMat = MaterialPool.MatFrom("UI/Target", ShaderDatabase.MetaOverlay);
			}
			return observerTargetMat;
		}
	}

	public static bool IsBattlefieldParticipant(Faction f)
	{
		if (f != null)
		{
			return BattlefieldParticipants.Contains(f.loadID);
		}
		return false;
	}

	public static bool IsActiveBattlefieldFaction(Faction f)
	{
		if (f == null)
		{
			return false;
		}
		List<Map> maps = Find.Maps;
		for (int i = 0; i < maps.Count; i++)
		{
			MapComponent_StrongholdSiege component = maps[i].GetComponent<MapComponent_StrongholdSiege>();
			if (component != null && component.isBattlefield && component.bfActive && (component.bfFactionC == f.loadID || component.bfFactionD == f.loadID || (maps[i].ParentFaction != null && maps[i].ParentFaction.loadID == f.loadID)))
			{
				return true;
			}
		}
		return false;
	}

	public bool HasActiveBattlefieldFaction(Faction faction)
	{
		return faction != null && isBattlefield && bfActive &&
			(bfFactionC == faction.loadID || bfFactionD == faction.loadID || map.ParentFaction == faction);
	}

	private static int NextReinforceInterval()
	{
		return Rand.Range(ReinforceMinTicks, Mathf.Max(ReinforceMinTicks + 1, ReinforceMaxTicks));
	}

	public void MarkGravshipAttack()
	{
		gravshipAttack = true;
	}

	public MapComponent_StrongholdSiege(Map map)
		: base(map)
	{
	}

	public override void FinalizeInit()
	{
		base.FinalizeInit();
		RestoreGenerationFootprint();
		RestoreTurretManningCells();
		if (commander == null || commander.apparel == null)
		{
			return;
		}
		List<Apparel> wornApparel = commander.apparel.WornApparel;
		for (int i = 0; i < wornApparel.Count; i++)
		{
			CompEESCommanderBelt compEESCommanderBelt = wornApparel[i]?.TryGetComp<CompEESCommanderBelt>();
			if (compEESCommanderBelt != null && !compEESCommanderBelt.legit)
			{
				compEESCommanderBelt.legit = true;
				EESLog.Message("Migration: marked existing commander shield belt on " + commander.LabelShortCap + " as legit (pre-self-destruct save guard).");
			}
		}
	}

	private void RestoreGenerationFootprint()
	{
		if (!eesStrongholdMap || map.Parent is not Settlement settlement) return;
		var data = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
		if (data == null) return;
		var legacy = data.generationPlan == null;
		var plan = StrongholdGenerationPlan.GetOrCreate(map, data);
		if (plan == null) return;
		if (eesBaseRect.Width <= 0)
			eesBaseRect = plan.baseRect.Width > 0 ? plan.baseRect : StrongholdGarrisonUtility.ComputeBaseRect(map, settlement.Faction);
		plan.RecordBase(eesBaseRect);
		if (eesVaultRect.Width <= 0 && plan.vaultRect.Width > 0) eesVaultRect = plan.vaultRect.ExpandedBy(1).ClipInsideMap(map);
		if (!data.commandRoomCenter.IsValid && plan.commandCell.IsValid) data.commandRoomCenter = plan.commandCell;
		if (legacy)
		{
			plan.legacyMap = true;
			plan.stage = "legacy-observed";
			plan.commandCell = data.commandRoomCenter;
			plan.lootFinalized = data.vaultValue > 0f;
		}
		// Existing maps are observed without rebuilding or reissuing their loot.
		eesGenerated = true;
	}

	private void RestoreTurretManningCells()
	{
		if (!eesStrongholdMap || map.ParentFaction == null || map.ParentFaction == Faction.OfPlayer) return;
		var operators = map.mapPawns.AllPawnsSpawned.Where(p => p.Faction == map.ParentFaction && p.RaceProps.ToolUser && !p.Dead && !p.Downed).ToList();
		if (operators.Count == 0) return;
		var turrets = map.listerBuildings.allBuildingsNonColonist.OfType<Building_Turret>()
			.Where(t => t.Faction == map.ParentFaction && t.TryGetComp<CompMannable>() != null).OrderBy(t => t.thingIDNumber).ToList();
		var used = new HashSet<IntVec3>();
		foreach (var turret in turrets)
		{
			var original = ThingUtility.InteractionCellWhenAt(turret.def, turret.Position, turret.Rotation, map);
			if (original.InBounds(map) && original.Walkable(map) && operators.Any(p => p.CanReach(original, PathEndMode.OnCell, Danger.Deadly)))
			{
				turretManningCells.Remove(turret.thingIDNumber);
				used.Add(original);
				continue;
			}
			if (turretManningCells.TryGetValue(turret.thingIDNumber, out var existing) && ValidTurretManningCell(turret, existing) &&
				!used.Contains(existing) && operators.Any(p => p.CanReach(existing, PathEndMode.OnCell, Danger.Deadly)))
			{
				used.Add(existing);
				continue;
			}
			turretManningCells.Remove(turret.thingIDNumber);
			foreach (var candidate in GenRadial.RadialCellsAround(turret.Position, 4f, true))
			{
				if (!ValidTurretManningCell(turret, candidate) || used.Contains(candidate) ||
					!operators.Any(p => p.CanReach(candidate, PathEndMode.OnCell, Danger.Deadly))) continue;
				turretManningCells[turret.thingIDNumber] = candidate;
				used.Add(candidate);
				EESLog.Message("Manning cell override " + turret.def.defName + " " + turret.Position + ": " + original + " -> " + candidate);
				break;
			}
		}
		foreach (var turret in turrets)
		{
			if (turret.Destroyed || operators.Any(p => p.CanReach(turret, PathEndMode.InteractionCell, Danger.Deadly))) continue;
			if (TryRelocateUnreachableTurret(turret, operators))
			{
				EESLog.Warning("Manning turret relocated after final reachability pass: " + turret.def.defName + " -> " + turret.Position + ".");
			}
		}
	}

	private bool TryRelocateUnreachableTurret(Thing turret, List<Pawn> operators)
	{
		if (turret?.Map != map || turret.def == null || operators == null || operators.Count == 0) return false;
		CellRect search = eesBaseRect.Width > 0 ? eesBaseRect.ExpandedBy(1).ClipInsideMap(map) : CellRect.CenteredOn(map.Center, 30).ClipInsideMap(map);
		var rotations = turret.def.rotatable ? new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West } : new[] { turret.Rotation };
		foreach (var candidate in search.Cells.OrderBy(c => (c - turret.Position).LengthHorizontalSquared))
		{
			foreach (var rotation in rotations)
			{
				var footprint = GenAdj.OccupiedRect(candidate, rotation, turret.def.size);
				if (footprint.Any(c => !c.InBounds(map) || !c.Standable(map) || c.GetEdifice(map) != null || c.GetFirstPawn(map) != null)) continue;
				var interaction = ThingUtility.InteractionCellWhenAt(turret.def, candidate, rotation, map);
				if (!interaction.InBounds(map) || !interaction.Standable(map) || interaction.GetEdifice(map) != null || interaction.GetFirstPawn(map) != null ||
					!operators.Any(p => p.CanReach(interaction, PathEndMode.OnCell, Danger.Deadly))) continue;
				var oldPosition = turret.Position;
				turret.DeSpawn();
				try
				{
					GenSpawn.Spawn(turret, candidate, map, rotation);
					turretManningCells.Remove(turret.thingIDNumber);
					EESLog.Warning("Manning turret relocation " + turret.def.defName + " " + oldPosition + " -> " + candidate + " interaction=" + interaction + ".");
					return true;
				}
				catch
				{
					if (!turret.Spawned) GenSpawn.Spawn(turret, oldPosition, map, turret.Rotation);
					EESLog.Warning("Manning turret relocation failed; original turret retained: " + turret.def.defName + " @" + oldPosition + ".");
					return false;
				}
			}
		}
		return false;
	}

	/// <summary>
	/// Re-evaluates mannable turret interaction cells after a new stronghold has finished placing
	/// walls, doors, roads and garrison. FinalizeInit runs before those generators, so generation
	/// must perform one explicit final pass as well.
	/// </summary>
	public void RepairTurretManningCells()
	{
		if (map?.Parent is Settlement && map.ParentFaction != null && map.ParentFaction != Faction.OfPlayer)
		{
			eesStrongholdMap = true;
			RestoreTurretManningCells();
		}
	}

	private bool ValidTurretManningCell(Thing turret, IntVec3 cell)
	{
		return cell.InBounds(map) && cell.Standable(map) && cell.GetEdifice(map) == null && turret.OccupiedRect().ExpandedBy(4).Contains(cell);
	}

	public bool TryGetTurretManningCell(Thing turret, out IntVec3 cell)
	{
		cell = IntVec3.Invalid;
		return eesStrongholdMap && turret.Faction == map.ParentFaction && turretManningCells.TryGetValue(turret.thingIDNumber, out cell) &&
			ValidTurretManningCell(turret, cell);
	}

	private void FixCommanderAssaultLord()
	{
		Pawn pawn = commander;
		if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Faction == null)
		{
			return;
		}
		Lord lord = pawn.GetLord();
		if (lord == null || lord.LordJob == null || lord.LordJob.GetType() != typeof(LordJob_DefendBase) || lord.ownedPawns == null || lord.ownedPawns.Count != 1)
		{
			return;
		}
		string text = ((lord.CurLordToil != null) ? lord.CurLordToil.GetType().Name : "(없음)");
		bool flag = lord.CurLordToil is LordToil_AssaultColony;
		try
		{
			lord.Notify_PawnLost(pawn, PawnLostCondition.ForcedToJoinOtherLord);
			LordMaker.MakeNewLord(pawn.Faction, new LordJob_EESCommanderDefend(pawn.Faction, map.Center, 999999), map, Gen.YieldSingle(pawn));
			string arg = InterruptStaleJobs(new List<Pawn> { pawn });
			EESLog.Message("[지휘관 로드 교정] 거점=" + ((map.Parent != null) ? map.Parent.LabelCap.ToString() : map.ToString()) + " 지휘관=" + pawn.LabelShortCap + " 세력=" + pawn.Faction.Name + " 종전=LordJob_DefendBase/" + text + " " + $"돌격중={flag} 인원=1 → LordJob_EESCommanderDefend(공세 전환 제거) 잡중단={arg}.");
		}
		catch (Exception ex)
		{
			EESLog.Warning("FixCommanderAssaultLord " + pawn.LabelShortCap + ": " + ex.Message);
		}
	}

	private void FixStaleDefendDuty()
	{
		if ((defendDutyFixDone && defendDutyFixLogged) || (defendDutyFixDone && !EESLog.Verbose))
		{
			return;
		}
		try
		{
			bool flag = !defendDutyFixDone;
			defendDutyFixDone = true;
			DutyDef duty = EESDefendDuty.Duty;
			List<Lord> list = map.lordManager?.lords;
			if (list == null)
			{
				return;
			}
			int num = 0;
			int num2 = 0;
			int num3 = 0;
			int num4 = 0;
			Dictionary<string, int> dictionary = new Dictionary<string, int>();
			for (int i = 0; i < list.Count; i++)
			{
				Lord lord = list[i];
				if (!(lord?.CurLordToil is LordToil_EESDefendBase lordToil_EESDefendBase) || lord.ownedPawns == null)
				{
					continue;
				}
				num++;
				List<Pawn> list2 = new List<Pawn>();
				for (int j = 0; j < lord.ownedPawns.Count; j++)
				{
					Pawn pawn = lord.ownedPawns[j];
					if (pawn != null && pawn.Spawned && !pawn.Dead && pawn.mindState != null)
					{
						num2++;
						PawnDuty duty2 = pawn.mindState.duty;
						if (duty2 == null || duty2.def != duty)
						{
							list2.Add(pawn);
							string key = duty2?.def?.defName ?? "(듀티 없음)";
							dictionary[key] = ((!dictionary.TryGetValue(key, out var value)) ? 1 : (value + 1));
						}
					}
				}
				num3 += list2.Count;
				if (list2.Count != 0 && flag)
				{
					lordToil_EESDefendBase.UpdateAllDuties();
					string text = InterruptStaleJobs(list2);
					num4 += list2.Count;
					EESLog.Message("[방어 듀티 교정] 거점=" + ((map.Parent != null) ? map.Parent.LabelCap.ToString() : map.ToString()) + " 세력=" + (lord.faction?.Name ?? "(없음)") + " Lord=" + lord.LordJob?.GetType().Name + " " + $"{list2.Count}/{lord.ownedPawns.Count}명 → {duty.defName} 잡중단={text}.");
				}
			}
			if (!EESLog.Verbose)
			{
				return;
			}
			defendDutyFixLogged = true;
			List<string> list3 = new List<string>();
			foreach (KeyValuePair<string, int> item in dictionary)
			{
				list3.Add($"{item.Key} {item.Value}명");
			}
			list3.Sort();
			EESLog.Message("[방어 듀티 점검] 거점=" + ((map.Parent != null) ? map.Parent.LabelCap.ToString() : map.ToString()) + " " + $"검사 Lord={num}개/폰 {num2}명 · " + string.Format("구 듀티 {0}명{1} · ", num3, (list3.Count > 0) ? ("(" + string.Join(" · ", list3) + ")") : "") + $"이미 정상 {num2 - num3}명 · 교정 {num4}명 · " + "듀티=" + duty.defName + "(표적 획득 65, 바닐라 DefendBase=25) · " + (flag ? "교정=이번 회" : "교정=이미 완료(로그를 나중에 켜 점검만)") + ".");
		}
		catch (Exception ex)
		{
			EESLog.Warning("FixStaleDefendDuty: " + ex.Message);
		}
	}

	private void TickVaultSweep(int now)
	{
		try
		{
			if (!(map.Parent is Settlement { Faction: not null } settlement) || settlement.Faction == Faction.OfPlayer)
			{
				return;
			}
			StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
			if (strongholdData?.sealedRects != null && strongholdData.sealedRects.Count != 0)
			{
				CellRect baseRect = ((eesBaseRect.Width > 0) ? eesBaseRect : CellRect.CenteredOn(map.Center, 30).ClipInsideMap(map));
				(int inside, int moved) tuple = StrongholdGarrisonUtility.EvictPawnsFromSealedRooms(map, settlement.Faction, strongholdData, baseRect, "상시");
				int item = tuple.inside;
				int item2 = tuple.moved;
				bool flag = lastVaultSweepBeatTick < 0 || now - lastVaultSweepBeatTick >= 2500;
				if (item > 0 || flag)
				{
					lastVaultSweepBeatTick = now;
					EESLog.Message("Vault sweep [" + settlement.Faction.Name + "] " + settlement.LabelCap + ": " + $"봉인방 {strongholdData.sealedRects.Count}개 · 안 {item}명 → 퇴거 {item2}명(실패 {item - item2}명) · " + "기지rect=" + ((eesBaseRect.Width > 0) ? eesBaseRect.ToString() : "없음(맵중앙 폴백)") + " · " + $"주기 {250}틱 · 0명보고 {2500}틱마다.");
				}
			}
		}
		catch (Exception ex)
		{
			EESLog.Warning("Vault sweep: " + ex.Message);
		}
	}

	public void RegisterCommander(Pawn pawn)
	{
		commander = pawn;
		commanderRegistered = pawn != null;
		commanderDead = false;
		commanderRegisteredTick = Find.TickManager.TicksGame;
		offMapSinceTick = -1;
		EESLog.Message("Siege " + ((map.Parent != null) ? map.Parent.LabelCap : map.ToString()) + ": commander registered (" + pawn?.LabelShortCap + ").");
	}

	public void RegisterSecurityStructures(List<Thing> structures)
	{
		securityStructures = ((structures != null) ? new HashSet<Thing>(structures) : new HashSet<Thing>());
		securityActive = securityStructures.Count > 0;
		if (securityActive)
		{
			AnySecurityActive = true;
		}
	}

	public bool SecurityBuffActive(Thing t)
	{
		if (securityActive && securityStructures != null)
		{
			return securityStructures.Contains(t);
		}
		return false;
	}

	private void UnlockVault()
	{
		securityActive = false;
		if (securityStructures == null)
		{
			return;
		}
		int num = 0;
		foreach (Thing item in securityStructures.ToList())
		{
			if (item != null && item.Spawned && item.def != null)
			{
				bool isDoor = item.def.IsDoor;
				bool flag = (item as ThingWithComps)?.TryGetComp<CompHackable>() != null;
				if (isDoor || flag)
				{
					item.Destroy();
					num++;
				}
			}
		}
		EESLog.Message($"Siege: security chief lost -> vault HP×5 off, {num} research door(s) unlocked.");
	}

	private void DestroyCommanderShieldBelt()
	{
		if (commander?.apparel == null)
		{
			return;
		}
		List<Apparel> wornApparel = commander.apparel.WornApparel;
		if (wornApparel == null)
		{
			return;
		}
		for (int num = wornApparel.Count - 1; num >= 0; num--)
		{
			Apparel apparel = wornApparel[num];
			if (apparel?.def != null && apparel.def.defName == "EES_CommanderShieldBelt")
			{
				commander.apparel.Remove(apparel);
				if (!apparel.Destroyed)
				{
					apparel.Destroy();
				}
				EESLog.Message("Siege: commander shield belt destroyed (commander down/dead) -> not lootable.");
			}
		}
	}

	private bool AnyPlayerPawnOnMap()
	{
		List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
		if (list != null && list.Count > 0)
		{
			return true;
		}
		return EESHostility.AnyEnemyOf(map, EESHostility.OwnerOf(map));
	}

	private bool CommanderFunctional(out string reason)
	{
		reason = "ok";
		if (!commanderRegistered)
		{
			reason = "no-commander";
			return true;
		}
		if (commander == null)
		{
			reason = "ref-null";
			return false;
		}
		if (commander.Dead)
		{
			reason = "dead";
			return false;
		}
		if (commander.Destroyed)
		{
			reason = "destroyed";
			return false;
		}
		if (!commander.Spawned)
		{
			reason = "off-map";
			return false;
		}
		if (commander.Downed)
		{
			reason = "downed";
			return false;
		}
		return true;
	}

	private bool Applies(out Settlement settlement, out Faction faction, out StrongholdData data)
	{
		settlement = map.Parent as Settlement;
		faction = null;
		data = null;
		if (settlement == null)
		{
			return false;
		}
		faction = settlement.Faction;
		if (!EESHostility.SystemsActive(map, faction))
		{
			return false;
		}
		data = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
		if (data == null)
		{
			return false;
		}
		if (!data.hasCommsRoom && !commanderRegistered && !(FinalStandUtility.Active(data) && data.finalStandBudgetVersion > 0))
		{
			return gravshipAttack;
		}
		return true;
	}

	public override void MapComponentTick()
	{
		base.MapComponentTick();
		int ticksGame = Find.TickManager.TicksGame;
		if (ticksGame % 250 != 0)
		{
			return;
		}
		if (!eesStrongholdMap && map.Parent is Settlement settlement && WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID) != null)
		{
			eesStrongholdMap = true;
		}
		if (map.Parent is Settlement settlement2 && EESHostility.SystemsActive(map, settlement2.Faction))
		{
			StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetExisting(settlement2.ID);
			if (strongholdData != null && strongholdData.groupPlanDecided)
			{
				TickGarrisonGroups(ticksGame, settlement2.Faction, strongholdData, settlement2);
			}
		}
		TryNeutralizeTrapsAfterVictory();
		CryoBarracksUtility.TickCryo(this, map);
		FixCommanderAssaultLord();
		FixStaleDefendDuty();
		TickVaultSweep(ticksGame);
		if (!Enabled || !Applies(out var settlement3, out var faction, out var data))
		{
			return;
		}
		if (isPolluted)
		{
			List<Pawn> list = new List<Pawn>(map.mapPawns.SpawnedPawnsInFaction(faction));
			for (int i = 0; i < list.Count; i++)
			{
				PollutedOutpostUtility.EnsurePawnPolluted(list[i]);
			}
		}
		TickObserver(ticksGame, data);
		if (isBattlefield)
		{
			TickBattlefield(ticksGame, settlement3, faction, data);
		}
		if (!data.vaultValueAnnounced && data.vaultValue > 0f)
		{
			data.vaultValueAnnounced = true;
			Messages.Message(EESText.T("EES.Vault.Value", settlement3.LabelCap, data.vaultValue.ToString("F0")), new TargetInfo(map.Center, map), MessageTypeDefOf.NeutralEvent, historical: false);
		}
		string reason;
		bool flag = CommanderFunctional(out reason);
		if (flag)
		{
			offMapSinceTick = -1;
		}
		else if (reason == "off-map" && offMapSinceTick < 0)
		{
			offMapSinceTick = ticksGame;
		}
		bool flag2 = reason == "off-map" && commanderRegisteredTick >= 0 && ticksGame - commanderRegisteredTick >= 1000 && offMapSinceTick >= 0 && ticksGame - offMapSinceTick >= 600 && AnyPlayerPawnOnMap();
		if (commanderRegistered && !commanderDead && !flag && (reason != "off-map" || flag2))
		{
			commanderDead = true;
			reinforceActive = false;
			int num = ((data.commanderType == CommanderType.Commander) ? CommanderUtility.ApplyCommanderDeathMorale(map, faction) : 0);
			Messages.Message(EESText.T("EES.Cmd.Killed"), new TargetInfo(map.Center, map), MessageTypeDefOf.PositiveEvent, historical: false);
			EESLog.Message($"Siege {settlement3.LabelCap}: COMMANDER LOST ({reason}) type={data.commanderType} " + string.Format("-> {0} pawns panicked{1}.", num, (data.commanderType == CommanderType.Commander) ? "" : " (이 타입은 패닉 없음)"));
			CommanderEffects.OnCommanderDeath(map, data, faction, commander);
			if (data.commanderType == CommanderType.Security)
			{
				UnlockVault();
			}
			if (data.commanderType == CommanderType.Defense)
			{
				CommanderEffects.RemoveCommanderShield(commander);
				DestroyCommanderShieldBelt();
			}
		}
		if (StrongholdMod.Settings != null && StrongholdMod.Settings.debugLogging && commanderRegistered)
		{
			string text = $"dead={commanderDead} null={commander == null} d={commander?.Dead} " + $"destr={commander?.Destroyed} downed={commander?.Downed} spawned={commander?.Spawned} " + $"reinfActive={reinforceActive} next={nextReinforceTick}";
			if (text != lastCmdDiagSig)
			{
				lastCmdDiagSig = text;
				EESLog.Message("Siege " + settlement3.LabelCap + " cmd-diag: " + text + ".");
			}
		}
		cachedData = data;
		cachedType = data.commanderType;
		if (commanderRegistered)
		{
			string reason2;
			bool functional = CommanderFunctional(out reason2);
			CommanderEffects.TickAuras(map, data, faction, commander, functional);
		}
		bool flag3 = cachedType == CommanderType.Fanatic && commander != null && commander.Spawned && !commander.Dead && !commander.Downed;
		if (cachedType == CommanderType.Fanatic)
		{
			string text2 = $"type={cachedType} cmdNull={commander == null} dead={commander?.Dead} " + $"downed={commander?.Downed} spawned={commander?.Spawned} alive={flag3} " + $"commanderDeadLatch={commanderDead} now={ticksGame} next={nextFanaticTick}";
			if (text2 != lastResurrectDiag)
			{
				lastResurrectDiag = text2;
				EESLog.Message("Siege " + settlement3.LabelCap + " 부활-gate: " + text2 + ".");
			}
		}
		if (flag3)
		{
			if (nextFanaticTick < 0)
			{
				nextFanaticTick = ticksGame;
			}
			if (ticksGame >= nextFanaticTick)
			{
				bool flag4 = CommanderEffects.ResurrectWave(map, data, faction);
				int num2 = ((isBattlefield && bfEntryTick >= 0 && ticksGame < bfEntryTick + 45000) ? 5000 : 15000);
				nextFanaticTick = ticksGame + (flag4 ? num2 : 300);
			}
		}
		if (!AnyPlayerCombatant())
		{
			reinforceActive = false;
			return;
		}
		if (isBattlefield)
		{
			reinforceActive = false;
			return;
		}
		if (RimPactsCompat.IsSpectateBattle(settlement3))
		{
			reinforceActive = false;
			return;
		}
		if (FinalStandUtility.Active(data) && data.finalStandBudgetVersion > 0)
		{
			reinforceActive = false;
			if (commanderRegistered && !CommanderFunctional(out _)) return;
			TickFinalStandWaves(ticksGame, settlement3, data);
			return;
		}
		bool flag5 = CommanderEffects.ReinforcementCommander(data);
		string reason3;
		bool flag6 = flag5 && CommanderFunctional(out reason3);
		if (!flag6 && !gravshipAttack)
		{
			reinforceActive = false;
			if (flag5 && !announcedBlocked)
			{
				announcedBlocked = true;
				Messages.Message(EESText.T("EES.Reinf.BlockedCmd"), new TargetInfo(map.Center, map), MessageTypeDefOf.PositiveEvent, historical: false);
				EESLog.Message("Siege " + settlement3.LabelCap + ": reinforcements blocked (comms officer down). announced.");
			}
			return;
		}
		reinforceActive = true;
		if (flag6 && !announcedActive)
		{
			announcedActive = true;
			string text3 = ReinforceMinTicks.ToStringTicksToPeriod() + "~" + ReinforceMaxTicks.ToStringTicksToPeriod();
			Messages.Message(EESText.T("EES.Reinf.Active", faction.Name, text3), new TargetInfo(map.Center, map), MessageTypeDefOf.ThreatSmall, historical: false);
			EESLog.Message("Siege " + settlement3.LabelCap + ": comms online, reinforcement subsystem active (interval=" + text3 + " random).");
		}
		if (nextReinforceTick < 0)
		{
			if (gravshipAttack)
			{
				nextReinforceTick = (flag6 ? ticksGame : (ticksGame + 15000));
				EESLog.Message($"Siege {settlement3.LabelCap}: ★중력부양선 증원 예약 (통신장교={flag6}, " + "도착 " + (flag6 ? "즉시" : 15000.ToStringTicksToPeriod()) + ").");
			}
			else
			{
				nextReinforceTick = ticksGame + StrongholdFeatureEffects.ReinforcementInterval(map, data, NextReinforceInterval());
				EESLog.Message($"Siege {settlement3.LabelCap}: next reinforcement scheduled at tick {nextReinforceTick} " + $"(now {ticksGame}, in {(nextReinforceTick - ticksGame).ToStringTicksToPeriod()}).");
			}
		}
		if (ticksGame < nextReinforceTick)
		{
			return;
		}
		EESLog.Message($"Siege {settlement3.LabelCap}: 증원 파 발사 — gravship={gravshipAttack} cmdOk={flag6} " + string.Format("형태={0} now={1}.", gravshipAttack ? "DROP(중력부양선대응)" : "EDGE(정상)", ticksGame));
		if (gravshipAttack)
		{
			GravshipReinforceUtility.SpawnResponseWave(map, settlement3, faction, data);
		}
		else
		{
			SpawnWave(settlement3, faction, data);
		}
		if (gravshipAttack)
		{
			gravshipAttack = false;
			if (!flag6)
			{
				reinforceActive = false;
				nextReinforceTick = -1;
				EESLog.Message("Siege " + settlement3.LabelCap + ": 중력부양선 일회성 증원 완료(통신장교 없음) → 반복 없음, 종료.");
				return;
			}
			EESLog.Message("Siege " + settlement3.LabelCap + ": 중력부양선 첫 파(즉시) 완료(통신장교 있음) → 이후 정상 반복 전환.");
		}
		nextReinforceTick = ticksGame + StrongholdFeatureEffects.ReinforcementInterval(map, data, NextReinforceInterval());
	}

	private void TickFinalStandWaves(int now, Settlement settlement, StrongholdData data)
	{
		if (finalStandWavesSpawned >= 2) { nextFinalStandWaveTick = -1; return; }
		if (nextFinalStandWaveTick < 0) { nextFinalStandWaveTick = now + 2500; return; }
		if (now < nextFinalStandWaveTick) return;
		int count = FinalStandUtility.TrySpawnFiniteWave(map, settlement, data, out var reason);
		finalStandWaveReason = reason;
		if (count > 0)
		{
			finalStandWavesSpawned++;
			Messages.Message(EESText.T("EES.Reinf.Wave", count), new TargetInfo(map.Center, map), MessageTypeDefOf.ThreatBig, historical: false);
		}
		nextFinalStandWaveTick = finalStandWavesSpawned >= 2 ? -1 : now + 2500;
		EESLog.Message($"Final stand finite wave {finalStandWavesSpawned}/2: {reason}; next={nextFinalStandWaveTick}.");
	}

	private bool AnyPlayerCombatant()
	{
		List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
		for (int i = 0; i < list.Count; i++)
		{
			Pawn pawn = list[i];
			if (pawn != null && !pawn.Dead && !pawn.Downed)
			{
				return true;
			}
		}
		return EESHostility.AnyEnemyOf(map, EESHostility.OwnerOf(map));
	}

	private bool AnyAwakeHostilePawn()
	{
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn != null && !pawn.Dead && !pawn.Downed && pawn.HostileTo(Faction.OfPlayer))
			{
				CompCanBeDormant compCanBeDormant = pawn.TryGetComp<CompCanBeDormant>();
				if (compCanBeDormant == null || compCanBeDormant.Awake)
				{
					return true;
				}
			}
		}
		return false;
	}

	private int NeutralizeEnemyTraps()
	{
		List<Thing> list = map.listerThings.AllThings.Where((Thing t) => t is Building_Trap && t.Faction != null && t.Faction != Faction.OfPlayer && t.Faction.HostileTo(Faction.OfPlayer)).ToList();
		int num = 0;
		for (int num2 = 0; num2 < list.Count; num2++)
		{
			Thing thing = list[num2];
			if (thing != null && thing.Spawned && !thing.Destroyed)
			{
				thing.Destroy();
				num++;
			}
		}
		return num;
	}

	private void TryNeutralizeTrapsAfterVictory()
	{
		if (trapsNeutralized || !(map.Parent is Settlement { Faction: not null } settlement) || !settlement.Faction.HostileTo(Faction.OfPlayer) || !AnyPlayerCombatant())
		{
			return;
		}
		if (AnyAwakeHostilePawn())
		{
			sawHostiles = true;
		}
		else if (sawHostiles)
		{
			int num = NeutralizeEnemyTraps();
			trapsNeutralized = true;
			if (num > 0)
			{
				Messages.Message(EESText.T("EES.Traps.Cleared", num), new TargetInfo(map.Center, map), MessageTypeDefOf.PositiveEvent, historical: false);
				EESLog.Message($"Siege {settlement.LabelCap}: base cleared -> neutralized {num} enemy trap(s) (post-victory safety).");
			}
			else
			{
				EESLog.Message("Siege " + settlement.LabelCap + ": base cleared -> no enemy traps to neutralize.");
			}
		}
	}

	private void SpawnWave(Settlement settlement, Faction faction, StrongholdData data)
	{
		try
		{
			float num = StrongholdMod.Settings?.reinforceSizeFraction ?? 0.25f;
			int num2 = StrongholdMod.Settings?.reinforceMaxPoints ?? 2000;
			float num3 = Mathf.Clamp((float)data.militaryPower * num, 150f, num2);
			IncidentParms parms = new IncidentParms
			{
				target = map,
				faction = faction,
				points = num3
			};
			List<Pawn> list = FinalStandUtility.LimitReinforcementPawns(map, faction, data,
				PawnGroupMakerUtility.GeneratePawns(IncidentParmsUtility.GetDefaultPawnGroupMakerParms(PawnGroupKindDefOf.Combat, parms, ensureCanGenerateAtLeastOnePawn: true)));
			if (list.Count == 0)
			{
				EESLog.Message($"Siege {settlement.LabelCap}: wave generated 0 pawns (points={num3:0}). skipped.");
				return;
			}
			int num4 = 0;
			foreach (Pawn item in list)
			{
				if (!RCellFinder.TryFindRandomPawnEntryCell(out var result, map, CellFinder.EdgeRoadChance_Hostile) && !CellFinder.TryFindRandomEdgeCellWith((IntVec3 c) => c.Standable(map) && !c.Fogged(map), map, CellFinder.EdgeRoadChance_Hostile, out result))
				{
					if (!item.Destroyed)
					{
						Find.WorldPawns.PassToWorld(item);
					}
				}
				else
				{
					GenSpawn.Spawn(item, result, map, Rot4.Random);
					num4++;
				}
			}
			if (num4 == 0)
			{
				EESLog.Message("Siege " + settlement.LabelCap + ": wave found no entry cells. skipped.");
				return;
			}
			LordMaker.MakeNewLord(faction, new LordJob_AssaultColony_NoFlee(faction, canKidnap: false, canTimeoutOrFlee: false, sappers: false), map, list.Where((Pawn p) => p.Spawned));
			StrongholdFeatureEffects.ApplyToPawns(map, faction, data, list.Where(p => p.Spawned));
			Messages.Message(EESText.T("EES.Reinf.Wave", num4), new TargetInfo(list.First((Pawn p) => p.Spawned).Position, map), MessageTypeDefOf.ThreatBig, historical: false);
			EESLog.Message($"Siege {settlement.LabelCap}: REINFORCEMENT WAVE spawned {num4} pawns " + $"(points={num3:0}, mil={data.militaryPower}).");
		}
		catch (Exception arg)
		{
			EESLog.Error($"Siege SpawnWave {settlement?.LabelCap}: {arg}");
		}
	}

	private void SpawnHeraldReinforcementDrop(Settlement settlement, Faction faction, StrongholdData data)
	{
		try
		{
			int num = StrongholdMod.Settings?.reinforceMaxPoints ?? 2000;
			float num2 = Mathf.Clamp((float)data.militaryPower * 0.4f, 150f, num);
			IncidentParms parms = new IncidentParms
			{
				target = map,
				faction = faction,
				points = num2
			};
			List<Pawn> list = FinalStandUtility.LimitReinforcementPawns(map, faction, data,
				PawnGroupMakerUtility.GeneratePawns(IncidentParmsUtility.GetDefaultPawnGroupMakerParms(PawnGroupKindDefOf.Combat, parms, ensureCanGenerateAtLeastOnePawn: true)));
			if (list.Count == 0)
			{
				EESLog.Message($"Herald drop {settlement?.LabelCap}: GeneratePawns=0 (points={num2:0}). skipped.");
				return;
			}
			if ((int)(faction.def?.techLevel ?? TechLevel.Industrial) >= 4)
			{
				IntVec3 intVec = ((eesBaseRect.Width > 0) ? eesBaseRect.CenterCell : map.Center);
				LordMaker.MakeNewLord(faction, new LordJob_AssaultColony_NoFlee(faction, canKidnap: false, canTimeoutOrFlee: false, sappers: false), map, list);
				DropPodUtility.DropThingsNear(intVec, map, list.Cast<Thing>(), 110, canInstaDropDuringInit: false, leaveSlag: false, canRoofPunch: true, forbid: false);
				Messages.Message(EESText.T("EES.Group.HeraldDrop", faction.Name, list.Count), new TargetInfo(intVec, map), MessageTypeDefOf.ThreatBig, historical: false);
				EESLog.Message($"GarrisonGroups: 전령 증원 DROP [{faction.Name}] {list.Count}명 @중앙{intVec} " + $"(points={num2:0}, mil={data.militaryPower:0}).");
				return;
			}
			IntVec3 intVec2 = IntVec3.Invalid;
			List<Pawn> list2 = new List<Pawn>();
			foreach (Pawn item in list)
			{
				if (!RCellFinder.TryFindRandomPawnEntryCell(out var result, map, CellFinder.EdgeRoadChance_Hostile))
				{
					StrongholdGarrisonUtility.DiscardUnspawnedPawn(item);
					continue;
				}
				GenSpawn.Spawn(item, result, map, Rot4.Random);
				list2.Add(item);
				if (!intVec2.IsValid)
				{
					intVec2 = result;
				}
			}
			if (list2.Count == 0)
			{
				EESLog.Message("Herald drop " + settlement?.LabelCap + ": 엣지 진입셀 0 → 증원 없음.");
				return;
			}
			LordMaker.MakeNewLord(faction, new LordJob_AssaultColony_NoFlee(faction, canKidnap: false, canTimeoutOrFlee: false, sappers: false), map, list2);
			Messages.Message(EESText.T("EES.Group.HeraldDrop", faction.Name, list2.Count), new TargetInfo(intVec2, map), MessageTypeDefOf.ThreatBig, historical: false);
			EESLog.Message($"GarrisonGroups: 전령 증원 EDGE [{faction.Name}] {list2.Count}명 @{intVec2} (points={num2:0}).");
		}
		catch (Exception arg)
		{
			EESLog.Error($"Siege SpawnHeraldReinforcementDrop {settlement?.LabelCap}: {arg}");
		}
	}

	private void TickObserver(int now, StrongholdData data)
	{
		if (data == null || data.commanderType != CommanderType.Observer || !commanderRegistered || commanderDead || commander == null || commander.Dead)
		{
			if (observerTarget != null)
			{
				EESLog.Message($"Observer 종료 [{data?.commanderType}]: 표적 해제 — cmdRegistered={commanderRegistered} cmdDead={commanderDead} cmdNull={commander == null} aliveDead={commander?.Dead}.");
				observerTarget = null;
			}
			return;
		}
		if (observerNextPickTick < 0)
		{
			int num = ((commanderRegisteredTick >= 0) ? commanderRegisteredTick : now);
			observerNextPickTick = num + 25000;
			EESLog.Message($"Observer 가동 [{commander?.LabelShortCap}]: 최초 표적까지 {observerNextPickTick - now}틱(약 {(float)(observerNextPickTick - now) / 2500f:F1}h). startRef={num}.");
		}
		bool flag = observerTarget == null || observerTarget.Dead || !observerTarget.Spawned || observerTarget.Map != map;
		if (observerStarted && flag && now < observerNextPickTick)
		{
			EESLog.Message($"Observer 표적 무효(dead/despawn/타맵) → 즉시 재지정(was nextPick +{observerNextPickTick - now}틱).");
			observerNextPickTick = now;
		}
		if (now >= observerNextPickTick)
		{
			int candCount;
			Pawn pawn = PickObserverTarget(out candCount);
			if (pawn != null)
			{
				bool flag2 = pawn != observerTarget;
				observerTarget = pawn;
				observerStarted = true;
				AnyObserverActive = true;
				observerNextPickTick = now + 25000;
				EESLog.Message($"Observer 표적 지정: {pawn.LabelShortCap} (플레이어 폰 후보 {candCount}명, isNew={flag2}, 다음 +{25000}틱).");
				Messages.Message(EESText.T("EES.Observer.Target", pawn.LabelShortCap), pawn, MessageTypeDefOf.ThreatSmall, historical: false);
			}
			else
			{
				if (observerTarget != null)
				{
					EESLog.Message($"Observer 표적 후보 0(플레이어 폰 없음) → 표적 해제, {250}틱 후 재시도.");
				}
				observerTarget = null;
				observerNextPickTick = now + 250;
			}
		}
		LogObserverFocus();
	}

	private Pawn PickObserverTarget(out int candCount)
	{
		List<Pawn> list = EESHostility.EnemyPawnsOf(map, EESHostility.OwnerOf(map));
		candCount = list.Count;
		if (list.Count <= 0)
		{
			return null;
		}
		return list.RandomElement();
	}

	private void LogObserverFocus()
	{
		Pawn pawn = observerTarget;
		if (pawn == null)
		{
			return;
		}
		Faction parentFaction = map.ParentFaction;
		if (parentFaction == null)
		{
			return;
		}
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int num6 = 0;
		List<Pawn> list = new List<Pawn>(map.mapPawns.SpawnedPawnsInFaction(parentFaction));
		for (int i = 0; i < list.Count; i++)
		{
			Pawn pawn2 = list[i];
			if (pawn2 == null || !pawn2.RaceProps.Humanlike)
			{
				continue;
			}
			Lord lord = pawn2.GetLord();
			if (lord != null && lord.LordJob is LordJob_ManTurrets)
			{
				num4++;
				continue;
			}
			Thing thing = pawn2.mindState?.enemyTarget;
			if (thing == pawn)
			{
				num++;
			}
			else if (thing != null)
			{
				num2++;
			}
			else
			{
				num3++;
			}
			if (pawn2.CanReach(pawn, PathEndMode.Touch, Danger.Deadly))
			{
				num5++;
			}
			else
			{
				num6++;
			}
		}
		string text = $"{num}/{num2}/{num3}/{num4}/{num5}/{num6}";
		if (!(text == lastObserverDiag))
		{
			lastObserverDiag = text;
			EESLog.Message($"Observer focus [{parentFaction.Name}] 표적={pawn.LabelShortCap}: onTarget={num} other={num2} none={num3} 사수={num4} " + $"| 표적도달 가능={num5} 불가(벽돌파)={num6} (other>0=집중미작동 / canReach 높은데 안 움직이면 leash 추격 작동중).");
		}
	}

	public override void MapComponentUpdate()
	{
		base.MapComponentUpdate();
		Pawn pawn = observerTarget;
		if (pawn != null && pawn.Spawned && !pawn.Dead && pawn.Map == map)
		{
			Material material = ObserverTargetMat;
			if (!(material == null))
			{
				Vector3 drawPos = pawn.DrawPos;
				drawPos.y = AltitudeLayer.MetaOverlays.AltitudeFor();
				drawPos += new Vector3(0f, 0f, 1f);
				Matrix4x4 matrix = Matrix4x4.TRS(drawPos, Quaternion.identity, new Vector3(0.8f, 1f, 0.8f));
				Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
			}
		}
	}

	private void TickBattlefield(int now, Settlement settlement, Faction b, StrongholdData data)
	{
		if (!bfSelected)
		{
			bfSelected = true;
			bfEntryTick = now;
			BattlefieldUtility.SelectFactions(b, settlement.ID, out var c, out var d);
			bfFactionC = c?.loadID ?? (-1);
			bfFactionD = d?.loadID ?? (-1);
			bfActive = c != null;
			if (bfActive)
			{
				BattlefieldParticipants.Add(b.loadID);
				if (c != null)
				{
					BattlefieldParticipants.Add(c.loadID);
				}
				if (d != null)
				{
					BattlefieldParticipants.Add(d.loadID);
				}
			}
			EESLog.Message(string.Format("Battlefield 시작 [{0} T{1}]: C={2} ", b.Name, data.tier, (c != null) ? c.Name : "(없음)") + string.Format("D={0} active={1} entry={2}", (d != null) ? d.Name : "(없음)", bfActive, bfEntryTick) + ((!bfActive) ? " → C 없음, 전장 이벤트 미작동" : "") + ".");
			if (bfActive)
			{
				CellRect rect = ((eesBaseRect.Width > 0) ? eesBaseRect : CellRect.CenteredOn(map.Center, 20));
				int num = StrongholdGarrisonUtility.RelordDefendersNoSally(map, b, rect);
				EESLog.Message($"Battlefield B 미출격 재배정 [{b.Name}]: {num}명 → DefendBase(출격안함).");
			}
		}
		if (!bfActive)
		{
			return;
		}
		DisableBattlefieldFlee(b);
		if (EESLog.Verbose && now % 1250 < 250)
		{
			StrongholdGarrisonUtility.LogGunnerState(map, b, $"전장틱+{(float)(now - bfEntryTick) / 2500f:0.0}h");
		}
		if (EESLog.Verbose && now % 1250 < 250)
		{
			LogBattlefieldRetreatScan(b);
		}
		CellRect cellRect = ((eesBaseRect.Width > 0) ? eesBaseRect : CellRect.CenteredOn(map.Center, 20));
		EnforceBattlefieldNoExit(b, cellRect);
		while (bfNextEvent < BfTick.Length && now >= bfEntryTick + BfTick[bfNextEvent])
		{
			int num2 = bfNextEvent;
			bfNextEvent++;
			char c2 = BfActor[num2];
			Faction faction = c2 switch
			{
				'D' => FactionByLoadId(bfFactionD),
				'C' => FactionByLoadId(bfFactionC),
				_ => b,
			};
			float num3 = (float)BfTick[num2] / 2500f;
			if (faction == null)
			{
				EESLog.Message($"Battlefield 이벤트#{num2}(+{num3:0}h, {c2}) SKIP: 세력 없음(D 미존재 등, #7).");
				continue;
			}
			float num4 = ((c2 == 'B') ? 0.8f : 1f);
			float num5 = Mathf.Max(100f, (float)data.militaryPower * num4);
			int num6 = BattlefieldSpawnUtility.SpawnWave(map, faction, num5, BfWave[num2], cellRect, BfNear[num2]);
			EESLog.Message($"Battlefield 이벤트#{num2} +{num3:0}h [{c2}={faction.Name} {BfWave[num2]} nearWall={BfNear[num2]} ×{num4}]: {num6}명 (pts={num5:0}).");
		}
		ConvertArrivedAttackersToHold(cellRect);
	}

	private void ConvertArrivedAttackersToHold(CellRect rect)
	{
		IntVec3 center = ((rect.Width > 0) ? rect.CenterCell : map.Center);
		int num = Mathf.Max(12, Mathf.Min(rect.Width, rect.Height) / 3);
		int r = num * num;
		ConvertFactionToHold(FactionByLoadId(bfFactionC), center, r);
		ConvertFactionToHold(FactionByLoadId(bfFactionD), center, r);
	}

	private void ConvertFactionToHold(Faction f, IntVec3 center, int r2)
	{
		if (f == null)
		{
			return;
		}
		List<Lord> list = new List<Lord>();
		foreach (Pawn item in map.mapPawns.SpawnedPawnsInFaction(f))
		{
			Lord lord = item.GetLord();
			if (lord != null && (lord.LordJob is LordJob_AssaultThings_NoExit || lord.LordJob is LordJob_AssaultColony_NoFlee) && (item.Position - center).LengthHorizontalSquared <= r2 && !list.Contains(lord))
			{
				list.Add(lord);
			}
		}
		for (int i = 0; i < list.Count; i++)
		{
			List<Pawn> list2 = new List<Pawn>(list[i].ownedPawns);
			for (int j = 0; j < list2.Count; j++)
			{
				list2[j].GetLord()?.RemovePawn(list2[j]);
			}
			LordMaker.MakeNewLord(f, new LordJob_DefendBase_NoFlee(f, center, 2000000000), map, list2);
			EESLog.Message($"Battlefield 공격대 중앙도달→hold 전환 [{f.Name}]: {list2.Count}명 → DefendBase(중앙, 추격중단).");
		}
	}

	private void DisableBattlefieldFlee(Faction b)
	{
		SetNoFlee(b);
		SetNoFlee(FactionByLoadId(bfFactionC));
		SetNoFlee(FactionByLoadId(bfFactionD));
	}

	private void SetNoFlee(Faction f)
	{
		if (f == null)
		{
			return;
		}
		List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(f);
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i]?.mindState != null && list[i].mindState.canFleeIndividual)
			{
				list[i].mindState.canFleeIndividual = false;
			}
		}
	}

	private void LogBattlefieldRetreatScan(Faction b)
	{
		LogFactionRetreat(b, "B");
		LogFactionRetreat(FactionByLoadId(bfFactionC), "C");
		LogFactionRetreat(FactionByLoadId(bfFactionD), "D");
	}

	private void LogFactionRetreat(Faction f, string tag)
	{
		if (f == null)
		{
			return;
		}
		List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(f);
		StringBuilder stringBuilder = null;
		int num = 0;
		int num2 = 0;
		for (int i = 0; i < list.Count; i++)
		{
			Pawn pawn = list[i];
			if (pawn == null || pawn.Dead || pawn.RaceProps == null || !pawn.RaceProps.Humanlike)
			{
				continue;
			}
			num++;
			string text = ((!pawn.InMentalState) ? null : pawn.MentalStateDef?.defName);
			bool flag = pawn.mindState != null && pawn.mindState.canFleeIndividual;
			bool flag2 = pawn.CurJob?.exitMapOnArrival ?? false;
			string text2 = pawn.GetLord()?.LordJob?.GetType().Name ?? "none";
			bool flag3 = text2.Contains("NoFlee") || text2.Contains("NoExit");
			string text3 = BattlefieldDiag.CeSuppressionState(pawn);
			bool flag4 = text3.Contains("SUPPRESSED") || text3.Contains("HUNKER");
			if (text != null || flag || flag2 || !flag3 || flag4)
			{
				num2++;
				if (stringBuilder == null)
				{
					stringBuilder = new StringBuilder();
				}
				stringBuilder.Append(string.Format("\n   - {0} @{1} lord={2}{3} ", pawn.LabelShort, pawn.Position, text2, flag3 ? "" : "★비EES") + "duty=" + (pawn.mindState?.duty?.def?.defName ?? "none") + " job=" + (pawn.CurJobDef?.defName ?? "none") + (flag2 ? "★EXIT" : "") + " " + string.Format("정신={0} canFleeIndiv={1}{2} CE={3}{4}", text ?? "-", flag, flag ? "★" : "", text3, flag4 ? "★" : ""));
			}
		}
		if (num2 > 0)
		{
			EESLog.Message($"[전장 퇴각진단:{tag}={f.Name}] humanlike {num}명 중 {num2}명 도주징후(★=문제):{stringBuilder}");
		}
	}

	private void EnforceBattlefieldNoExit(Faction b, CellRect rect)
	{
		EnforceNoExitFor(b, rect, attacker: false);
		EnforceNoExitFor(FactionByLoadId(bfFactionC), rect, attacker: true);
		EnforceNoExitFor(FactionByLoadId(bfFactionD), rect, attacker: true);
	}

	private void EnforceNoExitFor(Faction f, CellRect rect, bool attacker)
	{
		if (f == null)
		{
			return;
		}
		List<Pawn> list = null;
		int num = 0;
		int num2 = 0;
		List<Pawn> list2 = map.mapPawns.SpawnedPawnsInFaction(f);
		for (int i = 0; i < list2.Count; i++)
		{
			Pawn pawn = list2[i];
			if (pawn != null && !pawn.Dead && !pawn.Downed && pawn.RaceProps != null && pawn.RaceProps.Humanlike)
			{
				if (pawn.InMentalState && IsExitMentalState(pawn.MentalStateDef))
				{
					pawn.MentalState?.RecoverFromState();
					num++;
				}
				if (pawn.CurJob != null && pawn.CurJob.exitMapOnArrival && pawn.jobs != null)
				{
					pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
					num2++;
				}
				if (pawn.GetLord() == null)
				{
					(list ?? (list = new List<Pawn>())).Add(pawn);
				}
			}
		}
		if (list != null && list.Count > 0)
		{
			LordJob lordJob = (attacker ? ((LordJob)new LordJob_AssaultColony_NoFlee(f)) : ((LordJob)new LordJob_DefendBase_NoFlee(f, rect.CenterCell, 2000000000)));
			LordMaker.MakeNewLord(f, lordJob, map, list);
		}
		if ((list != null && list.Count > 0) || num > 0 || num2 > 0)
		{
			EESLog.Message($"[전장 무이탈강제] {f.Name}: 로드재배정={list?.Count ?? 0} 정신회복={num} 이탈잡중단={num2} (attacker={attacker}).");
		}
	}

	private static bool IsExitMentalState(MentalStateDef d)
	{
		if (d == null)
		{
			return false;
		}
		string defName = d.defName;
		if (!defName.Contains("Flee") && !defName.Contains("Exit") && !defName.Contains("GiveUp"))
		{
			return defName.Contains("Leave");
		}
		return true;
	}

	private static Faction FactionByLoadId(int id)
	{
		if (id < 0)
		{
			return null;
		}
		List<Faction> allFactionsListForReading = Find.FactionManager.AllFactionsListForReading;
		for (int i = 0; i < allFactionsListForReading.Count; i++)
		{
			if (allFactionsListForReading[i].loadID == id)
			{
				return allFactionsListForReading[i];
			}
		}
		return null;
	}

	private void TickGarrisonGroups(int now, Faction faction, StrongholdData data, Settlement settlement)
	{
		if (!GarrisonGroupUtility.IsApplicable(faction, data))
		{
			return;
		}
		int num = 0;
		List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(faction);
		for (int i = 0; i < list.Count; i++)
		{
			Pawn pawn = list[i];
			if (pawn != null && !pawn.Dead && !pawn.Downed && pawn.RaceProps != null && pawn.RaceProps.Humanlike)
			{
				num++;
			}
		}
		if (groupInitGarrison < 0)
		{
			groupInitGarrison = Mathf.Max(1, num);
		}
		float num2 = (float)num / (float)groupInitGarrison;
		bool flag = commanderRegistered && commanderDead;
		bool flag2 = PlayerInsideBase();
		if (EESLog.Verbose && now % 1250 < 250)
		{
			StringBuilder stringBuilder = new StringBuilder();
			foreach (Lord lord2 in map.lordManager.lords)
			{
				if (lord2 != null && lord2.faction == faction)
				{
					if (lord2.LordJob is LordJob_EESDefendGroup lordJob_EESDefendGroup)
					{
						stringBuilder.Append($"\n   [{(GarrisonGroupType)lordJob_EESDefendGroup.groupType}/Defend] alive={AliveInLord(lord2)}/{lordJob_EESDefendGroup.initialCount} thr={lordJob_EESDefendGroup.fleeThreshold:P0} fled={lordJob_EESDefendGroup.fledTriggered}" + $" down={DownedInLord(lord2)} released={lordJob_EESDefendGroup.releasedToHunt} hunt={lordJob_EESDefendGroup.huntInvaders}" + ((lordJob_EESDefendGroup.assaultTick > 0) ? $" assaultIn={lordJob_EESDefendGroup.assaultTick - now}t" : ((lordJob_EESDefendGroup.assaultDelayTicks > 0) ? $" assaultIn=미무장(적대기,+{lordJob_EESDefendGroup.assaultDelayTicks}t)" : "")) + " hold=" + ((lordJob_EESDefendGroup.holdRadius > 0f) ? lordJob_EESDefendGroup.holdPoint.ToString() : "-"));
					}
					else if (lord2.LordJob is LordJob_EESAssaultGroup lordJob_EESAssaultGroup)
					{
						stringBuilder.Append($"\n   [{(GarrisonGroupType)lordJob_EESAssaultGroup.groupType}/Assault] alive={AliveInLord(lord2)}/{lordJob_EESAssaultGroup.initialCount} thr={lordJob_EESAssaultGroup.fleeThreshold:P0} fled={lordJob_EESAssaultGroup.fledTriggered}");
					}
				}
			}
			if (stringBuilder.Length > 0)
			{
				EESLog.Message($"[그룹진단 {faction.Name}] garrison {num}/{groupInitGarrison}({num2:P0}) cmdDead={flag} playerInside={flag2}:{stringBuilder}");
			}
		}
		int num3 = 0;
		int num4 = 0;
		bool flag3 = false;
		foreach (Lord lord3 in map.lordManager.lords)
		{
			if (lord3?.faction == faction && lord3.LordJob is LordJob_EESDefendGroup { groupType: 3 })
			{
				heraldActive = true;
				num3 += AliveInLord(lord3);
			}
		}
		for (int j = 0; j < heraldRetreaters.Count; j++)
		{
			Pawn pawn2 = heraldRetreaters[j];
			if (pawn2 != null)
			{
				if (!pawn2.Dead && !pawn2.Spawned)
				{
					flag3 = true;
					num4++;
				}
				else if (!pawn2.Dead && pawn2.Spawned)
				{
					num3++;
				}
			}
		}
		if (heraldRetreaters.Count > 0)
		{
			heraldActive = true;
		}
		if (heraldActive && !heraldResolved)
		{
			if (flag3)
			{
				AlertUtility.RaiseAlert(data, settlement, 1, "herald escaped");
				Messages.Message(EESText.T("EES.Group.HeraldRetreat", faction.Name), new TargetInfo(map.Center, map), MessageTypeDefOf.NegativeEvent, historical: false);
				heraldResolved = true;
				EESLog.Message("GarrisonGroups: 전령 후퇴 성공 [" + faction.Name + "] → 경계도+1.");
			}
			else if (num3 == 0)
			{
				Messages.Message(EESText.T("EES.Group.HeraldKilled", faction.Name), new TargetInfo(map.Center, map), MessageTypeDefOf.PositiveEvent, historical: false);
				heraldResolved = true;
				EESLog.Message("GarrisonGroups: 전령 전멸(정보 차단) [" + faction.Name + "].");
			}
		}
		if (!heraldDropTriggered && num4 >= 2)
		{
			heraldDropTriggered = true;
			SpawnHeraldReinforcementDrop(settlement, faction, data);
		}
		bool flag4 = RimPactsCompat.IsSpectateBattle(map);
		bool flag5 = false;
		bool flag6 = false;
		List<Lord> list2 = new List<Lord>(map.lordManager.lords);
		for (int k = 0; k < list2.Count; k++)
		{
			Lord lord = list2[k];
			if (lord == null || lord.faction != faction)
			{
				continue;
			}
			if (lord.LordJob is LordJob_EESDefendGroup lordJob_EESDefendGroup3)
			{
				GarrisonGroupType groupType = (GarrisonGroupType)lordJob_EESDefendGroup3.groupType;
				if (groupType == GarrisonGroupType.Pursuer && !lordJob_EESDefendGroup3.releasedToHunt && lordJob_EESDefendGroup3.assaultTick <= 0 && lordJob_EESDefendGroup3.assaultDelayTicks > 0)
				{
					if (!flag5)
					{
						flag5 = true;
						flag6 = EESHostility.AnyEnemyOf(map, faction);
					}
					if (flag6)
					{
						lordJob_EESDefendGroup3.assaultTick = now + lordJob_EESDefendGroup3.assaultDelayTicks;
						EESLog.Message($"GarrisonGroups: 추격대 무장 [{faction.Name}] 적 최초 감지 → 공세 예정 +{lordJob_EESDefendGroup3.assaultDelayTicks}t " + $"(인원 {AliveInLord(lord)}/{lordJob_EESDefendGroup3.initialCount}명, 관전={flag4}).");
					}
				}
				if (groupType == GarrisonGroupType.Pursuer && !lordJob_EESDefendGroup3.releasedToHunt && lordJob_EESDefendGroup3.assaultTick > 0 && now >= lordJob_EESDefendGroup3.assaultTick)
				{
					ReLordToAssault(lord, faction, groupType, lordJob_EESDefendGroup3.fleeThreshold, lordJob_EESDefendGroup3.initialCount, "추격대 공세 개시", flag4);
				}
				else if (groupType == GarrisonGroupType.ZoneGuard && !lordJob_EESDefendGroup3.releasedToHunt)
				{
					bool flag7 = EESHostility.IsFriendlyOutpost(faction) && EnemiesInsideBase();
					if (flag || num2 <= 0.4f || flag7)
					{
						ReLordToAssault(lord, faction, groupType, lordJob_EESDefendGroup3.fleeThreshold, lordJob_EESDefendGroup3.initialCount, flag7 ? "경비대 release(침공군 기지 진입)" : (flag ? "경비대 release(지휘관 사망)" : "경비대 release(기지 40%)"), flag4);
					}
					else
					{
						ManageZoneGuards(lord);
					}
				}
				else if (groupType == GarrisonGroupType.Herald)
				{
					int num5 = AliveInLord(lord);
					if (flag2 || num2 <= 0.5f || num5 < lordJob_EESDefendGroup3.initialCount)
					{
						RetreatHerald(lord);
					}
				}
				else if (!lordJob_EESDefendGroup3.fledTriggered && lordJob_EESDefendGroup3.fleeThreshold > 0f && AliveInLord(lord) < lordJob_EESDefendGroup3.initialCount && AliveInLord(lord) <= Mathf.CeilToInt(lordJob_EESDefendGroup3.fleeThreshold * (float)lordJob_EESDefendGroup3.initialCount))
				{
					if (flag4)
					{
						LogFleeSuppressed(lord, lordJob_EESDefendGroup3.groupType, AliveInLord(lord), lordJob_EESDefendGroup3.initialCount, lordJob_EESDefendGroup3.fleeThreshold, ref lordJob_EESDefendGroup3.fleeSuppressLogged);
						continue;
					}
					PanicFleeLord(lord, (GarrisonGroupType)lordJob_EESDefendGroup3.groupType);
					lordJob_EESDefendGroup3.fledTriggered = true;
				}
			}
			else if (lord.LordJob is LordJob_EESAssaultGroup { groupType: not 6, fledTriggered: false, fleeThreshold: >0f } lordJob_EESAssaultGroup2 && AliveInLord(lord) < lordJob_EESAssaultGroup2.initialCount && AliveInLord(lord) <= Mathf.CeilToInt(lordJob_EESAssaultGroup2.fleeThreshold * (float)lordJob_EESAssaultGroup2.initialCount))
			{
				if (flag4)
				{
					LogFleeSuppressed(lord, lordJob_EESAssaultGroup2.groupType, AliveInLord(lord), lordJob_EESAssaultGroup2.initialCount, lordJob_EESAssaultGroup2.fleeThreshold, ref lordJob_EESAssaultGroup2.fleeSuppressLogged);
					continue;
				}
				PanicFleeLord(lord, (GarrisonGroupType)lordJob_EESAssaultGroup2.groupType);
				lordJob_EESAssaultGroup2.fledTriggered = true;
			}
		}
	}

	private void ManageZoneGuards(Lord l)
	{
		JobDef wait_Combat = JobDefOf.Wait_Combat;
		JobDef jobDef = JobDefOf.Goto;
		for (int i = 0; i < l.ownedPawns.Count; i++)
		{
			Pawn pawn = l.ownedPawns[i];
			if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Downed || pawn.jobs == null)
			{
				continue;
			}
			PawnDuty pawnDuty = pawn.mindState?.duty;
			if (pawnDuty == null || pawnDuty.def != DutyDefOf.Defend)
			{
				continue;
			}
			IntVec3 cell = pawnDuty.focus.Cell;
			if (!cell.IsValid || !cell.InBounds(map))
			{
				continue;
			}
			if ((pawn.Position - cell).LengthHorizontal <= 2.5f)
			{
				if (pawn.CurJobDef != wait_Combat)
				{
					Job job = JobMaker.MakeJob(wait_Combat);
					job.expiryInterval = 30000;
					job.checkOverrideOnExpire = false;
					pawn.jobs.StartJob(job, JobCondition.InterruptForced);
				}
			}
			else if (pawn.CurJobDef != jobDef || pawn.CurJob.targetA.Cell != cell)
			{
				Job job2 = JobMaker.MakeJob(jobDef, cell);
				job2.locomotionUrgency = LocomotionUrgency.Jog;
				pawn.jobs.StartJob(job2, JobCondition.InterruptForced);
			}
		}
	}

	private bool PlayerInsideBase()
	{
		if (eesBaseRect.Width <= 0)
		{
			return false;
		}
		List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i] != null && !list[i].Dead && eesBaseRect.Contains(list[i].Position))
			{
				return true;
			}
		}
		List<Pawn> list2 = EESHostility.EnemyPawnsOf(map, EESHostility.OwnerOf(map));
		for (int j = 0; j < list2.Count; j++)
		{
			if (eesBaseRect.Contains(list2[j].Position))
			{
				return true;
			}
		}
		return false;
	}

	private bool EnemiesInsideBase()
	{
		if (eesBaseRect.Width <= 0)
		{
			return false;
		}
		List<Pawn> list = EESHostility.EnemyPawnsOf(map, EESHostility.OwnerOf(map));
		for (int i = 0; i < list.Count; i++)
		{
			if (eesBaseRect.Contains(list[i].Position))
			{
				return true;
			}
		}
		return false;
	}

	private static int AliveInLord(Lord l)
	{
		int num = 0;
		for (int i = 0; i < l.ownedPawns.Count; i++)
		{
			Pawn pawn = l.ownedPawns[i];
			if (pawn != null && pawn.Spawned && !pawn.Dead && !pawn.Downed)
			{
				num++;
			}
		}
		return num;
	}

	private static int DownedInLord(Lord l)
	{
		int num = 0;
		for (int i = 0; i < l.ownedPawns.Count; i++)
		{
			Pawn pawn = l.ownedPawns[i];
			if (pawn != null && pawn.Spawned && !pawn.Dead && pawn.Downed)
			{
				num++;
			}
		}
		return num;
	}

	private static string InterruptStaleJobs(List<Pawn> pawns)
	{
		Dictionary<string, int> dictionary = new Dictionary<string, int>();
		int num = 0;
		for (int i = 0; i < pawns.Count; i++)
		{
			Pawn pawn = pawns[i];
			if (pawn != null && pawn.Spawned && pawn.jobs != null)
			{
				JobDef curJobDef = pawn.CurJobDef;
				if (curJobDef != null)
				{
					dictionary[curJobDef.defName] = ((!dictionary.TryGetValue(curJobDef.defName, out var value)) ? 1 : (value + 1));
					pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
					num++;
				}
			}
		}
		if (num == 0)
		{
			return "0명";
		}
		List<string> list = new List<string>();
		foreach (KeyValuePair<string, int> item in dictionary)
		{
			list.Add($"{item.Key} {item.Value}");
		}
		list.Sort();
		return string.Format("{0}명({1})", num, string.Join(" · ", list));
	}

	private void ReLordToAssault(Lord l, Faction faction, GarrisonGroupType t, float threshold, int initCount, string reason, bool spectate)
	{
		List<Pawn> list = new List<Pawn>();
		int num = 0;
		foreach (Pawn item in new List<Pawn>(l.ownedPawns))
		{
			if (item != null && item.Spawned && !item.Dead)
			{
				if (item.Downed)
				{
					num++;
					continue;
				}
				list.Add(item);
				l.RemovePawn(item);
			}
		}
		if (list.Count == 0)
		{
			if (l.LordJob is LordJob_EESDefendGroup { reformSkipLogged: false } lordJob_EESDefendGroup)
			{
				lordJob_EESDefendGroup.reformSkipLogged = true;
				EESLog.Message($"GarrisonGroups: {reason} 보류 [{faction?.Name} {t}] 재편성 가능 0명" + $"(쓰러짐 {num}명 제외 · lord 보유 {l.ownedPawns.Count}명) — 재편성·알림 없음.");
			}
		}
		else if (EESHostility.IsFriendlyOutpost(faction) || spectate)
		{
			IntVec3 intVec = ((eesBaseRect.Width > 0) ? eesBaseRect.CenterCell : map.Center);
			LordJob_EESDefendGroup lordJob = new LordJob_EESDefendGroup(faction, intVec, t, threshold, initCount)
			{
				huntInvaders = true,
				holdPoint = intVec,
				releasedToHunt = true
			};
			LordMaker.MakeNewLord(faction, lordJob, map, list);
			string arg = InterruptStaleJobs(list);
			EESLog.Message($"GarrisonGroups: {reason} [{faction.Name}] {list.Count}명(쓰러짐 {num}명 제외) → " + string.Format("★{0} 반격(추격 모드, 배회 앵커 {1}) · 기존 job 중단 {2}.", EESHostility.IsFriendlyOutpost(faction) ? "아군 거점" : "관전 전투", intVec, arg));
			Messages.Message(EESText.T((t == GarrisonGroupType.Pursuer) ? "EES.Group.PursuerAssaultAlly" : "EES.Group.ZoneReleaseAlly", faction.Name), new TargetInfo(list[0].Position, map), MessageTypeDefOf.ThreatSmall, historical: false);
		}
		else
		{
			LordMaker.MakeNewLord(faction, new LordJob_EESAssaultGroup(faction, t, threshold, initCount), map, list);
			string text = InterruptStaleJobs(list);
			EESLog.Message($"GarrisonGroups: {reason} [{faction.Name}] {list.Count}명(쓰러짐 {num}명 제외) → 공세 · 기존 job 중단 {text}.");
			Messages.Message(EESText.T((t == GarrisonGroupType.Pursuer) ? "EES.Group.PursuerAssault" : "EES.Group.ZoneRelease", faction.Name), new TargetInfo(map.Center, map), MessageTypeDefOf.ThreatSmall, historical: false);
		}
	}

	private void RetreatHerald(Lord l)
	{
		List<Pawn> list = new List<Pawn>();
		int num = 0;
		foreach (Pawn item in new List<Pawn>(l.ownedPawns))
		{
			if (item != null && item.Spawned && !item.Dead)
			{
				if (item.Downed)
				{
					num++;
					continue;
				}
				list.Add(item);
				l.RemovePawn(item);
			}
		}
		if (list.Count == 0)
		{
			if (l.LordJob is LordJob_EESDefendGroup { reformSkipLogged: false } lordJob_EESDefendGroup)
			{
				lordJob_EESDefendGroup.reformSkipLogged = true;
				EESLog.Message("GarrisonGroups: 전령대 퇴각 보류 [" + l.faction?.Name + "] 퇴각 가능 0명" + $"(쓰러짐 {num}명 제외 · lord 보유 {l.ownedPawns.Count}명).");
			}
			return;
		}
		bool flag = heraldRetreaters.Count == 0;
		MentalStateDef panicFlee = MentalStateDefOf.PanicFlee;
		for (int i = 0; i < list.Count; i++)
		{
			Pawn pawn = list[i];
			if (!heraldRetreaters.Contains(pawn))
			{
				heraldRetreaters.Add(pawn);
			}
			if (panicFlee != null)
			{
				pawn.mindState?.mentalStateHandler?.TryStartMentalState(panicFlee, "EES herald retreat");
			}
		}
		EESLog.Message($"GarrisonGroups: 전령대 퇴각 개시(PanicFlee) [{l.faction?.Name}] {list.Count}명(쓰러짐 {num}명 제외) → 도주.");
		if (flag)
		{
			Messages.Message(EESText.T("EES.Group.HeraldRetreatAttempt", l.faction?.Name), new TargetInfo(map.Center, map), MessageTypeDefOf.NeutralEvent, historical: false);
		}
	}

	private void LogFleeSuppressed(Lord l, int groupType, int alive, int initCount, float thr, ref bool logged)
	{
		if (!logged)
		{
			logged = true;
			EESLog.Message("GarrisonGroups: 그룹 도주 임계값 도달했으나 ★관전 전투라 도주 억제 " + $"[{l.faction?.Name} {(GarrisonGroupType)groupType}] alive={alive}/{initCount} " + $"thr={thr:P0}(=ceil {Mathf.CeilToInt(thr * (float)initCount)}명) — 끝까지 교전.");
		}
	}

	private void PanicFleeLord(Lord l, GarrisonGroupType t)
	{
		MentalStateDef panicFlee = MentalStateDefOf.PanicFlee;
		if (panicFlee == null)
		{
			return;
		}
		int num = 0;
		foreach (Pawn item in new List<Pawn>(l.ownedPawns))
		{
			if (item != null && !item.Dead && !item.Downed && !item.InMentalState && item.mindState?.mentalStateHandler != null && item.mindState.mentalStateHandler.TryStartMentalState(panicFlee, "EES group flee"))
			{
				num++;
			}
		}
		EESLog.Message($"GarrisonGroups: 그룹 도주 임계값 도달 [{l.faction?.Name} {t}] {num}명 PanicFlee.");
		if (num > 0)
		{
			Messages.Message(EESText.T("EES.Group.GroupFlee", l.faction?.Name), new TargetInfo(map.Center, map), MessageTypeDefOf.NeutralEvent, historical: false);
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref nextReinforceTick, "ees_nextReinforceTick", -1);
		Scribe_Values.Look(ref sawHostiles, "ees_sawHostiles", defaultValue: false);
		Scribe_Values.Look(ref trapsNeutralized, "ees_trapsNeutralized", defaultValue: false);
		Scribe_Values.Look(ref turretsGridPowered, "ees_turretsGridPowered", defaultValue: false);
		Scribe_Values.Look(ref gravshipAttack, "ees_gravshipAttack", defaultValue: false);
		Scribe_Values.Look(ref eesStrongholdMap, "ees_strongholdMap", defaultValue: false);
		Scribe_Values.Look(ref isPolluted, "ees_isPolluted", defaultValue: false);
		Scribe_Values.Look(ref isBattlefield, "ees_isBattlefield", defaultValue: false);
		Scribe_Values.Look(ref bfSelected, "ees_bfSelected", defaultValue: false);
		Scribe_Values.Look(ref bfActive, "ees_bfActive", defaultValue: false);
		Scribe_Values.Look(ref bfFactionC, "ees_bfFactionC", -1);
		Scribe_Values.Look(ref bfFactionD, "ees_bfFactionD", -1);
		Scribe_Values.Look(ref bfEntryTick, "ees_bfEntryTick", -1);
		Scribe_Values.Look(ref bfNextEvent, "ees_bfNextEvent", 0);
		Scribe_References.Look(ref observerTarget, "ees_observerTarget");
		Scribe_Values.Look(ref observerStarted, "ees_observerStarted", defaultValue: false);
		Scribe_Values.Look(ref observerNextPickTick, "ees_observerNextPickTick", -1);
		Scribe_Values.Look(ref lastCommsAlive, "ees_lastCommsAlive", defaultValue: true);
		Scribe_Values.Look(ref initialized, "ees_siegeInit", defaultValue: false);
		Scribe_Values.Look(ref announcedActive, "ees_reinfAnnouncedActive", defaultValue: false);
		Scribe_Values.Look(ref announcedBlocked, "ees_reinfAnnouncedBlocked", defaultValue: false);
		Scribe_References.Look(ref commander, "ees_commander");
		Scribe_Values.Look(ref commanderRegistered, "ees_commanderRegistered", defaultValue: false);
		Scribe_Values.Look(ref commanderDead, "ees_commanderDead", defaultValue: false);
		Scribe_Values.Look(ref commanderRegisteredTick, "ees_commanderRegisteredTick", -1);
		Scribe_Values.Look(ref nextFanaticTick, "ees_nextFanaticTick", -1);
		Scribe_Values.Look(ref securityActive, "ees_securityActive", defaultValue: false);
		Scribe_Collections.Look(ref securityStructures, "ees_securityStructures", LookMode.Reference);
		if (Scribe.mode == LoadSaveMode.PostLoadInit && securityActive)
		{
			AnySecurityActive = true;
		}
		if (Scribe.mode == LoadSaveMode.PostLoadInit && observerStarted)
		{
			AnyObserverActive = true;
		}
		Scribe_Values.Look(ref groupInitGarrison, "ees_groupInitGarrison", -1);
		Scribe_Values.Look(ref heraldActive, "ees_heraldActive", defaultValue: false);
		Scribe_Values.Look(ref heraldResolved, "ees_heraldResolved", defaultValue: false);
		Scribe_Values.Look(ref heraldDropTriggered, "ees_heraldDropTriggered", defaultValue: false);
		Scribe_Collections.Look(ref heraldRetreaters, "ees_heraldRetreaters", LookMode.Reference);
		Scribe_Collections.Look(ref cryoCaskets, "ees_cryoCaskets", LookMode.Reference);
		Scribe_Values.Look(ref cryoArmed, "ees_cryoArmed", defaultValue: false);
		Scribe_Values.Look(ref cryoWoken, "ees_cryoWoken", defaultValue: false);
		Scribe_Values.Look(ref cryoRoomMinX, "ees_cryoRoomMinX", -1);
		Scribe_Values.Look(ref cryoRoomMinZ, "ees_cryoRoomMinZ", -1);
		Scribe_Values.Look(ref cryoRoomW, "ees_cryoRoomW", 0);
		Scribe_Values.Look(ref cryoRoomH, "ees_cryoRoomH", 0);
		Scribe_Values.Look(ref cryoEdgeBaseline, "ees_cryoEdgeBaseline", -1);
		Scribe_Values.Look(ref cryoInitialGarrison, "ees_cryoInitialGarrison", -1);
		Scribe_Values.Look(ref eesGenerated, "ees_generated", false);
		Scribe_Values.Look(ref eesOverlay, "ees_overlay", false);
		Scribe_Values.Look(ref eesBaseRect, "ees_baseRect", CellRect.Empty);
		Scribe_Values.Look(ref eesVaultRect, "ees_vaultRect", CellRect.Empty);
		Scribe_Values.Look(ref finalStandWavesSpawned, "ees_finalStandWavesSpawned", 0);
		Scribe_Values.Look(ref nextFinalStandWaveTick, "ees_nextFinalStandWaveTick", -1);
		Scribe_Values.Look(ref finalStandWaveReason, "ees_finalStandWaveReason");
		Scribe_Collections.Look(ref turretManningCells, "ees_turretManningCells", LookMode.Value, LookMode.Value, ref turretManningKeys, ref turretManningValues);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (turretManningCells == null) turretManningCells = new Dictionary<int, IntVec3>();
			if (securityStructures == null)
			{
				securityStructures = new HashSet<Thing>();
			}
			if (heraldRetreaters == null)
			{
				heraldRetreaters = new List<Pawn>();
			}
			if (cryoCaskets == null)
			{
				cryoCaskets = new List<Thing>();
			}
		}
	}
}
