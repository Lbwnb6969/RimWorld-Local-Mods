using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;
using Verse.AI;
using Verse.Grammar;

namespace Local.StartupLogFixes
{
    public sealed partial class StartupLogFixesMod : Mod
    {
        private static readonly HashSet<string> MissingLordIds = new HashSet<string>();
        private static readonly HashSet<string> MissingBattleIds = new HashSet<string>();
        private static readonly HashSet<string> MissingLetterIds = new HashSet<string>();
        private static readonly HashSet<string> MissingPreceptIds = new HashSet<string>();
        private static readonly HashSet<string> MissingPawnIds = new HashSet<string>();
        private static readonly FieldInfo InteractionDefField =
            AccessTools.Field(typeof(PlayLogEntry_Interaction), "intDef");
        private static readonly FieldInfo TrainingTrackerPawnField =
            AccessTools.Field(typeof(Pawn_TrainingTracker), "pawn");
        private static readonly FieldInfo ThingMapIndexField =
            AccessTools.Field(typeof(Thing), "mapIndexOrState");
        private static readonly FieldInfo LordToilLordField =
            AccessTools.Field(typeof(LordToil), "lord");
        private static readonly FieldInfo LordToilDataField =
            AccessTools.Field(typeof(LordToil), "data");
        private static readonly ConcurrentDictionary<Type, FieldInfo> FuneralPresentFields =
            new ConcurrentDictionary<Type, FieldInfo>();
        private static MethodInfo meleeAnimGetFirstInvalidPawn;
        private static MethodInfo meleeAnimDestroy;
        private static FieldInfo meleeAnimWasInterruptedField;
        private static PropertyInfo meleeAnimActiveRenderersProperty;
        private static FieldInfo meleeAnimRendererMapField;
        private static FieldInfo meleeAnimManagerMapField;
        private static int meleeAnimMapDrawGuardNoticeLogged;
        [ThreadStatic]
        private static object activeMeleeAnimRenderer;
        private static System.WeakReference<XmlDocument> preparedDocument;
        private static int attemptedPatchCount;
        private static int successfulPatchCount;
        private static int performanceOptimizerExceptionBlockNoticeLogged;
        [ThreadStatic]
        private static int rimTalkLogScopeDepth;
        public StartupLogFixesMod(ModContentPack content) : base(content)
        {
            var harmony = new Harmony("Local.StartupLog.CompatibilityFixes");
#if WAYLITH
            PatchWaylithContent(harmony);
#endif
            PatchPerformanceOptimizerRuntimeGuard(harmony);
            MapModeWorldLifetimeCompatibility.Install(harmony);
            var targetType = AccessTools.TypeByName("OberoniaAurea.RatkinOrder.CompPropertiesShieldFightBack");
            var constructor = targetType == null ? null : AccessTools.Constructor(targetType);
            if (constructor != null)
            {
                TryPatch(harmony, constructor, "Ratkin shield constructor",
                    transpilerName: nameof(ShieldPropertiesConstructorTranspiler));
            }

            var compatibilityType = AccessTools.TypeByName("MapModeFramework.ModCompatibility");
            var patchWorldLayer = compatibilityType == null ? null : AccessTools.Method(compatibilityType, "PatchWorldLayer");
            if (patchWorldLayer != null)
            {
                TryPatch(harmony, patchWorldLayer, "Map Mode world-layer compatibility",
                    prefixName: nameof(MapModeWorldLayerPrefix));
            }

            var usefulMarksAutoCaptureType = AccessTools.TypeByName("UsefulMarksNewGamePreset.SortMarkers_AutoCapture");
            var usefulMarksAutoCapturePostfix = usefulMarksAutoCaptureType == null
                ? null
                : AccessTools.Method(usefulMarksAutoCaptureType, "Postfix");
            if (usefulMarksAutoCapturePostfix != null)
            {
                TryPatch(harmony, usefulMarksAutoCapturePostfix, "Useful Marks auto-capture",
                    prefixName: nameof(UsefulMarksAutoCapturePrefix));
            }

            var yaOptTypeSearcher = AccessTools.TypeByName("YaOpt.Helpers.TypeSearcher");
            var yaOptProcess = yaOptTypeSearcher == null
                ? null
                : AccessTools.Method(yaOptTypeSearcher, "Process");
            if (yaOptProcess != null)
            {
                TryPatch(harmony, yaOptProcess, "Yet Another Optimizer type search",
                    transpilerName: nameof(YaOptTypeSearchTranspiler));
            }

            var tacticalGroupsType = AccessTools.TypeByName("TacticalGroups.TacticalGroups");
            var tacticalGroupsExposeData = tacticalGroupsType == null
                ? null
                : AccessTools.Method(tacticalGroupsType, "ExposeData");
            if (tacticalGroupsExposeData != null)
            {
                TryPatch(harmony, tacticalGroupsExposeData, "Tactical Groups save repair",
                    prefixName: nameof(TacticalGroupsExposeDataPrefix));
            }

            var extendedDataStorageType = AccessTools.TypeByName("GiddyUp.ExtendedDataStorage");
            var extendedDataStorageExposeData = extendedDataStorageType == null
                ? null
                : AccessTools.Method(extendedDataStorageType, "ExposeData");
            if (extendedDataStorageExposeData != null)
            {
                TryPatch(harmony, extendedDataStorageExposeData, "Giddy-Up save repair",
                    prefixName: nameof(GiddyUpExposeDataPrefix));
            }

            var raidGroupType = AccessTools.TypeByName("VEF.Storyteller.RaidGroup");
            var raidGroupExposeData = raidGroupType == null ? null : AccessTools.Method(raidGroupType, "ExposeData");
            if (raidGroupExposeData != null)
            {
                TryPatch(harmony, raidGroupExposeData, "VEF raid-group save repair",
                    prefixName: nameof(RaidGroupExposeDataPrefix));
            }

            var toggleHediffType = AccessTools.TypeByName("VFEC.Comps.CompToggleHediff");
            var notifyUnequipped = toggleHediffType == null
                ? null
                : AccessTools.Method(toggleHediffType, "Notify_Unequipped");
            if (notifyUnequipped != null)
            {
                TryPatch(harmony, notifyUnequipped, "VFEC toggle-hediff cleanup",
                    prefixName: nameof(ToggleHediffNotifyUnequippedPrefix));
            }

            var vehicleCaravanHandlingType = AccessTools.TypeByName("Vehicles.Patch_CaravanHandling");
            var pawnMassUsageInVehicle = vehicleCaravanHandlingType == null
                ? null
                : AccessTools.Method(vehicleCaravanHandlingType, "PawnMassUsageInVehicle");
            if (pawnMassUsageInVehicle != null)
            {
                TryPatch(harmony, pawnMassUsageInVehicle, "Vehicle Framework caravan mass",
                    prefixName: nameof(VehiclePawnMassUsagePrefix));
            }

            var raidFactionGetter = AccessTools.PropertyGetter(typeof(RimWorld.Planet.TimedDetectionRaids), "RaidFaction");
            if (raidFactionGetter != null)
            {
                TryPatch(harmony, raidFactionGetter, "ReGrowth raid faction",
                    postfixName: nameof(ReGrowthRaidFactionPostfix));
            }

            var notifyDetectionRaid = AccessTools.Method(typeof(RimWorld.Planet.TimedDetectionRaids), "NotifyPlayer");
            if (notifyDetectionRaid != null)
            {
                TryPatch(harmony, notifyDetectionRaid, "ReGrowth detection raid",
                    prefixName: nameof(ReGrowthDetectionRaidPrefix));
            }

            PatchExposeDataSanitizers(harmony);

            PatchKnownRuntimeErrorGuards(harmony);
            PatchRimTalkErrorLogGuards(harmony);
            IncomingDamageOverflowHealthScale.EnsureInstalled();

            PatchPawnRenderTreeMaterialWarmup(harmony);
            PatchGameplayLogCompatibility(harmony);

            PatchRimTalkPawnQueryGuard(harmony);

            MethodInfo autonomousBillCheck = AccessTools.Method(typeof(Bill_Autonomous),
                nameof(Bill_Autonomous.PawnAllowedToStartAnew), new[] { typeof(Pawn) });
            if (autonomousBillCheck != null)
            {
                TryPatch(harmony, autonomousBillCheck, "autonomous bill worktable guard",
                    prefixName: nameof(AutonomousBillPawnAllowedPrefix));
            }

            PatchMedievalQuestTechGuards(harmony);

            PatchTrainingTrackerCompatibility(harmony);

            PatchRuntimeCorruptionGuards(harmony);

            PatchRimStoryAndRimTalkRuntimeCompatibility(harmony);

            PatchOberoniaQuestCompatibility(harmony);

            PatchVanillaMemesHeadgearGuard(harmony);

            var fillProcessorType = AccessTools.TypeByName("ProcessorFramework.WorkGiver_FillProcessor");
            var fillProcessorJob = fillProcessorType == null
                ? null
                : AccessTools.Method(fillProcessorType, "JobOnThing",
                    new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
            if (fillProcessorJob != null)
            {
                // Validate inputs before Processor Framework evaluates a candidate.
                // Unknown exceptions must remain visible so they cannot hide persistent
                // workgiver or save-state corruption.
                TryPatch(harmony, fillProcessorJob, "Processor Framework fill workgiver input guard",
                    prefixName: nameof(FillProcessorJobPrefix),
                    finalizerName: nameof(FillProcessorJobFinalizer));
            }

            Type socialThoughtType = AccessTools.TypeByName("RimWorld.Thought_SituationalSocial");
            MethodInfo socialOpinionOffset = socialThoughtType == null
                ? null
                : AccessTools.Method(socialThoughtType, "OpinionOffset", Type.EmptyTypes);
            if (socialOpinionOffset != null && socialOpinionOffset.ReturnType == typeof(float))
            {
                TryPatch(harmony, socialOpinionOffset,
                    "invalid social-thought stage opinion guard",
                    finalizerName: nameof(SocialThoughtOpinionOffsetFinalizer));
            }

            var vefExtractFlowerType = AccessTools.TypeByName("VEF.Plants.WorkGiver_ExtractFlower");
            var vefExtractFlower = vefExtractFlowerType == null
                ? null
                : AccessTools.Method(vefExtractFlowerType, "PotentialWorkThingsGlobal",
                    new[] { typeof(Pawn) });
            if (vefExtractFlower != null)
            {
                TryPatch(harmony, vefExtractFlower, "VEF extract-flower input guard",
                    prefixName: nameof(VefExtractFlowerPrefix));

                MethodInfo vefExtractFlowerMoveNext = FindIteratorMoveNext(vefExtractFlower);
                if (vefExtractFlowerMoveNext != null)
                {
                    TryPatch(harmony, vefExtractFlowerMoveNext,
                        "VEF extract-flower iterator scan guard",
                        finalizerName: nameof(VefExtractFlowerMoveNextFinalizer));
                }
                else
                {
                    TryPatch(harmony, vefExtractFlower, "VEF extract-flower scan guard",
                        finalizerName: nameof(VefExtractFlowerFinalizer));
                }
            }

            var captureExpansionRenderPatchType = AccessTools.TypeByName(
                "CaptureExpansion.PawnRenderTree_AdjustParms_Patch");
            var captureExpansionRenderPostfix = captureExpansionRenderPatchType == null
                ? null
                : AccessTools.Method(captureExpansionRenderPatchType, "Postfix");
            if (captureExpansionRenderPostfix != null)
            {
                TryPatch(harmony, captureExpansionRenderPostfix,
                    "Capture Expansion animated-pawn render guard",
                    finalizerName: nameof(CaptureExpansionRenderFinalizer));
            }

            var allRecipesGetter = AccessTools.PropertyGetter(typeof(ThingDef), "AllRecipes");
            if (allRecipesGetter != null)
            {
                TryPatch(harmony, allRecipesGetter, "Masters of Mankind recipe-list gate",
                    postfixName: nameof(MastersRecipeListPostfix));
            }
            var recipeAvailableGetter = AccessTools.PropertyGetter(typeof(RecipeDef), "AvailableNow");
            if (recipeAvailableGetter != null)
            {
                TryPatch(harmony, recipeAvailableGetter, "disabled legacy Masters bill gate",
                    prefixName: nameof(DisabledMastersRecipeAvailablePrefix));
            }
            TryPatch(harmony, AccessTools.Method(typeof(Bill_Production), nameof(Bill_Production.ShouldDoNow)),
                "disabled legacy Masters saved bill gate", postfixName: nameof(DisabledMastersBillShouldDoNowPostfix));

            var extraGrammarType = AccessTools.TypeByName("SpeakUp.ExtraGrammarUtility");
            var extraRules = extraGrammarType == null ? null : AccessTools.Method(extraGrammarType, "ExtraRules");
            if (extraRules != null)
            {
                TryPatch(harmony, extraRules, "Speak Up extra grammar",
                    prefixName: nameof(SpeakUpExtraRulesPrefix));
            }

            var extraRulesForPawn = extraGrammarType == null
                ? null
                : AccessTools.Method(extraGrammarType, "ExtraRulesForPawn",
                    new[] { typeof(string), typeof(Pawn), typeof(Pawn) });
            if (extraRulesForPawn != null)
            {
                TryPatch(harmony, extraRulesForPawn, "Speak Up missing relation rule guard",
                    finalizerName: nameof(SpeakUpExtraRulesForPawnFinalizer));
            }

            Log.Message("[StartupLog Compatibility] Registered " + successfulPatchCount + "/" +
                attemptedPatchCount + " available compatibility patches.");
        }

        private static void PatchPerformanceOptimizerRuntimeGuard(Harmony harmony)
        {
            Type optimizationType = AccessTools.TypeByName("PerformanceOptimizer.Optimization");
            MethodInfo patchMethod = optimizationType == null
                ? null
                : AccessTools.Method(optimizationType, "Patch",
                    new[] { typeof(MethodBase), typeof(MethodInfo), typeof(MethodInfo), typeof(MethodInfo) });
            if (patchMethod != null)
            {
                TryPatch(harmony, patchMethod, "Performance Optimizer exception-block patch guard",
                    prefixName: nameof(PerformanceOptimizerPatchPrefix),
                    finalizerName: nameof(PerformanceOptimizerPatchFinalizer));
            }
        }

        private static void PatchPawnRenderTreeMaterialWarmup(Harmony harmony)
        {
            MethodInfo parallelPreDraw = AccessTools.Method(typeof(PawnRenderTree), "ParallelPreDraw",
                new[] { typeof(PawnDrawParms) });
            if (parallelPreDraw != null)
            {
                TryPatch(harmony, parallelPreDraw,
                    "main-thread invisible pawn material warmup",
                    prefixName: nameof(PawnRenderTreeParallelPreDrawPrefix));
            }
        }

        public static bool PerformanceOptimizerPatchPrefix(MethodBase methodInfo, MethodInfo transpiler)
        {
            if (!IsPerformanceOptimizerTranspiler(transpiler) || methodInfo == null)
                return true;

            MethodBody body = methodInfo.GetMethodBody();
            if (body == null || body.ExceptionHandlingClauses.Count == 0)
                return true;

            if (Interlocked.Exchange(ref performanceOptimizerExceptionBlockNoticeLogged, 1) == 0)
            {
                Log.Message(
                    "[StartupLog Compatibility] Performance Optimizer exception-block targets were " +
                    "left on their original methods; compatible optimizations remain active.");
            }
            return false;
        }

        private static bool IsPerformanceOptimizerTranspiler(MethodInfo transpiler)
        {
            Type declaringType = transpiler?.DeclaringType;
            Assembly assembly = declaringType?.Assembly;
            return declaringType?.FullName != null &&
                declaringType.FullName.StartsWith("PerformanceOptimizer.", StringComparison.Ordinal) &&
                string.Equals(assembly?.GetName().Name, "PerformanceOptimizer",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static void PatchExposeDataSanitizers(Harmony harmony)
        {
            PatchExposeData(harmony, typeof(Pawn), nameof(PawnExposeDataPrefix));
            PatchExposeData(harmony, typeof(RimWorld.Pawn_RecordsTracker), nameof(PawnRecordsExposeDataPrefix));
            PatchExposeData(harmony, typeof(PawnDestinationReservationManager),
                nameof(PawnDestinationReservationExposeDataPrefix));
            PatchExposeData(harmony, typeof(PlayLog), nameof(PlayLogExposeDataPrefix),
                nameof(PlayLogExposeDataPostfix));
            PatchExposeData(harmony, typeof(LetterStack), nameof(LetterStackExposeDataPrefix));
            PatchExposeData(harmony, typeof(Game), nameof(SpecialGlobalEventManagerExposeDataPrefix));

            var creepJoinerOutcomesType = AccessTools.TypeByName("RimWorld.QuestPart_CreepJoinerOutcomes");
            if (creepJoinerOutcomesType != null)
            {
                PatchExposeData(harmony, creepJoinerOutcomesType, nameof(CreepJoinerOutcomesExposeDataPrefix));
            }
            PatchExposeData(harmony, typeof(Building), nameof(BuildingArtExposeDataPrefix));
            PatchExposeData(harmony, typeof(Thought_Memory), nameof(ThoughtMemoryExposeDataPrefix));

            var roundMemoryType = AccessTools.TypeByName("RimTalk.Memory.RoundMemory");
            var roundMemoryExposeData = roundMemoryType == null ? null : AccessTools.Method(roundMemoryType, "ExposeData");
            if (roundMemoryExposeData != null)
            {
                TryPatch(harmony, roundMemoryExposeData, "RimTalk round-memory save repair",
                    prefixName: nameof(RoundMemoryExposeDataPrefix));
            }

            var lordManagerExposeData = AccessTools.Method(typeof(LordManager), "ExposeData");
            if (lordManagerExposeData != null)
            {
                TryPatch(harmony, lordManagerExposeData, "RimStory damaged-lord cleanup",
                    postfixName: nameof(LordManagerExposeDataPostfix));
            }

            var alcoholTrackerType = AccessTools.TypeByName(
                "VanillaMemesExpanded.WorldComponent_AlcoholScarsAndSlaveryTracker");
            var alcoholTrackerExposeData = alcoholTrackerType == null
                ? null
                : AccessTools.Method(alcoholTrackerType, "ExposeData");
            if (alcoholTrackerExposeData != null)
            {
                TryPatch(harmony, alcoholTrackerExposeData, "Vanilla Memes alcohol tracker save repair",
                    prefixName: nameof(AlcoholTrackerExposeDataPrefix));
            }

            var travellingTradingTrackerType = AccessTools.TypeByName(
                "VanillaMemesExpanded.WorldComponent_TravellingAndTradingTracker");
            var travellingTradingTrackerExposeData = travellingTradingTrackerType == null
                ? null
                : AccessTools.Method(travellingTradingTrackerType, "ExposeData");
            if (travellingTradingTrackerExposeData != null)
            {
                TryPatch(harmony, travellingTradingTrackerExposeData,
                    "Vanilla Memes travelling/trading tracker save repair",
                    prefixName: nameof(TravellingTradingTrackerExposeDataPrefix));
            }

        }

        private static void PatchRimTalkPawnQueryGuard(Harmony harmony)
        {
            var pawnSelectorType = AccessTools.TypeByName("RimTalk.Service.PawnSelector");
            var nearbyPawns = pawnSelectorType == null
                ? null
                : AccessTools.Method(pawnSelectorType, "GetNearbyPawnsInternal");
            if (nearbyPawns != null)
            {
                TryPatch(harmony, nearbyPawns, "RimTalk invalid-map pawn query guard",
                    prefixName: nameof(RimTalkNearbyPawnsPrefix));
            }
        }

        private static void PatchRimTalkErrorLogGuards(Harmony harmony)
        {
            MethodInfo message = AccessTools.Method(typeof(Log), nameof(Log.Message),
                new[] { typeof(string) });
            if (message != null)
            {
                TryPatch(harmony, message, "RimTalk diagnostic message log-window guard",
                    prefixName: nameof(RimTalkTransportErrorLogPrefix),
                    finalizerName: nameof(RimTalkTransportErrorLogFinalizer));
            }

            MethodInfo objectMessage = AccessTools.Method(typeof(Log), nameof(Log.Message),
                new[] { typeof(object) });
            if (objectMessage != null)
            {
                TryPatch(harmony, objectMessage, "RimTalk object diagnostic message log-window guard",
                    prefixName: nameof(RimTalkTransportErrorLogPrefix),
                    finalizerName: nameof(RimTalkTransportErrorLogFinalizer));
            }

            MethodInfo warning = AccessTools.Method(typeof(Log), nameof(Log.Warning),
                new[] { typeof(string) });
            if (warning != null)
            {
                TryPatch(harmony, warning, "RimTalk diagnostic warning log-window guard",
                    prefixName: nameof(RimTalkTransportErrorLogPrefix),
                    finalizerName: nameof(RimTalkTransportErrorLogFinalizer));
            }

            MethodInfo error = AccessTools.Method(typeof(Log), nameof(Log.Error), new[] { typeof(string) });
            if (error != null)
            {
                TryPatch(harmony, error, "RimTalk diagnostic error log-window guard",
                    prefixName: nameof(RimTalkTransportErrorLogPrefix),
                    finalizerName: nameof(RimTalkTransportErrorLogFinalizer));
            }

            MethodInfo errorOnce = AccessTools.Method(typeof(Log), nameof(Log.ErrorOnce),
                new[] { typeof(string), typeof(int) });
            if (errorOnce != null)
            {
                TryPatch(harmony, errorOnce, "RimTalk diagnostic error-once log-window guard",
                    prefixName: nameof(RimTalkTransportErrorLogPrefix),
                    finalizerName: nameof(RimTalkTransportErrorLogFinalizer));
            }

            MethodInfo tryOpenLogWindow = AccessTools.Method(typeof(Log), nameof(Log.TryOpenLogWindow));
            if (tryOpenLogWindow != null)
            {
                TryPatch(harmony, tryOpenLogWindow, "RimTalk automatic DebugLog window guard",
                    prefixName: nameof(RimTalkTryOpenLogWindowPrefix));
            }

            Type loggerType = AccessTools.TypeByName("RimTalk.Util.Logger");
            MethodInfo loggerWarning = loggerType == null
                ? null
                : AccessTools.Method(loggerType, "Warning", new[] { typeof(object) });
            if (loggerWarning != null)
            {
                TryPatch(harmony, loggerWarning, "RimTalk logger transport warning suppression",
                    prefixName: nameof(RimTalkLoggerWarningPrefix));
            }

            Type errorHandlerType = AccessTools.TypeByName("RimTalk.Error.AIErrorHandler");
            MethodInfo quotaWarning = errorHandlerType == null
                ? null
                : AccessTools.Method(errorHandlerType, "ShowQuotaWarning",
                    new[] { typeof(Exception) });
            if (quotaWarning != null)
            {
                TryPatch(harmony, quotaWarning, "RimTalk quota alert suppression",
                    prefixName: nameof(RimTalkQuotaWarningPrefix));
            }

            MethodInfo generationWarning = errorHandlerType == null
                ? null
                : AccessTools.Method(errorHandlerType, "ShowGenerationWarning",
                    new[] { typeof(Exception) });
            if (generationWarning != null)
            {
                TryPatch(harmony, generationWarning, "RimTalk generation failure message suppression",
                    prefixName: nameof(RimTalkGenerationWarningPrefix));
            }

            MethodInfo retryMessage = errorHandlerType == null
                ? null
                : AccessTools.Method(errorHandlerType, "ShowRetryMessage",
                    new[] { typeof(Exception), typeof(string) });
            if (retryMessage != null)
            {
                TryPatch(harmony, retryMessage, "RimTalk transport retry message suppression",
                    prefixName: nameof(RimTalkRetryMessagePrefix));
            }

            MethodInfo visionFallbackMessage = errorHandlerType == null
                ? null
                : AccessTools.Method(errorHandlerType, "ShowVisionFallbackMessage",
                    new[] { typeof(string) });
            if (visionFallbackMessage != null)
            {
                TryPatch(harmony, visionFallbackMessage, "RimTalk vision fallback alert suppression",
                    prefixName: nameof(RimTalkVisionFallbackMessagePrefix));
            }

            Type messageUtilType = AccessTools.TypeByName("RimTalk.Memory.Utils.MessageUtil");
            MethodInfo messageAndError = messageUtilType == null
                ? null
                : AccessTools.Method(messageUtilType, "MessageAndError",
                    new[] { typeof(string) });
            if (messageAndError != null)
            {
                TryPatch(harmony, messageAndError,
                    "RimTalk ExpandMemory diagnostic message guard",
                    prefixName: nameof(RimTalkMessageAndErrorPrefix));
            }

            PatchRimTalkMessages(harmony);
        }

        private static void PatchRimTalkMessages(Harmony harmony)
        {
            Type[][] signatures = new[]
            {
                new[] { typeof(string), typeof(LookTargets), typeof(MessageTypeDef),
                    typeof(RimWorld.Quest), typeof(bool) },
                new[] { typeof(string), typeof(LookTargets), typeof(MessageTypeDef), typeof(bool) },
                new[] { typeof(string), typeof(MessageTypeDef), typeof(bool) },
                new[] { typeof(Message), typeof(bool) }
            };

            foreach (Type[] signature in signatures)
            {
                MethodInfo message = AccessTools.Method(typeof(Messages), nameof(Messages.Message), signature);
                if (message != null)
                {
                    TryPatch(harmony, message, "RimTalk alert message suppression",
                        prefixName: nameof(RimTalkMessageAlertPrefix));
                }
            }
        }

        internal static void PatchOberoniaQuestCompatibility(Harmony harmony)
        {
            MethodInfo questManagerExposeData = AccessTools.Method(typeof(QuestManager), "ExposeData");
            if (questManagerExposeData != null)
            {
                TryPatch(harmony, questManagerExposeData, "Oberonia invalid quest cleanup",
                    postfixName: nameof(MastersOfMankindRecipeCompatibility.OberoniaQuestManagerExposeDataPostfix));
            }

            foreach (string typeName in new[]
            {
                "OberoniaAurea_Frame.QuestPart_InitiateCategoryTradeRequest",
                "OberoniaAurea_Frame.QuestPart_InitiateTradeRequest"
            })
            {
                Type tradePartType = AccessTools.TypeByName(typeName);
                if (tradePartType == null)
                    continue;

                MethodInfo involvedFactions = AccessTools.PropertyGetter(tradePartType, "InvolvedFactions");
                if (involvedFactions != null)
                {
                    TryPatch(harmony, involvedFactions, "Oberonia quest faction display guard",
                        postfixName: nameof(MastersOfMankindRecipeCompatibility.OberoniaInvolvedFactionsPostfix));
                }

                MethodInfo signalReceived = AccessTools.Method(tradePartType, "Notify_QuestSignalReceived");
                if (signalReceived != null)
                {
                    TryPatch(harmony, signalReceived, "Oberonia invalid trade target guard",
                        prefixName: nameof(MastersOfMankindRecipeCompatibility.OberoniaTradeSignalPrefix));
                }
            }
        }

        internal static void PatchVanillaMemesHeadgearGuard(Harmony harmony)
        {
            Type patchType = AccessTools.TypeByName(
                "VanillaMemesExpanded.VanillaMemesExpanded_PawnRenderNodeWorker_Apparel_Head_HeadgearVisible_Patch");
            MethodInfo postfix = patchType == null ? null : AccessTools.Method(patchType, "Postfix");
            if (postfix != null)
            {
                TryPatch(harmony, postfix, "Vanilla Memes headgear null guard",
                    prefixName: nameof(MastersOfMankindRecipeCompatibility.VanillaMemesHeadgearPrefix));
            }
        }

        private static void PatchRimStoryAndRimTalkRuntimeCompatibility(Harmony harmony)
        {
            foreach (string eventTypeName in new[] { "RimStory.ADead", "RimStory.AMemorialDay" })
            {
                Type deadEventType = AccessTools.TypeByName(eventTypeName);
                MethodInfo exposeData = deadEventType == null
                    ? null
                    : AccessTools.DeclaredMethod(deadEventType, "ExposeData");
                if (exposeData != null)
                {
                    TryPatch(harmony, exposeData, eventTypeName + " discarded-pawn save repair",
                        prefixName: nameof(RimStoryDeadPawnExposeDataPrefix));
                }

                MethodInfo tryStartDeadEvent = deadEventType == null
                    ? null
                    : AccessTools.Method(deadEventType, "TryStartEvent", new[] { typeof(Map) });
                if (tryStartDeadEvent != null)
                {
                    TryPatch(harmony, tryStartDeadEvent, eventTypeName + " invalid death-event guard",
                        prefixName: nameof(RimStoryDeadEventPrefix));
                }
            }

            Type funeralType = AccessTools.TypeByName("RimStory.LordToil_Funeral");
            ConstructorInfo constructor = funeralType == null
                ? null
                : AccessTools.GetDeclaredConstructors(funeralType)
                    .FirstOrDefault(method =>
                    {
                        ParameterInfo[] parameters = method.GetParameters();
                        return parameters.Length == 2 &&
                            parameters[0].ParameterType == typeof(IntVec3) &&
                            parameters[1].ParameterType == typeof(int) &&
                            !parameters[0].ParameterType.IsByRef &&
                            !parameters[1].ParameterType.IsByRef;
                    });
            if (constructor != null)
            {
                TryPatch(harmony, constructor, "RimStory funeral Lord binding",
                    transpilerName: nameof(RimStoryFuneralTranspiler));
            }

            MethodInfo funeralTick = funeralType == null
                ? null
                : AccessTools.Method(funeralType, "LordToilTick");
            if (funeralTick != null)
            {
                TryPatch(harmony, funeralTick, "RimStory funeral participant timer initialization",
                    prefixName: nameof(RimStoryFuneralTickPrefix));
            }

        }

        private static void PatchTrainingTrackerCompatibility(Harmony harmony)
        {
            Type trackerType = typeof(Pawn_TrainingTracker);
            MethodInfo simpleCheck = AccessTools.Method(trackerType, "CanAssignToTrain",
                new[] { typeof(TrainableDef) });
            if (simpleCheck != null)
            {
                TryPatch(harmony, simpleCheck, "training tracker unbound-pawn guard",
                    prefixName: nameof(TrainingCanAssignPrefix));
            }

            MethodInfo visibleCheck = AccessTools.Method(trackerType, "CanAssignToTrain",
                new[] { typeof(TrainableDef), typeof(bool).MakeByRefType() });
            if (visibleCheck != null)
            {
                TryPatch(harmony, visibleCheck, "training tracker visible-check guard",
                    prefixName: nameof(TrainingCanAssignVisiblePrefix));
            }

            // The public overloads delegate to this private overload during
            // BackCompatibility post-load repair.  Save data can contain an
            // unbound tracker, so guard the actual dereference point too.
            MethodInfo internalCheck = AccessTools.GetDeclaredMethods(trackerType)
                .FirstOrDefault(method => method.Name == "CanAssignToTrain" &&
                    method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(new[]
                    {
                        typeof(TrainableDef),
                        typeof(ThingDef),
                        typeof(bool).MakeByRefType(),
                        typeof(Pawn)
                    }));
            if (internalCheck != null)
            {
                TryPatch(harmony, internalCheck, "training tracker internal unbound-pawn guard",
                    prefixName: nameof(TrainingCanAssignInternalPrefix),
                    finalizerName: nameof(TrainingCanAssignInternalFinalizer));
            }
        }

        private static void PatchRuntimeCorruptionGuards(Harmony harmony)
        {
            MethodInfo registerLoaded = AccessTools.Method(typeof(LoadedObjectDirectory), "RegisterLoaded");
            if (registerLoaded != null)
            {
                TryPatch(harmony, registerLoaded, "Tactical Groups duplicate object guard",
                    prefixName: nameof(RegisterLoadedPrefix));
            }

            MethodInfo thoughtState = AccessTools.Method(typeof(ThoughtWorker), "CurrentState",
                new[] { typeof(Pawn) });
            if (thoughtState != null)
            {
                TryPatch(harmony, thoughtState, "invalid-map pawn thought guard",
                    finalizerName: nameof(ThoughtWorkerCurrentStateFinalizer));
            }

            InstallBirthdayRegistryCompatibility(harmony);

            MethodInfo factionLeader = AccessTools.Method(typeof(Faction), "TryGenerateNewLeader");
            if (factionLeader != null)
            {
                TryPatch(harmony, factionLeader, "failed faction leader pawn guard",
                    finalizerName: nameof(FactionTryGenerateNewLeaderFinalizer));
            }

            MethodInfo pawnRenderAt = AccessTools.Method(typeof(PawnRenderer), "RenderPawnAt",
                new[] { typeof(UnityEngine.Vector3), typeof(Rot4?), typeof(bool) });
            if (pawnRenderAt != null)
            {
                TryPatch(harmony, pawnRenderAt, "Melee Animations invalid execution render guard",
                    finalizerName: nameof(PawnRendererRenderPawnAtFinalizer));
            }

            Type animRendererType = AccessTools.TypeByName("AM.AnimRenderer");
            if (animRendererType != null)
            {
                meleeAnimActiveRenderersProperty = AccessTools.Property(animRendererType, "ActiveRenderers");
                meleeAnimRendererMapField = AccessTools.Field(animRendererType, "Map");
                meleeAnimGetFirstInvalidPawn = AccessTools.Method(animRendererType, "GetFirstInvalidPawn",
                    new[] { typeof(bool) });
                meleeAnimDestroy = AccessTools.Method(animRendererType, "Destroy", Type.EmptyTypes);
                meleeAnimWasInterruptedField = AccessTools.Field(animRendererType, "<WasInterrupted>k__BackingField");
                MethodInfo drawSingle = AccessTools.Method(animRendererType, "DrawSingle");
                if (drawSingle != null)
                {
                    TryPatch(harmony, drawSingle, "Melee Animations invalid animator cleanup",
                        prefixName: nameof(MeleeAnimDrawSinglePrefix),
                        finalizerName: nameof(MeleeAnimDrawSingleFinalizer));
                }

                Type animationManagerType = AccessTools.TypeByName("AM.AnimationManager");
                MethodInfo animationManagerDraw = animationManagerType == null
                    ? null
                    : AccessTools.Method(animationManagerType, "Draw", new[] { typeof(float) });
                if (animationManagerDraw != null)
                {
                    meleeAnimManagerMapField = AccessTools.Field(animationManagerType, "map");
                    TryPatch(harmony, animationManagerDraw,
                        "Melee Animations empty-map draw guard",
                        prefixName: nameof(MeleeAnimationManagerDrawPrefix));
                }
            }

            Type vehiclePathingType = AccessTools.TypeByName("Vehicles.Patch_VehiclePathing");
            MethodInfo vehicleSetPosition = vehiclePathingType == null
                ? null
                : AccessTools.Method(vehiclePathingType, "SetPositionAndUpdateVehicleRegions",
                    new[] { typeof(Thing) });
            if (vehicleSetPosition != null)
            {
                TryPatch(harmony, vehicleSetPosition, "Vehicle Framework invalid pawn-flyer position guard",
                    prefixName: nameof(VehicleSetPositionAndUpdateRegionsPrefix));
            }

            Type knockbackFlyerType = AccessTools.TypeByName("AM.Grappling.KnockbackFlyer");
            MethodInfo getEndCell = knockbackFlyerType == null
                ? null
                : AccessTools.Method(knockbackFlyerType, "GetEndCell");
            if (getEndCell != null)
            {
                TryPatch(harmony, getEndCell, "Melee Animations knockback end-cell bounds guard",
                    postfixName: nameof(KnockbackFlyerGetEndCellPostfix));
            }

            MethodInfo makeKnockbackFlyer = knockbackFlyerType == null
                ? null
                : AccessTools.Method(knockbackFlyerType, "MakeKnockbackFlyer");
            if (makeKnockbackFlyer != null)
            {
                TryPatch(harmony, makeKnockbackFlyer, "Melee Animations knockback target bounds guard",
                    prefixName: nameof(KnockbackFlyerMakePrefix));
            }
        }

        public static Exception FactionTryGenerateNewLeaderFinalizer(
            Exception __exception,
            Faction __instance)
        {
            if (!(__exception is NullReferenceException) ||
                !ExceptionOriginatesIn(__exception, "Verse.PawnGenerator"))
                return __exception;

            string factionName = __instance?.def?.defName ?? "<unknown>";
            Log.WarningOnce(
                "[StartupLog Compatibility] Skipped one faction leader generation after PawnGenerator returned no valid leader for " +
                factionName + "; other faction-generation exceptions remain visible.",
                GenText.StableStringHash("StartupLogFixes:Faction:LeaderGeneration:" + factionName));
            return null;
        }

        public static bool RegisterLoadedPrefix(ILoadReferenceable reffable)
        {
            Type type = reffable?.GetType();
            bool tacticalGroup = type?.FullName != null && type.FullName.Contains("TacticalGroups.PawnGroup");
            bool factionTerritoriesLetter = type?.FullName ==
                "FactionTerritories.Vassalise.ChoiceLetter_VassalPurchasedPawn";
            if (!tacticalGroup && !factionTerritoriesLetter)
                return true;

            string loadId = reffable.GetUniqueLoadID();
            if (string.IsNullOrEmpty(loadId))
                return true;

            // LoadedObjectDirectory keeps its registry private.  Inspect all
            // instance dictionary fields so this remains compatible across
            // RimWorld minor versions without changing the vanilla type.
            LoadedObjectDirectory directory = CurrentLoadedObjectDirectory();
            if (directory == null)
                return true;

            foreach (FieldInfo field in typeof(LoadedObjectDirectory)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (!typeof(IDictionary).IsAssignableFrom(field.FieldType))
                    continue;

                IDictionary registry = field.GetValue(directory) as IDictionary;
                if (registry != null && registry.Contains(loadId))
                {
                    string kind = tacticalGroup ? "Tactical Groups object" : "Faction Territories letter";
                    Log.WarningOnce("[StartupLog Compatibility] Skipped duplicate " + kind + " " + loadId + ".",
                        factionTerritoriesLetter ? 1768432957 : 1768432953);
                    return false;
                }
            }

            return true;
        }

        private static LoadedObjectDirectory CurrentLoadedObjectDirectory()
        {
            CrossRefHandler crossRefs = Scribe.loader?.crossRefs;
            if (crossRefs == null)
                return null;

            FieldInfo field = AccessTools.Field(typeof(CrossRefHandler), "loadedObjectDirectory");
            if (field == null || !typeof(LoadedObjectDirectory).IsAssignableFrom(field.FieldType))
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Skipped duplicate-object lookup because the current CrossRefHandler API has no compatible loaded-object directory field.",
                    GenText.StableStringHash("StartupLogFixes:LoadedObjectDirectory:UnknownApi"));
                return null;
            }

            return field.GetValue(crossRefs) as LoadedObjectDirectory;
        }

        public static Exception ThoughtWorkerCurrentStateFinalizer(
            Exception __exception,
            Pawn p,
            ref ThoughtState __result)
        {
            if (__exception is KeyNotFoundException keyNotFound &&
                keyNotFound.Message != null &&
                keyNotFound.Message.Contains("Map-") &&
                ExceptionOriginatesIn(__exception, "AlphaMemes.ThoughtWorker_Precept_TreeDensity"))
            {
                __result = ThoughtState.Inactive;
                Log.WarningOnce(
                    "[StartupLog Compatibility] Ignored thought evaluation with a stale map dictionary key.",
                    GenText.StableStringHash("StartupLogFixes:ThoughtWorker:StaleMapKey"));
                return null;
            }

            if (__exception is NullReferenceException && HasInvalidMapIndex(p))
            {
                __result = ThoughtState.Inactive;
                Log.WarningOnce("[StartupLog Compatibility] Ignored thought evaluation for pawn " +
                    (p?.LabelShort ?? "<unknown>") + " with an invalid map index.", 1768432954);
                return null;
            }

            return __exception;
        }

        public static Exception PawnRendererRenderPawnAtFinalizer(
            Exception __exception,
            PawnRenderer __instance)
        {
            if (!(__exception is NullReferenceException))
                return __exception;

            string text = __exception.ToString();
            if (text.IndexOf("AM.AnimRenderer.DrawPawns", StringComparison.Ordinal) < 0 ||
                text.IndexOf("AM.AnimRenderer.DrawSingle", StringComparison.Ordinal) < 0)
                return __exception;

            object renderer = activeMeleeAnimRenderer;
            if (renderer != null)
                StopMeleeAnimRenderer(renderer, "render exception");
            Log.WarningOnce(
                "[StartupLog Compatibility] Stopped one Melee Animations renderer after an execution render NRE; other pawn render exceptions remain visible.",
                GenText.StableStringHash("StartupLogFixes:PawnRenderer:MeleeAnimation:InvalidExecution"));
            return null;
        }

        public static bool MeleeAnimDrawSinglePrefix(object renderer)
        {
            activeMeleeAnimRenderer = renderer;
            if (renderer == null || meleeAnimGetFirstInvalidPawn == null)
                return renderer != null;

            try
            {
                Pawn invalidPawn = meleeAnimGetFirstInvalidPawn.Invoke(renderer, new object[] { false }) as Pawn;
                if (invalidPawn == null)
                    return true;

                StopMeleeAnimRenderer(renderer, "invalid pawn " + (invalidPawn.LabelShort ?? "<unknown>"));
                return false;
            }
            catch (Exception exception)
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Invalid animator pre-draw check failed; leaving native rendering visible. " +
                    exception.Message,
                    GenText.StableStringHash("StartupLogFixes:MeleeAnimations:InvalidAnimator:GuardFailure"));
                return true;
            }
        }

