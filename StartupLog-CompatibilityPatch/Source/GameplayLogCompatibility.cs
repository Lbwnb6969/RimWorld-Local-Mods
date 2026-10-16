using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Xml;
using System.Threading;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace Local.StartupLogFixes
{
    public sealed partial class StartupLogFixesMod
    {
        private static Type bloomingComponentType;
        private static Type studyBuildingsType;
        private static string unityManagedDirectory;
        private static System.WeakReference<XmlDocument> villageDocument;
        private static HashSet<string> villageWorldObjectIds;
        private static HospitalThoughtSnapshot activeHospitalThought;
        private static FieldInfo heraldryInstanceField;
        private static FieldInfo heraldryPawnsField;

        private sealed class HospitalThoughtSnapshot
        {
            internal Pawn pawn;
            internal ThoughtDef def;
            internal ThoughtState state;
            internal HospitalThoughtSnapshot previous;
        }

        private static void PatchGameplayLogCompatibility(Harmony harmony)
        {
            PatchMeleeAnimationEventFactories(harmony);
            TryPatch(harmony, AccessTools.Method(typeof(PawnRenderNodeWorker_Carried), nameof(PawnRenderNodeWorker_Carried.CanDrawNow)),
                "carried render node invalid animated pawn", prefixName: nameof(CarriedPawnCanDrawPrefix));
            MethodInfo rocks = AccessTools.Method("ReGrowthCore.GenStep_RockChunks_GrowLowRockFormationFrom_Patch:TrySpawnThingAt");
            if (rocks != null)
                TryPatch(harmony, rocks, "ReGrowth rock decoration object preservation",
                    prefixName: nameof(ReGrowthRockDecorationPrefix));
            Type heraldry = AccessTools.TypeByName("VFEMedieval.WorldComponent_HeraldryPawns");
            if (heraldry != null)
            {
                heraldryInstanceField = AccessTools.Field(heraldry, "Instance");
                heraldryPawnsField = AccessTools.Field(heraldry, "heraldry_pawns");
                if (heraldryInstanceField != null && heraldryPawnsField?.FieldType == typeof(HashSet<Pawn>))
                {
                    TryPatch(harmony, AccessTools.Method(typeof(Pawn), nameof(Pawn.Discard), new[] { typeof(bool) }),
                        "VFE Medieval discarded heraldry pawn", postfixName: nameof(HeraldryPawnDiscardPostfix));
                    TryPatch(harmony, AccessTools.Method(heraldry, "ExposeData"), "VFE Medieval heraldry save lifetime",
                        prefixName: nameof(HeraldryExposeDataPrefix));
                }
            }
#if !WAYLITH
            unityManagedDirectory = Path.Combine(UnityEngine.Application.dataPath, "Managed");
            AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve += ResolveUnityReflectionDependency;
            string inputModule = Path.Combine(unityManagedDirectory, "UnityEngine.InputLegacyModule.dll");
            try
            {
                if (File.Exists(inputModule)) Assembly.ReflectionOnlyLoadFrom(inputModule);
            }
            catch (Exception exception) when (exception is FileLoadException || exception is FileNotFoundException || exception is BadImageFormatException)
            {
                Log.Warning("[StartupLog Compatibility] Unity reflection dependency preload failed: " + exception);
            }
#endif
            Type toddlerSwaddle = AccessTools.TypeByName("Toddlers.SwaddleBaby_Patch");
            if (toddlerSwaddle != null)
                TryPatch(harmony, AccessTools.Method(typeof(ChildcareUtility), "SwaddleBaby", new[] { typeof(Pawn) }),
                    "Toddlers invalid swaddle input", prefixName: nameof(ToddlerSwaddleInputPrefix));

            MethodInfo toddlerChew = AccessTools.Method("Toddlers.CarryIngestibleToChewSpot_Patch:Postfix");
            if (toddlerChew != null)
                TryPatch(harmony, toddlerChew, "Toddlers safe chew destination",
                    postfixName: nameof(ToddlerChewSpotPostfix));

            MethodInfo villageSave = AccessTools.Method("CaravanAdventures.CaravanStory.Quests.QuestCont_Village:ExposeData");
            if (villageSave != null)
                TryPatch(harmony, villageSave, "Caravan Adventures destroyed village reference",
                    prefixName: nameof(CaravanVillageExposeDataPrefix));

            MethodInfo thoughtUpdate = AccessTools.Method("YaOpt.Helpers.ParallelThoughtUpdater:Update");
            if (thoughtUpdate != null)
            {
                TryPatch(harmony, thoughtUpdate, "YaOpt hospital thought batch snapshot",
                    prefixName: nameof(HospitalThoughtBatchPrefix), finalizerName: nameof(HospitalThoughtBatchFinalizer));
                TryPatch(harmony, AccessTools.Method(typeof(ThoughtWorker), nameof(ThoughtWorker.CurrentState)),
                    "hospital thought batch result", prefixName: nameof(HospitalThoughtResultPrefix));
            }

            studyBuildingsType = AccessTools.TypeByName("VEF.Buildings.MapComponent_InteractableBuildingsInMap");
            Type studyWorker = AccessTools.TypeByName("VEF.Buildings.WorkGiver_StudyBuilding");
            if (studyBuildingsType != null && studyWorker != null)
            {
                TryPatch(harmony, AccessTools.Method(studyWorker, "ShouldSkip", new[] { typeof(Pawn), typeof(bool) }),
                    "VEF study-building skip lookup", prefixName: nameof(VefStudySkipInputPrefix),
                    transpilerName: nameof(VefMapLookupTranspiler));
                TryPatch(harmony, AccessTools.Method(studyWorker, "PotentialWorkThingsGlobal", new[] { typeof(Pawn) }),
                    "VEF study-building scan lookup", prefixName: nameof(VefStudyMapInputPrefix),
                    transpilerName: nameof(VefMapLookupTranspiler));
            }

            bloomingComponentType = AccessTools.TypeByName("VEF.Plants.MapComponent_BloomingPlants");
            if (bloomingComponentType == null) return;
            foreach (string name in new[] { "WorkGiver_RemoveWeeds", "WorkGiver_ExtractFlower" })
            {
                Type worker = AccessTools.TypeByName("VEF.Plants." + name);
                MethodInfo scan = worker == null ? null : AccessTools.Method(worker, "PotentialWorkThingsGlobal", new[] { typeof(Pawn) });
                if (scan == null) continue;
                TryPatch(harmony, scan, "VEF " + name + " map input",
                    prefixName: nameof(VefPlantMapInputPrefix));
                TryPatch(harmony, FindIteratorMoveNext(scan) ?? scan, "VEF " + name + " map component lookup",
                    transpilerName: nameof(VefMapLookupTranspiler));
            }
        }

        private static void HospitalThoughtBatchPrefix(SituationalThoughtHandler thoughtHandler, ref HospitalThoughtSnapshot __state)
        {
            __state = null;
            if (!UnityData.IsInMainThread || thoughtHandler?.pawn == null) return;
            ThoughtDef def = DefDatabase<ThoughtDef>.GetNamedSilentFail("HospitalPatientRoomStats");
            if (def == null) return;
            // Evaluate this room-dependent thought before YaOpt starts its worker jobs.
            ThoughtState state = def.Worker.CurrentState(thoughtHandler.pawn);
            __state = new HospitalThoughtSnapshot { pawn = thoughtHandler.pawn, def = def, state = state, previous = Volatile.Read(ref activeHospitalThought) };
            Volatile.Write(ref activeHospitalThought, __state);
        }

        public static void HeraldryPawnDiscardPostfix(Pawn __instance)
        {
            if (__instance == null || !__instance.Discarded) return;
            object component = heraldryInstanceField?.GetValue(null);
            if (component != null) ((HashSet<Pawn>)heraldryPawnsField.GetValue(component))?.Remove(__instance);
        }

        public static bool ReGrowthRockDecorationPrefix(IntVec3 centerPos, ThingDef thingDef, Map map, ref bool __result)
        {
            if (thingDef == null || map == null) return true;
            foreach (IntVec3 cell in GenAdj.OccupiedRect(centerPos, thingDef.defaultPlacingRot, thingDef.size))
            {
                if (!cell.InBounds(map)) continue;
                foreach (Thing thing in cell.GetThingList(map))
                {
                    if (thing is Pawn || thing is Corpse || thing is Blueprint || thing is Frame ||
                        thing.def.category == ThingCategory.Item && !thing.def.IsWithinCategory(ThingCategoryDefOf.StoneChunks) ||
                        thing.def.category == ThingCategory.Building && thing.def.building?.isNaturalRock != true)
                    {
                        __result = false;
                        return false;
                    }
                }
            }
            return true;
        }

        public static bool CarriedPawnCanDrawPrefix(PawnDrawParms parms, ref bool __result)
        {
            if (parms.pawn?.health != null) return true;
            __result = false;
            return false;
        }

        public static void HeraldryExposeDataPrefix(object __instance)
        {
            if (Scribe.mode != LoadSaveMode.Saving) return;
            int removed = RemoveDiscardedHeraldryReferences(__instance);
            if (removed > 0) Log.Warning("[StartupLog Compatibility] Removed " + removed + " discarded/null heraldry pawn references before saving.");
        }

        public static int RemoveDiscardedHeraldryReferences(object component)
        {
            var pawns = component == null ? null : (HashSet<Pawn>)heraldryPawnsField?.GetValue(component);
            return pawns?.RemoveWhere(pawn => pawn == null || pawn.Discarded) ?? 0;
        }

        private static Exception HospitalThoughtBatchFinalizer(Exception __exception, HospitalThoughtSnapshot __state)
        {
            if (__state != null) Interlocked.CompareExchange(ref activeHospitalThought, __state.previous, __state);
            return __exception;
        }

        public static bool HospitalThoughtResultPrefix(ThoughtWorker __instance, Pawn p, ref ThoughtState __result)
        {
            HospitalThoughtSnapshot snapshot = Volatile.Read(ref activeHospitalThought);
            if (snapshot == null || snapshot.pawn != p || snapshot.def != __instance?.def) return true;
            __result = snapshot.state;
            return false;
        }

        public static void CaravanVillageExposeDataPrefix()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars) RemoveConfirmedMissingVillageReference(Scribe.loader?.curXmlParent);
        }

        public static bool RemoveConfirmedMissingVillageReference(XmlNode parent)
        {
            XmlDocument document = parent?.OwnerDocument;
            XmlNode reference = parent?.SelectSingleNode("destroyedSettlement");
            string value = reference?.InnerText?.Trim();
            if (document == null || value?.StartsWith("WorldObject_", StringComparison.Ordinal) != true) return false;
            if (villageDocument == null || !villageDocument.TryGetTarget(out XmlDocument cached) || !ReferenceEquals(cached, document))
            {
                villageDocument = new System.WeakReference<XmlDocument>(document);
                villageWorldObjectIds = BuildPrefixedIdSet(document.SelectNodes("//worldObjects/li[def]/ID"), "WorldObject_");
            }
            if (villageWorldObjectIds.Count == 0 || villageWorldObjectIds.Contains(value)) return false;
            parent.RemoveChild(reference);
            LogMissingReferenceOnce("Caravan Adventures destroyed village", value);
            return true;
        }

