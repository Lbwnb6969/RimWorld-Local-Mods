using HarmonyLib;
using RimWorld;
using Verse;

namespace Local.DubsApparelTweaksColonistsOnly
{
    public sealed class DubsApparelTweaksColonistsOnlyMod : Mod
    {
        private const string HarmonyId = "local.dubsappareltweaks.colonistsonly";

        public DubsApparelTweaksColonistsOnlyMod(ModContentPack content) : base(content)
        {
            var targetType = AccessTools.TypeByName("QuickFast.Source.H_ApparelPatches");
            var target = targetType == null
                ? null
                : AccessTools.Method(targetType, "ShouldHideApparel", new[] { typeof(Pawn), typeof(Apparel) });
            if (target != null)
            {
                var harmony = new Harmony(HarmonyId);
                harmony.Patch(target, postfix: new HarmonyMethod(
                    typeof(DubsApparelTweaksColonistsOnlyMod), nameof(OnlyColonistsPostfix)));
#if WAYLITH
                WaylithVisibility.Install(harmony, target);
#endif
                // The 1.6 graphics iterator consumes this predicate. Its iterator Postfix
                // cannot itself be patched reliably on the old Harmony/Mono runtime.
                Log.Message("[Dubs Apparel Tweaks - Colonists Only] Visibility filter registered.");
            }
            else
            {
                Log.Warning("[Dubs Apparel Tweaks - Colonists Only] ShouldHideApparel was not found; no patch applied.");
            }
        }

        private static void OnlyColonistsPostfix(Pawn pawn, Apparel apparel, ref bool __result)
        {
            if (pawn == null || pawn.Drafted)
            {
                return;
            }

            if (pawn.Faction == null || !pawn.Faction.IsPlayer)
            {
                __result = false;
            }
#if WAYLITH
            else
            {
                WaylithVisibility.ApplyArmorLayerAlias(pawn, apparel, ref __result);
            }
#endif
        }

    }
}
