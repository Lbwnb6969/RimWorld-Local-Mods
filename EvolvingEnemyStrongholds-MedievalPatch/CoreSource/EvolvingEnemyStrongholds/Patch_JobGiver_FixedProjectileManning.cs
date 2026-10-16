using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

// The native giver unconditionally searches for mortar shells. A fixed gun
// never loads those shells in JobDriver_ManTurret; it uses its own projectile
// and fuel. Preserve native jobs, and fill only this missing eligible case.
[HarmonyPatch(typeof(JobGiver_ManTurrets), "TryGiveJob")]
public static class Patch_JobGiver_FixedProjectileManning
{
    private static void Postfix(JobGiver_ManTurrets __instance, Pawn pawn, ref Job __result)
    {
        if (__result != null || CECompat.Active || pawn?.Map?.Parent is not Settlement site ||
            pawn.Faction == null || pawn.Faction != site.Faction || pawn.Faction == Faction.OfPlayer ||
            !pawn.Faction.HostileTo(Faction.OfPlayer) || pawn.GetLord()?.LordJob is not LordJob_ManTurrets ||
            EESFactionBlacklist.IsBlocked(site.Faction) || EESCustomBaseDetector.IsForeignSettlement(site)) return;
        var data = WorldComponent_Strongholds.Instance?.GetExisting(site.ID);
        var plan = data?.generationPlan;
        if (plan == null || plan.legacyMap || plan.factionLoadId != site.Faction.loadID || plan.mapId != pawn.Map.uniqueID ||
            pawn.Map.GetComponent<MapComponent_StrongholdSiege>()?.eesGenerated != true) return;
        var root = (IntVec3)AccessTools.Method(__instance.GetType(), "GetRoot").Invoke(__instance, new object[] { pawn });
        var selected = GenClosest.ClosestThingReachable(root, pawn.Map,
            ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial), PathEndMode.InteractionCell,
            TraverseParms.For(pawn), __instance.maxDistFromPoint, t => CanManFixed(t, pawn));
        if (selected == null) return;
        __result = JobMaker.MakeJob(JobDefOf.ManTurret, selected);
        __result.expiryInterval = 2000;
        __result.checkOverrideOnExpire = true;
    }

    private static bool CanManFixed(Thing thing, Pawn pawn)
    {
        if (!IsFixedGun(thing) || thing.Faction != pawn.Faction || thing.IsForbidden(pawn) || !pawn.CanReserve(thing))
            return false;
        var turret = (Building_TurretGun)thing;
        var fuel = turret.TryGetComp<CompRefuelable>();
        if ((fuel == null || fuel.HasFuel) && turret.AttackVerb.Available()) return true;
        return NeedsRefueling(turret) && JobDriver_ManTurret.FindFuelForTurret(pawn, turret) != null;
    }

    internal static bool IsFixedGun(Thing thing) => thing is Building_TurretGun turret && turret.def.hasInteractionCell &&
        turret.def.HasComp(typeof(CompMannable)) && turret.gun != null && turret.gun.TryGetComp<CompChangeableProjectile>() == null &&
        turret.AttackVerb is Verb_LaunchProjectile && turret.AttackVerb.verbProps.defaultProjectile?.projectile != null;

    internal static bool Eligible(Thing thing)
    {
        if (CECompat.Active || !IsFixedGun(thing) || thing.Map?.Parent is not Settlement site ||
            thing.Faction == null || thing.Faction != site.Faction || thing.Faction == Faction.OfPlayer ||
            !thing.Faction.HostileTo(Faction.OfPlayer) || EESFactionBlacklist.IsBlocked(site.Faction) ||
            EESCustomBaseDetector.IsForeignSettlement(site)) return false;
        var plan = WorldComponent_Strongholds.Instance?.GetExisting(site.ID)?.generationPlan;
        return plan != null && !plan.legacyMap && plan.factionLoadId == site.Faction.loadID &&
            plan.mapId == thing.Map.uniqueID && thing.Map.GetComponent<MapComponent_StrongholdSiege>()?.eesGenerated == true;
    }

    internal static bool NeedsRefueling(Thing thing)
    {
        var fuel = thing?.TryGetComp<CompRefuelable>();
        // Native mortar-barrel and classicMortars behavior remains authoritative.
        return fuel != null && !fuel.Props.fuelIsMortarBarrel && Eligible(thing) && (!fuel.HasFuel ||
            fuel.Fuel < ((Building_TurretGun)thing).AttackVerb.verbProps.consumeFuelPerShot);
    }
}

// Use the driver's existing hauling, reservation, wait and real refuel toils.
// It carries one actual fuel unit per cycle, just as native turret service does.
[HarmonyPatch(typeof(JobDriver_ManTurret), "GunNeedsRefueling")]
public static class Patch_JobDriver_FixedProjectileRefueling
{
    private static void Postfix(Building b, ref bool __result)
    {
        if (!__result && Patch_JobGiver_FixedProjectileManning.NeedsRefueling(b)) __result = true;
    }
}