#if !WAYLITH
        private static Assembly ResolveUnityReflectionDependency(object sender, ResolveEventArgs args)
        {
            var requested = new AssemblyName(args.Name);
            if (string.IsNullOrEmpty(requested.Name) || requested.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
            string path = Path.Combine(unityManagedDirectory, requested.Name + ".dll");
            if (!File.Exists(path) || !AssemblyName.ReferenceMatchesDefinition(requested, AssemblyName.GetAssemblyName(path))) return null;
            return Assembly.ReflectionOnlyLoadFrom(path);
        }

#endif
        [HarmonyPriority(Priority.First)]
        public static bool ToddlerSwaddleInputPrefix(Pawn baby, ref bool __result)
        {
            if (baby != null && baby.ageTracker != null &&
                (baby.RaceProps?.Humanlike != true || baby.apparel != null)) return true;
            __result = false;
            return false;
        }

        public static void ToddlerChewSpotPostfix(ref Toil __result, TargetIndex ingestibleInd)
        {
            Toil toil = __result;
            if (toil?.initAction?.Method.DeclaringType?.FullName?.StartsWith(
                    "Toddlers.CarryIngestibleToChewSpot_Patch+", StringComparison.Ordinal) != true) return;
            toil.initAction = () =>
            {
                Pawn actor = toil.actor;
                Map map = actor?.Map;
                Job job = actor?.CurJob;
                Thing food = job?.GetTarget(ingestibleInd).Thing;
                if (map == null || job == null || food == null || food.Destroyed)
                {
                    actor?.jobs?.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }
                // The original predicate requires Danger.Some and excludes safe cells.
                IntVec3 spot = RCellFinder.SpotToChewStandingNear(actor, food, c =>
                    c.InBounds(map) && actor.CanReserveSittableOrSpot(c) && c.GetDangerFor(actor, map) <= Danger.Some);
                if (!spot.IsValid || !spot.InBounds(map))
                {
                    actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }
                actor.ReserveSittableOrSpot(spot, job);
                map.pawnDestinationReservationManager.Reserve(actor, job, spot);
                actor.pather.StartPath(spot, PathEndMode.OnCell);
            };
        }

        public static MapComponent GetBloomingComponent(Map map)
        {
            return map?.components != null && bloomingComponentType != null
                ? map.GetComponent(bloomingComponentType) : null;
        }

        public static MapComponent GetNativeMapComponent(Map map, Type type)
        {
            return map?.components != null && type != null ? map.GetComponent(type) : null;
        }

        public static bool VefStudySkipInputPrefix(Pawn pawn, ref bool __result)
        {
            if (GetNativeMapComponent(pawn?.Map, studyBuildingsType) != null) return true;
            __result = true;
            return false;
        }

        public static bool VefStudyMapInputPrefix(Pawn pawn, ref IEnumerable<Thing> __result)
        {
            if (GetNativeMapComponent(pawn?.Map, studyBuildingsType) != null) return true;
            __result = Enumerable.Empty<Thing>();
            return false;
        }

        public static bool VefPlantMapInputPrefix(Pawn pawn, ref IEnumerable<Thing> __result)
        {
            if (GetBloomingComponent(pawn?.Map) != null) return true;
            __result = Enumerable.Empty<Thing>();
            return false;
        }

        public static IEnumerable<CodeInstruction> VefMapLookupTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> original = instructions.ToList();
            Func<CodeInstruction, bool> match = instruction =>
            {
                MethodInfo method = instruction.operand as MethodInfo;
                return (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                    method?.Name == "Get" && method.IsStatic && typeof(MapComponent).IsAssignableFrom(method.ReturnType) &&
                    method.DeclaringType?.IsGenericType == true &&
                    method.DeclaringType.GetGenericTypeDefinition().FullName == "VEF.Utils.FastMapComp`1" &&
                    method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == typeof(Map);
            };
            if (original.Count(match) != 1)
            {
                Log.WarningOnce("[StartupLog Compatibility] VEF plant lookup changed; retained original scanner.",
                    GenText.StableStringHash("StartupLogFixes:VEF:BloomingLookupPattern"));
                return original;
            }
            var result = new List<CodeInstruction>();
            foreach (CodeInstruction instruction in original)
            {
                if (!match(instruction)) result.Add(instruction);
                else
                {
                    Type component = ((MethodInfo)instruction.operand).ReturnType;
                    result.Add(new CodeInstruction(OpCodes.Ldtoken, component)
                        .MoveLabelsFrom(instruction).MoveBlocksFrom(instruction));
                    result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Type), nameof(Type.GetTypeFromHandle))));
                    result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(StartupLogFixesMod), nameof(GetNativeMapComponent))));
                    result.Add(new CodeInstruction(OpCodes.Castclass, component));
                }
            }
            return result;
        }
    }
}
