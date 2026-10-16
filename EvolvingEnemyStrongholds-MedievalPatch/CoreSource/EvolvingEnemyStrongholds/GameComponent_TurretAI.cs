using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public class GameComponent_TurretAI : GameComponent
{
	private const int ScanInterval = 60;

	private const int FallbackCooldown = 1200;

	private const int NoFireRetryTicks = 180;

	private const int LoaderServiceRadius = 5;

	private const int DiagInterval = 300;

	private readonly Dictionary<int, int> nextFireTick = new Dictionary<int, int>();

	private readonly Dictionary<int, int> nextDiagTick = new Dictionary<int, int>();

	private static MethodInfo _tryFindNewTarget;

	private static bool _reflectInit;

	private HashSet<int> loaderIds = new HashSet<int>();

	private HashSet<int> fledLoaderIds = new HashSet<int>();

	private Dictionary<int, int> loaderTurret = new Dictionary<int, int>();

	public GameComponent_TurretAI(Game game)
	{
	}

	public override void GameComponentTick()
	{
		int ticksGame = Find.TickManager.TicksGame;
		if (ticksGame % 60 == 0)
		{
			List<Map> maps = Find.Maps;
			for (int i = 0; i < maps.Count; i++)
			{
				ScanMap(maps[i], ticksGame);
			}
		}
	}

	private void ScanMap(Map map, int tick)
	{
		if (map?.listerBuildings == null || (map.Parent is Settlement wo && EESCustomBaseDetector.IsForeignSettlement(wo)))
		{
			return;
		}
		StrongholdData strongholdData = ((!(map.Parent is Settlement settlement)) ? null : WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID));
		if ((strongholdData != null && FakeOutpostUtility.IsFake(strongholdData.roleDefName)) || ((map.ParentFaction == null || map.ParentFaction == Faction.OfPlayer) && loaderIds.Count == 0))
		{
			return;
		}
		ProcessLoaders(map, tick);
		List<Building> allBuildingsNonColonist = map.listerBuildings.allBuildingsNonColonist;
		for (int i = 0; i < allBuildingsNonColonist.Count; i++)
		{
			Building building = allBuildingsNonColonist[i];
			if (building == null || building.Destroyed)
			{
				continue;
			}
			if (building is Building_Turret && building.Faction != null && building.Faction != Faction.OfPlayer)
			{
				CECompat.LogTurretRuntimeDiag(building, tick);
			}
			if (!(building is Building_TurretGun building_TurretGun))
			{
				continue;
			}
			CompInteractable compInteractable = building.TryGetComp<CompInteractable>();
			if (compInteractable == null)
			{
				continue;
			}
			Faction faction = building.Faction;
			if (faction == null || faction == Faction.OfPlayer)
			{
				continue;
			}
			Verb attackVerb = building_TurretGun.AttackVerb;
			if (attackVerb?.verbProps == null)
			{
				continue;
			}
			float range = attackVerb.verbProps.range;
			CompRefuelable compRefuelable = building_TurretGun.TryGetComp<CompRefuelable>();
			if (nextFireTick.TryGetValue(building.thingIDNumber, out var value) && tick < value)
			{
				continue;
			}
			if (!NearestHostile(map, building, faction, range, out var nearestDist))
			{
				Diag(building, tick, $"TurretAI[NO-FIRE: range밖] {faction.Name} {building.def.defName} @ {building.Position} " + string.Format("range={0:0} nearestHostile={1}", range, (nearestDist < 0f) ? "none" : nearestDist.ToString("0")));
				continue;
			}
			if (compRefuelable != null && !compRefuelable.HasFuel)
			{
				Diag(building, tick, $"TurretAI[NO-FIRE: 연료없음] {faction.Name} {building.def.defName} @ {building.Position} " + $"range={range:0} nearestHostile={nearestDist:0} fuel={compRefuelable.Fuel:0}/{compRefuelable.Props.fuelCapacity:0} (장전병 못채움/연료소진/장전병사망)");
				continue;
			}
			bool flag = attackVerb.ProjectileFliesOverhead();
			bool flag2 = building.Position.Roofed(map);
			bool flag3 = attackVerb.Available();
			bool flag4 = VanillaFindsTarget(building_TurretGun);
			building_TurretGun.TryActivateBurst();
			if (building_TurretGun.CurrentTarget.IsValid)
			{
				float num = compInteractable.Props?.cooldownTicks ?? 1200;
				nextFireTick[building.thingIDNumber] = tick + Mathf.Max(1200, Mathf.RoundToInt(num));
				EESLog.Message($"TurretAI[FIRED] {faction.Name} {building.def.defName} @ {building.Position} | " + $"range={range:0} nearestHostile={nearestDist:0} cd={num:0} | " + $"flyOverhead={flag} roofedAtTurret={flag2} verbAvailable={flag3} " + $"vanillaFindsTarget={flag4}");
			}
			else
			{
				nextFireTick[building.thingIDNumber] = tick + 180;
				Diag(building, tick, $"TurretAI[NO-FIRE: 타겟못잡음(직사 시야차단 의심)] {faction.Name} {building.def.defName} @ {building.Position} | " + $"range={range:0} nearestHostile={nearestDist:0} | flyOverhead={flag} " + $"roofedAtTurret={flag2} verbAvailable={flag3} vanillaFindsTarget={flag4} " + $"→ 긴 쿨다운 안 걸고 {180}틱 후 재시도");
			}
		}
	}

	private void Diag(Building b, int tick, string msg)
	{
		if (!nextDiagTick.TryGetValue(b.thingIDNumber, out var value) || tick >= value)
		{
			nextDiagTick[b.thingIDNumber] = tick + 300;
			EESLog.Message(msg);
		}
	}

	private static bool NearestHostile(Map map, Building turret, Faction owner, float range, out float nearestDist)
	{
		float num = range * range;
		float num2 = float.MaxValue;
		bool result = false;
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn != null && !pawn.Dead && !pawn.Downed && pawn.Faction != null && pawn.HostileTo(owner))
			{
				float num3 = (pawn.Position - turret.Position).LengthHorizontalSquared;
				if (num3 < num2)
				{
					num2 = num3;
				}
				if (num3 <= num)
				{
					result = true;
				}
			}
		}
		nearestDist = ((num2 == float.MaxValue) ? (-1f) : Mathf.Sqrt(num2));
		return result;
	}

	private static bool VanillaFindsTarget(Building_TurretGun tg)
	{
		if (!_reflectInit)
		{
			_reflectInit = true;
			_tryFindNewTarget = typeof(Building_TurretGun).GetMethod("TryFindNewTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		}
		if (_tryFindNewTarget == null)
		{
			return false;
		}
		try
		{
			if (_tryFindNewTarget.Invoke(tg, null) is LocalTargetInfo localTargetInfo)
			{
				return localTargetInfo.IsValid;
			}
		}
		catch
		{
		}
		return false;
	}

	private static bool TryEnsureFuel(Thing turret, out int absorbed, out bool hadRefuel)
	{
		absorbed = 0;
		CompRefuelable compRefuelable = turret.TryGetComp<CompRefuelable>();
		hadRefuel = compRefuelable != null;
		if (compRefuelable == null)
		{
			return true;
		}
		if (compRefuelable.HasFuel)
		{
			return true;
		}
		ThingFilter thingFilter = compRefuelable.Props?.fuelFilter;
		if (thingFilter == null)
		{
			return false;
		}
		Map map = turret.Map;
		List<Thing> list = new List<Thing>();
		foreach (IntVec3 item in GenRadial.RadialCellsAround(turret.Position, 4f, useCenter: true))
		{
			if (!item.InBounds(map))
			{
				continue;
			}
			List<Thing> thingList = item.GetThingList(map);
			for (int i = 0; i < thingList.Count; i++)
			{
				Thing thing = thingList[i];
				if (thing.def.category == ThingCategory.Item && thingFilter.Allows(thing.def))
				{
					list.Add(thing);
				}
			}
		}
		absorbed = list.Count;
		if (list.Count == 0)
		{
			return false;
		}
		compRefuelable.Refuel(list);
		return compRefuelable.HasFuel;
	}

	public void RegisterLoader(Pawn loader, Thing turret)
	{
		if (loader != null && turret != null)
		{
			loaderIds.Add(loader.thingIDNumber);
			loaderTurret[loader.thingIDNumber] = turret.thingIDNumber;
			PinLoader(loader);
		}
	}

	private void ProcessLoaders(Map map, int tick)
	{
		if (loaderIds.Count == 0)
		{
			return;
		}
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn == null || !loaderIds.Contains(pawn.thingIDNumber) || fledLoaderIds.Contains(pawn.thingIDNumber) || pawn.Dead || pawn.Destroyed)
			{
				continue;
			}
			if (pawn.mindState != null && pawn.mindState.lastHarmTick > 0)
			{
				FleeLoader(pawn, map, "피격");
				continue;
			}
			Building building = FindAssignedTurret(map, pawn);
			if (building == null)
			{
				FleeLoader(pawn, map, "담당 포탑 소멸");
				continue;
			}
			CompRefuelable compRefuelable = building.TryGetComp<CompRefuelable>();
			bool flag = building.TryGetComp<CompInteractable>()?.OnCooldown ?? false;
			if ((compRefuelable?.IsFull ?? true) || flag)
			{
				PinLoader(pawn);
			}
			else
			{
				if (pawn.CurJobDef == JobDefOf.Refuel || pawn.CurJobDef == JobDefOf.RefuelAtomic)
				{
					continue;
				}
				if (!pawn.CanReserve(building))
				{
					PinLoader(pawn);
					continue;
				}
				Job job = null;
				try
				{
					job = RefuelWorkGiverUtility.RefuelJob(pawn, building, forced: true);
				}
				catch (Exception ex)
				{
					EESLog.Warning("RefuelJob " + pawn?.LabelShort + ": " + ex.Message);
				}
				if (job != null && (job.targetB.IsValid || (job.targetQueueB != null && job.targetQueueB.Count > 0)))
				{
					pawn.jobs.StartJob(job, JobCondition.InterruptForced);
				}
				else
				{
					FleeLoader(pawn, map, "옆 연료(Chemfuel) 소진/도달불가");
				}
			}
		}
	}

	private void PinLoader(Pawn p)
	{
		if (p.jobs != null && p.CurJobDef != JobDefOf.Wait_MaintainPosture)
		{
			p.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait_MaintainPosture), JobCondition.InterruptForced);
		}
	}

	private void FleeLoader(Pawn p, Map map, string reason)
	{
		fledLoaderIds.Add(p.thingIDNumber);
		try
		{
			p.GetLord()?.RemovePawn(p);
			Lord lord = null;
			List<Lord> lords = map.lordManager.lords;
			for (int i = 0; i < lords.Count; i++)
			{
				Lord lord2 = lords[i];
				if (lord2.faction == p.Faction && (lord2.LordJob is LordJob_DefendBase || lord2.LordJob is LordJob_AssaultColony))
				{
					lord = lord2;
					break;
				}
			}
			if (lord != null)
			{
				lord.AddPawn(p);
			}
			else
			{
				LordMaker.MakeNewLord(p.Faction, new LordJob_AssaultColony_NoFlee(p.Faction, canKidnap: false, canTimeoutOrFlee: false, sappers: false), map, new List<Pawn> { p });
			}
			if (p.jobs?.curJob?.def == JobDefOf.Wait_MaintainPosture)
			{
				p.jobs.EndCurrentJob(JobCondition.InterruptForced);
			}
		}
		catch (Exception ex)
		{
			EESLog.Warning("FleeLoader " + p?.LabelShort + ": " + ex.Message);
		}
		EESLog.Message($"Loader 이탈 [{p.Faction?.Name}] {p.LabelShort} @ {p.Position}: {reason} → 방어대 합류(이후 전투).");
	}

	private Building FindAssignedTurret(Map map, Pawn loader)
	{
		if (!loaderTurret.TryGetValue(loader.thingIDNumber, out var value))
		{
			return null;
		}
		foreach (IntVec3 item in GenRadial.RadialCellsAround(loader.Position, 6f, useCenter: true))
		{
			if (item.InBounds(map))
			{
				Building firstBuilding = item.GetFirstBuilding(map);
				if (firstBuilding != null && firstBuilding.thingIDNumber == value && !firstBuilding.Destroyed)
				{
					return firstBuilding;
				}
			}
		}
		return null;
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Collections.Look(ref loaderIds, "ees_loaderIds", LookMode.Value);
		Scribe_Collections.Look(ref fledLoaderIds, "ees_fledLoaderIds", LookMode.Value);
		Scribe_Collections.Look(ref loaderTurret, "ees_loaderTurret", LookMode.Value, LookMode.Value);
		if (Scribe.mode == LoadSaveMode.PostLoadInit)
		{
			if (loaderIds == null)
			{
				loaderIds = new HashSet<int>();
			}
			if (fledLoaderIds == null)
			{
				fledLoaderIds = new HashSet<int>();
			}
			if (loaderTurret == null)
			{
				loaderTurret = new Dictionary<int, int>();
			}
		}
	}
}
