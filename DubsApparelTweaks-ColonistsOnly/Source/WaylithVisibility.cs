#if WAYLITH
using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using QuickFast.Source;

namespace Local.DubsApparelTweaksColonistsOnly
{
    // Waylith and mod-defined nodes can provide their own graphic/state graphics.
    // Keep the native graphic graph intact and apply the visibility policy when
    // its cached draw requests are rebuilt, rather than deleting graphics.
    internal static class WaylithVisibility
    {
        private static Func<Pawn, Apparel, bool> shouldHide;

        // This lamellar cuirass uses Middle so it can be worn under a shell.
        // Apply the shell visibility preference without changing equipment layers.
        internal static void ApplyArmorLayerAlias(Pawn pawn, Apparel apparel, ref bool hidden)
        {
            if (hidden || apparel?.def.defName != "BA_KlibanionArmor_2New") return;
            if (!pawn.Spawned || pawn.Map == null || !pawn.RaceProps.Humanlike) return;
            if (!Settings.LayerVis.Contains("Shell") || Settings.hatfilter.Contains(apparel.def)) return;
            hidden = Settings.apparelHidingMode == ApparelHidingMode.WhenNotDrafted ||
                (Settings.apparelHidingMode == ApparelHidingMode.IndoorsOrNotDrafted &&
                 !pawn.Position.UsesOutdoorTemperature(pawn.Map));
        }

        internal static void Install(Harmony harmony, MethodInfo predicate)
        {
            shouldHide = (Func<Pawn, Apparel, bool>)Delegate.CreateDelegate(
                typeof(Func<Pawn, Apparel, bool>), predicate);
            var graphics = AccessTools.Method(typeof(PawnRenderNode_Apparel), "GraphicsFor");
            var oldFilter = AccessTools.TypeByName("QuickFast.Source.H_ApparelPatches+H_ApparelGraphicsFor");
            harmony.Unpatch(graphics, AccessTools.Method(oldFilter, "Postfix"));
            harmony.Patch(AccessTools.Method(typeof(PawnRenderNode), "AppendRequests"),
                prefix: new HarmonyMethod(typeof(WaylithVisibility), nameof(AppendRequests)));
            // The original predicate contains a plain Dictionary cache. Seed it on
            // the main thread before Waylith starts parallel draw-request work.
            var hidesHead = (Func<ThingDef, bool>)Delegate.CreateDelegate(
                typeof(Func<ThingDef, bool>), AccessTools.Method(predicate.DeclaringType, "HidesHead"));
            LongEventHandler.ExecuteWhenFinished(() =>
            {
                foreach (var def in DefDatabase<ThingDef>.AllDefsListForReading)
                    if (def.apparel != null) hidesHead(def);
            });
            Log.Message("[Dubs Apparel Tweaks - Colonists Only] Waylith apparel draw-request filter registered; native graphics retained.");
        }

        private static bool AppendRequests(PawnRenderNode __instance, PawnDrawParms parms)
        {
            var apparel = __instance.apparel;
            if (apparel == null) return true;
            var pawn = parms.pawn;
            if (pawn?.Faction?.IsPlayer != true || pawn.Drafted) return true;
            return !shouldHide(pawn, apparel);
        }
    }
}
#endif
