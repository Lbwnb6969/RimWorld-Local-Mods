using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace Local.StartupLogFixes
{
    internal static class MapModeWorldLifetimeCompatibility
    {
        private static readonly ReaderWriterLockSlim WorldLifetime =
            new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        private static MethodInfo killQueue;
        private static MethodInfo interruptRegeneration;

        internal static void Install(Harmony harmony)
        {
            Type cachedMode = AccessTools.TypeByName("MapModeFramework.MapMode_Cached");
            Type worldLayer = AccessTools.TypeByName("MapModeFramework.WorldLayer_MapMode");
            killQueue = AccessTools.Method("MapModeFramework.TaskHandler:KillQueue");
            interruptRegeneration = AccessTools.Method("MapModeFramework.WorldRegenHandler:Interrupt");
            if (cachedMode == null || worldLayer == null || killQueue == null || interruptRegeneration == null) return;

            var workers = new HashSet<MethodInfo>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in StartupLogFixesMod.GetLoadableTypes(assembly))
                {
                    if (!cachedMode.IsAssignableFrom(type) && !worldLayer.IsAssignableFrom(type)) continue;
                    foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
                    {
                        if (!method.IsAbstract && method.ReturnType == typeof(void) &&
                            (method.Name == "StartCaching" || method.Name == "PrepareMeshes" || method.Name == "PrepareBorders") &&
                            Array.Exists(method.GetParameters(), parameter =>
                                parameter.Name == "token" && parameter.ParameterType == typeof(CancellationToken)))
                            workers.Add(method);
                    }
                }
            }
            foreach (MethodInfo worker in workers)
                harmony.Patch(worker, prefix: new HarmonyMethod(typeof(MapModeWorldLifetimeCompatibility), nameof(WorkerPrefix)),
                    finalizer: new HarmonyMethod(typeof(MapModeWorldLifetimeCompatibility), nameof(WorkerFinalizer)));

            MethodInfo clearWorld = AccessTools.Method(typeof(Verse.Profile.MemoryUtility), "ClearAllMapsAndWorld");
            MethodInfo asynchronousPrefix = AccessTools.Method("MapModeFramework.MemoryUtility_ClearAllMapsAndWorld_Patch:Prefix");
            if (asynchronousPrefix != null) harmony.Unpatch(clearWorld, asynchronousPrefix);
            // Game.Dispose releases the native world grid before ClearAllMapsAndWorld runs in 1.6.
            foreach (MethodInfo release in new[] { clearWorld, AccessTools.Method(typeof(Game), nameof(Game.Dispose)),
                AccessTools.Method(typeof(World), nameof(World.Dispose)), AccessTools.Method(typeof(PlanetLayer), nameof(PlanetLayer.Dispose)) })
                harmony.Patch(release,
                    prefix: new HarmonyMethod(typeof(MapModeWorldLifetimeCompatibility), nameof(ClearWorldPrefix)),
                    finalizer: new HarmonyMethod(typeof(MapModeWorldLifetimeCompatibility), nameof(ClearWorldFinalizer)));
            // This mod can be constructed before Map Mode Framework installs its own patches.
            if (asynchronousPrefix != null)
                LongEventHandler.ExecuteWhenFinished(() => harmony.Unpatch(clearWorld, asynchronousPrefix));
            Log.Message("[StartupLog Compatibility] Map Mode world lifetime guard installed for " + workers.Count + " background worker methods.");
        }

        private static void WorkerPrefix(CancellationToken token, out bool __state)
        {
            __state = false;
            token.ThrowIfCancellationRequested();
            WorldLifetime.EnterReadLock();
            __state = true;
            token.ThrowIfCancellationRequested();
        }

        private static Exception WorkerFinalizer(Exception __exception, bool __state)
        {
            if (__state) WorldLifetime.ExitReadLock();
            return __exception;
        }

        private static void ClearWorldPrefix(out bool __state)
        {
            __state = false;
            if (!WorldLifetime.IsWriteLockHeld)
            {
                killQueue.Invoke(null, null);
                interruptRegeneration.Invoke(null, null);
            }
            // Wait for synchronous workers, not async tasks whose continuations need the main thread.
            WorldLifetime.EnterWriteLock();
            __state = true;
        }

        private static Exception ClearWorldFinalizer(Exception __exception, bool __state)
        {
            if (__state) WorldLifetime.ExitWriteLock();
            return __exception;
        }
    }
}
