using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace EESMedievalPatch
{
    // Hospitality registers components after generation; a newly added map can
    // already be queried by pawn needs and despawn handlers before registration.
    [HarmonyPatch]
    internal static class HospitalityMapCacheCapacityPatch
    {
        private static readonly object Sync = new object();
        private static Type cacheType;
        private static Type componentType;
        private static Func<Array> readCache;
        private static Action<Array> writeCache;

        private static bool Prepare()
        {
            cacheType = AccessTools.TypeByName("Hospitality.Utilities.MapComponentCache");
            if (cacheType == null) return false;
            FieldInfo field = AccessTools.Field(cacheType, "cachedComponents");
            if (field == null || !field.IsStatic || !field.FieldType.IsArray) return false;
            componentType = field.FieldType.GetElementType();
            if (!typeof(MapComponent).IsAssignableFrom(componentType)) return false;
            var access = Expression.Field(null, field);
            readCache = Expression.Lambda<Func<Array>>(Expression.Convert(access, typeof(Array))).Compile();
            var value = Expression.Parameter(typeof(Array), "value");
            writeCache = Expression.Lambda<Action<Array>>(
                Expression.Assign(access, Expression.Convert(value, field.FieldType)), value).Compile();
            return true;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var forMap = AccessTools.Method(cacheType, "GetMapComponent", new[] { typeof(Map) });
            var forThing = AccessTools.Method(cacheType, "GetMapComponent", new[] { typeof(Thing) });
            if (forMap != null) yield return forMap;
            if (forThing != null) yield return forThing;
        }

        private static void Prefix(object __0)
        {
            Map map = __0 is Map direct ? direct : (__0 as Thing)?.Map;
            if (map == null || map.Index < 0) return;
            int index = map.Index;
            Array cache = readCache();
            if (index < cache.Length) return;
            lock (Sync)
            {
                cache = readCache();
                if (index < cache.Length) return;
                Array expanded = Array.CreateInstance(componentType, Math.Max(index + 1, Find.Maps.Count + 6));
                Array.Copy(cache, expanded, cache.Length);
                expanded.SetValue(map.GetComponent(componentType), index);
                writeCache(expanded);
            }
        }
    }
}