        public static Exception MeleeAnimDrawSingleFinalizer(Exception __exception)
        {
            activeMeleeAnimRenderer = null;
            return __exception;
        }

        public static bool MeleeAnimationManagerDrawPrefix(object __instance)
        {
            if (__instance == null || meleeAnimActiveRenderersProperty == null ||
                meleeAnimRendererMapField == null || meleeAnimManagerMapField == null)
                return true;

            Map map = meleeAnimManagerMapField.GetValue(__instance) as Map;
            if (map == null)
                return true;

            object active = meleeAnimActiveRenderersProperty.GetValue(null, null);
            if (!(active is System.Collections.IEnumerable renderers))
                return true;

            foreach (object renderer in renderers)
            {
                if (renderer != null && ReferenceEquals(map, meleeAnimRendererMapField.GetValue(renderer)))
                    return true;
            }

            if (Interlocked.Exchange(ref meleeAnimMapDrawGuardNoticeLogged, 1) == 0)
            {
                Log.Message("[StartupLog Compatibility] Skipping Melee Animations draw on a map with no active renderers; map update callbacks remain enabled.");
            }
            return false;
        }

        private static void StopMeleeAnimRenderer(object renderer, string reason)
        {
            if (renderer == null)
                return;

            try
            {
                meleeAnimWasInterruptedField?.SetValue(renderer, true);
                meleeAnimDestroy?.Invoke(renderer, null);
                Log.WarningOnce(
                    "[StartupLog Compatibility] Stopped one Melee Animations renderer (" + reason + ").",
                    GenText.StableStringHash("StartupLogFixes:MeleeAnimations:InvalidAnimator:" + reason));
            }
            catch (Exception exception)
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Failed to stop one Melee Animations renderer after " + reason + ": " + exception.Message,
                    GenText.StableStringHash("StartupLogFixes:MeleeAnimations:InvalidAnimator:StopFailure"));
            }
        }

        public static bool VehicleSetPositionAndUpdateRegionsPrefix(Thing __0)
        {
            if (!(__0 is PawnFlyer flyer))
                return true;

            Map map = flyer.Map;
            if (!flyer.Destroyed && flyer.Spawned && map != null &&
                flyer.Position.x >= 0 && flyer.Position.z >= 0 &&
                flyer.Position.x < map.Size.x && flyer.Position.z < map.Size.z)
                return true;

            Log.WarningOnce(
                "[StartupLog Compatibility] Skipped Vehicle Framework region update for an invalid pawn flyer position.",
                GenText.StableStringHash("StartupLogFixes:Vehicles:PawnFlyer:InvalidPosition"));
            return false;
        }

        public static void KnockbackFlyerGetEndCellPostfix(Pawn victim, ref IntVec3 __result)
        {
            ClampKnockbackTarget(victim, ref __result);
        }

        public static bool KnockbackFlyerMakePrefix(Pawn victim, ref IntVec3 targetPos)
        {
            ClampKnockbackTarget(victim, ref targetPos);
            return true;
        }

        private static bool ClampKnockbackTarget(Pawn victim, ref IntVec3 targetPos)
        {
            Map map = victim?.Map;
            if (map == null || targetPos.InBounds(map))
                return false;

            IntVec3 fallback = victim.Position;
            if (!fallback.InBounds(map))
                fallback = map.Center;
            if (!fallback.InBounds(map))
                return false;

            targetPos = fallback;
            Log.WarningOnce(
                "[StartupLog Compatibility] Clamped one Melee Animations knockback target back inside its map.",
                GenText.StableStringHash("StartupLogFixes:MeleeAnimations:KnockbackTarget:OutOfBounds"));
            return true;
        }

        public static Exception AlphaMemesTreeDensityFinalizer(
            Exception __exception,
            ref ThoughtState __result)
        {
            if (__exception is KeyNotFoundException keyNotFound &&
                keyNotFound.Message != null &&
                keyNotFound.Message.Contains("Map-") &&
                ExceptionOriginatesIn(__exception, "AlphaMemes.ThoughtWorker_Precept_TreeDensity"))
            {
                __result = ThoughtState.Inactive;
                Log.WarningOnce(
                    "[StartupLog Compatibility] Ignored Alpha Memes tree-density thought evaluation for a stale map key.",
                    GenText.StableStringHash("StartupLogFixes:AlphaMemes:TreeDensity:StaleMapKey"));
                return null;
            }

            return __exception;
        }

        private static bool HasInvalidMapIndex(Pawn pawn)
        {
            if (pawn == null || Find.Maps == null)
                return false;

            object rawMapIndex = ThingMapIndexField?.GetValue(pawn);
            if (rawMapIndex == null)
                return false;

            long mapIndex;
            switch (rawMapIndex)
            {
                case sbyte signedByte:
                    mapIndex = signedByte;
                    break;
                case byte unsignedByte:
                    mapIndex = unsignedByte;
                    break;
                case short signedShort:
                    mapIndex = signedShort;
                    break;
                case ushort unsignedShort:
                    mapIndex = unsignedShort;
                    break;
                case int signedInt:
                    mapIndex = signedInt;
                    break;
                case uint unsignedInt:
                    mapIndex = unsignedInt;
                    break;
                case long signedLong:
                    mapIndex = signedLong;
                    break;
                case ulong unsignedLong:
                    return unsignedLong >= (ulong)Find.Maps.Count;
                default:
                    return false;
            }

            // RimWorld reserves -1 for a normal unspawned Thing. Memory and
            // discarded states are lower values and are the invalid cases here.
            return mapIndex < -1 || mapIndex >= Find.Maps.Count;
        }

        public static bool TrainingCanAssignPrefix(
            Pawn_TrainingTracker __instance,
            ref AcceptanceReport __result)
        {
            if (!TrainingTrackerHasPawn(__instance))
            {
                __result = AcceptanceReport.WasRejected;
                return false;
            }

            return true;
        }

        public static bool TrainingCanAssignVisiblePrefix(
            Pawn_TrainingTracker __instance,
            ref bool visible,
            ref AcceptanceReport __result)
        {
            if (!TrainingTrackerHasPawn(__instance))
            {
                visible = false;
                __result = AcceptanceReport.WasRejected;
                return false;
            }

            return true;
        }

        public static bool TrainingCanAssignInternalPrefix(
            TrainableDef td,
            ThingDef pawnDef,
            ref bool visible,
            Pawn pawn,
            ref AcceptanceReport __result)
        {
            // The static overload deliberately permits a null Pawn for race previews.
            if (pawnDef?.race == null || td == null ||
                (td.requiredTrainability != null && pawnDef.race.trainability == null &&
                 TrainableUtility.GetTrainability(pawn) == null))
            {
                visible = false;
                __result = AcceptanceReport.WasRejected;
                return false;
            }

            return true;
        }

        public static Exception TrainingCanAssignInternalFinalizer(
            Exception __exception,
            Pawn pawn,
            ref bool visible,
            ref AcceptanceReport __result)
        {
            if (__exception is NullReferenceException && Scribe.mode == LoadSaveMode.PostLoadInit &&
                pawn == null)
            {
                visible = false;
                __result = AcceptanceReport.WasRejected;
                Log.WarningOnce(
                    "[StartupLog Compatibility] Rejected one incomplete animal training entry during post-load repair.",
                    1768432955);
                return null;
            }

            return __exception;
        }

        private static bool TrainingTrackerHasPawn(Pawn_TrainingTracker tracker)
        {
            // A missing private field means a different RimWorld API shape, not
            // an unbound tracker.  Only reject the tracker when the known field
            // exists and explicitly contains no pawn.
            return tracker != null &&
                (TrainingTrackerPawnField == null || TrainingTrackerPawnField.GetValue(tracker) != null);
        }

        public static void RimStoryFuneralTickPrefix(object __instance)
        {
            if (__instance == null)
                return;

            Lord lord = LordToilLordField?.GetValue(__instance) as Lord;
            object data = LordToilDataField?.GetValue(__instance);
            if (lord?.ownedPawns == null || data == null)
                return;

            Type dataType = data.GetType();
            if (!FuneralPresentFields.TryGetValue(dataType, out FieldInfo presentField))
            {
                presentField = AccessTools.Field(dataType, "presentForTicks");
                if (presentField != null)
                    FuneralPresentFields.TryAdd(dataType, presentField);
            }
            IDictionary presentForTicks = presentField?.GetValue(data) as IDictionary;
            if (presentForTicks == null)
                return;

            foreach (Pawn pawn in lord.ownedPawns)
            {
                if (pawn != null && !presentForTicks.Contains(pawn))
                    presentForTicks[pawn] = 600;
            }
        }

        public static IEnumerable<CodeInstruction> RimStoryFuneralTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo lordField = AccessTools.Field(typeof(LordToil), "lord");
            MethodInfo safeLord = AccessTools.Method(typeof(StartupLogFixesMod), nameof(GetSafeLord));
            List<CodeInstruction> original = instructions?.ToList() ?? new List<CodeInstruction>();
            if (lordField == null || lordField.FieldType != typeof(Lord) || lordField.IsStatic ||
                safeLord == null || !safeLord.IsStatic || safeLord.ReturnType != typeof(Lord) ||
                safeLord.GetParameters().Length != 1 || safeLord.GetParameters()[0].ParameterType != typeof(LordToil))
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Skipped the RimStory funeral Lord binding transpiler because its API does not match this build.",
                    GenText.StableStringHash("StartupLogFixes:RimStory:Funeral:UnknownApi"));
                return original;
            }

            if (!original.Any(instruction => instruction.opcode == OpCodes.Ldfld &&
                    Equals(instruction.operand, lordField)))
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Skipped the RimStory funeral Lord binding transpiler because the expected Lord field access was not found.",
                    GenText.StableStringHash("StartupLogFixes:RimStory:Funeral:LordAccessMissing"));
                return original;
            }

            var rewritten = new List<CodeInstruction>(original.Count);
            foreach (CodeInstruction instruction in original)
            {
                if (instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, lordField))
                    rewritten.Add(new CodeInstruction(OpCodes.Call, safeLord)
                        .MoveLabelsFrom(instruction)
                        .MoveBlocksFrom(instruction));
                else
                    rewritten.Add(instruction);
            }
            return rewritten;
        }

        public static Lord GetSafeLord(LordToil toil)
        {
            Lord bound = toil?.lord;
            if (bound != null)
                return bound;
            return new Lord { ownedPawns = new List<Pawn>() };
        }

        public static void LordManagerExposeDataPostfix(LordManager __instance)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit || __instance == null)
                return;

            FieldInfo lordsField = AccessTools.Field(typeof(LordManager), "lords");
            var lords = lordsField?.GetValue(__instance) as List<Lord>;
            if (lords == null)
                return;

            FieldInfo jobField = AccessTools.Field(typeof(Lord), "curJob");
            FieldInfo graphField = AccessTools.Field(typeof(Lord), "graph");
            FieldInfo initializedField = AccessTools.Field(typeof(Lord), "initialized");
            FieldInfo ownedPawnsField = AccessTools.Field(typeof(Lord), "ownedPawns");
            if (jobField == null || graphField == null || initializedField == null || ownedPawnsField == null)
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Skipped RimStory Lord cleanup because the current Lord API fields were not all found.",
                    GenText.StableStringHash("StartupLogFixes:RimStory:LordCleanup:UnknownApi"));
                return;
            }

            int removed = 0;
            for (int index = lords.Count - 1; index >= 0; index--)
            {
                Lord lord = lords[index];
                // A malformed save can contain a null list slot.  Ignore it here;
                // removing arbitrary non-null Lords would risk changing gameplay.
                if (lord == null)
                    continue;
                object job = jobField?.GetValue(lord);
                if (job == null || job.GetType().FullName != "RimStory.LordJob_RimStory")
                    continue;
                bool initialized = initializedField.GetValue(lord) is bool value && value;
                object graph = graphField?.GetValue(lord);
                var owned = ownedPawnsField?.GetValue(lord) as List<Pawn>;
                if (!initialized && graph == null && (owned == null || owned.Count == 0))
                {
                    lords.RemoveAt(index);
                    removed++;
                    Log.Warning("[StartupLog Compatibility] Removed damaged RimStory Lord " +
                        lord.loadID + " (uninitialized, no graph, no owned pawns).");
                }
            }
            if (removed > 0)
                Log.Message("[StartupLog Compatibility] Removed " + removed +
                    " damaged RimStory Lord instance(s) after load.");
        }

        public static void MastersRecipeListPostfix(ThingDef __instance, ref List<RecipeDef> __result)
        {
            if (__instance == null || __result == null)
                return;

            __result.RemoveAll(IsDisabledMastersRecipe);

            ThingDef autoforge = DefDatabase<ThingDef>.GetNamedSilentFail("GW_Autoforge");
            ThingDef anvil = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Anvil");
            if (autoforge == null || autoforge.recipes == null ||
                ReferenceEquals(__instance, autoforge) || ReferenceEquals(__instance, anvil))
                return;

            RecipeDef goldenStc = DefDatabase<RecipeDef>.GetNamedSilentFail("Local_MakeGoldenSTC");
            // Only remove entries that can be identified as stale cache data.
            // Keep a recipe that the workbench explicitly owns or that another
            // mod still registers through recipeUsers.
            for (int i = __result.Count - 1; i >= 0; i--)
            {
                RecipeDef recipe = __result[i];
                bool owned = recipe != null &&
                    ((IsMastersTargetRecipe(recipe) && autoforge.recipes.Contains(recipe)) ||
                     (recipe == goldenStc && anvil?.recipes?.Contains(recipe) == true));
                if (owned && __instance.recipes?.Contains(recipe) != true &&
                    recipe.recipeUsers?.Contains(__instance) != true)
                    __result.RemoveAt(i);
            }
        }

        private static readonly HashSet<string> DisabledMastersRecipeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Auto_GW_Rulers_FirstbornArmor", "Auto_GW_Rulers_PrimarisArmor", "Auto_GW_Rulers_GravisArmor",
            "Auto_GW_SM_FirstbornHelmet", "Auto_GW_Rulers_PrimarisHelmet", "Auto_GW_Rulers_PrimarisTenatHelmet",
            "Auto_GW_Rulers_GravisHelmet", "Auto_GW_Rulers_XthHelmet", "Auto_GW_Rulers_Gun_AstartesPlasmaCannon",
            "Auto_GW_Rulers_Bolter_Gun_Bolter", "Auto_GW_Rulers_MeltaGun", "Auto_GW_Rulers_AstartesShotgun",
            "Make_GW_Rulers_Powersword", "Make_GW_Rulers_Chainsword"
        };

        internal static bool IsDisabledMastersRecipe(RecipeDef recipe)
        {
            return recipe?.defName != null && DisabledMastersRecipeNames.Contains(recipe.defName) &&
                (string.Equals(recipe.modContentPack?.PackageId, "Local.RimDark.MastersOfMankind", StringComparison.OrdinalIgnoreCase) ||
                 recipe.products?.Any(product => string.Equals(product?.thingDef?.modContentPack?.PackageId,
                     "Local.RimDark.MastersOfMankind", StringComparison.OrdinalIgnoreCase)) == true) &&
                (recipe.recipeUsers == null || recipe.recipeUsers.Count == 0);
        }

        public static bool DisabledMastersRecipeAvailablePrefix(RecipeDef __instance, ref bool __result)
        {
            if (!IsDisabledMastersRecipe(__instance)) return true;
            __result = false;
            return false;
        }

        [HarmonyPriority(Priority.Last)]
        public static void DisabledMastersBillShouldDoNowPostfix(Bill_Production __instance, ref bool __result)
        {
            // Runs even when another mod supplies a cached result and skips the original.
            if (IsDisabledMastersRecipe(__instance?.recipe)) __result = false;
        }

        public static bool AutonomousBillPawnAllowedPrefix(
            Bill_Autonomous __instance, Pawn p, ref bool __result)
        {
            if (__instance?.billStack?.billGiver is Building_WorkTableAutonomous)
                return true;

            __result = false;
            return false;
        }

        public static bool FillProcessorJobPrefix(Pawn pawn, Thing t, ref Job __result)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed || !pawn.Spawned ||
                t == null || t.Destroyed || !t.Spawned ||
                t.Map == null || pawn.Map != t.Map)
            {
                __result = null;
                return false;
            }

            return true;
        }

        public static Exception FillProcessorJobFinalizer(Exception __exception, ref Job __result)
        {
            string stackTrace = __exception?.StackTrace;
            if (!(__exception is NullReferenceException) || stackTrace == null ||
                stackTrace.IndexOf(
                    "ProcessorFramework.WorkGiver_FillProcessor.JobOnThing",
                    StringComparison.Ordinal) < 0)
                return __exception;

            __result = null;
            Log.WarningOnce(
                "[StartupLog Compatibility] Skipped one Processor Framework fill candidate after the known parallel null-reference race; YaOpt parallel WorkGiver processing remains enabled.",
                GenText.StableStringHash("StartupLogFixes:ProcessorFramework:FillProcessor:KnownNRE"));
            return null;
        }

        [HarmonyPriority(Priority.First)]
        public static void PawnRenderTreeParallelPreDrawPrefix(
            PawnRenderTree __instance, Pawn ___pawn, PawnDrawParms parms)
        {
            if (__instance == null || ___pawn == null || parms.pawn == null ||
                !ReferenceEquals(___pawn, parms.pawn) || !UnityData.IsInMainThread ||
                (parms.flags & PawnRenderFlags.Invisible) != PawnRenderFlags.Invisible ||
                ___pawn.RaceProps?.renderTree?.root?.nodeClass == null)
                return;

            // Unity materials must be created on the main thread before YaOpt's
            // parallel render path can request invisible variants.
            __instance.EnsureInitialized(parms.flags);
        }

        public static Exception SocialThoughtOpinionOffsetFinalizer(
            Exception __exception, ref float __result)
        {
            if (!(__exception is ArgumentOutOfRangeException argument) ||
                !string.Equals(argument.ParamName, "index", StringComparison.Ordinal) ||
                (!ExceptionOriginatesIn(__exception, "RimWorld.Thought") &&
                 !ExceptionOriginatesIn(__exception, "RimWorld.Thought_SituationalSocial")))
                return __exception;

            __result = 0f;
            Log.WarningOnce(
                "[StartupLog Compatibility] Ignored one social thought with an invalid stage index while calculating opinion; other social thoughts remain active.",
                GenText.StableStringHash("StartupLogFixes:ThoughtSocial:InvalidStageIndex"));
            return null;
        }

        public static bool VefExtractFlowerPrefix(Pawn pawn, ref IEnumerable<Thing> __result)
        {
            if (pawn?.Map?.listerThings != null)
                return true;

            __result = Enumerable.Empty<Thing>();
            Log.WarningOnce(
                "[StartupLog Compatibility] Skipped one VEF extract-flower scan with an invalid pawn map without disabling parallel WorkGiver processing.",
                GenText.StableStringHash("StartupLogFixes:VEF:ExtractFlower:InvalidPawnMap"));
            return false;
        }

        public static Exception VefExtractFlowerFinalizer(
            Exception __exception, ref IEnumerable<Thing> __result)
        {
            if (!(__exception is NullReferenceException) ||
                !ExceptionOriginatesIn(__exception, "VEF.Plants.WorkGiver_ExtractFlower"))
                return __exception;

            __result = Enumerable.Empty<Thing>();
            Log.WarningOnce(
                "[StartupLog Compatibility] Skipped one invalid VEF extract-flower scan without disabling parallel WorkGiver processing.",
                GenText.StableStringHash("StartupLogFixes:VEF:ExtractFlower:InvalidMapComponent"));
            return null;
        }

        public static Exception VefExtractFlowerMoveNextFinalizer(Exception __exception)
        {
            if (!(__exception is NullReferenceException) ||
                !ExceptionOriginatesIn(__exception, "VEF.Plants.WorkGiver_ExtractFlower"))
                return __exception;

            Log.WarningOnce(
                "[StartupLog Compatibility] Skipped one invalid VEF extract-flower iterator step without disabling parallel WorkGiver processing.",
                GenText.StableStringHash("StartupLogFixes:VEF:ExtractFlower:InvalidIteratorStep"));
            return null;
        }

        public static Exception SpeakUpExtraRulesForPawnFinalizer(Exception __exception)
        {
            if (!(__exception is KeyNotFoundException) ||
                !ExceptionOriginatesIn(__exception, "SpeakUp.ExtraGrammarUtility"))
                return __exception;

            Log.WarningOnce(
                "[StartupLog Compatibility] Skipped one Speak Up relation rule with a missing pawn entry.",
                GenText.StableStringHash("StartupLogFixes:SpeakUp:MissingRelationRule"));
            return null;
        }

        public static Exception CaptureExpansionRenderFinalizer(Exception __exception, ref PawnDrawParms parms)
        {
            bool invalidData = parms.pawn == null;
            if (!invalidData && parms.pawn.ParentHolder?.GetType().FullName == "CaptureExpansion.Building_RestraintPlatform")
            {
                object holder = parms.pawn.ParentHolder;
                PropertyInfo extension = AccessTools.Property(holder.GetType(), "RestraintExtension");
                invalidData = extension != null && extension.GetValue(holder, null) == null;
            }
            if (!(__exception is NullReferenceException) || !invalidData)
                return __exception;

            Log.WarningOnce(
                "[StartupLog Compatibility] Skipped Capture Expansion's invalid animated-pawn render adjustment.",
                GenText.StableStringHash("StartupLogFixes:CaptureExpansion:AnimatedPawnRender"));
            return null;
        }

        public static Exception AlphaMemesFuneralObligationFinalizer(Exception __exception)
        {
            string stackTrace = __exception?.StackTrace;
            if (!(__exception is NullReferenceException) ||
                stackTrace == null ||
                stackTrace.IndexOf("RitualObligationTargetWorker_GraveWithTarget",
                    StringComparison.Ordinal) < 0 ||
                stackTrace.IndexOf("GetTargets", StringComparison.Ordinal) < 0 ||
                stackTrace.IndexOf("AlphaMemes.Precept_Ritual_AddObligation.Prefix",
                    StringComparison.Ordinal) < 0)
                return __exception;

            Log.WarningOnce(
                "[StartupLog Compatibility] Skipped one AlphaMemes funeral obligation check with an invalid grave target; the vanilla obligation path remains active.",
                GenText.StableStringHash("StartupLogFixes:AlphaMemes:FuneralObligation:InvalidGraveTarget"));
            return null;
        }

        public static Exception PawnTattooCanDrawNowFinalizer(
            Exception __exception, PawnDrawParms parms, ref bool __result)
        {
            if (!(__exception is NullReferenceException) || parms.pawn != null)
                return __exception;

            __result = false;
            Log.WarningOnce(
                "[StartupLog Compatibility] Skipped one tattoo render node after invalid pawn style data; YaOpt parallel rendering remains enabled.",
                GenText.StableStringHash("StartupLogFixes:PawnRender:Tattoo:InvalidPawnStyle"));
            return null;
        }

        private static bool ExceptionOriginatesIn(Exception exception, string typeName)
        {
            StackFrame[] frames = exception == null ? null : new StackTrace(exception, false).GetFrames();
            if (frames == null) return false;
            foreach (StackFrame frame in frames)
            {
                MethodBase method = frame.GetMethod();
                string methodName = method?.Name;
                if (methodName?.StartsWith(typeName + ".", StringComparison.Ordinal) == true ||
                    methodName?.StartsWith(typeName + "::", StringComparison.Ordinal) == true ||
                    methodName?.StartsWith("DMD<" + typeName + "::", StringComparison.Ordinal) == true) return true;
                string name = method?.DeclaringType?.FullName;
                if (name == null)
                {
                    if (method?.Name?.StartsWith(typeName + ".", StringComparison.Ordinal) == true) return true;
                    continue;
                }
                if (name.StartsWith("System.", StringComparison.Ordinal) || name.StartsWith("Mono.", StringComparison.Ordinal) ||
                    name.StartsWith("MonoMod.", StringComparison.Ordinal) || name.StartsWith("HarmonyLib.", StringComparison.Ordinal)) continue;
                return name == typeName || name.StartsWith(typeName + "+", StringComparison.Ordinal);
            }
            return false;
        }

        public static void RimStoryDeadPawnExposeDataPrefix(ref Pawn ___deadPawn)
        {
            if (Scribe.mode == LoadSaveMode.Saving && ___deadPawn != null && ___deadPawn.Discarded)
                ___deadPawn = null;
        }

        public static bool RimStoryDeadEventPrefix(Pawn ___deadPawn, ref bool __result)
        {
            if (___deadPawn != null && !___deadPawn.Discarded)
                return true;

            __result = false;
            return false;
        }

        public static bool RimTalkNearbyPawnsPrefix(Pawn pawn1, Pawn pawn2, ref List<Pawn> __result)
        {
            if (IsValidPawnForMapQuery(pawn1) &&
                (pawn2 == null || IsValidPawnForMapQuery(pawn2)))
                return true;

            // GetNearbyPawnsInternal returns List<Pawn>.  A skipped prefix must
            // provide the method's neutral result so callers do not dereference
            // a null list and turn one stale pawn into a new runtime failure.
            __result = new List<Pawn>();
            Log.WarningOnce(
                "[StartupLog Compatibility] Returned an empty RimTalk nearby-pawn result for an invalid map pawn.",
                GenText.StableStringHash("StartupLogFixes:RimTalk:InvalidNearbyPawn"));
            return false;
        }

        public static bool RimTalkTransportErrorLogPrefix(object[] __args, out int __state)
        {
            __state = rimTalkLogScopeDepth;
            if (IsRimTalkDiagnosticLog(GetFirstLogText(__args)))
                rimTalkLogScopeDepth++;

            // Keep the diagnostic in Player.log.  The TryOpenLogWindow prefix
            // below suppresses only the automatic DebugLog window side effect.
            return true;
        }

        public static Exception RimTalkTransportErrorLogFinalizer(Exception __exception, int __state)
        {
            rimTalkLogScopeDepth = __state;
            return __exception;
        }

        public static bool RimTalkTryOpenLogWindowPrefix()
        {
            return rimTalkLogScopeDepth <= 0;
        }

        public static bool RimTalkLoggerWarningPrefix(object message)
        {
            return true;
        }

        public static bool RimTalkQuotaWarningPrefix(Exception ex)
        {
            WriteRimTalkDiagnostic("Quota warning: " + FormatException(ex));
            return false;
        }

        public static bool RimTalkGenerationWarningPrefix(Exception ex)
        {
            WriteRimTalkDiagnostic("Generation warning: " + FormatException(ex));
            return false;
        }

        public static bool RimTalkRetryMessagePrefix(Exception ex, string nextModel)
        {
            WriteRimTalkDiagnostic("Retry message (next model=" + (nextModel ?? "unknown") + "): " +
                FormatException(ex));
            return false;
        }

        public static bool RimTalkVisionFallbackMessagePrefix(string model)
        {
            WriteRimTalkDiagnostic("Vision fallback alert (model=" + (model ?? "unknown") + ").");
            return false;
        }

        public static bool RimTalkMessageAndErrorPrefix(string message)
        {
            WriteRimTalkDiagnostic("ExpandMemory: " + (message ?? "(empty message)"));
            return false;
        }

        private static string FormatException(Exception exception)
        {
            if (exception == null)
                return "(no exception details)";

            try
            {
                return exception.ToString();
            }
            catch
            {
                return exception.Message ?? exception.GetType().FullName ?? "(unknown exception)";
            }
        }

        private static void WriteRimTalkDiagnostic(string message)
        {
            string text = "[StartupLog Compatibility] RimTalk alert suppressed: " +
                (message ?? "(empty message)");
            rimTalkLogScopeDepth++;
            try
            {
                Log.Message(text);
            }
            finally
            {
                if (rimTalkLogScopeDepth > 0)
                    rimTalkLogScopeDepth--;
            }
        }

        public static bool RimTalkMessageAlertPrefix(object[] __args)
        {
            return !IsRimTalkAlertMessage(__args);
        }

        private static string GetFirstLogText(object[] args)
        {
            if (args == null || args.Length == 0 || args[0] == null)
                return null;

            return args[0] as string ?? args[0].ToString();
        }

        private static string GetMessageText(object[] args)
        {
            if (args == null)
                return null;

            foreach (object argument in args)
            {
                string text = argument as string;
                if (text != null)
                    return text;

                Verse.Message message = argument as Verse.Message;
                if (message != null)
                    return message.text;
            }

            return null;
        }

        private static bool IsRimTalkAlertMessage(object[] args)
        {
            string text = GetMessageText(args);
            if (IsRimTalkDiagnosticLog(text))
                return true;

            if (!IsLikelyRimTalkAlertText(text))
                return false;

            return IsRimTalkAlertCallStack();
        }

        private static bool IsRimTalkAlertCallStack()
        {
            string stack = new StackTrace(1, false).ToString();
            return stack.IndexOf("RimTalk.Error.AIErrorHandler", StringComparison.Ordinal) >= 0 ||
                stack.IndexOf("RimTalk.Memory.Utils.MessageUtil", StringComparison.Ordinal) >= 0 ||
                stack.IndexOf("RimTalk.Memory.AI.Client", StringComparison.Ordinal) >= 0;
        }

        private static bool IsRimTalkDiagnosticLog(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            text = text.TrimStart();
            bool coreRimTalk = text.StartsWith("[RimTalk]", StringComparison.OrdinalIgnoreCase);
            bool expandMemory = text.StartsWith("[RimTalk-ExpandMemory]",
                StringComparison.OrdinalIgnoreCase);
            bool memoryRimTalk = text.StartsWith("[RimTalk.Memory.",
                StringComparison.OrdinalIgnoreCase);
            if (!coreRimTalk && !expandMemory && !memoryRimTalk)
            {
                // UnityWebRequest may emit the native Curl text without the
                // RimTalk prefix.  Treat only transport-shaped text as a
                // diagnostic here; ordinary errors remain untouched.
                return IsRimTalkTransportExceptionText(text) && IsRimTalkAlertCallStack();
            }

            // ExpandMemory's MessageAndError and the client-pool status lines
            // are diagnostic by contract, including their localized text.
            if (text.IndexOf("[RimTalk.Memory.AI]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("[RimTalk.Memory.AI.Client]", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return IsLikelyRimTalkAlertText(text);
        }

        private static bool IsLikelyRimTalkAlertText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return IsRimTalkRequestFailure(text) ||
                text.IndexOf("Curl error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("necessary data rewind", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Failure writing output", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Unable to write data", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("ServiceUnavailable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("System is too busy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("task was canceled", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Request aborted", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Thread was being aborted", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Failed to parse stream chunk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("request failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("failure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("invalid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("illegal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("quota", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("失败", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("错误", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("异常", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("非法", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("无效", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("没有更多", StringComparison.Ordinal) >= 0 ||
                text.IndexOf("响应失败", StringComparison.Ordinal) >= 0;
        }

        private static bool IsRimTalkTransportLog(string text)
        {
            return IsRimTalkDiagnosticLog(text);
        }

        private static bool IsRimTalkRequestFailure(string text)
        {
            int marker = text.IndexOf("Request failed", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
                return false;

            if (text.IndexOf("Unable to write data", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            int colon = text.IndexOf(':', marker);
            if (colon < 0)
                return false;

            int separator = text.IndexOf('-', colon + 1);
            if (separator < 0)
                return false;

            string statusText = text.Substring(colon + 1, separator - colon - 1).Trim();
            int statusCode;
            if (!int.TryParse(statusText, out statusCode))
                return false;

            return IsRimTalkTransportStatus(statusCode);
        }

        private static bool IsRimTalkTransportFailure(Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (current is OperationCanceledException)
                    return true;

                Type type = current.GetType();
                string typeName = type?.FullName;
                bool isRimTalkException = typeName != null &&
                    typeName.StartsWith("RimTalk.", StringComparison.Ordinal);
                if (isRimTalkException && IsRimTalkTransportExceptionText(current.Message))
                    return true;

                if (string.Equals(typeName, "RimTalk.Error.AIRequestException",
                    StringComparison.Ordinal))
                {
                    object payload = GetInstancePropertyValue(current, "Payload");
                    object statusValue = GetInstancePropertyValue(payload, "StatusCode");
                    int statusCode;
                    if (statusValue != null &&
                        int.TryParse(statusValue.ToString(), out statusCode) &&
                        IsRimTalkTransportStatus(statusCode))
                        return true;

                    object payloadError = GetInstancePropertyValue(payload, "ErrorMessage");
                    if (IsRimTalkTransportExceptionText(payloadError?.ToString()))
                        return true;
                }
            }

            return false;
        }

        private static bool IsRimTalkTransportExceptionText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return IsRimTalkRequestFailure(text) ||
                text.IndexOf("Curl error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("necessary data rewind", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Failure writing output", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Unable to write data", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Connection timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Read timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("HTTP 0 error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Get a error payLoad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("ServiceUnavailable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("System is too busy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Request aborted", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Thread was being aborted", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Failed to parse stream chunk", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsRimTalkTransportStatus(int statusCode)
        {
            return statusCode == 0 || statusCode == 502 || statusCode == 503 || statusCode == 504;
        }

        private static object GetInstancePropertyValue(object instance, string propertyName)
        {
            if (instance == null || string.IsNullOrEmpty(propertyName))
                return null;

            try
            {
                PropertyInfo property = instance.GetType().GetProperty(propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return property?.GetValue(instance, null);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsValidPawnForMapQuery(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Map == null)
                return false;
            return pawn.Spawned;
        }

        public static bool Genes40kRawSlurryDespawnPrefix(Thing __instance)
        {
            if (__instance?.def?.defName == "BEWH_RawGestationalSlurry" && !__instance.Spawned)
                return false;

            return true;
        }

        private static bool IsMastersTargetRecipe(RecipeDef recipe)
        {
            if (recipe?.defName == null)
                return false;
            if (recipe.defName.StartsWith("Auto_GW_Rulers_", StringComparison.Ordinal) ||
                recipe.defName.StartsWith("Auto_GW_SM_", StringComparison.Ordinal))
                return true;

            return recipe.products != null && recipe.products.Any(product => product?.thingDef != null &&
                (product.thingDef.defName == "GW_Rulers_Chainsword" ||
                 product.thingDef.defName == "GW_Rulers_Powersword" ||
                 product.thingDef.defName == "GW_SM_Melee_EmperorClaw" ||
                 product.thingDef.defName == "GW_SM_Melee_ValdorSpear" ||
                 product.thingDef.defName == "GW_SM_Melee_MalcadorStaff"));
        }

        private static void PatchExposeData(Harmony harmony, Type type, string prefixName,
            string postfixName = null)
        {
            var exposeData = AccessTools.DeclaredMethod(type, "ExposeData");
            if (exposeData != null)
            {
                TryPatch(harmony, exposeData, type.FullName + ".ExposeData",
                    prefixName: prefixName, postfixName: postfixName);
            }
        }

        private static bool TryPatch(
            Harmony harmony,
            MethodBase target,
            string label,
            string prefixName = null,
            string postfixName = null,
            string transpilerName = null,
            string finalizerName = null)
        {
            if (target == null)
                return false;

            attemptedPatchCount++;
            string reason;
            if (!ValidateHarmonyPatch(target, prefixName, "prefix", out reason) ||
                !ValidateHarmonyPatch(target, postfixName, "postfix", out reason) ||
                !ValidateHarmonyPatch(target, transpilerName, "transpiler", out reason) ||
                !ValidateHarmonyPatch(target, finalizerName, "finalizer", out reason))
            {
                Log.Warning("[StartupLog Compatibility] Skipped " + label +
                    " because its patch signature does not match this RimWorld build: " + reason);
                return false;
            }

            try
            {
                harmony.Patch(
                    target,
                    ToHarmonyMethod(prefixName),
                    ToHarmonyMethod(postfixName),
                    ToHarmonyMethod(transpilerName),
                    ToHarmonyMethod(finalizerName));
                successfulPatchCount++;
                return true;
            }
            catch (Exception exception)
            {
                Log.Error("[StartupLog Compatibility] Failed to register " + label +
                    " compatibility patch; other independent fixes will remain active. " + exception);
                return false;
            }
        }

        private static MethodInfo FindIteratorMoveNext(MethodInfo iteratorMethod)
        {
            if (iteratorMethod == null)
                return null;

            try
            {
                IteratorStateMachineAttribute attribute = iteratorMethod
                    .GetCustomAttributes(typeof(IteratorStateMachineAttribute), false)
                    .OfType<IteratorStateMachineAttribute>()
                    .FirstOrDefault();
                Type stateMachineType = attribute?.StateMachineType;
                if (stateMachineType == null)
                    return null;

                MethodInfo moveNext = AccessTools.Method(stateMachineType, "MoveNext");
                return moveNext != null && moveNext.ReturnType == typeof(bool) &&
                    moveNext.GetParameters().Length == 0
                    ? moveNext
                    : null;
            }
            catch (Exception exception)
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Could not locate the VEF extract-flower iterator state machine; the input guard remains active. " +
                    exception.Message,
                    GenText.StableStringHash("StartupLogFixes:VEF:ExtractFlower:IteratorLookup"));
                return null;
            }
        }

        private static bool ValidateHarmonyPatch(
            MethodBase target, string patchName, string patchKind, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(patchName))
                return true;

            MethodInfo patch = AccessTools.Method(typeof(StartupLogFixesMod), patchName);
            if (patch == null)
            {
                reason = patchKind + " method " + patchName + " is missing";
                return false;
            }
            if (!patch.IsStatic)
            {
                reason = patchKind + " method " + patchName + " is not static";
                return false;
            }

            if (patchKind == "prefix" && patch.ReturnType != typeof(void) && patch.ReturnType != typeof(bool))
            {
                reason = "prefix " + patchName + " must return void or bool";
                return false;
            }
            if (patchKind == "postfix" && patch.ReturnType != typeof(void))
            {
                reason = "postfix " + patchName + " must return void";
                return false;
            }
            if (patchKind == "transpiler" &&
                !typeof(IEnumerable<CodeInstruction>).IsAssignableFrom(patch.ReturnType))
            {
                reason = "transpiler " + patchName + " must return IEnumerable<CodeInstruction>";
                return false;
            }
            if (patchKind == "finalizer" &&
                patch.ReturnType != typeof(void) && !typeof(Exception).IsAssignableFrom(patch.ReturnType))
            {
                reason = "finalizer " + patchName + " must return Exception or void";
                return false;
            }

            if (patchKind == "transpiler")
            {
                ParameterInfo[] transpilerParameters = patch.GetParameters();
                if (transpilerParameters.Length < 1 || transpilerParameters.Length > 2 ||
                    transpilerParameters[0].ParameterType.IsByRef ||
                    transpilerParameters[0].ParameterType != typeof(IEnumerable<CodeInstruction>))
                {
                    reason = "transpiler " + patchName +
                        " must accept IEnumerable<CodeInstruction> as its first parameter";
                    return false;
                }

                if (transpilerParameters.Length == 2 &&
                    (transpilerParameters[1].ParameterType.IsByRef ||
                     transpilerParameters[1].ParameterType != typeof(ILGenerator)))
                {
                    reason = "transpiler " + patchName +
                        " may only use an optional ILGenerator as its second parameter";
                    return false;
                }

                // Transpiler parameters describe Harmony's instruction pipeline,
                // not the parameters of the method being patched.
                return true;
            }

            MethodInfo targetMethod = target as MethodInfo;
            Type targetReturnType = targetMethod == null ? typeof(void) : targetMethod.ReturnType;
            ParameterInfo[] targetParameters = target.GetParameters();
            foreach (ParameterInfo patchParameter in patch.GetParameters())
            {
                string name = patchParameter.Name;
                Type patchType = UnwrapByRef(patchParameter.ParameterType);
                if (!string.IsNullOrEmpty(name) && name.StartsWith("__", StringComparison.Ordinal) &&
                    int.TryParse(name.Substring(2), out int positionalIndex))
                {
                    if (positionalIndex < 0 || positionalIndex >= targetParameters.Length)
                    {
                        reason = "positional parameter " + name + " on " + patchName +
                            " is not present on the target";
                        return false;
                    }

                    ParameterInfo positionalTargetParameter = targetParameters[positionalIndex];
                    if (!ArePatchTypesCompatible(patchType, positionalTargetParameter.ParameterType,
                        patchParameter.ParameterType.IsByRef, positionalTargetParameter.ParameterType.IsByRef))
                    {
                        reason = "positional parameter " + name + " on " + patchName +
                            " has the wrong type";
                        return false;
                    }
                    continue;
                }
                if (name == "__instance")
                {
                    if (target.IsStatic || target.DeclaringType == null ||
                        !patchType.IsAssignableFrom(target.DeclaringType))
                    {
                        reason = "__instance on " + patchName + " is incompatible with the target";
                        return false;
                    }
                    continue;
                }
                if (name == "__result")
                {
                    if (targetMethod == null || targetReturnType == typeof(void) ||
                        !ArePatchTypesCompatible(patchType, targetReturnType,
                            patchParameter.ParameterType.IsByRef, false))
                    {
                        reason = "__result on " + patchName + " is incompatible with the target return value";
                        return false;
                    }
                    continue;
                }
                if (name == "__exception")
                {
                    if (patchType != typeof(Exception))
                    {
                        reason = "__exception on " + patchName + " must be Exception";
                        return false;
                    }
                    continue;
                }
                if (name == "__args")
                {
                    if (patchType != typeof(object[]))
                    {
                        reason = "__args on " + patchName + " must be object[]";
                        return false;
                    }
                    continue;
                }
                if (name == "__state")
                    continue;

                if (!string.IsNullOrEmpty(name) && name.StartsWith("___", StringComparison.Ordinal))
                {
                    FieldInfo field = FindField(target.DeclaringType, name.Substring(3));
                    if (field == null || !ArePatchTypesCompatible(patchType, field.FieldType,
                            patchParameter.ParameterType.IsByRef, field.FieldType.IsByRef))
                    {
                        reason = name + " on " + patchName + " does not match a target field";
                        return false;
                    }
                    continue;
                }

                ParameterInfo targetParameter = targetParameters.FirstOrDefault(parameter =>
                    string.Equals(parameter.Name, name, StringComparison.Ordinal));
                if (targetParameter == null)
                {
                    reason = "parameter " + name + " on " + patchName + " is not present on the target";
                    return false;
                }

                if (!ArePatchTypesCompatible(patchType, targetParameter.ParameterType,
                        patchParameter.ParameterType.IsByRef, targetParameter.ParameterType.IsByRef))
                {
                    reason = "parameter " + name + " on " + patchName + " has the wrong type";
                    return false;
                }
            }

            return true;
        }

        private static bool ArePatchTypesCompatible(
            Type patchType, Type targetType, bool patchByRef, bool targetByRef)
        {
            if (patchType == null || targetType == null || (targetByRef && !patchByRef))
                return false;
            if (patchByRef || targetByRef)
                return patchType == UnwrapByRef(targetType);
            return patchType == targetType || patchType.IsAssignableFrom(targetType);
        }

        private static Type UnwrapByRef(Type type)
        {
            return type != null && type.IsByRef ? type.GetElementType() : type;
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                FieldInfo field = current.GetField(name,
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            return null;
        }

        private static HarmonyMethod ToHarmonyMethod(string methodName)
        {
            return string.IsNullOrEmpty(methodName)
                ? null
                : new HarmonyMethod(typeof(StartupLogFixesMod), methodName);
        }

        public static void OberoniaQuestManagerExposeDataPostfix(QuestManager __instance)
        {
            MastersOfMankindRecipeCompatibility.OberoniaQuestManagerExposeDataPostfix(__instance);
        }

        public static void OberoniaInvolvedFactionsPostfix(ref IEnumerable<Faction> __result)
        {
            MastersOfMankindRecipeCompatibility.OberoniaInvolvedFactionsPostfix(ref __result);
        }

        public static bool OberoniaTradeSignalPrefix(object __instance, Signal signal)
        {
            return MastersOfMankindRecipeCompatibility.OberoniaTradeSignalPrefix(__instance, signal);
        }

        public static bool VanillaMemesHeadgearPrefix(PawnDrawParms parms)
        {
            return MastersOfMankindRecipeCompatibility.VanillaMemesHeadgearPrefix(parms);
        }

        private static void PrepareMissingReferenceIds(XmlNode parent)
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars || parent?.OwnerDocument == null)
                return;

            var document = parent.OwnerDocument;
            if (preparedDocument != null && preparedDocument.TryGetTarget(out XmlDocument cachedDocument) &&
                ReferenceEquals(document, cachedDocument))
                return;

            preparedDocument = new System.WeakReference<XmlDocument>(document);
            MissingLordIds.Clear();
            MissingBattleIds.Clear();
            MissingLetterIds.Clear();
            MissingPreceptIds.Clear();
            MissingPawnIds.Clear();
            // VEF Storyteller.RaidGroup stores references as <lords><li>Lord_X</li>,
            // while vanilla/Pawn records use a direct <lord>Lord_X</lord> node.
            AddMissingIds(document.SelectNodes("//lord | //lords/li"),
                BuildPrefixedIdSet(document.SelectNodes("//lordManager/lords/li/loadID"), "Lord_"),
                "Lord_", MissingLordIds);
            AddMissingIds(document.SelectNodes("//battleActive"),
                BuildPrefixedIdSet(document.SelectNodes("//battleLog/battles/li/loadID"), "Battle_"),
                "Battle_", MissingBattleIds);
            AddMissingIds(document.SelectNodes("//letterStack/letters/li"),
                BuildLetterIdSet(document),
                "Letter_", MissingLetterIds);
            AddMissingIds(document.SelectNodes("//sourcePrecept | //precept"),
                BuildPrefixedIdSet(document.SelectNodes("//*[local-name()='id']"), "Precept_"),
                "Precept_", MissingPreceptIds);
            AddMissingIds(document.SelectNodes("//Pawns/li"), BuildThingIdSet(document),
                "Thing_", MissingPawnIds);
        }

        private static HashSet<string> BuildLetterIdSet(XmlDocument document)
        {
            var defined = new HashSet<string>();
            var letterTypes = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (XmlNode node in document.SelectNodes("//*[@Class]"))
            {
                string className = node.Attributes["Class"].Value;
                if (!letterTypes.TryGetValue(className, out bool isLetter))
                {
                    Type type = GenTypes.GetTypeInAnyAssembly(className, typeof(Letter).Namespace);
                    isLetter = type != null && typeof(Letter).IsAssignableFrom(type);
                    letterTypes.Add(className, isLetter);
                }
                if (!isLetter) continue;
                XmlNode idNode = node.SelectSingleNode("ID");
                // Letter.ExposeData omits the native default ID=0. This still
                // requires an actual Letter definition; absent Letter_0 stays stale.
                if (idNode == null) defined.Add("Letter_0");
                else if (int.TryParse(idNode.InnerText.Trim(), out int id))
                    defined.Add("Letter_" + id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            return defined;
        }

        private static HashSet<string> BuildPrefixedIdSet(XmlNodeList nodes, string prefix)
        {
            var defined = new HashSet<string>();
            if (nodes == null)
                return defined;

            foreach (XmlNode node in nodes)
            {
                string value = node?.InnerText?.Trim();
                if (!string.IsNullOrEmpty(value))
                    defined.Add(prefix + value);
            }
            return defined;
        }

        private static HashSet<string> BuildThingIdSet(XmlDocument document)
        {
            var defined = new HashSet<string>();
            foreach (XmlNode id in document.SelectNodes("//*[def and id]/id"))
            {
                string value = id?.InnerText?.Trim();
                if (!string.IsNullOrEmpty(value))
                    defined.Add("Thing_" + value);
            }
            return defined;
        }

        private static void AddMissingIds(
            XmlNodeList nodes,
            HashSet<string> defined,
            string expectedPrefix,
            HashSet<string> missing)
        {
            if (nodes == null)
                return;
            foreach (XmlNode node in nodes)
            {
                string value = node?.InnerText?.Trim();
                if (!string.IsNullOrEmpty(value) && value != "null" &&
                    value.StartsWith(expectedPrefix, StringComparison.Ordinal) && !defined.Contains(value))
                    missing.Add(value);
            }
        }

        private static void RemoveMissingChild(string name, HashSet<string> missing)
        {
            XmlNode parent = Scribe.loader?.curXmlParent;
            XmlNode child = parent?.SelectSingleNode(name);
            if (child != null && missing.Contains(child.InnerText.Trim()))
            {
                string value = child.InnerText.Trim();
                parent.RemoveChild(child);
                LogMissingReferenceOnce(name, value);
            }
        }

        private static void LogMissingReferenceOnce(string field, string value)
        {
            Log.WarningOnce(
                "[StartupLog Compatibility] Removed confirmed stale " + field + " reference " + value + ".",
                GenText.StableStringHash("StartupLogFixes:" + field + ":" + value));
        }

        public static void PawnExposeDataPrefix()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;
            PrepareMissingReferenceIds(Scribe.loader?.curXmlParent);
            RemoveMissingChild("lord", MissingLordIds);
        }

        public static void PawnRecordsExposeDataPrefix()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;
            PrepareMissingReferenceIds(Scribe.loader?.curXmlParent);
            RemoveMissingChild("battleActive", MissingBattleIds);
        }

        public static void PawnDestinationReservationExposeDataPrefix()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars)
                return;

            XmlNode manager = Scribe.loader?.curXmlParent;
            XmlNode reserved = manager?.SelectSingleNode("reservedDestinations");
            XmlNode keys = reserved?.SelectSingleNode("keys");
            XmlNode values = reserved?.SelectSingleNode("values");
            if (keys == null || values == null)
                return;

            List<XmlNode> keyEntries = keys.SelectNodes("li")?.Cast<XmlNode>().ToList();
            List<XmlNode> valueEntries = values.SelectNodes("li")?.Cast<XmlNode>().ToList();
            if (keyEntries == null || valueEntries == null || keyEntries.Count < 2 ||
                keyEntries.Count != valueEntries.Count)
                return;

            var firstIndexByFaction = new Dictionary<string, int>();
            var duplicateIndexes = new List<int>();
            var repairStats = new Dictionary<string, int[]>();
            for (int index = 0; index < keyEntries.Count; index++)
            {
                string factionId = keyEntries[index].InnerText?.Trim();
                if (string.IsNullOrEmpty(factionId) || !firstIndexByFaction.TryGetValue(factionId, out int firstIndex))
                {
                    if (!string.IsNullOrEmpty(factionId))
                        firstIndexByFaction[factionId] = index;
                    continue;
                }

                int merged = MergeDestinationReservations(valueEntries[firstIndex], valueEntries[index]);
                duplicateIndexes.Add(index);
                if (!repairStats.TryGetValue(factionId, out int[] stats))
                    repairStats[factionId] = stats = new int[2];
                stats[0]++;
                stats[1] += merged;
            }

            for (int index = duplicateIndexes.Count - 1; index >= 0; index--)
            {
                int entryIndex = duplicateIndexes[index];
                keys.RemoveChild(keyEntries[entryIndex]);
                values.RemoveChild(valueEntries[entryIndex]);
            }

            string mapId = FindAncestorValue(manager, "uniqueID") ??
                FindAncestorValue(manager, "loadID") ?? "unknown";
            foreach (KeyValuePair<string, int[]> repair in repairStats)
            {
                Log.Warning("[StartupLog Compatibility] Repaired reservedDestinations on map " + mapId +
                    " for " + repair.Key + ": removedKeys=" + repair.Value[0] +
                    ", mergedReservations=" + repair.Value[1] + ".");
            }
        }

        public static void AlcoholTrackerExposeDataPrefix()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars)
                return;

            XmlNode parent = Scribe.loader?.curXmlParent;
            RemoveNullDictionaryKeys(parent, "colonist_booze_tracker");
            RemoveNullDictionaryKeys(parent, "colonist_scar_counter");
        }

        public static void TravellingTradingTrackerExposeDataPrefix()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars)
                return;

            RemoveNullDictionaryKeys(Scribe.loader?.curXmlParent,
                "colonist_caravan_tracker");
        }

        public static void SpecialGlobalEventManagerExposeDataPrefix()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars)
                return;

            RemoveNullDictionaryKeys(Scribe.loader?.curXmlParent,
                "specialGlobalEventManager/birthdayPawnsOncePerYear");
        }

        private static void RemoveNullDictionaryKeys(XmlNode parent, string label)
        {
            XmlNode tracker = parent?.Name == label
                ? parent
                : parent?.SelectSingleNode(label);
            if (tracker == null)
            {
                int separator = label.IndexOf('/');
                if (separator > 0 && parent != null)
                {
                    string containerLabel = label.Substring(0, separator);
                    string childLabel = label.Substring(separator + 1);
                    tracker = parent.Name == containerLabel
                        ? parent.SelectSingleNode(childLabel)
                        : parent.SelectSingleNode("//" + label);
                }
            }
            XmlNode keys = tracker?.SelectSingleNode("keys");
            XmlNode values = tracker?.SelectSingleNode("values");
            if (keys == null || values == null)
                return;

            List<XmlNode> keyEntries = keys.SelectNodes("li")?.Cast<XmlNode>().ToList();
            List<XmlNode> valueEntries = values.SelectNodes("li")?.Cast<XmlNode>().ToList();
            if (keyEntries == null || valueEntries == null || keyEntries.Count != valueEntries.Count)
            {
                Log.Warning("[StartupLog Compatibility] Skipped " + label +
                    " cleanup because dictionary keys/values are not aligned.");
                return;
            }

            int removed = 0;
            for (int index = keyEntries.Count - 1; index >= 0; index--)
            {
                string key = keyEntries[index].InnerText?.Trim();
                if (!string.IsNullOrEmpty(key) && !string.Equals(key, "null", StringComparison.OrdinalIgnoreCase))
                    continue;

                keys.RemoveChild(keyEntries[index]);
                values.RemoveChild(valueEntries[index]);
                removed++;
            }

            if (removed > 0)
            {
                Log.Warning("[StartupLog Compatibility] Removed " + removed +
                    " null/empty key(s) from " + label + " while loading.");
            }
        }

        public static void PlayLogExposeDataPrefix()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars)
                return;

            XmlNode current = Scribe.loader?.curXmlParent;
            XmlNode entries = current?.Name == "entries" ? current : current?.SelectSingleNode("entries");
            if (entries == null)
                return;

            int removed = 0;
            var missingDefs = new HashSet<string>();
            foreach (XmlNode entry in entries.SelectNodes("li[intDef]").Cast<XmlNode>().ToList())
            {
                string defName = entry.SelectSingleNode("intDef")?.InnerText?.Trim();
                if (string.IsNullOrEmpty(defName) ||
                    DefDatabase<InteractionDef>.GetNamedSilentFail(defName) != null)
                    continue;

                entries.RemoveChild(entry);
                missingDefs.Add(defName);
                removed++;
            }

            if (removed > 0)
            {
                Log.Warning("[StartupLog Compatibility] Removed " + removed +
                    " play-log interaction entries whose InteractionDefs are no longer loaded: " +
                    string.Join(", ", missingDefs.OrderBy(name => name).ToArray()) + ".");
            }
        }

        public static void PlayLogExposeDataPostfix(PlayLog __instance)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit || __instance?.AllEntries == null ||
                InteractionDefField == null)
                return;

            int removed = __instance.AllEntries.RemoveAll(entry =>
                entry is PlayLogEntry_Interaction interaction &&
                InteractionDefField?.GetValue(interaction) == null);
            if (removed > 0)
            {
                Log.Warning("[StartupLog Compatibility] Removed " + removed +
                    " loaded play-log interaction entries with missing InteractionDefs.");
            }
        }

        private static int MergeDestinationReservations(XmlNode retainedValue, XmlNode duplicateValue)
        {
            XmlNode retainedList = retainedValue?.SelectSingleNode("list");
            XmlNode duplicateList = duplicateValue?.SelectSingleNode("list");
            if (retainedList == null || duplicateList == null)
                return 0;

            var targets = new HashSet<string>(
                retainedList.SelectNodes("li/target")?.Cast<XmlNode>()
                    .Select(node => node.InnerText?.Trim())
                    .Where(value => !string.IsNullOrEmpty(value)) ?? Enumerable.Empty<string>());
            int merged = 0;
            foreach (XmlNode reservation in duplicateList.SelectNodes("li")?.Cast<XmlNode>().ToList() ??
                new List<XmlNode>())
            {
                string target = reservation.SelectSingleNode("target")?.InnerText?.Trim();
                if (string.IsNullOrEmpty(target) || !targets.Add(target))
                    continue;

                retainedList.AppendChild(reservation.CloneNode(true));
                merged++;
            }
            return merged;
        }

        private static string FindAncestorValue(XmlNode node, string childName)
        {
            for (XmlNode current = node?.ParentNode; current != null; current = current.ParentNode)
            {
                string value = current.SelectSingleNode(childName)?.InnerText?.Trim();
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
            return null;
        }

        public static void LetterStackExposeDataPrefix()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;
            PrepareMissingReferenceIds(Scribe.loader?.curXmlParent);
            XmlNode letters = Scribe.loader?.curXmlParent?.SelectSingleNode("letters");
            if (letters == null) return;
            foreach (XmlNode node in letters.SelectNodes("li").Cast<XmlNode>().ToList())
            {
                if (MissingLetterIds.Contains(node.InnerText.Trim()))
                {
                    string value = node.InnerText.Trim();
                    letters.RemoveChild(node);
                    LogMissingReferenceOnce("letter", value);
                }
            }
        }

        public static void CreepJoinerOutcomesExposeDataPrefix()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars)
                return;

            XmlNode parent = Scribe.loader?.curXmlParent;
            PrepareMissingReferenceIds(parent);
            XmlNode letter = parent?.SelectSingleNode("letter");
            string value = letter?.InnerText?.Trim();
            if (letter != null && !string.IsNullOrEmpty(value) && MissingLetterIds.Contains(value))
            {
                letter.ParentNode.RemoveChild(letter);
                LogMissingReferenceOnce("creep-joiner outcome letter", value);
            }
        }

        public static void ThoughtMemoryExposeDataPrefix()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars)
                return;

            XmlNode parent = Scribe.loader?.curXmlParent;
            PrepareMissingReferenceIds(parent);
            XmlNode sourcePrecept = parent?.SelectSingleNode("sourcePrecept");
            string value = sourcePrecept?.InnerText?.Trim();
            if (sourcePrecept != null && !string.IsNullOrEmpty(value) && MissingPreceptIds.Contains(value))
            {
                sourcePrecept.ParentNode.RemoveChild(sourcePrecept);
                LogMissingReferenceOnce("thought source precept", value);
            }
        }

        private static void PatchKnownRuntimeErrorGuards(Harmony harmony)
        {
            MethodInfo alphaMemesTreeDensityState = AccessTools.DeclaredMethod(
                typeof(ThoughtWorker_Precept), "CurrentStateInternal",
                new[] { typeof(Pawn) });
            if (alphaMemesTreeDensityState != null)
            {
                TryPatch(harmony, alphaMemesTreeDensityState,
                    "Alpha Memes stale tree-density map key guard",
                    finalizerName: nameof(AlphaMemesTreeDensityFinalizer));
            }

            MethodInfo deSpawn = AccessTools.Method(typeof(Thing), "DeSpawn", new[] { typeof(DestroyMode) });
            if (deSpawn != null)
            {
                TryPatch(harmony, deSpawn, "Genes40k duplicate raw-slurry despawn guard",
                    prefixName: nameof(Genes40kRawSlurryDespawnPrefix));
            }

            // PickUpAndHaul's position comparer can observe a moving/invalid
            // thing list while YaOpt dispatches the work search in parallel.
            // Isolate only the comparer-contract failure so the workgiver can
            // decline this candidate; other haul failures remain visible.
            Type haulType = AccessTools.TypeByName("PickUpAndHaul.WorkGiver_HaulToInventory");
            MethodInfo haulJob = haulType == null
                ? null
                : AccessTools.Method(haulType, "JobOnThing");
            if (haulJob != null)
            {
                TryPatch(harmony, haulJob, "Pick Up And Haul inconsistent comparer",
                    finalizerName: nameof(PickUpAndHaulJobFinalizer));
            }

            Type haulDriverType = AccessTools.TypeByName("PickUpAndHaul.JobDriver_HaulToInventory");
            MethodInfo haulReservations = haulDriverType == null
                ? null
                : AccessTools.Method(haulDriverType, "TryMakePreToilReservations");
            if (haulReservations != null)
            {
                TryPatch(harmony, haulReservations, "Pick Up And Haul invalid reservation list",
                    finalizerName: nameof(PickUpAndHaulReservationFinalizer));
            }

            Type logWindowType = AccessTools.TypeByName("LudeonTK.EditWindow_Log");
            MethodInfo logListing = logWindowType == null
                ? null
                : AccessTools.Method(logWindowType, "DoMessagesListing");
            if (logListing != null)
            {
                TryPatch(harmony, logListing, "debug log concurrent listing",
                    finalizerName: nameof(DebugLogListingFinalizer));
            }

            MethodInfo modWarnings = AccessTools.Method(typeof(ModsConfig), "GetModWarnings", Type.EmptyTypes);
            if (modWarnings != null)
            {
                TryPatch(harmony, modWarnings, "duplicate package ID warning-page guard",
                    transpilerName: nameof(ModWarningsTranspiler));
            }

            // Core40k builds a stat-factor dictionary and can encounter the
            // same VEF stat twice.  Preserve all other failures; a duplicate
            // factor simply contributes the method's default factor.
            Type rankInfoType = AccessTools.TypeByName("Core40k.CompRankInfo");
            MethodInfo statFactor = rankInfoType == null
                ? null
                : AccessTools.Method(rankInfoType, "GetStatFactor", new[] { typeof(StatDef) });
            if (statFactor != null)
            {
                TryPatch(harmony, statFactor, "Core40k duplicate stat factor",
                    finalizerName: nameof(Core40kStatFactorFinalizer));
            }

            MethodInfo setupToils = AccessTools.Method(typeof(JobDriver), "SetupToils");
            if (setupToils != null)
            {
                TryPatch(harmony, setupToils, "invalid DoBill recovery guard",
                    prefixName: nameof(JobDriverSetupToilsPrefix),
                    finalizerName: nameof(JobDriverSetupToilsFinalizer));
            }

            MethodInfo addRitualObligation = AccessTools.Method(typeof(Precept_Ritual), "AddObligation",
                new[] { typeof(RitualObligation) });
            if (addRitualObligation != null)
            {
                // The exception is raised by an AlphaMemes prefix. Attach the
                // finalizer to the vanilla wrapper so it can observe prefix failures.
                TryPatch(harmony, addRitualObligation,
                    "AlphaMemes funeral obligation null-target guard",
                    finalizerName: nameof(AlphaMemesFuneralObligationFinalizer));
            }

            Type tattooWorkerType = AccessTools.TypeByName(
                "Verse.PawnRenderNodeWorker_Body_Tattoo");
            MethodInfo tattooCanDrawNow = tattooWorkerType == null
                ? null
                : AccessTools.Method(tattooWorkerType, "CanDrawNow",
                    new[] { typeof(PawnRenderNode), typeof(PawnDrawParms) });
            if (tattooCanDrawNow != null && tattooCanDrawNow.ReturnType == typeof(bool))
            {
                TryPatch(harmony, tattooCanDrawNow,
                    "vanilla tattoo render null guard",
                    finalizerName: nameof(PawnTattooCanDrawNowFinalizer));
            }

        }

        private static void PatchMedievalQuestTechGuards(Harmony harmony)
        {
            Type requestNodeType = AccessTools.TypeByName(
                "RimWorld.QuestGen.QuestNode_TradeRequest_GetRequestedThing");
            MethodInfo requestMethod = requestNodeType == null
                ? null
                : AccessTools.Method(requestNodeType, "TryFindRandomRequestedThingDef");
            if (requestMethod != null)
            {
                TryPatch(harmony, requestMethod, "medieval trade-request tech filter",
                    prefixName: nameof(StartupLogFixesMod.TradeRequestPrefix));
            }

            MethodInfo rewardGenerate = AccessTools.Method(typeof(RewardsGenerator), "Generate",
                new[] { typeof(RewardsGeneratorParams) });
            if (rewardGenerate != null)
            {
                TryPatch(harmony, rewardGenerate, "medieval quest reward tech filter",
                    postfixName: nameof(StartupLogFixesMod.RewardsGeneratePostfix));
            }

        }

        public static Exception PickUpAndHaulJobFinalizer(Exception __exception)
        {
            if (__exception is ArgumentException argument &&
                argument.Message != null &&
                argument.Message.IndexOf("IComparer.Compare", StringComparison.OrdinalIgnoreCase) >= 0 &&
                argument.Message.IndexOf("inconsistent", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Skipped one Pick Up And Haul candidate after an inconsistent position comparer.",
                    1768432959);
                return null;
            }

            return __exception;
        }

        public static Exception PickUpAndHaulReservationFinalizer(Exception __exception)
        {
            if (__exception is ArgumentOutOfRangeException argument &&
                argument.Message != null &&
                (argument.Message.IndexOf("index", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 argument.Message.IndexOf("count", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 argument.Message.IndexOf("reservation", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Skipped one Pick Up And Haul job with an invalid reservation list.",
                    GenText.StableStringHash("StartupLogFixes:PickUpAndHaul:ReservationIndex"));
                return null;
            }

            return __exception;
        }

        public static Exception DebugLogListingFinalizer(Exception __exception)
        {
            if (__exception is InvalidOperationException invalid &&
                invalid.Message != null &&
                invalid.Message.IndexOf("Collection was modified", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Skipped one debug-log listing update while its message queue changed.",
                    GenText.StableStringHash("StartupLogFixes:DebugLog:ConcurrentListing"));
                return null;
            }

            return __exception;
        }

        public static Exception PerformanceOptimizerPatchFinalizer(
            Exception __exception, MethodBase methodInfo, MethodInfo transpiler)
        {
            if (!(__exception is InvalidOperationException invalid) ||
                !string.Equals(invalid.Message, "Incorrect code generation for exception block",
                    StringComparison.Ordinal) || methodInfo == null ||
                !IsPerformanceOptimizerTranspiler(transpiler))
                return __exception;

            string declaringType = methodInfo.DeclaringType?.FullName ?? "<dynamic>";
            string targetName = declaringType + "::" + methodInfo.Name;
            Log.WarningOnce(
                "[StartupLog Compatibility] Performance Optimizer skipped " +
                targetName + " because its transpiler cannot preserve an exception block.",
                GenText.StableStringHash("StartupLogFixes:PerformanceOptimizer:ExceptionBlock:" + targetName));
            return null;
        }

        public static Exception Core40kStatFactorFinalizer(Exception __exception, object __instance, StatDef stat, ref float __result)
        {
            if (__exception is ArgumentException argument &&
                argument.Message != null &&
                argument.Message.IndexOf("same key", StringComparison.OrdinalIgnoreCase) >= 0 &&
                ExceptionOriginatesIn(__exception, "Core40k.CompRankInfo"))
            {
                IDictionary cache = AccessTools.Property(__instance?.GetType(), "CachedStatFactor")?.GetValue(__instance, null) as IDictionary;
                __result = stat != null && cache?.Contains(stat) == true ? (float)cache[stat] : 1f;
                Log.WarningOnce(
                    "[StartupLog Compatibility] Ignored a duplicate Core40k stat factor (the method's default factor was preserved).",
                    1768432958);
                return null;
            }

            return __exception;
        }

        public static Exception JobDriverSetupToilsFinalizer(Exception __exception, JobDriver __instance)
        {
            if (__exception is InvalidOperationException invalid &&
                invalid.Message != null &&
                invalid.Message.IndexOf("DoBill on non-Billgiver", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                EndInvalidBillJob(__instance);
                Log.WarningOnce("[StartupLog Compatibility] Stopped an invalid DoBill job whose target was not a bill giver.",
                    GenText.StableStringHash("StartupLogFixes:JobDriver:DoBillNonBillgiver"));
                return null;
            }

            return __exception;
        }

        // CommonSense can construct DoBill from a stale workgiver target.  Stop it
        // before vanilla/CommonSense toil enumeration dereferences BillGiver; this
        // avoids entering the error-recovery loop that repeatedly starts Wait jobs.
        public static bool JobDriverSetupToilsPrefix(JobDriver __instance)
        {
            if (!(__instance is JobDriver_DoBill) || __instance.job == null)
                return true;

            Thing target = __instance.job.targetA.Thing;
            if (target is IBillGiver)
                return true;

            EndInvalidBillJob(__instance);
            Log.WarningOnce("[StartupLog Compatibility] Rejected a DoBill job whose target was not a bill giver before toil setup.",
                GenText.StableStringHash("StartupLogFixes:JobDriver:DoBillNonBillgiver:Prefix"));
            return false;
        }

        private static void EndInvalidBillJob(JobDriver driver)
        {
            Pawn pawn = driver?.pawn;
            if (pawn?.jobs == null)
                return;

            try
            {
                pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
            }
            catch (InvalidOperationException ex)
            {
                Log.WarningOnce("[StartupLog Compatibility] Could not end an invalid DoBill job cleanly: " + ex.Message,
                    GenText.StableStringHash("StartupLogFixes:JobDriver:DoBillNonBillgiver:End"));
            }
        }

        public static void BuildingArtExposeDataPrefix(Building __instance)
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars || !(__instance is Building_Art))
                return;

            XmlNode parent = Scribe.loader?.curXmlParent;
            int removed = RemoveInvalidOrDuplicateArtReferences(parent,
                "sourcePrecept", "lastUser", "taleRef");
            if (removed > 0)
            {
                string id = parent?.SelectSingleNode("id")?.InnerText ?? "unknown";
                Log.Warning("[StartupLog Compatibility] Removed " + removed +
                    " invalid/duplicate art reference field(s) from " + id + ".");
            }
        }

        private static int RemoveInvalidOrDuplicateArtReferences(XmlNode parent, params string[] names)
        {
            if (parent == null)
                return 0;

            int removed = 0;
            foreach (string name in names)
            {
                List<XmlNode> nodes = parent.SelectNodes(name)?.Cast<XmlNode>().ToList();
                if (nodes == null || nodes.Count == 0)
                    continue;

                XmlNode retained = nodes.FirstOrDefault(IsValidArtReferenceNode);
                for (int i = nodes.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(nodes[i], retained))
                        continue;
                    parent.RemoveChild(nodes[i]);
                    removed++;
                }
            }
            return removed;
        }

        private static bool IsValidArtReferenceNode(XmlNode node)
        {
            if (node == null || string.Equals(node.Attributes?["IsNull"]?.Value, "True",
                StringComparison.OrdinalIgnoreCase))
                return false;

            if (node.Name == "taleRef")
                return IsValidArtReferenceNode(node.SelectSingleNode("tale"));

            string value = node.InnerText?.Trim();
            return !string.IsNullOrEmpty(value) &&
                !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase);
        }

        public static void RoundMemoryExposeDataPrefix()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;
            PrepareMissingReferenceIds(Scribe.loader?.curXmlParent);
            XmlNode pawns = Scribe.loader?.curXmlParent?.SelectSingleNode("Pawns");
            if (pawns == null) return;
            foreach (XmlNode node in pawns.SelectNodes("li").Cast<XmlNode>().ToList())
            {
                if (MissingPawnIds.Contains(node.InnerText.Trim()))
                {
                    string value = node.InnerText.Trim();
                    pawns.RemoveChild(node);
                    LogMissingReferenceOnce("round-memory pawn", value);
                }
            }
        }

        public static bool SpeakUpExtraRulesPrefix(ref IEnumerable<Rule> __result)
        {
            var dialogType = AccessTools.TypeByName("SpeakUp.DialogManager");
            FieldInfo initiatorField = dialogType == null ? null : AccessTools.Field(dialogType, "Initiator");
            FieldInfo recipientField = dialogType == null ? null : AccessTools.Field(dialogType, "Recipient");
            if (!IsStaticFieldOfType(initiatorField, typeof(Pawn)) ||
                !IsStaticFieldOfType(recipientField, typeof(Pawn)))
            {
                if (dialogType != null)
                {
                    Log.WarningOnce(
                        "[StartupLog Compatibility] Skipped the Speak Up pawn-data guard because its DialogManager fields do not match this build; the original call remains visible.",
                        GenText.StableStringHash("StartupLogFixes:SpeakUp:UnknownDialogApi"));
                }
                return true;
            }

            Pawn initiator = initiatorField.GetValue(null) as Pawn;
            Pawn recipient = recipientField.GetValue(null) as Pawn;
            if (HasCompleteSpeakUpData(initiator) && (recipient == null || HasCompleteSpeakUpData(recipient)))
                return true;

            __result = Enumerable.Empty<Rule>();
            return false;
        }

        private static bool IsStaticFieldOfType(FieldInfo field, Type expectedType)
        {
            return field != null && field.IsStatic && field.FieldType == expectedType;
        }

        private static bool HasCompleteSpeakUpData(Pawn pawn)
        {
            return pawn?.RaceProps?.Humanlike == true && pawn.needs?.mood != null && pawn.story != null &&
                pawn.story.traits != null && pawn.skills?.skills != null && pawn.health != null &&
                pawn.inventory != null && pawn.apparel != null && pawn.pather != null;
        }

        public static bool VehiclePawnMassUsagePrefix(float massUsage, Pawn pawn, ref float __result)
        {
            if (pawn != null)
                return true;

            __result = massUsage;
            Log.WarningOnce(
                "[StartupLog Compatibility] Ignored a null caravan pawn in Vehicle Framework mass calculation.",
                1768432951);
            return false;
        }

        public static void ReGrowthRaidFactionPostfix(
            RimWorld.Planet.TimedDetectionRaids __instance,
            ref Faction __result)
        {
            if (__result != null || __instance?.parent?.def?.defName != "Camp")
                return;

            List<Faction> candidates = Find.FactionManager?.AllFactionsVisible
                .Where(faction => faction != null && !faction.IsPlayer && faction.def != null &&
                    !faction.def.raidsForbidden && faction.HostileTo(Faction.OfPlayer))
                .OrderBy(faction => faction.loadID)
                .ToList();
            if (candidates == null || candidates.Count == 0)
                return;

            int stableIndex = (__instance.parent.ID & int.MaxValue) % candidates.Count;
            __result = candidates[stableIndex];
        }

        public static bool ReGrowthDetectionRaidPrefix(RimWorld.Planet.TimedDetectionRaids __instance)
        {
            if (__instance?.parent?.def?.defName != "Camp")
                return true;

            MethodInfo getter = AccessTools.PropertyGetter(typeof(RimWorld.Planet.TimedDetectionRaids), "RaidFaction");
            MethodInfo resetCountdown = AccessTools.Method(
                typeof(RimWorld.Planet.TimedDetectionRaids), "ResetCountdown", Type.EmptyTypes);
            if (getter == null || resetCountdown == null || getter.IsStatic || resetCountdown.IsStatic ||
                getter.ReturnType != typeof(Faction) || getter.GetParameters().Length != 0 ||
                resetCountdown.ReturnType != typeof(void) || resetCountdown.GetParameters().Length != 0 ||
                getter.DeclaringType == null || !getter.DeclaringType.IsInstanceOfType(__instance) ||
                resetCountdown.DeclaringType == null || !resetCountdown.DeclaringType.IsInstanceOfType(__instance))
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Skipped the ReGrowth camp-detection guard because TimedDetectionRaids API members do not match this build; the original call remains visible.",
                    GenText.StableStringHash("StartupLogFixes:ReGrowth:UnknownDetectionApi"));
                return true;
            }

            if (getter.Invoke(__instance, null) is Faction)
                return true;

            resetCountdown.Invoke(__instance, null);
            Log.WarningOnce(
                "[StartupLog Compatibility] Caravan-camp detection was paused because this world has no hostile raid-capable faction.",
                1768432952);
            return false;
        }

        public static bool UsefulMarksAutoCapturePrefix()
        {
            return Scribe.mode == LoadSaveMode.Inactive;
        }

        public static void TacticalGroupsExposeDataPrefix()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars || Scribe.loader == null)
                return;

            int renumbered;
            if (TryRepairColonyGroupIds(Scribe.loader.curXmlParent, out renumbered))
                Log.Warning("[StartupLog Compatibility] Renumbered " + renumbered + " duplicate Tactical Groups colony group ID(s) while preserving group data.");
        }

        public static void GiddyUpExposeDataPrefix()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars || Scribe.loader == null)
                return;

            int removed = RemoveStaleGiddyUpRecords(Scribe.loader.curXmlParent);
            if (removed > 0)
                Log.Warning("[StartupLog Compatibility] Removed " + removed + " stale GiddyUp pawn record(s).");
        }

        public static void RaidGroupExposeDataPrefix()
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars)
                return;

            PrepareMissingReferenceIds(Scribe.loader?.curXmlParent);
            XmlNode lords = Scribe.loader?.curXmlParent?.SelectSingleNode("lords");
            if (lords == null)
                return;
            foreach (XmlNode node in lords.SelectNodes("li").Cast<XmlNode>().ToList())
            {
                if (MissingLordIds.Contains(node.InnerText.Trim()))
                {
                    string value = node.InnerText.Trim();
                    lords.RemoveChild(node);
                    LogMissingReferenceOnce("raid-group lord", value);
                }
            }
        }

        public static bool ToggleHediffNotifyUnequippedPrefix(object __instance, Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null || __instance == null)
                return false;

            object props = AccessTools.Property(__instance.GetType(), "Props")?.GetValue(__instance, null);
            HediffDef hediffDef = AccessTools.Field(props?.GetType(), "hediff")?.GetValue(props) as HediffDef;
            return hediffDef == null || pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef) != null;
        }

        private static bool TryRepairColonyGroupIds(XmlNode parent, out int renumbered)
        {
            renumbered = 0;
            XmlNodeList groupIds = parent?.SelectNodes("colonyGroups/values/li/groupID");
            if (groupIds == null || groupIds.Count < 2)
                return false;

            var seen = new HashSet<string>();
            int nextId = 1;
            foreach (XmlNode groupId in groupIds)
            {
                string value = groupId.InnerText;
                if (seen.Add(value))
                {
                    if (int.TryParse(value, out int parsed) && parsed >= nextId)
                        nextId = parsed + 1;
                    continue;
                }

                while (seen.Contains(nextId.ToString()))
                    nextId++;
                groupId.InnerText = nextId.ToString();
                seen.Add(groupId.InnerText);
                nextId++;
                renumbered++;
            }
            return renumbered > 0;
        }

        private static int RemoveStaleGiddyUpRecords(XmlNode parent)
        {
            XmlNode store = parent?.SelectSingleNode("store");
            XmlNode keys = store?.SelectSingleNode("keys");
            XmlNode values = store?.SelectSingleNode("values");
            if (keys == null || values == null)
                return 0;

            List<XmlNode> keyEntries = keys.SelectNodes("li")?.Cast<XmlNode>().ToList();
            List<XmlNode> valueEntries = values.SelectNodes("li")?.Cast<XmlNode>().ToList();
            if (keyEntries == null || valueEntries == null || keyEntries.Count != valueEntries.Count)
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Skipped Giddy-Up cleanup because its save dictionary keys and values are not aligned.",
                    GenText.StableStringHash("StartupLogFixes:GiddyUp:UnalignedDictionary"));
                return 0;
            }

            var definedPawns = new HashSet<string>(
                parent.OwnerDocument.SelectNodes("//*[def and id]/id").Cast<XmlNode>()
                    .Select(node => node.InnerText?.Trim())
                    .Where(value => !string.IsNullOrEmpty(value))
                    .Select(value => "Thing_" + value));
            var staleIndexes = new List<int>();
            int index = 0;
            foreach (XmlNode value in valueEntries)
            {
                string pawnId = value.SelectSingleNode("pawn")?.InnerText?.Trim();
                if (!string.IsNullOrEmpty(pawnId) && pawnId != "null" && !definedPawns.Contains(pawnId))
                    staleIndexes.Add(index);
                index++;
            }

            for (int i = staleIndexes.Count - 1; i >= 0; i--)
            {
                XmlNode key = keyEntries[staleIndexes[i]];
                XmlNode value = valueEntries[staleIndexes[i]];
                key?.ParentNode.RemoveChild(key);
                value?.ParentNode.RemoveChild(value);
            }
            return staleIndexes.Count;
        }

        private static int IndexOfElement(XmlNode parent, XmlNode target)
        {
            int index = 0;
            foreach (XmlNode child in parent.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element || child.Name != "li")
                    continue;
                if (ReferenceEquals(child, target))
                    return index;
                index++;
            }
            return -1;
        }

        private static XmlNode ElementAt(XmlNode parent, int index)
        {
            if (index < 0)
                return null;
            int current = 0;
            foreach (XmlNode child in parent.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element || child.Name != "li")
                    continue;
                if (current++ == index)
                    return child;
            }
            return null;
        }

        public static void MapModeWorldLayerPrefix(string assemblyName, ref string worldLayerName)
        {
            if (assemblyName == "ReGrowthCore" && worldLayerName == "BiomesKitWorldLayer")
                worldLayerName = "WorldDrawLayer_Beautification";
        }

        public static IEnumerable<CodeInstruction> ShieldPropertiesConstructorTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo blunt = AccessTools.Field(typeof(DamageDefOf), nameof(DamageDefOf.Blunt));
            List<CodeInstruction> original = instructions?.ToList();
            if (original == null)
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Skipped the shield-properties transpiler because Harmony supplied no instructions.",
                    GenText.StableStringHash("StartupLogFixes:ShieldProperties:NullInstructions"));
                return original;
            }

            if (blunt == null || !blunt.IsStatic || blunt.FieldType != typeof(DamageDef))
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Skipped the shield-properties transpiler because DamageDefOf.Blunt has an unexpected API shape.",
                    GenText.StableStringHash("StartupLogFixes:ShieldProperties:UnknownApi"));
                return original;
            }

            if (!original.Any(instruction => instruction.opcode == OpCodes.Ldsfld &&
                    Equals(instruction.operand, blunt)))
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Skipped the shield-properties transpiler because the expected Blunt field access was not found.",
                    GenText.StableStringHash("StartupLogFixes:ShieldProperties:PatternMissing"));
                return original;
            }

            var rewritten = new List<CodeInstruction>(original.Count);
            foreach (CodeInstruction instruction in original)
            {
                if (instruction.opcode == OpCodes.Ldsfld && Equals(instruction.operand, blunt))
                    rewritten.Add(new CodeInstruction(OpCodes.Ldnull)
                        .MoveLabelsFrom(instruction)
                        .MoveBlocksFrom(instruction));
                else
                    rewritten.Add(instruction);
            }
            return rewritten;
        }

        public static IEnumerable<CodeInstruction> YaOptTypeSearchTranspiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getTypes = AccessTools.Method(typeof(Assembly), nameof(Assembly.GetTypes));
            MethodInfo safeGetTypes = AccessTools.Method(typeof(StartupLogFixesMod), nameof(GetLoadableTypes));
            List<CodeInstruction> original = instructions?.ToList();
            if (original == null)
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Skipped the YaOpt type-search transpiler because Harmony supplied no instructions.",
                    GenText.StableStringHash("StartupLogFixes:YaOptTypeSearch:NullInstructions"));
                return original;
            }

            if (getTypes == null || getTypes.IsStatic || getTypes.GetParameters().Length != 0 ||
                getTypes.ReturnType != typeof(Type[]) || safeGetTypes == null || !safeGetTypes.IsStatic ||
                safeGetTypes.ReturnType != typeof(Type[]) || safeGetTypes.GetParameters().Length != 1 ||
                safeGetTypes.GetParameters()[0].ParameterType != typeof(Assembly))
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Skipped the YaOpt type-search transpiler because Assembly.GetTypes or its replacement has an unexpected API shape.",
                    GenText.StableStringHash("StartupLogFixes:YaOptTypeSearch:UnknownApi"));
                return original;
            }

            if (!original.Any(instruction =>
                    (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                    Equals(instruction.operand, getTypes)))
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Skipped the YaOpt type-search transpiler because the expected Assembly.GetTypes call was not found.",
                    GenText.StableStringHash("StartupLogFixes:YaOptTypeSearch:PatternMissing"));
                return original;
            }

            var rewritten = new List<CodeInstruction>(original.Count);
            foreach (CodeInstruction instruction in original)
            {
                if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                    Equals(instruction.operand, getTypes))
                {
                    rewritten.Add(new CodeInstruction(OpCodes.Call, safeGetTypes)
                        .MoveLabelsFrom(instruction)
                        .MoveBlocksFrom(instruction));
                }
                else
                    rewritten.Add(instruction);
            }
            return rewritten;
        }

        public static Type[] GetLoadableTypes(Assembly assembly)
        {
            if (assembly == null || assembly.IsDynamic)
                return Type.EmptyTypes;

            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                string assemblyName = assembly.GetName()?.Name ?? assembly.FullName ?? "<unknown>";
                string details = string.Join(" | ", (exception.LoaderExceptions ?? new Exception[0])
                    .Where(error => error != null)
                    .Select(error => error.Message)
                    .Take(3)
                    .ToArray());
                Log.WarningOnce(
                    "[StartupLog Compatibility] Type search loaded only the available types from " +
                    assemblyName + ". Loader errors were not suppressed: " + details,
                    GenText.StableStringHash("StartupLogFixes:ReflectionTypeLoad:" + assemblyName));
                return exception.Types.Where(type => type != null).ToArray();
            }
            catch (FileNotFoundException exception)
            {
                string assemblyName = assembly.GetName()?.Name ?? assembly.FullName ?? "<unknown>";
                Log.WarningOnce(
                    "[StartupLog Compatibility] YaOpt type search skipped " + assemblyName +
                    " because an optional assembly could not be loaded: " + exception.Message,
                    GenText.StableStringHash("StartupLogFixes:ReflectionFileNotFound:" + assemblyName));
                return Type.EmptyTypes;
            }
            catch (FileLoadException exception)
            {
                string assemblyName = assembly.GetName()?.Name ?? assembly.FullName ?? "<unknown>";
                Log.WarningOnce(
                    "[StartupLog Compatibility] YaOpt type search skipped " + assemblyName +
                    " because its assembly could not be loaded: " + exception.Message,
                    GenText.StableStringHash("StartupLogFixes:ReflectionFileLoad:" + assemblyName));
                return Type.EmptyTypes;
            }
        }

        public static void TradeRequestPrefix(ref List<ThingDef> dontRequest)
        {
            MedievalQuestTechCompatibility.TradeRequestPrefix(ref dontRequest);
        }

        public static void RewardsGeneratePostfix(ref List<Reward> __result)
        {
            MedievalQuestTechCompatibility.RewardsGeneratePostfix(ref __result);
        }

    }

    [StaticConstructorOnStartup]
    internal static class MedievalBrazierComponentCompatibility
    {
        static MedievalBrazierComponentCompatibility()
        {
            ThingDef brazier = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Brazier2x2c");
            if (brazier?.comps == null) return;
            CompProperties_Styleable style = null;
            CompProperties_MeditationFocus focus = null;
            int merged = 0;
            for (int i = 0; i < brazier.comps.Count; i++)
            {
                CompProperties properties = brazier.comps[i];
                if (properties is CompProperties_Styleable currentStyle)
                {
                    if (style == null) { style = currentStyle; continue; }
                }
                else if (properties is CompProperties_MeditationFocus currentFocus)
                {
                    if (focus == null) { focus = currentFocus; continue; }
                    if (focus.statDef != currentFocus.statDef) continue;
                    // Keep artistic quality offsets and add the brazier's flame offsets.
                    focus.focusTypes = (focus.focusTypes ?? new List<MeditationFocusDef>())
                        .Concat(currentFocus.focusTypes ?? new List<MeditationFocusDef>()).Distinct().ToList();
                    focus.offsets = (focus.offsets ?? new List<FocusStrengthOffset>())
                        .Concat(currentFocus.offsets ?? new List<FocusStrengthOffset>()).ToList();
                }
                else continue;
                brazier.comps.RemoveAt(i--);
                merged++;
            }
            if (merged > 0)
                Log.Message("[StartupLog Compatibility] Consolidated " + merged +
                    " duplicate ancient obelisk brazier components while preserving both meditation focus types.");
        }
    }

    [StaticConstructorOnStartup]
    internal static class MedievalQuestTechCompatibility
    {
        private static readonly HashSet<ThingDef> NonMedievalDefs = new HashSet<ThingDef>();
        private static readonly HashSet<int> InspectedQuestIds = new HashSet<int>();
        private static readonly HashSet<int> EndedQuestIds = new HashSet<int>();
        private static bool cacheBuilt;

        public static void ResetQuestAuditState()
        {
            InspectedQuestIds.Clear();
            EndedQuestIds.Clear();
        }

        public static void TradeRequestPrefix(ref List<ThingDef> dontRequest)
        {
            if (!IsMedievalPlayer() || dontRequest == null)
                return;

            BuildCache();
            var excluded = new HashSet<ThingDef>(dontRequest);
            foreach (ThingDef def in NonMedievalDefs)
            {
                if (excluded.Add(def))
                    dontRequest.Add(def);
            }
        }

        public static void RewardsGeneratePostfix(ref List<Reward> __result)
        {
            if (!IsMedievalPlayer() || __result == null)
                return;

            BuildCache();
            for (int i = __result.Count - 1; i >= 0; i--)
            {
                Reward reward = __result[i];
                Reward_DefinedThingDef defined = reward as Reward_DefinedThingDef;
                if (defined != null && IsBlocked(defined.thingDef))
                {
                    __result.RemoveAt(i);
                    continue;
                }

                Reward_Items items = reward as Reward_Items;
                if (items == null)
                    continue;

                List<Thing> itemList = items.ItemsListForReading;
                if (itemList != null)
                    itemList.RemoveAll(item => item != null && IsBlocked(item.def));

                IList rememberedDefs = AccessTools.Field(typeof(Reward_Items), "itemDefs")?.GetValue(items) as IList;
                if (rememberedDefs != null)
                {
                    for (int j = rememberedDefs.Count - 1; j >= 0; j--)
                    {
                        object remembered = rememberedDefs[j];
                        object pair = remembered == null
                            ? null
                            : AccessTools.Field(remembered.GetType(), "thing")?.GetValue(remembered);
                        ThingDef rememberedDef = pair == null
                            ? null
                            : AccessTools.Field(pair.GetType(), "thing")?.GetValue(pair) as ThingDef;
                        if (IsBlocked(rememberedDef))
                            rememberedDefs.RemoveAt(j);
                    }
                }

                if ((itemList == null || itemList.Count == 0) &&
                    (rememberedDefs == null || rememberedDefs.Count == 0))
                    __result.RemoveAt(i);
            }
        }

        public static void CleanupInvalidPreAcceptanceQuests(IEnumerable<Quest> quests)
        {
            if (quests == null)
                return;

            foreach (Quest quest in quests.ToList())
            {
                if (quest == null || quest.PartsListForReading == null ||
                    !InspectedQuestIds.Add(quest.id))
                    continue;

                string reason;
                bool invalid = IsStructurallyInvalid(quest, out reason);
                if (!invalid || !ShouldEndInvalidQuest(quest))
                    continue;

                if (!EndedQuestIds.Add(quest.id))
                    continue;
                quest.End(QuestEndOutcome.InvalidPreAcceptance, false, false);
                Log.WarningOnce("[StartupLog Compatibility] Ended invalid quest " +
                    quest.id + " (" + reason + ").",
                    GenText.StableStringHash("StartupLogFixes:MedievalQuest:Ended:" + quest.id));
            }
        }

        private static bool ShouldEndInvalidQuest(Quest quest)
        {
            return quest.State == QuestState.NotYetAccepted;
        }

        private static bool IsStructurallyInvalid(Quest quest, out string reason)
        {
            reason = null;
            if (quest == null || quest.PartsListForReading == null)
                return false;

            if (HasInvalidKnownQuestTarget(quest, out reason))
                return true;

            return false;
        }

        private static bool HasInvalidKnownQuestTarget(Quest quest, out string reason)
        {
            reason = null;
            foreach (QuestPart part in quest.PartsListForReading)
            {
                if (part == null)
                    continue;

                string typeName = part.GetType().FullName;
                if (typeName == "RimWorld.QuestPart_RefugeeInteractions")
                {
                    if (HasInvalidPawnList(part, "pawns"))
                    {
                        reason = "Hospitality refugee pawn list is invalid";
                        return true;
                    }
                    if (HasInvalidFaction(part, "faction"))
                    {
                        reason = "Hospitality refugee faction is invalid";
                        return true;
                    }
                    if (HasInvalidWorldObject(part, "mapParent"))
                    {
                        reason = "Hospitality refugee map parent is invalid";
                        return true;
                    }
                }
                else if (typeName == "RimWorld.QuestPart_AddQuestRefugeeDelayedReward")
                {
                    if (HasInvalidPawnList(part, "lodgers"))
                    {
                        reason = "Hospitality delayed reward lodger list is invalid";
                        return true;
                    }
                }
                else if (typeName == "RimWorld.QuestPart_AddQuest_RefugeeBetrayal")
                {
                    if (HasInvalidPawnList(part, "lodgers") || HasInvalidPawn(part, "asker"))
                    {
                        reason = "Hospitality betrayal pawn target is invalid";
                        return true;
                    }
                    if (HasInvalidFaction(part, "factionOpponent"))
                    {
                        reason = "Hospitality betrayal faction is invalid";
                        return true;
                    }
                    if (HasInvalidWorldObject(part, "mapParent"))
                    {
                        reason = "Hospitality betrayal map parent is invalid";
                        return true;
                    }
                }
                else if (typeName == "RimWorld.QuestPart_AllowDecreesForLodger" &&
                    HasInvalidPawn(part, "lodger"))
                {
                    reason = "Hospitality decree lodger is invalid";
                    return true;
                }

                if (IsOberoniaTradePart(part.GetType()) && HasInvalidWorldObject(part, "worldObject"))
                {
                    reason = "Oberonia trade world object is invalid";
                    return true;
                }
            }
            return false;
        }

        private static bool HasInvalidPawn(QuestPart part, string fieldName)
        {
            FieldInfo field = AccessTools.Field(part.GetType(), fieldName);
            if (field == null)
                return false;

            Pawn pawn = field.GetValue(part) as Pawn;
            return pawn == null || pawn.Destroyed || pawn.def == null;
        }

        private static bool HasInvalidPawnList(QuestPart part, string fieldName)
        {
            FieldInfo field = AccessTools.Field(part.GetType(), fieldName);
            if (field == null)
                return false;

            IList pawns = field.GetValue(part) as IList;
            // A missing/empty list is ambiguous during post-load ordering.  It
            // is not proof that a Hospitality quest target is broken; only an
            // explicitly present invalid Pawn makes the quest unsafe.
            if (pawns == null || pawns.Count == 0)
                return false;

            foreach (object value in pawns)
            {
                Pawn pawn = value as Pawn;
                if (pawn == null || pawn.Destroyed || pawn.def == null)
                    return true;
            }
            return false;
        }

        private static bool HasInvalidFaction(QuestPart part, string fieldName)
        {
            FieldInfo field = AccessTools.Field(part.GetType(), fieldName);
            if (field == null)
                return false;

            Faction faction = field.GetValue(part) as Faction;
            return faction == null || faction.def == null;
        }

        private static bool HasInvalidWorldObject(QuestPart part, string fieldName)
        {
            FieldInfo field = AccessTools.Field(part.GetType(), fieldName);
            if (field == null)
                return false;

            RimWorld.Planet.WorldObject worldObject = field.GetValue(part) as RimWorld.Planet.WorldObject;
            if (worldObject == null || worldObject.Destroyed)
                return true;

            // A missing world registry is an ordering/API condition, not proof
            // that the quest target is stale.  Preserve the quest until the
            // registry is available rather than ending a valid quest on load.
            if (Find.WorldObjects == null || Find.WorldObjects.AllWorldObjects == null)
                return false;

            return !Find.WorldObjects.AllWorldObjects.Contains(worldObject);
        }

        private static bool IsOberoniaTradePart(Type type)
        {
            string name = type?.FullName;
            return name == "OberoniaAurea_Frame.QuestPart_InitiateCategoryTradeRequest" ||
                name == "OberoniaAurea_Frame.QuestPart_InitiateTradeRequest";
        }

        private static bool IsBlocked(ThingDef def)
        {
            return def != null && NonMedievalDefs.Contains(def);
        }

        private static bool IsMedievalPlayer()
        {
            return Faction.OfPlayer?.def != null &&
                Faction.OfPlayer.def.techLevel <= TechLevel.Medieval;
        }

        private static void BuildCache()
        {
            if (cacheBuilt)
                return;

            cacheBuilt = true;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def != null && def.techLevel > TechLevel.Medieval && !IsRimDark(def))
                    NonMedievalDefs.Add(def);
            }
            Log.Message("[StartupLog Compatibility] Cached " + NonMedievalDefs.Count +
                " non-RimDark above-medieval item definitions for quest filtering.");
        }

        private static bool IsRimDark(ThingDef def)
        {
            string packageId = def.modContentPack?.PackageId;
            return !string.IsNullOrEmpty(packageId) &&
                (packageId.StartsWith("Phonicmas.RimDark.", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(packageId, "Local.RimDark.MankindsFinest.MedievalPatch", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(packageId, "Local.RimDark.MastersOfMankind", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(packageId, "Local.EES40K.LostChapterArmory", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(packageId, "grimworld.HighestRulers", StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static class MastersOfMankindRecipeCompatibility
    {
        static MastersOfMankindRecipeCompatibility()
        {
            ThingDef autoforge = DefDatabase<ThingDef>.GetNamedSilentFail("GW_Autoforge");
            ThingDef anvil = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Anvil");
            RecipeDef goldenStc = DefDatabase<RecipeDef>.GetNamedSilentFail("Local_MakeGoldenSTC");
            if (autoforge == null && anvil == null)
                return;

            List<RecipeDef> legendaryRecipes = DefDatabase<RecipeDef>.AllDefsListForReading
                .Where(recipe => recipe?.defName != null && !StartupLogFixesMod.IsDisabledMastersRecipe(recipe) &&
                    (recipe.defName.StartsWith("Auto_GW_Rulers_", StringComparison.Ordinal) ||
                     recipe.defName.StartsWith("Auto_GW_SM_", StringComparison.Ordinal) ||
                     IsTargetRecipe(recipe)))
                .ToList();
            if (legendaryRecipes.Count == 0 && goldenStc == null)
                return;

            var legendarySet = new HashSet<RecipeDef>(legendaryRecipes);
            int removedKnownWorkbenchLinks = 0;
            if (autoforge?.recipes != null && goldenStc != null)
                removedKnownWorkbenchLinks += autoforge.recipes.RemoveAll(recipe => recipe == goldenStc);
            if (anvil?.recipes != null)
                removedKnownWorkbenchLinks += anvil.recipes.RemoveAll(recipe => legendarySet.Contains(recipe));

            int repairedUsers = 0;
            int repairedResearch = 0;
            foreach (RecipeDef recipe in legendaryRecipes)
            {
                if (autoforge != null &&
                    (recipe.recipeUsers == null || recipe.recipeUsers.Count != 1 || recipe.recipeUsers[0] != autoforge))
                {
                    recipe.recipeUsers = new List<ThingDef> { autoforge };
                    repairedUsers++;
                }

                if (autoforge?.recipes != null && !autoforge.recipes.Contains(recipe))
                    autoforge.recipes.Add(recipe);

                ResearchProjectDef requiredResearch = ResearchForLegendaryRecipe(recipe);
                if (requiredResearch != null && recipe.researchPrerequisite != requiredResearch)
                {
                    recipe.researchPrerequisite = requiredResearch;
                    repairedResearch++;
                }
            }

            if (goldenStc != null && anvil != null)
            {
                goldenStc.recipeUsers = new List<ThingDef> { anvil };
                if (anvil.recipes == null)
                    anvil.recipes = new List<RecipeDef>();
                if (!anvil.recipes.Contains(goldenStc))
                    anvil.recipes.Add(goldenStc);
                Log.Message("[StartupLog Compatibility] Enforced Golden STC on DankPyon_Anvil.");
            }

            FieldInfo recipeCache = AccessTools.Field(typeof(ThingDef), "allRecipesCached");
            if (recipeCache != null && recipeCache.FieldType == typeof(List<RecipeDef>))
            {
                if (autoforge != null)
                    recipeCache.SetValue(autoforge, null);
                if (anvil != null)
                    recipeCache.SetValue(anvil, null);
            }
            else if (recipeCache != null)
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Could not invalidate the known Masters workbench recipe caches because ThingDef.allRecipesCached has an incompatible type.",
                    GenText.StableStringHash("StartupLogFixes:MastersRecipes:UnknownCacheApi"));
            }

            Log.Message("[StartupLog Compatibility] Enforced Masters of Mankind recipe gates: recipes=" +
                legendaryRecipes.Count + ", repairedUsers=" + repairedUsers +
                ", removedKnownWorkbenchLinks=" + removedKnownWorkbenchLinks +
                ", repairedResearch=" + repairedResearch + ".");
        }

        private static ResearchProjectDef ResearchForLegendaryRecipe(string recipeName)
        {
            if (string.IsNullOrEmpty(recipeName))
                return null;

            string researchName = recipeName.IndexOf("Malcador", StringComparison.Ordinal) >= 0
                ? "GW_Rulers_EmperorRightHand"
                : recipeName.IndexOf("Valdor", StringComparison.Ordinal) >= 0
                    ? "GW_Rulers_FirstCaptainr"
                    : recipeName.IndexOf("Emperor", StringComparison.Ordinal) >= 0
                        ? "GW_Rulers_OneTrueArmor"
                        : null;
            return string.IsNullOrEmpty(researchName)
                ? null
                : DefDatabase<ResearchProjectDef>.GetNamedSilentFail(researchName);
        }

        private static ResearchProjectDef ResearchForLegendaryRecipe(RecipeDef recipe)
        {
            if (recipe?.products != null && recipe.products.Any(product =>
                    product?.thingDef?.defName == "GW_Rulers_Chainsword" ||
                    product?.thingDef?.defName == "GW_Rulers_Powersword"))
                return DefDatabase<ResearchProjectDef>.GetNamedSilentFail("GW_Rulers_MasterForge");
            if (recipe?.products != null && recipe.products.Any(product =>
                    product?.thingDef?.defName == "GW_SM_Melee_EmperorClaw" ||
                    product?.thingDef?.defName == "GW_SM_Melee_ValdorSpear" ||
                    product?.thingDef?.defName == "GW_SM_Melee_MalcadorStaff"))
            {
                string researchName = recipe.products.Any(product =>
                    product?.thingDef?.defName == "GW_SM_Melee_EmperorClaw")
                    ? "GW_Rulers_OneTrueArmor"
                    : recipe.products.Any(product =>
                        product?.thingDef?.defName == "GW_SM_Melee_ValdorSpear")
                        ? "GW_Rulers_FirstCaptainr"
                        : "GW_Rulers_EmperorRightHand";
                return DefDatabase<ResearchProjectDef>.GetNamedSilentFail(researchName);
            }
            return ResearchForLegendaryRecipe(recipe?.defName);
        }

        private static bool IsTargetRecipe(RecipeDef recipe)
        {
            return recipe?.products != null && recipe.products.Any(product =>
                product?.thingDef?.defName == "GW_Rulers_Chainsword" ||
                product?.thingDef?.defName == "GW_Rulers_Powersword" ||
                product?.thingDef?.defName == "GW_SM_Melee_EmperorClaw" ||
                product?.thingDef?.defName == "GW_SM_Melee_ValdorSpear" ||
                product?.thingDef?.defName == "GW_SM_Melee_MalcadorStaff");
        }

        public static void OberoniaQuestManagerExposeDataPostfix(QuestManager __instance)
        {
            if (Scribe.mode != LoadSaveMode.PostLoadInit || __instance == null ||
                __instance.QuestsListForReading == null)
                return;

            if (Find.WorldObjects == null || Find.WorldObjects.AllWorldObjects == null)
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Deferred Oberonia quest cleanup because the world-object registry is not ready.",
                    GenText.StableStringHash("StartupLogFixes:Oberonia:WorldObjectsNotReady"));
                return;
            }

            // Quest IDs are only unique within one save.  Re-evaluate all quests
            // when loading another save in the same RimWorld process.
            MedievalQuestTechCompatibility.ResetQuestAuditState();
            MedievalQuestTechCompatibility.CleanupInvalidPreAcceptanceQuests(
                __instance.QuestsListForReading);

            foreach (string typeName in new[]
            {
                "OberoniaAurea_Frame.QuestPart_InitiateCategoryTradeRequest",
                "OberoniaAurea_Frame.QuestPart_InitiateTradeRequest"
            })
            {
                Type tradePartType = AccessTools.TypeByName(typeName);
                if (tradePartType == null)
                    continue;

                FieldInfo worldObjectField = AccessTools.Field(tradePartType, "worldObject");
                if (worldObjectField == null ||
                    !typeof(RimWorld.Planet.WorldObject).IsAssignableFrom(worldObjectField.FieldType))
                {
                    Log.WarningOnce(
                        "[StartupLog Compatibility] Skipped Oberonia quest cleanup because " +
                        typeName + " has no compatible worldObject field.",
                        GenText.StableStringHash("StartupLogFixes:Oberonia:UnknownWorldObjectField:" + typeName));
                    continue;
                }
                foreach (Quest quest in __instance.QuestsListForReading.ToList())
                {
                    if (quest == null || quest.State != QuestState.NotYetAccepted ||
                        quest.PartsListForReading == null)
                        continue;

                    foreach (QuestPart part in quest.PartsListForReading)
                    {
                        if (part == null || !tradePartType.IsInstanceOfType(part))
                            continue;

                        var worldObject = worldObjectField?.GetValue(part) as RimWorld.Planet.WorldObject;
                        bool valid = worldObject != null && !worldObject.Destroyed && worldObject.Faction != null &&
                            Find.WorldObjects != null && Find.WorldObjects.AllWorldObjects.Contains(worldObject);
                        if (!valid)
                        {
                            quest.End(QuestEndOutcome.InvalidPreAcceptance, false, false);
                            Log.Warning("[StartupLog Compatibility] Ended invalid Oberonia quest " +
                                quest.id.ToString() + " (missing trade settlement target).");
                            break;
                        }
                    }
                }
            }
        }

        public static void OberoniaInvolvedFactionsPostfix(ref IEnumerable<Faction> __result)
        {
            if (__result != null)
                __result = __result.Where(faction => faction != null);
        }

        public static bool OberoniaTradeSignalPrefix(object __instance, Signal signal)
        {
            if (__instance == null)
                return true;

            Type type = __instance.GetType();
            FieldInfo inSignalField = AccessTools.Field(type, "inSignal");
            FieldInfo worldObjectField = AccessTools.Field(type, "worldObject");
            if (inSignalField == null || inSignalField.FieldType != typeof(string) ||
                worldObjectField == null ||
                !typeof(RimWorld.Planet.WorldObject).IsAssignableFrom(worldObjectField.FieldType))
                return true;

            string inSignal = inSignalField.GetValue(__instance) as string;
            if (string.IsNullOrEmpty(inSignal) || string.IsNullOrEmpty(signal.tag) ||
                !string.Equals(signal.tag, inSignal, StringComparison.Ordinal))
                return true;

            if (Find.WorldObjects == null || Find.WorldObjects.AllWorldObjects == null)
                return true;

            var worldObject = worldObjectField.GetValue(__instance) as RimWorld.Planet.WorldObject;
            bool valid = worldObject != null && !worldObject.Destroyed && worldObject.Faction != null &&
                Find.WorldObjects.AllWorldObjects.Contains(worldObject);
            if (valid)
                return true;

            FieldInfo questField = AccessTools.Field(typeof(QuestPart), "quest");
            if (questField == null || questField.FieldType != typeof(Quest))
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Could not end an invalid Oberonia quest because the current QuestPart API has no compatible quest field.",
                    GenText.StableStringHash("StartupLogFixes:Oberonia:UnknownQuestField"));
                return true;
            }

            Quest quest = questField.GetValue(__instance) as Quest;
            if (quest != null && quest.State == QuestState.NotYetAccepted)
                quest.End(QuestEndOutcome.InvalidPreAcceptance, false, false);
            Log.WarningOnce("[StartupLog Compatibility] Skipped invalid Oberonia trade request signal.",
                GenText.StableStringHash("StartupLogFixes:Oberonia:InvalidTradeSignal"));
            return false;
        }

        public static bool VanillaMemesHeadgearPrefix(PawnDrawParms parms)
        {
            return parms.pawn?.health?.hediffSet != null;
        }
    }

    [StaticConstructorOnStartup]
    internal static class OberoniaIncidentCompatibility
    {
        static OberoniaIncidentCompatibility()
        {
            IncidentDef incident = DefDatabase<IncidentDef>.GetNamedSilentFail("OA_RK_DiplomaticOffices");
            if (incident != null && incident.workerClass?.FullName == "OberoniaAurea.IncidentWorker_OptionalDiplomaticOffices")
            {
                if (HasCompatibleOberoniaFactionApi())
                    incident.workerClass = typeof(SafeOberoniaDiplomaticOfficesWorker);
                else
                    Log.WarningOnce(
                        "[StartupLog Compatibility] Kept the original Oberonia diplomatic-offices worker because ModUtility.OAFaction does not match this build.",
                        GenText.StableStringHash("StartupLogFixes:Oberonia:SkippedDiplomaticWorker"));
            }

            IncidentDef construction = DefDatabase<IncidentDef>.GetNamedSilentFail("OA_RK_UrbanConstruction");
            if (construction != null &&
                construction.workerClass?.FullName == "OberoniaAurea.IncidentWorker_UrbanConstruction")
            {
                if (HasCompatibleOberoniaFactionApi())
                    construction.workerClass = typeof(SafeOberoniaUrbanConstructionWorker);
                else
                    Log.WarningOnce(
                        "[StartupLog Compatibility] Kept the original Oberonia urban-construction worker because ModUtility.OAFaction does not match this build.",
                        GenText.StableStringHash("StartupLogFixes:Oberonia:SkippedConstructionWorker"));
            }
        }

        private static bool TryGetOberoniaFactionGetter(out MethodInfo getter)
        {
            getter = null;
            Type utilityType = AccessTools.TypeByName("OberoniaAurea.ModUtility");
            if (utilityType == null)
                return false;

            MethodInfo candidate = AccessTools.PropertyGetter(utilityType, "OAFaction");
            if (candidate == null || !candidate.IsStatic || candidate.ReturnType != typeof(Faction) ||
                candidate.GetParameters().Length != 0)
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Oberonia ModUtility.OAFaction has an incompatible signature; related compatibility workers were not replaced.",
                    GenText.StableStringHash("StartupLogFixes:Oberonia:UnknownFactionApi"));
                return false;
            }

            getter = candidate;
            return true;
        }

        private static bool HasCompatibleOberoniaFactionApi()
        {
            return TryGetOberoniaFactionGetter(out MethodInfo getter);
        }

        public static bool TryGetOberoniaFaction(out Faction faction)
        {
            faction = null;
            if (!TryGetOberoniaFactionGetter(out MethodInfo getter))
                return false;

            try
            {
                faction = getter.Invoke(null, null) as Faction;
                return true;
            }
            catch (TargetInvocationException exception)
            {
                string detail = exception.InnerException?.Message ?? exception.Message;
                Log.WarningOnce("[StartupLog Compatibility] Oberonia ModUtility.OAFaction threw while reading the current faction; related incident execution was skipped: " + detail,
                    GenText.StableStringHash("StartupLogFixes:Oberonia:FactionGetterThrew"));
                return false;
            }
            catch (ArgumentException exception)
            {
                Log.WarningOnce("[StartupLog Compatibility] Oberonia ModUtility.OAFaction could not be invoked with the validated signature; related incident execution was skipped: " + exception.Message,
                    GenText.StableStringHash("StartupLogFixes:Oberonia:FactionGetterInvoke"));
                return false;
            }
        }
    }

    internal sealed class SafeOberoniaDiplomaticOfficesWorker : IncidentWorker_GiveQuest
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!base.CanFireNowSub(parms))
                return false;

            if (!OberoniaIncidentCompatibility.TryGetOberoniaFaction(out Faction oaFaction))
                return false;

            return oaFaction?.leader != null && oaFaction.PlayerRelationKind == FactionRelationKind.Ally &&
                Find.FactionManager.AllFactionsVisible.Any(faction => faction != oaFaction &&
                    faction.PlayerRelationKind == FactionRelationKind.Hostile &&
                    faction.RelationKindWith(oaFaction) != FactionRelationKind.Hostile);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!OberoniaIncidentCompatibility.TryGetOberoniaFaction(out Faction oaFaction) ||
                oaFaction?.leader == null || def?.questScriptDef == null || Find.LetterStack == null)
                return false;

            TaggedString text = "OA_OptionalDiplomaticOffices".Translate(oaFaction.NameColored, oaFaction.leader);
            ChoiceLetter letter = LetterMaker.MakeLetter(def.letterLabel, text, def.letterDef, oaFaction);
            if (letter == null)
                return false;

            FieldInfo questScriptField = AccessTools.Field(letter.GetType(), "questScriptDef");
            if (questScriptField == null || questScriptField.IsStatic || questScriptField.FieldType != typeof(QuestScriptDef))
            {
                Log.WarningOnce(
                    "[StartupLog Compatibility] Skipped an Oberonia diplomatic-offices letter because its questScriptDef field does not match this build.",
                    GenText.StableStringHash("StartupLogFixes:Oberonia:UnknownLetterQuestField"));
                return false;
            }
            questScriptField.SetValue(letter, def.questScriptDef);
            if (!(letter is LetterWithTimeout timedLetter))
                return false;

            timedLetter.StartTimeout(30000);
            Find.LetterStack.ReceiveLetter(letter);
            return true;
        }

    }

    internal sealed class SafeOberoniaUrbanConstructionWorker : IncidentWorker_GiveQuest
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!base.CanFireNowSub(parms))
                return false;

            if (!OberoniaIncidentCompatibility.TryGetOberoniaFaction(out Faction oaFaction) ||
                oaFaction?.leader == null || parms?.target == null || Find.WorldObjects == null ||
                Find.WorldReachability == null || !parms.target.Tile.Valid)
                return false;

            RimWorld.Planet.PlanetLayer targetLayer = parms.target.Tile.Layer;
            foreach (RimWorld.Planet.Settlement settlement in Find.WorldObjects.SettlementBases)
            {
                if (settlement == null || settlement.Destroyed || settlement.Faction != oaFaction ||
                    !settlement.Visitable || !settlement.Tile.Valid || settlement.Tile.Layer != targetLayer)
                    continue;

                float distance = Find.WorldGrid.ApproxDistanceInTiles(parms.target.Tile, settlement.Tile);
                if (distance <= 24f && Find.WorldReachability.CanReach(parms.target.Tile, settlement.Tile))
                    return true;
            }

            return false;
        }

    }

    [StaticConstructorOnStartup]
    internal static class IncomingDamageOverflowHealthScale
    {
        private const string HarmonyId = "Local.StartupLog.CompatibilityFixes.IncomingDamageOverflow";
        internal const float DamageFloor = 0.12f;
        private const float MaximumHealthBonus = 1f;

        [ThreadStatic]
        private static bool calculatingBonus;

        static IncomingDamageOverflowHealthScale()
        {
            MethodInfo healthScaleGetter = AccessTools.PropertyGetter(typeof(Pawn), nameof(Pawn.HealthScale));
            if (healthScaleGetter == null)
            {
                Log.Error("[StartupLog Compatibility] Could not install the incoming-damage overflow health conversion because Pawn.HealthScale is unavailable.");
                return;
            }

            Harmony harmony = new Harmony(HarmonyId);
            MethodInfo postfix = AccessTools.Method(typeof(IncomingDamageOverflowHealthScale),
                nameof(HealthScalePostfix));
            if (postfix == null)
            {
                Log.Error("[StartupLog Compatibility] Could not install the incoming-damage overflow health conversion because its postfix method is unavailable.");
                return;
            }

            try
            {
                harmony.Patch(
                    healthScaleGetter,
                    postfix: new HarmonyMethod(postfix));

                HarmonyLib.Patches patchInfo = Harmony.GetPatchInfo(healthScaleGetter);
                bool installed = patchInfo != null && patchInfo.Postfixes.Any(patch =>
                    patch != null && patch.owner == HarmonyId && patch.PatchMethod == postfix);
                if (installed)
                {
                    Log.Message("[StartupLog Compatibility] Incoming-damage overflow health conversion is active: Pawn.HealthScale postfix registered (floor=12%, health cap=+100%).");
                }
                else
                {
                    Log.Error("[StartupLog Compatibility] Incoming-damage overflow health conversion failed its Harmony registration check.");
                }
            }
            catch (Exception exception)
            {
                Log.Error("[StartupLog Compatibility] Incoming-damage overflow health conversion could not be registered: " + exception);
            }
        }

        internal static void EnsureInstalled()
        {
            // Calling this method forces the type initializer to run while the
            // compatibility mod is being constructed.  The method body is
            // intentionally empty; installation is performed by the static
            // constructor above and remains idempotent for this assembly load.
        }

        public static void HealthScalePostfix(Pawn __instance, ref float __result)
        {
            float bonus = GetHealthBonus(__instance);
            if (bonus > 0f)
                __result *= 1f + bonus;
        }

        internal static float GetHealthBonus(Pawn pawn)
        {
            if (pawn == null || calculatingBonus)
                return 0f;

            StatDef incomingDamageFactor = StatDefOf.IncomingDamageFactor;
            if (incomingDamageFactor == null || incomingDamageFactor.Worker == null)
                return 0f;

            try
            {
                calculatingBonus = true;
                // HealthScale is read by pawn rendering and combat many times per tick.
                // Use RimWorld's bounded one-tick stat cache so the compatibility
                // conversion keeps the native invalidation path without recomputing
                // IncomingDamageFactor for every read.
                float unboundedDamageFactor = incomingDamageFactor.Worker.GetValue(
                    pawn, applyPostProcess: false, cacheStaleAfterTicks: 1);
                return Math.Max(0f, Math.Min(MaximumHealthBonus,
                    DamageFloor - unboundedDamageFactor));
            }
            finally
            {
                calculatingBonus = false;
            }
        }
    }
}
