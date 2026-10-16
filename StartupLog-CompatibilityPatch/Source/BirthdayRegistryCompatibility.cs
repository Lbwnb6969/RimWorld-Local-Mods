using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using Verse;

namespace Local.StartupLogFixes
{
    public sealed partial class StartupLogFixesMod
    {
        private static void InstallBirthdayRegistryCompatibility(Harmony harmony)
        {
            Type manager = AccessTools.TypeByName("OberoniaAurea.SpecialGlobalEventManager");
            if (manager == null) return;

            // Serialize the native registry access and save/load reset. Keep the
            // original birthday rules, thought results, and serialized fields.
            foreach (MethodInfo method in new[] {
                AccessTools.DeclaredMethod(manager, "IsPawnOnFirstBirthdayPerYear", new[] { typeof(Pawn), typeof(bool) }),
                AccessTools.DeclaredMethod(manager, "ExposeData", Type.EmptyTypes) })
            {
                TryPatch(harmony, method, "Oberonia birthday registry synchronization",
                    prefixName: nameof(BirthdayRegistryPrefix),
                    finalizerName: nameof(BirthdayRegistryFinalizer));
            }
        }

        public static void BirthdayRegistryPrefix(object __instance, out bool __state)
        {
            __state = false;
            if (__instance == null) return;
            Monitor.Enter(__instance);
            __state = true;
        }

        public static Exception BirthdayRegistryFinalizer(Exception __exception, object __instance, bool __state)
        {
            if (__state) Monitor.Exit(__instance);
            return __exception;
        }
    }
}
