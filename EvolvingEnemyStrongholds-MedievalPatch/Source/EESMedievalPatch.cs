using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using EvolvingEnemyStrongholds;
using HarmonyLib;
using RimWorld;
using RimWorld.BaseGen;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace EESMedievalPatch
{
    public sealed class EESMedievalPatchMod : Mod
    {
        internal static readonly string[] MedievalResearchGates =
        {
            "Smithing",
            "DankPyon_Engineering",
            "DankPyon_Mining",
            "DankPyon_Trebuchet",
            "DankPyon_Steel",
            "DankPyon_Plasteel"
        };

        public EESMedievalPatchMod(ModContentPack content) : base(content)
        {
            ReplaceResearchGates();
            RegisterMedievalTurretTiers();
            new Harmony("Local.EvolvingEnemyStrongholds.MedievalPatch").PatchAll(typeof(EESMedievalPatchMod).Assembly);
            LongEventHandler.ExecuteWhenFinished(LogMedievalCoverage);
        }

        private static void LogMedievalCoverage()
        {
            List<FactionDef> medieval = DefDatabase<FactionDef>.AllDefsListForReading
                .Where(def => !def.isPlayer && def.humanlikeFaction &&
                    (MedievalUtility.StrongholdTechLevel(def) == TechLevel.Medieval || def.techLevel == TechLevel.Neolithic))
                .ToList();
            Log.Message("[EES Medieval Patch] Faction coverage: eligible=" +
                string.Join(",", medieval.Where(def => !EESFactionBlacklist.IsBlocked(def)).Select(def => def.defName)) +
                "; excludedByUser=" + string.Join(",", medieval.Where(EESFactionBlacklist.IsBlocked).Select(def => def.defName)) +
                "; settlement objects only; custom world-object generators keep their EES exclusions.");
        }

        private static void ReplaceResearchGates()
        {
            string[] gates = AccessTools.Field(typeof(StrongholdScoreUtility), "EraGateDefs")?.GetValue(null) as string[];
            if (gates == null || gates.Length != MedievalResearchGates.Length)
            {
                Log.Error("[EES Medieval Patch] Could not replace the six stronghold research gates.");
                return;
            }

            Array.Copy(MedievalResearchGates, gates, gates.Length);
        }

        private static void RegisterMedievalTurretTiers()
        {
            IDictionary tiers = AccessTools.Field(typeof(StrongholdGarrisonUtility), "TurretTierOverride")?.GetValue(null) as IDictionary;
            if (tiers == null)
            {
                Log.Error("[EES Medieval Patch] Could not register medieval siege-engine tier windows.");
                return;
            }

            Dictionary<string, IntRange> medievalTiers = new Dictionary<string, IntRange>
            {
                { "DankPyon_Turret_Scorpio", new IntRange(2, 7) },
                { "VFEM2_Turret_WallMountedArbalest", new IntRange(2, 8) },
                { "DankPyon_Turret_Ballista", new IntRange(5, 16) },
                { "VFEM2_Turret_WallMountedArquebus", new IntRange(5, 16) },
                { "DankPyon_Turret_Trebuchet", new IntRange(8, 16) },
                { "DankPyon_Turret_RepeaterBallista", new IntRange(8, 16) }
            };
            foreach (KeyValuePair<string, IntRange> entry in medievalTiers)
            {
                tiers[entry.Key] = entry.Value;
            }
        }
    }

    public sealed class AstartesVaultRewardState : GameComponent
    {
        private HashSet<int> generatedSettlementIds = new HashSet<int>();

        public AstartesVaultRewardState(Game game)
        {
        }

        internal static AstartesVaultRewardState Current => Verse.Current.Game?.GetComponent<AstartesVaultRewardState>();

        internal bool TryMarkGenerated(int settlementId)
        {
            return generatedSettlementIds.Add(settlementId);
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref generatedSettlementIds, "astartesVaultRewardSettlementIds", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && generatedSettlementIds == null)
            {
                generatedSettlementIds = new HashSet<int>();
            }
        }
    }

    [HarmonyPatch]
    internal static class MedievalResearchProgressPatch
    {
        private static readonly FieldInfo EraGateDefsField =
            AccessTools.Field(typeof(StrongholdScoreUtility), "EraGateDefs");
        private static readonly FieldInfo EraGateValsField =
            AccessTools.Field(typeof(StrongholdScoreUtility), "EraGateVals");

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(StrongholdScoreUtility), "TechProgress01");
        }

        private static bool Prefix(ref float __result)
        {
            string[] gates = EraGateDefsField?.GetValue(null) as string[];
            float[] values = EraGateValsField?.GetValue(null) as float[];
            if (gates == null || values == null || gates.Length != EESMedievalPatchMod.MedievalResearchGates.Length ||
                values.Length < gates.Length || !gates.SequenceEqual(EESMedievalPatchMod.MedievalResearchGates))
            {
                return true;
            }

            __result = 0f;
            for (int i = 0; i < gates.Length; i++)
            {
                ResearchProjectDef research = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(gates[i]);
                if (research == null || !research.IsFinished)
                {
                    break;
                }
                __result = values[i];
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(StrongholdScoreUtility), nameof(StrongholdScoreUtility.ComputeTier))]
    internal static class MedievalTierEvaluationPatch
    {
        [ThreadStatic]
        internal static bool IsEvaluatingMedieval;

        private static void Prefix(Settlement settlement, out bool __state)
        {
            __state = IsEvaluatingMedieval;
            IsEvaluatingMedieval = MedievalUtility.IsMedieval(settlement?.Faction);
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            IsEvaluatingMedieval = __state;
            return __exception;
        }

        private static void Postfix(Settlement settlement, ref int __result)
        {
            if (!MedievalUtility.IsMedieval(settlement?.Faction) || __result < 13)
            {
                return;
            }

            float wealth = MedievalGrowthProgressPatch.HomeWealth;
            __result = wealth >= 3000000f ? 16 : wealth >= 2000000f ? 15 : wealth >= 1000000f ? 14 : 13;
        }
    }

    [HarmonyPatch(typeof(StrongholdScoreUtility), nameof(StrongholdScoreUtility.Progress01))]
    internal static class MedievalGrowthProgressPatch
    {
        private const float DaysWeight = 0.06f;
        private const float WealthWeight = 0.05f;
        private const float PopulationWeight = 0.04f;
        private const int PopulationFloor = 3;
        private const int PopulationCeiling = 18;
        private static readonly object ProgressCacheLock = new object();
        private static Game cachedGame;
        private static int cachedTick = -1;
        private static int cachedColonists;
        private static float cachedHomeWealth;
        private static bool cachedIncludesWealth;

        internal static float HomeWealth
        {
            get
            {
                RefreshProgressCache(true);
                return cachedHomeWealth;
            }
        }

        private static bool Prefix(ref float __result)
        {
            if (!MedievalTierEvaluationPatch.IsEvaluatingMedieval)
            {
                return true;
            }

            StrongholdSettings settings = StrongholdMod.Settings;
            RefreshProgressCache(settings == null || settings.usePlayerWealthScaling);
            int colonists = cachedColonists;
            float population = Math.Min(1f, Math.Max(0f,
                (colonists - PopulationFloor) / (float)(PopulationCeiling - PopulationFloor)));
            float research = settings == null || settings.usePlayerResearchScaling
                ? StrongholdScoreUtility.TechProgress01()
                : 0f;
            __result = Math.Min(1f,
                research +
                DaysComponent(settings) * DaysWeight +
                WealthComponent(settings) * WealthWeight +
                population * PopulationWeight);
            return false;
        }

        private static float DaysComponent(StrongholdSettings settings)
        {
            float growthRate = settings?.growthRate ?? 1f;
            return Math.Min(1f, Math.Max(0f, GenDate.DaysPassed * growthRate / 120f));
        }

        private static float WealthComponent(StrongholdSettings settings)
        {
            if (settings != null && !settings.usePlayerWealthScaling)
            {
                return 0f;
            }

            return Math.Min(1f, Math.Max(0f, cachedHomeWealth / 200000f));
        }

        private static void RefreshProgressCache(bool includeWealth)
        {
            int tick = Find.TickManager?.TicksGame ?? -1;
            Game game = Verse.Current.Game;
            if (ReferenceEquals(cachedGame, game) && cachedTick == tick &&
                cachedIncludesWealth == includeWealth)
            {
                return;
            }

            lock (ProgressCacheLock)
            {
                if (ReferenceEquals(cachedGame, game) && cachedTick == tick &&
                    cachedIncludesWealth == includeWealth)
                {
                    return;
                }

                cachedColonists =
                    PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoLodgers.Count;
                cachedHomeWealth = includeWealth
                    ? Find.Maps.Where(map => map.IsPlayerHome)
                        .Select(map => map.wealthWatcher.WealthTotal)
                        .DefaultIfEmpty(0f)
                        .Max()
                    : 0f;
                cachedGame = game;
                cachedIncludesWealth = includeWealth;
                cachedTick = tick;
            }
        }
    }

    internal static class SettlementCultureRegistry
    {
        internal sealed class CultureProfile
        {
            internal readonly string FactionDefName;
            internal readonly IntVec2[] TownSizes;
            internal readonly bool PreferWaterfront;

            internal CultureProfile(string factionDefName, IntVec2[] townSizes, bool preferWaterfront)
            {
                FactionDefName = factionDefName;
                TownSizes = townSizes;
                PreferWaterfront = preferWaterfront;
            }
        }

        private static readonly Dictionary<string, string> FactionByTemplate =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "EESMK_France_BastideTownV3", "BA_France_NobleHouse" },
                { "EESMK_France_RoyalBoroughTownV3", "BA_France_NobleHouse" },
                { "EESMK_France_ChateauMarketTownV3", "BA_France_NobleHouse" },
                { "EESMK_France_CathedralCloseTownV3", "BA_France_NobleHouse" },
                { "EESMK_HRE_FreeCityTownV2", "BA_HRE_NobleHouse" },
                { "EESMK_HRE_PrinceBishopTownV2", "BA_HRE_NobleHouse" },
                { "EESMK_HRE_ImperialBurgTownV2", "BA_HRE_NobleHouse" },
                { "EESMK_HRE_HanseaticGateTownV2", "BA_HRE_NobleHouse" },
                { "EESMK_Poland_RiverGrodTownV2", "BA_Poland_NobleHouse" },
                { "EESMK_Poland_BrickCastleTownV2", "BA_Poland_NobleHouse" },
                { "EESMK_Poland_RoyalGrodTownV2", "BA_Poland_NobleHouse" },
                { "EESMK_Poland_BorderStarostyTownV2", "BA_Poland_NobleHouse" },
                { "EESMK_Byzantium_ProvincialCitadelTownV2", "BA_Byzantium_NobleHouse" },
                { "EESMK_Byzantium_MonasteryWardTownV2", "BA_Byzantium_NobleHouse" },
                { "EESMK_Byzantium_ThemeFortTownV2", "BA_Byzantium_NobleHouse" },
                { "EESMK_Byzantium_PalaceQuarterTownV2", "BA_Byzantium_NobleHouse" },
                { "EESMK_Turkic_CaravanseraiTownV2", "BA_Turkic_NobleHouse" },
                { "EESMK_Turkic_FrontierHisarTownV2", "BA_Turkic_NobleHouse" },
                { "EESMK_Turkic_AnatolianHisarTownV2", "BA_Turkic_NobleHouse" },
                { "EESMK_Turkic_BazaarHisarTownV2", "BA_Turkic_NobleHouse" },
                { "EESMK_Moor_MedinaTownV2", "BA_Moor_NobleHouse" },
                { "EESMK_Moor_AlcazabaTownV2", "BA_Moor_NobleHouse" },
                { "EESMK_Moor_RibatTownV2", "BA_Moor_NobleHouse" },
                { "EESMK_Moor_KasbahQuarterTownV2", "BA_Moor_NobleHouse" },
                { "EESMK_Portugal_HillFortTownV2", "BA_Portugal_NobleHouse" },
                { "EESMK_Portugal_CastleMarketTownV2", "BA_Portugal_NobleHouse" },
                { "EESMK_Portugal_TemplarKeepTownV2", "BA_Portugal_NobleHouse" },
                { "EESMK_Portugal_BorderCastleTownV2", "BA_Portugal_NobleHouse" },
                { "EESMK_Spain_FrontierAlcazarTownV2", "BA_Spain_NobleHouse" },
                { "EESMK_Spain_PilgrimTownV2", "BA_Spain_NobleHouse" },
                { "EESMK_Spain_CrownAlcazarTownV2", "BA_Spain_NobleHouse" },
                { "EESMK_Spain_PlazaFortTownV2", "BA_Spain_NobleHouse" },
                { "EESMK_Mongol_KhanOrduTownV2", "BA_Mongol_NobleHouse" },
                { "EESMK_Mongol_CaravanCampTownV2", "BA_Mongol_NobleHouse" },
                { "EESMK_Mongol_WinterOrduTownV2", "BA_Mongol_NobleHouse" },
                { "EESMK_Mongol_RiverOrduTownV2", "BA_Mongol_NobleHouse" }
            };

        private static readonly Dictionary<string, HashSet<string>> NativeFactionsByTemplate =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                { "BA_HRETown", new HashSet<string> { "BA_France_NobleHouse", "BA_HRE_NobleHouse" } },
                { "BA_PolishTown", new HashSet<string> { "BA_Poland_NobleHouse" } },
                { "BA_ByzantiumTown", new HashSet<string> { "BA_Byzantium_NobleHouse" } },
                { "EESMK_Byzantium_AccessibleConstantinopleTown", new HashSet<string> { "BA_Byzantium_NobleHouse" } },
                { "BA_TurkishTown", new HashSet<string> { "BA_Turkic_NobleHouse" } },
                { "BA_IberiaTown", new HashSet<string> { "BA_Moor_NobleHouse", "BA_Portugal_NobleHouse", "BA_Spain_NobleHouse" } },
                { "BA_MongolTown", new HashSet<string> { "BA_Mongol_NobleHouse" } }
            };

        private static readonly Dictionary<string, IntVec2> NativeFootprintByTemplate =
            new Dictionary<string, IntVec2>(StringComparer.Ordinal)
            {
                { "BA_HRETown", new IntVec2(141, 136) },
                { "BA_PolishTown", new IntVec2(66, 56) },
                { "BA_ByzantiumTown", new IntVec2(139, 88) },
                { "EESMK_Byzantium_AccessibleConstantinopleTown", new IntVec2(139, 88) },
                { "BA_TurkishTown", new IntVec2(63, 63) },
                { "BA_IberiaTown", new IntVec2(44, 52) },
                { "BA_MongolTown", new IntVec2(43, 48) }
            };

        private static readonly Dictionary<string, CultureProfile> ProfileByFaction =
            new Dictionary<string, CultureProfile>(StringComparer.Ordinal)
            {
                {
                    "BA_France_NobleHouse",
                    new CultureProfile("BA_France_NobleHouse", new[]
                    {
                        new IntVec2(100, 100),
                        new IntVec2(88, 88),
                        new IntVec2(76, 76)
                    }, true)
                },
                {
                    "BA_HRE_NobleHouse",
                    new CultureProfile("BA_HRE_NobleHouse", new[]
                    {
                        new IntVec2(100, 100),
                        new IntVec2(88, 88),
                        new IntVec2(76, 76)
                    }, false)
                },
                {
                    "BA_Poland_NobleHouse",
                    new CultureProfile("BA_Poland_NobleHouse", new[]
                    {
                        new IntVec2(100, 100),
                        new IntVec2(88, 88),
                        new IntVec2(76, 76)
                    }, false)
                },
                {
                    "BA_Byzantium_NobleHouse",
                    new CultureProfile("BA_Byzantium_NobleHouse", new[]
                    {
                        new IntVec2(100, 100),
                        new IntVec2(88, 88),
                        new IntVec2(76, 76)
                    }, false)
                },
                {
                    "BA_Turkic_NobleHouse",
                    new CultureProfile("BA_Turkic_NobleHouse", new[]
                    {
                        new IntVec2(100, 100),
                        new IntVec2(88, 88),
                        new IntVec2(76, 76)
                    }, false)
                },
                {
                    "BA_Moor_NobleHouse",
                    new CultureProfile("BA_Moor_NobleHouse", new[]
                    {
                        new IntVec2(100, 100),
                        new IntVec2(88, 88),
                        new IntVec2(76, 76)
                    }, false)
                },
                {
                    "BA_Portugal_NobleHouse",
                    new CultureProfile("BA_Portugal_NobleHouse", new[]
                    {
                        new IntVec2(100, 100),
                        new IntVec2(88, 88),
                        new IntVec2(76, 76)
                    }, false)
                },
                {
                    "BA_Spain_NobleHouse",
                    new CultureProfile("BA_Spain_NobleHouse", new[]
                    {
                        new IntVec2(100, 100),
                        new IntVec2(88, 88),
                        new IntVec2(76, 76)
                    }, false)
                },
                {
                    "BA_Mongol_NobleHouse",
                    new CultureProfile("BA_Mongol_NobleHouse", new[]
                    {
                        new IntVec2(100, 100),
                        new IntVec2(88, 88),
                        new IntVec2(76, 76)
                    }, false)
                }
            };

        internal static bool IsTemplateForFaction(string templateDefName, string factionDefName)
        {
            if (string.IsNullOrEmpty(templateDefName) || string.IsNullOrEmpty(factionDefName))
            {
                return false;
            }
            return (FactionByTemplate.TryGetValue(templateDefName, out string registeredFaction) &&
                    registeredFaction == factionDefName) ||
                (NativeFactionsByTemplate.TryGetValue(templateDefName, out HashSet<string> nativeFactions) &&
                    nativeFactions.Contains(factionDefName));
        }

        internal static bool TryGetNativeFootprint(string templateDefName, string factionDefName, out IntVec2 size)
        {
            size = IntVec2.Zero;
            return IsTemplateForFaction(templateDefName, factionDefName) &&
                NativeFootprintByTemplate.TryGetValue(templateDefName, out size);
        }

        internal static bool IsRegisteredFaction(string factionDefName)
        {
            return !string.IsNullOrEmpty(factionDefName) && ProfileByFaction.ContainsKey(factionDefName);
        }

        internal static bool TryGetProfile(string templateDefName, string factionDefName, out CultureProfile profile)
        {
            profile = null;
            return IsTemplateForFaction(templateDefName, factionDefName) &&
                ProfileByFaction.TryGetValue(factionDefName, out profile);
        }
    }

    internal sealed class MedievalAcceptanceRunner : GameComponent
    {
        private static readonly (string Faction, string Native, string Custom)[] Cases =
        {
            ("BA_France_NobleHouse", "BA_HRETown", "EESMK_France_BastideTownV3"),
            ("BA_HRE_NobleHouse", "BA_HRETown", "EESMK_HRE_FreeCityTownV2"),
            ("BA_Poland_NobleHouse", "BA_PolishTown", "EESMK_Poland_RiverGrodTownV2"),
            ("BA_Byzantium_NobleHouse", "BA_ByzantiumTown", "EESMK_Byzantium_ProvincialCitadelTownV2"),
            ("BA_Turkic_NobleHouse", "BA_TurkishTown", "EESMK_Turkic_CaravanseraiTownV2"),
            ("BA_Moor_NobleHouse", "BA_IberiaTown", "EESMK_Moor_MedinaTownV2"),
            ("BA_Portugal_NobleHouse", "BA_IberiaTown", "EESMK_Portugal_HillFortTownV2"),
            ("BA_Spain_NobleHouse", "BA_IberiaTown", "EESMK_Spain_PlazaFortTownV2"),
            ("BA_Mongol_NobleHouse", "BA_MongolTown", "EESMK_Mongol_KhanOrduTownV2")
        };

        private bool queued;

        public MedievalAcceptanceRunner(Game game)
        {
        }

        public override void StartedNewGame()
        {
            if (queued || !GenCommandLine.CommandLineArgPassed("eesmkacceptance"))
            {
                return;
            }

            queued = true;
            LongEventHandler.QueueLongEvent(Run, "GeneratingMap", false, exception =>
                Log.Error($"[EESMK Acceptance] ABORT: {exception}"), false, false, null);
        }

        private static void Run()
        {
            int passed = 0;
            int failed = 0;
            Log.Message($"[EESMK Acceptance] START cases={Cases.Length * 2}.");
            foreach ((string factionDefName, string native, string custom) in Cases)
            {
                foreach ((string kind, string template) in new[] { ("native", native), ("custom", custom) })
                {
                    if (RunCase(factionDefName, kind, template))
                    {
                        passed++;
                    }
                    else
                    {
                        failed++;
                    }
                }
            }
            Log.Message($"[EESMK Acceptance] COMPLETE passed={passed}, failed={failed}, total={passed + failed}.");
        }

        private static bool RunCase(string factionDefName, string kind, string templateDefName)
        {
            Faction faction = Find.FactionManager.AllFactions.FirstOrDefault(item => item.def.defName == factionDefName);
            if (faction == null)
            {
                Log.Error($"[EESMK Acceptance] FAIL faction={factionDefName}, kind={kind}, template={templateDefName}, reason=faction-missing.");
                return false;
            }

            PlanetTile tile = TileFinder.RandomSettlementTileFor(faction, false,
                candidate => !Find.WorldObjects.AnyWorldObjectAt(candidate));
            Settlement settlement = (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            settlement.SetFaction(faction);
            settlement.Tile = tile;
            Find.WorldObjects.Add(settlement);
            Map map = null;
            try
            {
                AcceptanceCandidateOverride.TemplateDefName = templateDefName;
                map = GetOrGenerateMapUtility.GetOrGenerateMap(tile, settlement.def, null);
                int buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                    .Count(building => building.Faction == faction);
                int waterCells = map.AllCells.Count(cell => cell.GetTerrain(map)?.IsWater == true);
                int mountainCells = map.AllCells.Count(cell => cell.GetRoof(map) == RoofDefOf.RoofRockThick);
                MapComponent_StrongholdSiege siege = map.GetComponent<MapComponent_StrongholdSiege>();
                StrongholdData acceptanceData = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
                bool validVault = siege != null && MedievalUtility.HasValidVaultEnclosure(map, siege.eesVaultRect);
                bool result = buildings > 0 && siege != null && siege.eesBaseRect.Width > 0 && validVault;
                Log.Message($"[EESMK Acceptance] {(result ? "PASS" : "FAIL")} faction={factionDefName}, kind={kind}, " +
                    $"template={templateDefName}, tile={tile}, buildings={buildings}, waterCells={waterCells}, " +
                    $"mountainCells={mountainCells}, baseRect={siege?.eesBaseRect ?? CellRect.Empty}, " +
                    $"vaultRect={siege?.eesVaultRect ?? CellRect.Empty}, validVault={validVault}.");
                HashSet<string> namedDefenseDefs = new HashSet<string>(StringComparer.Ordinal)
                {
                    "DankPyon_Turret_Scorpio", "VFEM2_Turret_WallMountedArbalest", "DankPyon_Turret_Ballista",
                    "VFEM2_Turret_WallMountedArquebus", "DankPyon_Turret_Trebuchet", "DankPyon_Turret_RepeaterBallista",
                    "TrapSpike", "Barricade", "VFEM2_Palisade", "VFEM2_CastleWall", "DankPyon_CastleWall"
                };
                int defenseBuildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                    .Count(building => building.Faction == faction &&
                        (namedDefenseDefs.Contains(building.def?.defName) || building.def?.IsDoor == true ||
                         building.def?.passability == Traversability.Impassable));
                int factionPawns = map.mapPawns.AllPawnsSpawned.Count(pawn => pawn.Faction == faction && !pawn.Dead);
                int armedPawns = map.mapPawns.AllPawnsSpawned.Count(pawn => pawn.Faction == faction && !pawn.Dead &&
                    pawn.equipment?.Primary != null);
                int namedSiegeEngines = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                    .Count(building => building.Faction == faction &&
                        (building.def?.defName == "DankPyon_Turret_Ballista" ||
                         building.def?.defName == "DankPyon_Turret_RepeaterBallista" ||
                         building.def?.defName == "DankPyon_Turret_Trebuchet"));
                Log.Message($"[EESMK Defense] faction={factionDefName}, kind={kind}, template={templateDefName}, " +
                    $"tier=T{acceptanceData?.tier ?? 0}, factionPawns={factionPawns}, armedPawns={armedPawns}, " +
                    $"defenseBuildings={defenseBuildings}, siegeEngines={namedSiegeEngines}.");
                return result;
            }
            catch (Exception exception)
            {
                Log.Error($"[EESMK Acceptance] FAIL faction={factionDefName}, kind={kind}, template={templateDefName}: {exception}");
                return false;
            }
            finally
            {
                AcceptanceCandidateOverride.TemplateDefName = null;
                if (map != null)
                {
                    try
                    {
                        Verse.Current.Game.DeinitAndRemoveMap(map, true);
                    }
                    catch (NullReferenceException) when (!Verse.Current.Game.Maps.Contains(map))
                    {
                        Log.Warning("[EESMK Acceptance] Test map was removed before its uninitialized render drawer could be disposed.");
                    }
                }
                if (Find.WorldObjects.AllWorldObjects.Contains(settlement))
                {
                    Find.WorldObjects.Remove(settlement);
                }
            }
        }
    }

    [HarmonyPatch]
    internal static class AcceptanceCandidateOverride
    {
        internal static string TemplateDefName;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("KCSG.CustomGenOption"), "Generate");
        }

        private static void Prefix(object __instance, out object __state)
        {
            __state = null;
            if (string.IsNullOrEmpty(TemplateDefName))
            {
                return;
            }

            FieldInfo field = AccessTools.Field(__instance.GetType(), "chooseFromSettlements");
            object original = field?.GetValue(__instance);
            if (!(original is IList originalList))
            {
                throw new InvalidOperationException("KCSG candidate list is unavailable.");
            }

            object settlementDef = GenDefDatabase.GetDef(originalList.GetType().GetGenericArguments()[0], TemplateDefName, true);
            IList forced = (IList)Activator.CreateInstance(originalList.GetType());
            forced.Add(settlementDef);
            field.SetValue(__instance, forced);
            __state = original;
        }

        private static Exception Finalizer(Exception __exception, object __instance, object __state)
        {
            if (__state != null)
            {
                AccessTools.Field(__instance.GetType(), "chooseFromSettlements")?.SetValue(__instance, __state);
            }
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class MongolKcsgGenerationGuard
    {
        private sealed class GenerationState
        {
            internal FieldInfo SettlementsField;
            internal object SettlementsOriginal;
            internal FieldInfo LayoutsField;
            internal object LayoutsOriginal;
        }

        private sealed class ReplacementSelection
        {
            internal string TemplateName;
            internal string LayoutName;
        }

        private static readonly ConditionalWeakTable<Map, ReplacementSelection> ReplacementByMap =
            new ConditionalWeakTable<Map, ReplacementSelection>();
        private static readonly string[] ReplacementTemplates =
        {
            "EESMK_Mongol_KhanOrduTownV2",
            "EESMK_Mongol_CaravanCampTownV2",
            "EESMK_Mongol_WinterOrduTownV2",
            "EESMK_Mongol_RiverOrduTownV2"
        };

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("KCSG.CustomGenOption"), "Generate");
        }

        private static bool Prefix(Map map, object __instance, out object __state)
        {
            __state = null;
            if (!(map?.Parent is Settlement settlement) || !MedievalUtility.IsSupportedHostileSettlement(map))
            {
                return true;
            }

            string factionDefName = settlement.Faction?.def?.defName;
            var data = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
            if (factionDefName == "BA_Byzantium_NobleHouse" && data?.tier <= 10)
            {
                Log.Message("[EES Medieval Patch] Skipping incompatible Byzantine T" + data.tier + " KCSG settlement candidate; EES reduced center fallback owns the map.");
                return false;
            }
            if (factionDefName == "BA_Byzantium_NobleHouse" && MapGenerator.mapBeingGenerated == map &&
                StrongholdCenterPlanner.Eligible(map, data) && data.generationPlan?.legacyMap != true &&
                data.generationPlan?.centerBuilt != true && string.IsNullOrEmpty(AcceptanceCandidateOverride.TemplateDefName))
            {
                var field = AccessTools.Field(__instance.GetType(), "chooseFromSettlements");
                if (field?.GetValue(__instance) is IList original && original.Cast<object>().OfType<Def>().Any(d => d.defName == "BA_ByzantiumTown"))
                {
                    var element = original.GetType().GetGenericArguments().FirstOrDefault();
                    var replacement = element == null ? null : GenDefDatabase.GetDef(element, "EESMK_Byzantium_AccessibleConstantinopleTown", false);
                    if (replacement == null)
                        Log.Warning("[EES Medieval Patch] Accessible Constantinople Def unavailable; retaining the original native template and its known sealed-bedroom limitation.");
                    else
                    {
                        var selected = (IList)Activator.CreateInstance(original.GetType());
                        foreach (object candidate in original)
                            selected.Add(candidate is Def def && def.defName == "BA_ByzantiumTown" ? replacement : candidate);
                        field.SetValue(__instance, selected);
                        __state = new GenerationState { SettlementsField = field, SettlementsOriginal = original };
                        Log.Message("[EES Medieval Patch] New hostile EES Byzantine generation selects the native-content clone with two actual residential doors; original Def and candidate weights retained.");
                    }
                }
                return true;
            }
            if (factionDefName != "BA_Mongol_NobleHouse")
            {
                return true;
            }

            FieldInfo settlementsField = AccessTools.Field(__instance?.GetType(), "chooseFromSettlements");
            object settlementsValue = settlementsField?.GetValue(__instance);
            bool hasNativeCandidate = settlementsValue is IList settlementsList &&
                settlementsList.Cast<object>().OfType<Def>().Any(candidate => candidate.defName == "BA_MongolTown");
            ReplacementSelection selection;
            if (hasNativeCandidate)
            {
                if (!ReplacementByMap.TryGetValue(map, out selection))
                {
                    int selector = (Gen.HashCombineInt(settlement.ID, 731923) & int.MaxValue) % ReplacementTemplates.Length;
                    string templateName = ReplacementTemplates[selector];
                    selection = new ReplacementSelection
                    {
                        TemplateName = templateName,
                        LayoutName = templateName.Substring(0, templateName.Length - "TownV2".Length) + "LayoutV2"
                    };
                    ReplacementByMap.Add(map, selection);
                }
            }
            else if (!ReplacementByMap.TryGetValue(map, out selection))
            {
                return true;
            }

            if (hasNativeCandidate)
            {
                if (!(settlementsValue is IList settlementsOriginal)) return false;
                Type settlementType = settlementsOriginal.GetType().GetGenericArguments().FirstOrDefault();
                if (settlementType == null)
                {
                    Log.Warning("[EES Medieval Patch] Mongol native settlement list has no generic element type; falling back to EES overlay.");
                    return false;
                }
                object replacement = GenDefDatabase.GetDef(settlementType, selection.TemplateName, true);
                IList forcedSettlements = (IList)Activator.CreateInstance(settlementsOriginal.GetType());
                forcedSettlements.Add(replacement);
                settlementsField.SetValue(__instance, forcedSettlements);
                __state = new GenerationState
                {
                    SettlementsField = settlementsField,
                    SettlementsOriginal = settlementsOriginal
                };
                Log.Message("[EES Medieval Patch] Replaced broken Mongol native candidate BA_MongolTown with " + selection.TemplateName + ".");
            }

            FieldInfo layoutsField = AccessTools.Field(__instance.GetType(), "chooseFromlayouts");
            object layoutsValue = layoutsField?.GetValue(__instance);
            if (layoutsField != null && layoutsValue is IList layoutsOriginal)
            {
                Type layoutType = layoutsOriginal.GetType().GetGenericArguments().FirstOrDefault();
                if (layoutType == null)
                {
                    if (hasNativeCandidate)
                    {
                        Log.Warning("[EES Medieval Patch] Mongol native layout list has no generic element type; falling back to EES overlay.");
                        return false;
                    }
                }
                else
                {
                    object layoutReplacement = GenDefDatabase.GetDef(layoutType, selection.LayoutName, true);
                    IList forcedLayouts = (IList)Activator.CreateInstance(layoutsOriginal.GetType());
                    forcedLayouts.Add(layoutReplacement);
                    layoutsField.SetValue(__instance, forcedLayouts);
                    var state = __state as GenerationState ?? new GenerationState();
                    state.LayoutsField = layoutsField;
                    state.LayoutsOriginal = layoutsOriginal;
                    __state = state;
                }
            }
            else if (hasNativeCandidate)
            {
                Log.Warning("[EES Medieval Patch] Mongol native candidate has no paired layout list; falling back to EES overlay.");
                return false;
            }
            return true;
        }

        private static Exception Finalizer(Exception __exception, object __instance, object __state)
        {
            if (__state is GenerationState state)
            {
                state.SettlementsField?.SetValue(__instance, state.SettlementsOriginal);
                state.LayoutsField?.SetValue(__instance, state.LayoutsOriginal);
            }
            return __exception;
        }
    }

    internal static partial class MedievalUtility
    {
        internal const string MedievalOverhaulPackageId = "dankpyon.medieval.overhaul";
        internal const string VfeMedieval2PackageId = "oskarpotocki.vfe.medieval2";
        internal const string MedievalKingdomsPackageIdPrefix = "athelas.medievalkingdoms.";
        private static readonly ConditionalWeakTable<Map, FranceGenerationDiagnostics> FranceDiagnostics =
            new ConditionalWeakTable<Map, FranceGenerationDiagnostics>();

        private sealed class FranceGenerationDiagnostics
        {
            internal string FactionDefName = "unknown";
            internal string TemplateDefName = "unknown";
            internal CellRect OriginalRect = CellRect.Empty;
            internal CellRect SelectedRect = CellRect.Empty;
            internal Rot4 HarborSide = Rot4.Invalid;
            internal bool RiverHarbor;
            internal int SelectionSizeIndex = -1;
            internal int QuayTiles;
            internal int PierTiles;
            internal int CrossRiverBridgeTiles;
            internal int ClearedWallBlockers;
            internal int PlannedWallCells;
            internal int BuiltWallCells;
            internal int GateCount;
            internal int ClearedIndoorBlockers;
            internal int Tier;
            internal bool Logged;
        }

        internal static readonly HashSet<string> VfeTurretDefNames = new HashSet<string>
        {
            "VFEM2_Turret_WallMountedArbalest",
            "VFEM2_Turret_WallMountedArquebus"
        };
        internal static readonly HashSet<string> MedievalKingdomsNpcFactionDefNames = new HashSet<string>
        {
            "BA_Turkic_NobleHouse",
            "BA_Poland_NobleHouse",
            "BA_HRE_NobleHouse",
            "BA_Byzantium_NobleHouse",
            "BA_France_NobleHouse",
            "BA_Spain_NobleHouse",
            "BA_Portugal_NobleHouse",
            "BA_Moor_NobleHouse",
            "BA_Mongol_NobleHouse",
            "BA_Takeda_NobleHouse",
            "BA_Tokugawa_NobleHouse",
            "BA_Uesugi_NobleHouse",
            "BA_Mori_NobleHouse"
        };
        private static readonly Dictionary<string, string[]> MedievalKingdomsVaultGear = new Dictionary<string, string[]>
        {
            { "BA_Turkic_NobleHouse", new[] { "TA_SingleEdgeSword", "TA_CompositeCrossbow", "TA_SingleEdgeSword", "TA_CloakClosed" } },
            { "BA_Poland_NobleHouse", new[] { "TA_ArmingSword", "TA_CompositeCrossbow", "TA_Greathelm", "TA_Greathelm" } },
            { "BA_HRE_NobleHouse", new[] { "TA_Pollaxe", "TA_ArmingSword", "TA_PlateCuirass", "TA_Greathelm" } },
            { "BA_Byzantium_NobleHouse", new[] { "TA_ArmingSword", "TA_CompositeCrossbow", "TA_ReinforcedGambeson", "BA_BannerSpear_Byzantine" } },
            { "BA_France_NobleHouse", new[] { "TA_ArmingSword", "TA_Spear", "TA_PlateCuirass", "TA_Spear" } },
            { "BA_Spain_NobleHouse", new[] { "TA_ArmingSword", "TA_Spear", "TA_CompositeCrossbow", "TA_PlateCuirass" } },
            { "BA_Portugal_NobleHouse", new[] { "TA_ArmingSword", "TA_CompositeCrossbow", "TA_CloakClosed", "BA_BannerSpear_Portugal" } },
            { "BA_Moor_NobleHouse", new[] { "TA_SingleEdgeSword", "TA_CompositeCrossbow", "TA_CloakClosed", "TA_Headscarf" } },
            { "BA_Mongol_NobleHouse", new[] { "TA_SingleEdgeSword", "TA_CompositeCrossbow", "TA_LamellarVestMetal", "TA_NasalHelm" } },
            { "BA_Takeda_NobleHouse", new[] { "TA_SingleEdgeSword", "TA_Spear", "TA_CompositeCrossbow", "TA_Pollaxe" } },
            { "BA_Tokugawa_NobleHouse", new[] { "TA_SingleEdgeSword", "TA_Spear", "TA_CompositeCrossbow", "TA_Pollaxe" } },
            { "BA_Uesugi_NobleHouse", new[] { "TA_SingleEdgeSword", "TA_Spear", "TA_CompositeCrossbow", "TA_Pollaxe" } },
            { "BA_Mori_NobleHouse", new[] { "TA_SingleEdgeSword", "TA_Spear", "TA_CompositeCrossbow", "TA_Pollaxe" } }
        };
        private static readonly string[] MkViiArmor =
        {
            "BEWH_MkVIIPowerArmor",
            "BEWH_MkVIIPowerArmorHelmet"
        };
        private static readonly string[] MkXArmor =
        {
            "BEWH_MkXPowerArmor",
            "BEWH_MkXPowerArmorHelmet"
        };
        private static readonly string[] MkXChaplainArmor =
        {
            "BEWH_MkXPowerArmorChaplain",
            "BEWH_MkXPowerArmorChaplainHelmet"
        };
        private static readonly string[] AstartesMelee =
        {
            "BEWH_AstartesChainsword",
            "BEWH_AstartesPowerSword"
        };

        internal static bool IsMedieval(Faction faction)
        {
            return faction?.def != null && faction.def.humanlikeFaction &&
                (StrongholdTechLevel(faction.def) == TechLevel.Medieval || faction.def.techLevel == TechLevel.Neolithic);
        }

        internal static TechLevel StrongholdTechLevel(FactionDef def)
        {
            // Ratkin Kingdom is tagged Industrial by its source mod; only EES uses this override.
            return def?.defName == "Rakinia" ? TechLevel.Medieval : def?.techLevel ?? TechLevel.Undefined;
        }

        internal static TechLevel StrongholdGrowthTechLevel(FactionDef def)
        {
            TechLevel tech = StrongholdTechLevel(def);
            return tech == TechLevel.Neolithic ? TechLevel.Medieval : tech;
        }

        internal static bool UsesOpenPerimeter(Faction faction)
        {
            FactionDef def = faction?.def;
            return IsMedieval(faction) && (def.techLevel == TechLevel.Neolithic ||
                string.Equals(def.categoryTag, "Tribal", StringComparison.OrdinalIgnoreCase) ||
                def.categoryTag == "VFEM2_Vikings" || IsVfeClan(faction));
        }

        internal static bool SkipWallTowerForOpenPerimeter(Faction faction, ThingDef def)
        {
            return UsesOpenPerimeter(faction) && def?.defName?.StartsWith("JDS_", StringComparison.Ordinal) == true &&
                def.defName.EndsWith("_Defense_Tower", StringComparison.Ordinal);
        }

        internal static bool IsVfeMedieval2(Faction faction)
        {
            return faction?.def?.modContentPack?.PackageId?.ToLowerInvariant() == VfeMedieval2PackageId;
        }

        internal static bool IsVfeKingdom(Faction faction)
        {
            if (!IsVfeMedieval2(faction))
            {
                return false;
            }
            string defName = faction.def.defName;
            return defName == "VFEM2_KingdomCivil" || defName == "VFEM2_KingdomRough" ||
                defName == "VFEM2_KingdomSavage";
        }

        internal static bool IsVfeClan(Faction faction)
        {
            if (!IsVfeMedieval2(faction))
            {
                return false;
            }
            string defName = faction.def.defName;
            return defName == "VFEM2_ClanRough" || defName == "VFEM2_ClanSavage" ||
                defName == "VFEM2_CivilClan";
        }

        internal static bool IsVfeEnemySettlement(Faction faction)
        {
            return IsVfeKingdom(faction) || IsVfeClan(faction);
        }

        internal static bool IsMedievalKingdomsNpcFaction(Faction faction)
        {
            FactionDef def = faction?.def;
            string packageId = def?.modContentPack?.PackageId;
            return IsMedieval(faction) &&
                packageId != null && packageId.StartsWith(MedievalKingdomsPackageIdPrefix, StringComparison.OrdinalIgnoreCase) &&
                MedievalKingdomsNpcFactionDefNames.Contains(def.defName);
        }

        internal static bool NeedsWholeMapSettlementCleanup(Faction faction)
        {
            return IsEligibleMedievalFaction(faction) && IsMedievalKingdomsNpcFaction(faction);
        }

        internal static bool IsEligibleMedievalFaction(Faction faction)
        {
            return IsMedieval(faction) && !faction.IsPlayer && !EESFactionBlacklist.IsBlocked(faction);
        }

        internal static bool IsFrance(Faction faction)
        {
            return IsMedievalKingdomsNpcFaction(faction) && faction.def.defName == "BA_France_NobleHouse";
        }

        internal static bool IsFranceTownTemplate(string templateDefName)
        {
            return SettlementCultureRegistry.IsTemplateForFaction(templateDefName, "BA_France_NobleHouse");
        }

        internal static void RecordRegisteredTemplate(Map map, string templateDefName)
        {
            string factionDefName = map?.ParentFaction?.def?.defName;
            if (SettlementCultureRegistry.TryGetProfile(templateDefName, factionDefName, out _))
            {
                FranceGenerationDiagnostics diagnostics = FranceDiagnostics.GetValue(map, _ => new FranceGenerationDiagnostics());
                diagnostics.FactionDefName = factionDefName;
                diagnostics.TemplateDefName = templateDefName;
            }
        }

        private static void RecordFranceWallBlockers(Map map, int count)
        {
            if (count > 0 && map != null && FranceDiagnostics.TryGetValue(map, out FranceGenerationDiagnostics diagnostics))
            {
                diagnostics.ClearedWallBlockers += count;
            }
        }

        private static void RecordFranceCrossRiverBridgeTiles(Map map, int count)
        {
            if (count > 0 && IsFrance(map?.ParentFaction))
            {
                FranceDiagnostics.GetValue(map, _ => new FranceGenerationDiagnostics()).CrossRiverBridgeTiles += count;
            }
        }

        internal static bool TryPrepareRegisteredTownSite(Map map, string templateDefName, ref ResolveParams rp)
        {
            string factionDefName = map?.ParentFaction?.def?.defName;
            if (!SettlementCultureRegistry.TryGetProfile(templateDefName, factionDefName,
                out SettlementCultureRegistry.CultureProfile profile))
            {
                return false;
            }

            FranceGenerationDiagnostics diagnostics = FranceDiagnostics.GetValue(map, _ => new FranceGenerationDiagnostics());
            diagnostics.FactionDefName = factionDefName;
            diagnostics.TemplateDefName = templateDefName;
            diagnostics.OriginalRect = rp.rect;

            CellRect riverProbe = (rp.rect.Width > 0 ? rp.rect : CellRect.CenteredOn(map.Center, 100))
                .ExpandedBy(30).ClipInsideMap(map);
            bool riverMap = HasRiverCrossing(map, riverProbe);
            CellRect bestRect = CellRect.Empty;
            Rot4 bestSide = Rot4.Invalid;
            int bestScore = int.MaxValue;
            int selectedSizeIndex = -1;
            IntVec3 preferred = rp.rect.Width > 0 ? rp.rect.CenterCell : map.Center;

            IntVec2[] selectionSizes = SettlementCultureRegistry.TryGetNativeFootprint(templateDefName, factionDefName,
                out IntVec2 nativeSize) ? new[] { nativeSize } : profile.TownSizes;
            for (int sizeIndex = 0; sizeIndex < selectionSizes.Length && bestRect.Width == 0; sizeIndex++)
            {
                IntVec2 size = selectionSizes[sizeIndex];
                int minCenterX = 15 + size.x / 2;
                int maxCenterX = map.Size.x - 16 - (size.x - 1) / 2;
                int minCenterZ = 15 + size.z / 2;
                int maxCenterZ = map.Size.z - 16 - (size.z - 1) / 2;
                for (int z = minCenterZ; z <= maxCenterZ; z += 3)
                {
                    for (int x = minCenterX; x <= maxCenterX; x += 3)
                    {
                        CellRect candidate = CellRect.CenteredOn(new IntVec3(x, 0, z), size.x, size.z);
                        if (!IsBuildableTownRect(map, candidate))
                        {
                            continue;
                        }

                        Rot4 side = FindHarborSide(map, candidate, out int riverCells);
                        if (profile.PreferWaterfront && riverMap && riverCells < 12)
                        {
                            continue;
                        }
                        int centerDistance = Math.Abs(x - preferred.x) + Math.Abs(z - preferred.z);
                        int score = centerDistance * 4 + (profile.PreferWaterfront ? -riverCells * 3 : riverCells * 3);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestRect = candidate;
                            bestSide = side;
                            selectedSizeIndex = sizeIndex;
                        }
                    }
                }
            }

            if (bestRect.Width == 0)
            {
                IntVec2 fallbackSize = selectionSizes[selectionSizes.Length - 1];
                foreach (IntVec3 center in CellRect.CenteredOn(preferred, 100).ClipInsideMap(map).Cells
                    .OrderBy(cell => Math.Abs(cell.x - preferred.x) + Math.Abs(cell.z - preferred.z)))
                {
                    CellRect candidate = CellRect.CenteredOn(center, fallbackSize.x, fallbackSize.z);
                    if (candidate == candidate.ClipInsideMap(map) && IsBuildableTownRect(map, candidate))
                    {
                        bestRect = candidate;
                        bestSide = FindHarborSide(map, candidate, out _);
                        selectedSizeIndex = profile.TownSizes.Length - 1;
                        break;
                    }
                }
            }

            if (bestRect.Width == 0)
            {
                Log.Warning($"[EES Medieval Patch] Registered settlement could not find a dry town footprint for faction={factionDefName}, template={templateDefName}; KCSG will use its original placement rules.");
                return false;
            }

            rp.rect = bestRect;
            diagnostics.SelectedRect = bestRect;
            diagnostics.SelectionSizeIndex = selectedSizeIndex;
            diagnostics.HarborSide = bestSide;
            diagnostics.RiverHarbor = profile.PreferWaterfront && bestSide.IsValid &&
                CountWaterOutsideSide(map, bestRect, bestSide, 8) >= 12;
            Log.Message($"[EES Medieval Patch] Registered settlement site: faction={factionDefName}, template={templateDefName}, originalRect={diagnostics.OriginalRect}, " +
                $"selectedRect={bestRect}, riverHarbor={diagnostics.RiverHarbor}, harborSide={SideName(bestSide)}, fallbackIndex={selectedSizeIndex}.");
            return true;
        }

        internal static void CalibrateRegisteredTownRect(Map map, string templateDefName, HashSet<int> existingBuildingIds)
        {
            string factionDefName = map?.ParentFaction?.def?.defName;
            if (!SettlementCultureRegistry.IsTemplateForFaction(templateDefName, factionDefName) ||
                !FranceDiagnostics.TryGetValue(map, out FranceGenerationDiagnostics diagnostics))
            {
                return;
            }

            List<Thing> generated = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                .Where(thing => !thing.Destroyed && thing.Faction == map.ParentFaction &&
                    (existingBuildingIds == null || !existingBuildingIds.Contains(thing.thingIDNumber)))
                .ToList();
            if (generated.Count == 0)
            {
                Log.Warning($"[EES Medieval Patch] Registered settlement generated no faction buildings for faction={factionDefName}, template={templateDefName}; retaining selectedRect={diagnostics.SelectedRect}.");
                return;
            }

            int minX = generated.Min(thing => thing.OccupiedRect().minX);
            int minZ = generated.Min(thing => thing.OccupiedRect().minZ);
            int maxX = generated.Max(thing => thing.OccupiedRect().maxX);
            int maxZ = generated.Max(thing => thing.OccupiedRect().maxZ);
            CellRect actual = new CellRect(minX, minZ, maxX - minX + 1, maxZ - minZ + 1)
                .ExpandedBy(2).ClipInsideMap(map);
            diagnostics.SelectedRect = actual;
            diagnostics.HarborSide = FindHarborSide(map, actual, out _);
            diagnostics.RiverHarbor = HasRiverCrossing(map, actual);
            Log.Message($"[EES Medieval Patch] Calibrated registered town footprint: faction={factionDefName}, template={templateDefName}, generatedBuildings={generated.Count}, actualRect={actual}.");
            if (map.Parent is Settlement settlement)
                StrongholdCenterPlanner.ReserveAfterNativeTown(map,
                    WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID), actual);
        }

        private static bool IsBuildableTownRect(Map map, CellRect rect)
        {
            if (map == null || rect.Width < 40 || rect != rect.ClipInsideMap(map))
            {
                return false;
            }

            foreach (IntVec3 cell in rect.Cells)
            {
                TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
                if (IsBridgeable(terrain) || terrain?.affordances?.Contains(TerrainAffordanceDefOf.Heavy) != true)
                {
                    return false;
                }
            }
            return true;
        }

        private static Rot4 FindHarborSide(Map map, CellRect rect, out int waterCells)
        {
            Rot4 best = Rot4.Invalid;
            waterCells = 0;
            foreach (Rot4 side in new[] { Rot4.North, Rot4.East, Rot4.South, Rot4.West })
            {
                int count = CountWaterOutsideSide(map, rect, side, 8);
                if (count > waterCells)
                {
                    waterCells = count;
                    best = side;
                }
            }
            return best;
        }

        private static int CountWaterOutsideSide(Map map, CellRect rect, Rot4 side, int depth)
        {
            int count = 0;
            foreach (IntVec3 edge in SideCells(rect, side, 1))
            {
                for (int distance = 1; distance <= depth; distance++)
                {
                    IntVec3 cell = edge + side.FacingCell * distance;
                    if (cell.InBounds(map) && IsBridgeable(map.terrainGrid.TerrainAt(cell)))
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private static IEnumerable<IntVec3> SideCells(CellRect rect, Rot4 side, int cornerInset)
        {
            if (side == Rot4.North || side == Rot4.South)
            {
                int z = side == Rot4.North ? rect.maxZ : rect.minZ;
                for (int x = rect.minX + cornerInset; x <= rect.maxX - cornerInset; x++)
                {
                    yield return new IntVec3(x, 0, z);
                }
            }
            else
            {
                int x = side == Rot4.East ? rect.maxX : rect.minX;
                for (int z = rect.minZ + cornerInset; z <= rect.maxZ - cornerInset; z++)
                {
                    yield return new IntVec3(x, 0, z);
                }
            }
        }

        private static string SideName(Rot4 side)
        {
            return side.IsValid ? side.ToStringHuman() : "none";
        }

        internal static bool TryGetRegisteredTownRect(Map map, out CellRect rect)
        {
            rect = CellRect.Empty;
            if (map == null || !FranceDiagnostics.TryGetValue(map, out FranceGenerationDiagnostics diagnostics) ||
                diagnostics.SelectedRect.Width < 40)
            {
                return false;
            }
            rect = diagnostics.SelectedRect;
            return true;
        }

        internal static bool IsFrenchRiverHarbor(Map map)
        {
            return IsFrance(map?.ParentFaction) && FranceDiagnostics.TryGetValue(map, out FranceGenerationDiagnostics diagnostics) &&
                diagnostics.RiverHarbor && diagnostics.SelectedRect.Width >= 40;
        }

        internal static bool IsMongolCamp(Map map)
        {
            return map?.ParentFaction?.def?.defName == "BA_Mongol_NobleHouse" &&
                FranceDiagnostics.TryGetValue(map, out FranceGenerationDiagnostics diagnostics) &&
                diagnostics.TemplateDefName.StartsWith("EESMK_Mongol_", StringComparison.Ordinal);
        }

        internal static int BuildMongolSegmentedPerimeter(Map map, StrongholdData data, CellRect rect)
        {
            if (!IsMongolCamp(map) || data == null || rect.Width < 20)
            {
                return 0;
            }

            List<IntVec3> wallCells = rect.EdgeCells.Where(cell =>
            {
                int edgeOffset = cell.x == rect.minX || cell.x == rect.maxX ? cell.z - rect.minZ : cell.x - rect.minX;
                int edgeLength = cell.x == rect.minX || cell.x == rect.maxX ? rect.Height : rect.Width;
                int center = edgeLength / 2;
                return Math.Abs(edgeOffset - center) > 3 && edgeOffset % 12 < 8 && CanUseHarborWallTerrain(map, cell);
            }).Distinct().ToList();

            ClearWallCells(map, rect.ClipInsideMap(map).EdgeCells);
            StrongholdDefenseUtility.ResolveWall(map, map.ParentFaction, data.tier, out ThingDef wallDef, out ThingDef wallStuff);
            int built = 0;
            foreach (IntVec3 cell in wallCells)
            {
                if (wallDef == null || !CanBuildAt(map, cell, wallDef, wallStuff))
                {
                    continue;
                }
                Thing wall = ThingMaker.MakeThing(wallDef, wallDef.MadeFromStuff ? wallStuff : null);
                wall.SetFactionDirect(map.ParentFaction);
                GenSpawn.Spawn(wall, cell, map, WipeMode.Vanish);
                built++;
            }

            FranceGenerationDiagnostics diagnostics = FranceDiagnostics.GetValue(map, _ => new FranceGenerationDiagnostics());
            diagnostics.Tier = data.tier;
            diagnostics.PlannedWallCells = Math.Max(diagnostics.PlannedWallCells, wallCells.Count);
            diagnostics.BuiltWallCells = Math.Max(diagnostics.BuiltWallCells, built);
            diagnostics.GateCount = 4;
            Log.Message($"[EES Medieval Patch] Mongol segmented perimeter: tier=T{data.tier}, walls={built}/{wallCells.Count}, openings=4, baseRect={rect}.");
            return built;
        }

        internal static void LogRegisteredGenerationSummary(Map map, MapComponent_StrongholdSiege siege,
            CellRect baseRect, bool skippedWholeBaseRefog)
        {
            if (map == null || !FranceDiagnostics.TryGetValue(map, out FranceGenerationDiagnostics diagnostics))
            {
                return;
            }

            if (diagnostics.Logged)
            {
                return;
            }

            StrongholdData data = null;
            Settlement settlement = map.Parent as Settlement;
            if (settlement != null)
            {
                data = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
            }
            if (data == null && siege != null)
            {
                data = AccessTools.Field(typeof(MapComponent_StrongholdSiege), "cachedData")?.GetValue(siege) as StrongholdData;
            }
            int tier = diagnostics.Tier > 0 ? diagnostics.Tier : data?.tier ?? 0;
            int coreWaterCells = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                .Where(thing => thing.Faction == map.ParentFaction)
                .SelectMany(thing => thing.OccupiedRect().Cells)
                .Where(cell => cell.InBounds(map) && IsBridgeable(map.terrainGrid.TerrainAt(cell)))
                .Distinct().Count();
            bool riverMode = diagnostics.RiverHarbor;
            bool closedPerimeter = baseRect.Width >= 5 && HasClosedPerimeter(map, baseRect);
            Log.Message($"[EES Medieval Patch] Registered settlement generation: faction={diagnostics.FactionDefName}, template={diagnostics.TemplateDefName}, " +
                $"tier=T{tier}, originalRect={diagnostics.OriginalRect}, selectedRect={diagnostics.SelectedRect}, baseRect={baseRect}, " +
                $"riverMode={riverMode}, harborSide={SideName(diagnostics.HarborSide)}, coreWaterCells={coreWaterCells}, " +
                $"quayTiles={diagnostics.QuayTiles}, pierTiles={diagnostics.PierTiles}, crossRiverBridgeTiles={diagnostics.CrossRiverBridgeTiles}, " +
                $"wallCells={diagnostics.BuiltWallCells}/{diagnostics.PlannedWallCells}, gates={diagnostics.GateCount}, " +
                $"wallBlockersCleared={diagnostics.ClearedWallBlockers}, indoorBlockersCleared={diagnostics.ClearedIndoorBlockers}, " +
                $"closedPerimeter={closedPerimeter}, " +
                $"fallbackIndex={diagnostics.SelectionSizeIndex}, skippedWholeBaseRefog={skippedWholeBaseRefog}, " +
                $"validVault={HasValidVaultEnclosure(map, siege?.eesVaultRect ?? CellRect.Empty)}.");
            diagnostics.Logged = true;
        }

        internal static bool IsSupportedHostileSettlement(Map map)
        {
            Faction faction = map?.ParentFaction;
            return map?.Parent is Settlement && IsEligibleMedievalFaction(faction) &&
                !EESCustomBaseDetector.IsForeignSettlement(map.Parent) &&
                faction.HostileTo(Faction.OfPlayer);
        }

        internal static bool IsBridgeable(TerrainDef terrain)
        {
            return terrain != null && (terrain.IsWater ||
                terrain.affordances?.Contains(TerrainAffordanceDefOf.Bridgeable) == true);
        }

        internal static bool HasRiverCrossing(Map map, CellRect rect)
        {
            if (map == null || rect.Width < 5 || rect.Height < 5)
            {
                return false;
            }

            CellRect clipped = rect.ClipInsideMap(map);
            int waterCells = 0;
            int waterEdgeCells = 0;
            foreach (IntVec3 cell in clipped.Cells)
            {
                if (IsBridgeable(map.terrainGrid.TerrainAt(cell)))
                {
                    waterCells++;
                }
            }
            foreach (IntVec3 cell in clipped.EdgeCells)
            {
                if (IsBridgeable(map.terrainGrid.TerrainAt(cell)))
                {
                    waterEdgeCells++;
                }
            }
            return waterCells >= Math.Max(12, Math.Min(clipped.Width, clipped.Height) * 2) && waterEdgeCells >= 2;
        }

        internal static bool HasClosedPerimeter(Map map, CellRect rect)
        {
            if (map == null || rect.Width < 5 || rect.Height < 5)
            {
                return false;
            }

            int edgeCount = 0;
            int closedCount = 0;
            int doorCount = 0;
            foreach (IntVec3 cell in rect.ClipInsideMap(map).EdgeCells)
            {
                edgeCount++;
                Building edifice = cell.GetThingList(map).OfType<Building>()
                    .FirstOrDefault(building => building.def.IsDoor ||
                        building.def.passability == Traversability.Impassable);
                if (edifice?.def?.IsDoor == true)
                {
                    doorCount++;
                    closedCount++;
                }
                else if (edifice?.def?.passability == Traversability.Impassable)
                {
                    closedCount++;
                }
            }
            return edgeCount > 0 && doorCount > 0 && closedCount == edgeCount;
        }

        internal static int ClearWallFootprint(Map map, CellRect rect)
        {
            if (!IsSupportedHostileSettlement(map) || rect.Width < 5)
            {
                return 0;
            }

            return ClearWallCells(map, rect.ClipInsideMap(map).EdgeCells);
        }

        internal static int ClearOuterDefenseFootprint(Map map, StrongholdData data, CellRect rect)
        {
            if (!IsSupportedHostileSettlement(map) || data == null || rect.Width < 5)
            {
                return 0;
            }

            if (HasCastleContour(map, data))
                return ClearWallCells(map, CastleContourPlanner.Cells(data.generationPlan.castleInnerPads.Concat(data.generationPlan.castleOuterPads)));

            if (data.tier >= 14)
            {
                CellRect[] rings = CastleDefenseLayout.Rings(rect,
                    StrongholdAreaUtility.OuterRingOffsetFor(data), data.tier, map.Size);
                return ClearWallCells(map, rings.SelectMany(ring => ring.EdgeCells));
            }

            CellRect outer = rect.ExpandedBy(StrongholdAreaUtility.OuterRingOffsetFor(data)).ClipInsideMap(map);
            CellRect wire = outer.ExpandedBy(1).ClipInsideMap(map);
            IEnumerable<IntVec3> footprint = outer.EdgeCells.Where(cell => !IsEesGateCell(cell, outer))
                .Concat(wire.EdgeCells.Where(cell => !IsEesGateCell(cell, wire)));
            return ClearWallCells(map, footprint);
        }

        private static bool IsEesGateCell(IntVec3 cell, CellRect rect)
        {
            int centerX = rect.minX + rect.Width / 2;
            int centerZ = rect.minZ + rect.Height / 2;
            return ((cell.x == rect.minX || cell.x == rect.maxX) && Math.Abs(cell.z - centerZ) <= 2) ||
                ((cell.z == rect.minZ || cell.z == rect.maxZ) && Math.Abs(cell.x - centerX) <= 2);
        }

        private static int ClearWallCells(Map map, IEnumerable<IntVec3> cells)
        {
            if (!IsSupportedHostileSettlement(map))
            {
                return 0;
            }

            int removed = 0;
            var footprint = new HashSet<IntVec3>(cells.Where(cell => cell.InBounds(map)));
            foreach (IntVec3 cell in footprint)
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }
                foreach (Thing thing in cell.GetThingList(map).ToList())
                {
                    if (!thing.Destroyed && IsNaturalBlocker(thing) && ClearNaturalBlocker(map, thing, footprint))
                    {
                        removed++;
                    }
                }
            }
            int relocated = RelocateLooseDefenseBlockers(map, footprint, reportBlocked: true);
            if (removed > 0)
            {
                Log.Message($"[EES Medieval Patch] Cleared {removed} natural blockers from the exact stronghold wall footprint; loose items relocated.");
            }
            if (relocated > 0)
            {
                Log.Message($"[EES Medieval Patch] Relocated {relocated} loose non-structural buildings from the exact stronghold wall footprint.");
            }
            RecordFranceWallBlockers(map, removed);
            return removed;
        }

        private static bool IsNaturalBlocker(Thing thing)
        {
            ThingDef def = thing?.def;
            bool chunk = def?.thingCategories?.Any(category =>
                IsChunkCategory(category) || category?.Parents.Any(IsChunkCategory) == true) == true;
            bool naturalRock = def?.building != null &&
                def.building.isNaturalRock && !def.building.isResourceRock;
            return chunk || naturalRock || CastleDefenseLayout.IsBoulder(def) || def?.plant?.IsTree == true;
        }

        private static bool IsChunkCategory(ThingCategoryDef category)
        {
            return category?.defName == "Chunks" || category?.defName == "StoneChunks";
        }

        private static bool ClearNaturalBlocker(Map map, Thing thing, HashSet<IntVec3> footprint)
        {
            if (!CanClearNaturalBlocker(map, thing)) return false;
            if (thing.def.category != ThingCategory.Item && !CastleDefenseLayout.IsBoulder(thing.def))
            {
                thing.Destroy(DestroyMode.Vanish);
                return true;
            }
            return RelocateConstructionItem(map, thing, footprint);
        }

        private static bool CanClearNaturalBlocker(Map map, Thing thing)
        {
            if (!ConstructionContext(map) || thing == null || thing.Destroyed || thing.Map != map ||
                thing.questTags?.Count > 0 || thing.Faction != null ||
                thing.def.building?.isResourceRock == true || IsCapturedNativeDefense(thing, map) ||
                IsProtectedStrongholdDef(thing.def) || !IsNaturalBlocker(thing)) return false;
            // Connected/specialized trees remain gameplay content. ReGrowth's
            // four ordinary trees only emit cosmetic filth; no other component
            // set or product is covered by this exception.
            return thing is not Plant plant || !plant.sown && plant.GetType() == typeof(Plant) &&
                ((plant.AllComps?.Count ?? 0) == 0 || HasOnlyAutumnLeaves(plant));
        }

        private static bool HasOnlyAutumnLeaves(Plant plant)
        {
            if (plant.AllComps.Count != 1) return false;
            string leaves = plant.def.defName switch
            {
                "Plant_TreeOak" or "Plant_TreeMaple" => "RG_Filth_LeavesTreeOak",
                "Plant_TreePoplar" => "RG_Filth_LeavesTreePopolar",
                "Plant_TreeBirch" => "RG_Filth_LeavesTreeBirch",
                _ => null
            };
            var comp = plant.AllComps[0];
            return leaves != null && comp.GetType().FullName == "ReGrowthCore.CompAutumnLeavesSpawner" &&
                comp.GetType().Assembly.GetName().Name == "ReGrowthCore" &&
                comp.props is CompProperties_Spawner props && props.compClass == comp.GetType() &&
                props.saveKeysPrefix == "leaves" && props.thingToSpawn?.defName == leaves &&
                props.thingToSpawn.thingClass == typeof(Filth);
        }

        private static bool ConstructionContext(Map map)
        {
            if (map == null || (MapGenerator.mapBeingGenerated != map && finalizingDefenseMap != map) ||
                !IsSupportedHostileSettlement(map)) return false;
            var plan = WorldComponent_Strongholds.Instance?.GetExisting(map.Parent.ID)?.generationPlan;
            return plan?.legacyMap != true;
        }

        [ThreadStatic] private static Thing relocatingConstructionThing;
        [ThreadStatic] private static Map relocatingConstructionMap;

        internal static bool IsConstructionRelocation(Thing thing, Map map) =>
            thing != null && ReferenceEquals(thing, relocatingConstructionThing) && ReferenceEquals(map, relocatingConstructionMap) &&
            ConstructionContext(map);

        private static Thing SpawnRelocatedConstructionThing(Thing thing, IntVec3 cell, Map map, Rot4 rotation, bool preserveSpawnState = false)
        {
            // Only this exact original object in the existing guarded relocation
            // paths bypasses the native random-rotation branch. Nested new spawns
            // and ordinary new generation keep their original native behavior.
            Thing previousThing = relocatingConstructionThing;
            Map previousMap = relocatingConstructionMap;
            relocatingConstructionThing = thing;
            relocatingConstructionMap = map;
            try { return GenSpawn.Spawn(thing, cell, map, rotation, respawningAfterLoad: preserveSpawnState); }
            finally
            {
                relocatingConstructionThing = previousThing;
                relocatingConstructionMap = previousMap;
            }
        }

        private static bool RelocateConstructionItem(Map map, Thing thing, HashSet<IntVec3> footprint)
        {
            if (!ConstructionContext(map) || thing == null || thing.Destroyed || thing.Map != map ||
                thing.questTags?.Count > 0 || thing.Faction == Faction.OfPlayer ||
                thing.Faction != null && thing.Faction != map.ParentFaction ||
                thing.def.category != ThingCategory.Item && !CastleDefenseLayout.IsBoulder(thing.def)) return false;
            var original = thing.Position;
            var originalRotation = thing.Rotation;
            foreach (var cell in GenRadial.RadialCellsAround(original, 65f, true))
            {
                CellRect occupied = GenAdj.OccupiedRect(cell, thing.Rotation, thing.def.size);
                if (IntersectsCastleGateEntrance(map, occupied) || occupied.Any(point => !point.InBounds(map) || footprint.Contains(point) || !point.Standable(map) ||
                    point.GetEdifice(map) != null || point.GetFirstItem(map) != null || point.GetFirstPawn(map) != null)) continue;
                thing.DeSpawn();
                try { SpawnRelocatedConstructionThing(thing, cell, map, originalRotation); return true; }
                catch (Exception exception)
                {
                    if (!thing.Spawned) SpawnRelocatedConstructionThing(thing, original, map, originalRotation);
                    Log.Warning($"[EES Medieval Patch] Retained {thing.def.defName} after relocation failed: {exception.Message}");
                    return false;
                }
            }
            Log.Warning($"[EES Medieval Patch] Retained {thing.def.defName} at {original}; no free relocation cell outside the construction footprint.");
            return false;
        }

        internal static int ClearRegisteredTownNaturalBlockers(Map map, CellRect townRect)
        {
            if (!IsSupportedHostileSettlement(map) || !TryGetRegisteredTownRect(map, out CellRect selectedRect) ||
                townRect != selectedRect)
            {
                return 0;
            }

            int removed = 0;
            var footprint = new HashSet<IntVec3>(townRect.ClipInsideMap(map).Cells);
            foreach (IntVec3 cell in footprint)
            {
                foreach (Thing thing in cell.GetThingList(map).ToList())
                {
                    if (!thing.Destroyed && IsNaturalBlocker(thing) && ClearNaturalBlocker(map, thing, footprint))
                    {
                        removed++;
                    }
                }
            }

            FranceGenerationDiagnostics diagnostics = FranceDiagnostics.GetValue(map, _ => new FranceGenerationDiagnostics());
            diagnostics.ClearedIndoorBlockers += removed;
            Log.Message($"[EES Medieval Patch] Prepared registered town footprint: faction={diagnostics.FactionDefName}, " +
                $"template={diagnostics.TemplateDefName}, rect={townRect}, naturalBlockersReplaced={removed}.");
            return removed;
        }

        internal static int ClearFranceIndoorNaturalBlockers(Map map)
        {
            if (!IsFrance(map?.ParentFaction) || !TryGetRegisteredTownRect(map, out CellRect townRect))
            {
                return 0;
            }

            int removed = 0;
            IntVec3[] cardinalOffsets = { IntVec3.North, IntVec3.East, IntVec3.South, IntVec3.West };
            var footprint = new HashSet<IntVec3>(townRect.ClipInsideMap(map).Cells);
            foreach (IntVec3 cell in townRect.ClipInsideMap(map).Cells)
            {
                if (!cell.Roofed(map))
                {
                    continue;
                }

                Room room = cell.GetRoom(map);
                bool enclosed = room != null && !room.PsychologicallyOutdoors && !room.TouchesMapEdge;
                if (!enclosed)
                {
                    int enclosedNeighbours = 0;
                    foreach (IntVec3 offset in cardinalOffsets)
                    {
                        IntVec3 neighbour = cell + offset;
                        Room neighbourRoom = neighbour.InBounds(map) ? neighbour.GetRoom(map) : null;
                        if (neighbour.Roofed(map) && neighbourRoom != null &&
                            !neighbourRoom.PsychologicallyOutdoors && !neighbourRoom.TouchesMapEdge)
                        {
                            enclosedNeighbours++;
                        }
                    }
                    enclosed = enclosedNeighbours >= 2;
                }
                if (!enclosed)
                {
                    continue;
                }

                foreach (Thing thing in cell.GetThingList(map).ToList())
                {
                    if (!thing.Destroyed && IsNaturalBlocker(thing) && ClearNaturalBlocker(map, thing, footprint))
                    {
                        removed++;
                    }
                }
            }

            FranceGenerationDiagnostics diagnostics = FranceDiagnostics.GetValue(map, _ => new FranceGenerationDiagnostics());
            diagnostics.ClearedIndoorBlockers += removed;
            if (removed > 0)
            {
                Log.Message($"[EES Medieval Patch] Cleared {removed} natural blockers from enclosed roofed rooms inside the selected France V3 town rect.");
            }
            return removed;
        }

        internal static bool IsValidVaultTerrain(Map map, CellRect rect)
        {
            if (map == null || rect.Width < 3 || rect.Height < 3 || rect != rect.ClipInsideMap(map))
            {
                return false;
            }

            foreach (IntVec3 cell in rect.ExpandedBy(1).Cells)
            {
                if (!cell.InBounds(map))
                {
                    return false;
                }
                TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
                if (IsBridgeable(terrain) || terrain?.affordances?.Contains(TerrainAffordanceDefOf.Heavy) != true)
                {
                    return false;
                }
            }
            return true;
        }

        internal static CellRect FindValidVaultRect(Map map, CellRect baseRect, int size, CellRect proposed)
        {
            if (IsValidVaultTerrain(map, proposed))
            {
                return proposed;
            }

            CellRect search = baseRect.ContractedBy(2).ClipInsideMap(map);
            foreach (IntVec3 center in search.Cells.OrderBy(cell =>
                Math.Abs(cell.x - baseRect.CenterCell.x) + Math.Abs(cell.z - baseRect.CenterCell.z)))
            {
                CellRect candidate = CellRect.CenteredOn(center, Math.Max(3, size)).ClipInsideMap(map);
                if (!baseRect.Contains(new IntVec3(candidate.minX, 0, candidate.minZ)) ||
                    !baseRect.Contains(new IntVec3(candidate.maxX, 0, candidate.maxZ)) || !IsValidVaultTerrain(map, candidate))
                {
                    continue;
                }
                int edifices = candidate.ExpandedBy(1).Cells.Count(cell => cell.GetEdifice(map) != null);
                if (edifices <= 2)
                {
                    return candidate;
                }
            }

            Log.Warning($"[EES Medieval Patch] No dry buildable vault site was available inside the stronghold at {baseRect.CenterCell}; vault generation was skipped.");
            return CellRect.Empty;
        }

        internal static bool TryBuildRegisteredMedievalVault(Map map, Faction faction, StrongholdData data,
            CellRect proposed, bool forceRelocation = false)
        {
            if (faction == null || data == null ||
                !SettlementCultureRegistry.IsRegisteredFaction(faction.def?.defName))
            {
                return false;
            }

            MapComponent_StrongholdSiege siege = map.GetComponent<MapComponent_StrongholdSiege>();
            CellRect currentOuter = siege?.eesVaultRect ?? CellRect.Empty;
            if (!forceRelocation && HasValidVaultEnclosure(map, currentOuter))
            {
                return false;
            }

            CellRect baseRect = siege?.eesBaseRect ?? CellRect.Empty;
            if (baseRect.Width < 20 && TryGetRegisteredTownRect(map, out CellRect registeredRect))
            {
                baseRect = registeredRect;
            }

            int size = proposed.Width >= 5 ? proposed.Width : 7;
            size = Math.Min(9, Math.Max(5, size));
            if (size % 2 == 0)
            {
                size--;
            }

            CellRect room = FindAccessibleVaultRect(map, baseRect, size, proposed);
            if (room.Width < 3)
            {
                if (forceRelocation)
                {
                    Log.Warning($"[EES Medieval Patch] Registered medieval town faction={faction.def.defName} could not find an accessible vault site inside {baseRect}; the orphan vault marker will be discarded.");
                }
                return false;
            }

            CellRect outer = room.ExpandedBy(1).ClipInsideMap(map);
            if (!TryFindReachableVaultDoor(map, outer, out IntVec3 doorCell))
            {
                return false;
            }

            StrongholdDefenseUtility.ResolveWall(map, faction, data.tier, out ThingDef wallDef, out ThingDef wallStuff);
            ThingDef doorDef = MedievalDoor();
            ThingDef doorStuff = doorDef?.MadeFromStuff == true ? ThingDefOf.BlocksGranite : null;
            TerrainDef floor = MedievalFloor() ?? TerrainDefOf.PackedDirt;
            MethodInfo builder = AccessTools.Method(
                AccessTools.TypeByName("EvolvingEnemyStrongholds.StrongholdLayoutGenerator"),
                "BuildEnclosedRoom");
            if (wallDef == null || doorDef == null || floor == null || builder == null)
            {
                Log.Error("[EES Medieval Patch] Registered medieval vault could not resolve its wall, door, floor, or EES room builder.");
                return false;
            }

            ClearWallCells(map, outer.EdgeCells);
            try
            {
                builder.Invoke(null, new object[]
                {
                    map,
                    faction,
                    outer,
                    doorCell,
                    wallDef,
                    wallStuff,
                    doorDef,
                    doorStuff,
                    floor,
                    true,
                    new HashSet<IntVec3>()
                });
            }
            catch (TargetInvocationException exception)
            {
                Log.Error($"[EES Medieval Patch] Registered medieval vault generation failed: {exception.InnerException ?? exception}");
                return false;
            }
            catch (Exception exception)
            {
                Log.Error($"[EES Medieval Patch] Registered medieval vault generation failed: {exception}");
                return false;
            }

            if (!HasValidVaultEnclosure(map, outer) || !TryFindReachableVaultDoor(map, outer, out _))
            {
                Log.Warning($"[EES Medieval Patch] Registered medieval vault builder did not produce a reachable enclosure at {outer}; the orphan vault marker will be discarded.");
                return false;
            }

            data.RegisterSealedRect(room, "Vault");
            if (siege != null)
            {
                siege.eesVaultRect = outer;
            }
            Log.Message($"[EES Medieval Patch] Built reachable registered medieval vault: faction={faction.def.defName}, room={room}, outer={outer}, door={doorCell}, tier=T{data.tier}.");
            return true;
        }

        private static CellRect FindAccessibleVaultRect(Map map, CellRect baseRect, int size, CellRect proposed)
        {
            if (map == null || baseRect.Width < size + 2)
            {
                return CellRect.Empty;
            }

            IEnumerable<IntVec3> centers = Enumerable.Empty<IntVec3>();
            if (proposed.Width >= size && proposed.Height >= size)
            {
                centers = centers.Concat(new[] { proposed.CenterCell });
            }

            CellRect search = baseRect.ContractedBy(2).ClipInsideMap(map);
            centers = centers.Concat(search.Cells.OrderBy(cell =>
                Math.Abs(cell.x - baseRect.CenterCell.x) + Math.Abs(cell.z - baseRect.CenterCell.z)));
            foreach (IntVec3 center in centers.Distinct())
            {
                CellRect candidate = CellRect.CenteredOn(center, size).ClipInsideMap(map);
                if (!baseRect.Contains(new IntVec3(candidate.minX, 0, candidate.minZ)) ||
                    !baseRect.Contains(new IntVec3(candidate.maxX, 0, candidate.maxZ)) ||
                    !IsValidVaultTerrain(map, candidate))
                {
                    continue;
                }

                CellRect outer = candidate.ExpandedBy(1).ClipInsideMap(map);
                int existingEdifices = outer.Cells.Count(cell => cell.GetEdifice(map) != null);
                if (existingEdifices > 2 || !candidate.Cells.Any(cell => cell.GetEdifice(map) == null) ||
                    !TryFindReachableVaultDoor(map, outer, out _))
                {
                    continue;
                }
                return candidate;
            }

            return CellRect.Empty;
        }

        private static bool TryFindReachableVaultDoor(Map map, CellRect outer, out IntVec3 doorCell)
        {
            doorCell = IntVec3.Invalid;
            if (map == null || outer.Width < 3 || outer != outer.ClipInsideMap(map) || map.reachability == null)
            {
                return false;
            }

            IntVec3[] directions =
            {
                new IntVec3(1, 0, 0),
                new IntVec3(-1, 0, 0),
                new IntVec3(0, 0, 1),
                new IntVec3(0, 0, -1)
            };
            TraverseParms traverse = TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly,
                false, false, false, true, false);
            foreach (IntVec3 edge in outer.EdgeCells)
            {
                foreach (IntVec3 direction in directions)
                {
                    IntVec3 approach = edge + direction;
                    if (outer.Contains(approach) || !approach.InBounds(map) || !approach.Standable(map))
                    {
                        continue;
                    }
                    if (map.reachability.CanReachMapEdge(approach, traverse))
                    {
                        doorCell = edge;
                        return true;
                    }
                }
            }
            return false;
        }

        internal static int TryBuildFrenchRiverBridge(Map map, CellRect rect)
        {
            if (!IsFrance(map?.ParentFaction) || !HasRiverCrossing(map, rect) ||
                rect.Cells.Any(cell => cell.InBounds(map) && map.terrainGrid.TerrainAt(cell)?.bridge == true))
            {
                return 0;
            }

            List<Building> factionBuildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                .OfType<Building>().Where(building => building.Faction == map.ParentFaction &&
                    rect.Contains(building.Position)).ToList();
            if (factionBuildings.Count < 4)
            {
                return 0;
            }

            BridgeCandidate best = default;
            bool found = false;
            for (int z = rect.minZ + 1; z < rect.maxZ; z++)
            {
                TryBridgeRow(z, true);
            }
            for (int x = rect.minX + 1; x < rect.maxX; x++)
            {
                TryBridgeRow(x, false);
            }
            if (!found)
            {
                return 0;
            }

            TerrainDef bridge = TerrainDefOf.Bridge;
            TerrainDef approach = MedievalFloor() ?? TerrainDefOf.PackedDirt;
            int placed = 0;
            for (int lane = 0; lane < 2; lane++)
            {
                for (int value = best.Start; value <= best.End; value++)
                {
                    IntVec3 cell = best.Horizontal
                        ? new IntVec3(value, 0, best.Fixed + lane)
                        : new IntVec3(best.Fixed + lane, 0, value);
                    if (cell.InBounds(map) && IsBridgeable(map.terrainGrid.TerrainAt(cell)) && cell.GetEdifice(map) == null)
                    {
                        map.terrainGrid.SetTerrain(cell, bridge);
                        placed++;
                    }
                }
                for (int extension = 1; extension <= 3; extension++)
                {
                    IntVec3 before = best.Horizontal
                        ? new IntVec3(best.Start - extension, 0, best.Fixed + lane)
                        : new IntVec3(best.Fixed + lane, 0, best.Start - extension);
                    IntVec3 after = best.Horizontal
                        ? new IntVec3(best.End + extension, 0, best.Fixed + lane)
                        : new IntVec3(best.Fixed + lane, 0, best.End + extension);
                    PaveApproach(before);
                    PaveApproach(after);
                }
            }
            if (placed > 0)
            {
                RecordFranceCrossRiverBridgeTiles(map, placed);
                Log.Message($"[EES Medieval Patch] France river mode: built a two-cell bridge with {placed} bridge tiles; rectangular EES perimeter and base refog will be skipped.");
            }
            return placed;

            void TryBridgeRow(int fixedValue, bool horizontal)
            {
                List<int> values = new List<int>();
                int min = horizontal ? rect.minX + 1 : rect.minZ + 1;
                int max = horizontal ? rect.maxX - 1 : rect.maxZ - 1;
                for (int value = min; value <= max; value++)
                {
                    IntVec3 cell = horizontal ? new IntVec3(value, 0, fixedValue) : new IntVec3(fixedValue, 0, value);
                    if (IsBridgeable(map.terrainGrid.TerrainAt(cell)))
                    {
                        values.Add(value);
                    }
                }
                if (values.Count == 0)
                {
                    return;
                }
                int start = values.Min();
                int end = values.Max();
                int length = end - start + 1;
                if (length > 20 || values.Count < length * 0.8f)
                {
                    return;
                }
                int firstBank = factionBuildings.Count(building => horizontal
                    ? building.Position.x < start : building.Position.z < start);
                int secondBank = factionBuildings.Count(building => horizontal
                    ? building.Position.x > end : building.Position.z > end);
                if (firstBank < 2 || secondBank < 2)
                {
                    return;
                }
                int score = length * 10 + Math.Abs(fixedValue - (horizontal ? rect.CenterCell.z : rect.CenterCell.x));
                if (!found || score < best.Score)
                {
                    best = new BridgeCandidate(horizontal, fixedValue, start, end, score);
                    found = true;
                }
            }

            void PaveApproach(IntVec3 cell)
            {
                if (cell.InBounds(map) && !IsBridgeable(map.terrainGrid.TerrainAt(cell)) && cell.GetEdifice(map) == null)
                {
                    map.terrainGrid.SetTerrain(cell, approach);
                }
            }
        }

        private readonly struct BridgeCandidate
        {
            internal readonly bool Horizontal;
            internal readonly int Fixed;
            internal readonly int Start;
            internal readonly int End;
            internal readonly int Score;

            internal BridgeCandidate(bool horizontal, int fixedValue, int start, int end, int score)
            {
                Horizontal = horizontal;
                Fixed = fixedValue;
                Start = start;
                End = end;
                Score = score;
            }
        }

        internal static int BuildFrenchHarborPerimeter(Map map, StrongholdData data, CellRect rect, bool buildHarbor)
        {
            if (!IsFrenchRiverHarbor(map) || data == null || rect.Width < 20)
            {
                return 0;
            }

            FranceGenerationDiagnostics diagnostics = FranceDiagnostics.GetValue(map, _ => new FranceGenerationDiagnostics());
            diagnostics.Tier = data.tier;
            Rot4 harborSide = diagnostics.HarborSide;
            List<IntVec3> wallCells = GetFrenchHarborWallCells(map, rect, harborSide).Distinct().ToList();
            if (wallCells.Count == 0)
            {
                return 0;
            }

            List<IntVec3> oppositeEdge = SideCells(rect, harborSide.Opposite, 4)
                .Where(wallCells.Contains).ToList();
            List<IntVec3> sideEdge = SideCells(rect, RightOf(harborSide), 4)
                .Where(wallCells.Contains).ToList();
            HashSet<IntVec3> doorCells = new HashSet<IntVec3>();
            if (oppositeEdge.Count >= 6)
            {
                int middle = oppositeEdge.Count / 2;
                doorCells.Add(oppositeEdge[middle - 1]);
                doorCells.Add(oppositeEdge[middle]);
            }
            if (sideEdge.Count >= 8)
            {
                doorCells.Add(sideEdge[sideEdge.Count / 2]);
            }

            ClearWallCells(map, wallCells);
            StrongholdDefenseUtility.ResolveWall(map, map.ParentFaction, data.tier, out ThingDef wallDef, out ThingDef wallStuff);
            ThingDef doorDef = MedievalDoor();
            ThingDef doorStuff = doorDef.MadeFromStuff ? ThingDefOf.BlocksGranite : null;
            int built = 0;
            foreach (IntVec3 cell in wallCells)
            {
                bool door = doorCells.Contains(cell);
                ThingDef targetDef = door ? doorDef : wallDef;
                ThingDef targetStuff = door ? doorStuff : wallStuff;
                Building existing = cell.GetEdifice(map);
                if (existing != null)
                {
                    if ((door && existing.def.IsDoor) || (!door && !existing.def.IsDoor &&
                        existing.def.passability == Traversability.Impassable))
                    {
                        built++;
                        continue;
                    }
                    if (existing.Faction != map.ParentFaction)
                    {
                        continue;
                    }
                    existing.Destroy(DestroyMode.Vanish);
                }
                if (targetDef == null || !CanBuildAt(map, cell, targetDef, targetStuff))
                {
                    continue;
                }
                Thing thing = ThingMaker.MakeThing(targetDef, targetDef.MadeFromStuff ? targetStuff : null);
                thing.SetFactionDirect(map.ParentFaction);
                GenSpawn.Spawn(thing, cell, map, WipeMode.Vanish);
                built++;
            }

            diagnostics.PlannedWallCells = Math.Max(diagnostics.PlannedWallCells, wallCells.Count);
            diagnostics.BuiltWallCells = Math.Max(diagnostics.BuiltWallCells, built);
            diagnostics.GateCount = doorCells.Count >= 3 ? 2 : doorCells.Count > 0 ? 1 : 0;
            if (buildHarbor && diagnostics.QuayTiles == 0 && diagnostics.PierTiles == 0)
            {
                BuildFrenchHarborQuay(map, diagnostics.SelectedRect, harborSide, diagnostics);
            }
            Log.Message($"[EES Medieval Patch] France open harbor perimeter: tier=T{data.tier}, side={SideName(harborSide)}, " +
                $"walls={built}/{wallCells.Count}, gates={diagnostics.GateCount}.");
            return built;
        }

        private static IEnumerable<IntVec3> GetFrenchHarborWallCells(Map map, CellRect rect, Rot4 harborSide)
        {
            Rot4 opposite = harborSide.Opposite;
            foreach (IntVec3 cell in SideCells(rect, opposite, 0))
            {
                if (CanUseHarborWallTerrain(map, cell))
                {
                    yield return cell;
                }
            }

            Rot4 right = RightOf(harborSide);
            foreach (Rot4 side in new[] { right, right.Opposite })
            {
                IEnumerable<IntVec3> ordered = SideCells(rect, side, 0)
                    .OrderByDescending(cell => DistanceToward(cell, harborSide));
                foreach (IntVec3 cell in ordered)
                {
                    if (IsBridgeable(map.terrainGrid.TerrainAt(cell)))
                    {
                        break;
                    }
                    if (CanUseHarborWallTerrain(map, cell))
                    {
                        yield return cell;
                    }
                }
            }
        }

        private static int DistanceToward(IntVec3 cell, Rot4 direction)
        {
            if (direction == Rot4.North)
            {
                return -cell.z;
            }
            if (direction == Rot4.South)
            {
                return cell.z;
            }
            if (direction == Rot4.East)
            {
                return -cell.x;
            }
            return cell.x;
        }

        private static Rot4 RightOf(Rot4 side)
        {
            if (side == Rot4.North)
            {
                return Rot4.East;
            }
            if (side == Rot4.East)
            {
                return Rot4.South;
            }
            if (side == Rot4.South)
            {
                return Rot4.West;
            }
            return Rot4.North;
        }

        private static bool CanUseHarborWallTerrain(Map map, IntVec3 cell)
        {
            TerrainDef terrain = cell.InBounds(map) ? map.terrainGrid.TerrainAt(cell) : null;
            return terrain != null && !IsBridgeable(terrain) &&
                terrain.affordances?.Contains(TerrainAffordanceDefOf.Heavy) == true;
        }

        private static bool CanBuildAt(Map map, IntVec3 cell, ThingDef def, ThingDef stuff)
        {
            return cell.InBounds(map) && CanUseHarborWallTerrain(map, cell) &&
                GenConstruct.CanBuildOnTerrain(def, cell, map, Rot4.North);
        }

        private static void BuildFrenchHarborQuay(Map map, CellRect rect, Rot4 harborSide,
            FranceGenerationDiagnostics diagnostics)
        {
            if (rect.Width < 40 || !harborSide.IsValid)
            {
                return;
            }
            TerrainDef quay = MedievalFloor() ?? TerrainDefOf.PackedDirt;
            List<IntVec3> edge = SideCells(rect, harborSide, 8).ToList();
            foreach (IntVec3 cell in edge)
            {
                for (int inward = 0; inward < 2; inward++)
                {
                    IntVec3 quayCell = cell + harborSide.Opposite.FacingCell * inward;
                    if (quayCell.InBounds(map) && !IsBridgeable(map.terrainGrid.TerrainAt(quayCell)) &&
                        quayCell.GetEdifice(map) == null)
                    {
                        map.terrainGrid.SetTerrain(quayCell, quay);
                        diagnostics.QuayTiles++;
                    }
                }
            }

            foreach (int fraction in new[] { 1, 2 })
            {
                int index = edge.Count * fraction / 3;
                if (index < 0 || index + 1 >= edge.Count)
                {
                    continue;
                }
                int waterDepth = 0;
                for (int lane = 0; lane < 2; lane++)
                {
                    bool reachedWater = false;
                    for (int distance = 1; distance <= 9; distance++)
                    {
                        IntVec3 cell = edge[index + lane] + harborSide.FacingCell * distance;
                        if (!cell.InBounds(map) || cell.GetEdifice(map) != null)
                        {
                            break;
                        }
                        if (IsBridgeable(map.terrainGrid.TerrainAt(cell)))
                        {
                            map.terrainGrid.SetTerrain(cell, TerrainDefOf.Bridge);
                            diagnostics.PierTiles++;
                            reachedWater = true;
                            waterDepth++;
                        }
                        else if (!reachedWater)
                        {
                            map.terrainGrid.SetTerrain(cell, quay);
                            diagnostics.QuayTiles++;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                if (waterDepth == 0)
                {
                    continue;
                }
            }
        }

        internal static void BuildFrenchHarborOuterDefenses(Map map, StrongholdData data, CellRect rect)
        {
            if (!IsFrenchRiverHarbor(map) || data == null || data.tier < 4)
            {
                return;
            }

            FranceGenerationDiagnostics diagnostics = FranceDiagnostics.GetValue(map, _ => new FranceGenerationDiagnostics());
            Rot4 landward = diagnostics.HarborSide.Opposite;
            List<IntVec3> edge = SideCells(rect, landward, 8).ToList();
            ThingDef trap = ThingDefOf.TrapSpike;
            int trapCount = data.tier >= 11 ? 6 : data.tier >= 8 ? 4 : 2;
            for (int i = 1; i <= trapCount; i++)
            {
                int index = edge.Count * i / (trapCount + 1);
                if (index < 0 || index >= edge.Count)
                {
                    continue;
                }
                IntVec3 cell = edge[index] + landward.FacingCell * 2;
                if (cell.GetEdifice(map) == null && CanBuildAt(map, cell, trap, ThingDefOf.WoodLog))
                {
                    Thing thing = ThingMaker.MakeThing(trap, trap.MadeFromStuff ? ThingDefOf.WoodLog : null);
                    thing.SetFactionDirect(map.ParentFaction);
                    GenSpawn.Spawn(thing, cell, map, WipeMode.Vanish);
                }
            }

            if (data.tier < 11)
            {
                return;
            }

            CellRect outer = rect.ExpandedBy(5).ClipInsideMap(map);
            List<IntVec3> secondLine = SideCells(outer, landward, Math.Max(10, (landward.IsHorizontal ? outer.Height : outer.Width) / 4)).ToList();
            ClearWallCells(map, secondLine);
            StrongholdDefenseUtility.ResolveWall(map, map.ParentFaction, data.tier, out ThingDef wallDef, out ThingDef stuff);
            int middle = secondLine.Count / 2;
            for (int i = 0; i < secondLine.Count; i++)
            {
                IntVec3 cell = secondLine[i];
                ThingDef def = Math.Abs(i - middle) <= 1 ? MedievalDoor() : wallDef;
                ThingDef useStuff = def.MadeFromStuff ? (def.IsDoor ? ThingDefOf.BlocksGranite : stuff) : null;
                if (cell.GetEdifice(map) != null || !CanBuildAt(map, cell, def, useStuff))
                {
                    continue;
                }
                Thing thing = ThingMaker.MakeThing(def, useStuff);
                thing.SetFactionDirect(map.ParentFaction);
                GenSpawn.Spawn(thing, cell, map, WipeMode.Vanish);
            }
        }

        internal static ThingDef MedievalDoor()
        {
            return DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_SlabDoor") ?? ThingDefOf.Door;
        }

        internal static ThingDef CastleGate()
        {
            // Castle Walls Expanded uses the Reborn gate in the installed 1.6
            // package: three cells wide and two cells deep, with native door AI.
            ThingDef gate = DefDatabase<ThingDef>.GetNamedSilentFail("JDS_Castle_Gate");
            return gate?.IsDoor == true && gate.size.x == 3 && gate.size.z == 2 ? gate : null;
        }

        internal static TerrainDef MedievalFloor()
        {
            return DefDatabase<TerrainDef>.GetNamedSilentFail("DankPyon_Floor_Cobblestone_Granite");
        }

        internal static bool RequiresPower(ThingDef def)
        {
            return def?.comps != null && def.comps.Any(p => p?.compClass != null && typeof(CompPowerTrader).IsAssignableFrom(p.compClass));
        }

        internal static bool IsCeAmmo(ThingDef def)
        {
            Type type = def?.GetType();
            while (type != null)
            {
                if (type.FullName == "CombatExtended.AmmoDef")
                {
                    return true;
                }
                type = type.BaseType;
            }
            return false;
        }

        internal static string RationDefName(Pawn pawn)
        {
            return IsMedieval(pawn?.Faction) ? "Pemmican" : "MealSurvivalPack";
        }

        internal static string RationDefName(Faction faction)
        {
            return IsMedieval(faction) ? "Pemmican" : "MealSurvivalPack";
        }

        internal static void ReplaceModernThings(Map map, Faction faction, CellRect area)
        {
            if (map?.Parent is not Settlement settlement || faction == null || faction.IsPlayer ||
                settlement.Faction != faction || !IsMedieval(faction))
            {
                return;
            }
            var data = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
            var protectedIds = new HashSet<int>(data?.generationPlan?.rewardProtectedThingIds ?? new List<int>());

            List<Thing> things = new List<Thing>();
            foreach (IntVec3 cell in area.Cells)
            {
                if (cell.InBounds(map))
                {
                    things.AddRange(cell.GetThingList(map));
                }
            }

            foreach (Thing thing in things.Distinct().ToList())
            {
                if (thing == null || thing.Destroyed || thing.def == null)
                {
                    continue;
                }
                // Only newly generated generic loot is eligible for broad tech conversion.
                // Existing/native quest items, packed buildings and corpses keep their identity and contents.
                if (protectedIds.Contains(thing.thingIDNumber) || !thing.questTags.NullOrEmpty() ||
                    thing is MinifiedThing || thing is Corpse || thing is IThingHolder) continue;
                if (IsProtectedStrongholdDef(thing.def)) continue;

                string replacement = null;
                int multiplier = 1;
                switch (thing.def.defName)
                {
                    case "ComponentIndustrial": replacement = "DankPyon_ComponentBasic"; break;
                    case "ComponentSpacer": replacement = "Gold"; multiplier = 20; break;
                    case "Steel": replacement = "DankPyon_IronIngot"; break;
                    case "MedicineIndustrial": replacement = "MedicineHerbal"; multiplier = 2; break;
                    case "MealSurvivalPack": replacement = "Pemmican"; multiplier = 5; break;
                    case "Neutroamine": replacement = "MedicineHerbal"; break;
                    case "Chemfuel":
                    case "Prometheum": replacement = "DankPyon_Tar"; break;
                    case "Hyperweave": replacement = "DankPyon_Silk"; multiplier = 3; break;
                    case "Penoxycyline": replacement = "MedicineHerbal"; multiplier = 2; break;
                    case "WakeUp": replacement = "Gold"; multiplier = 4; break;
                    case "Yayo": replacement = "Gold"; multiplier = 2; break;
                    case "Flake": replacement = "Gold"; break;
                    case "GoJuice": replacement = "Gold"; multiplier = 5; break;
                    case "Luciferium": replacement = "DankPyon_Miracle"; break;
                    case "ReinforcedBarrel": replacement = "Gold"; multiplier = 60; break;
                    case "MechSerumHealer": replacement = "DankPyon_Miracle"; multiplier = 6; break;
                    case "MechSerumResurrector": replacement = "DankPyon_Miracle"; multiplier = 8; break;
                    case "Shell_HighExplosive":
                    case "Shell_Incendiary":
                    case "Shell_EMP": replacement = "DankPyon_IronIngot"; multiplier = 2; break;
                }

                if (replacement == null && IsCeAmmo(thing.def))
                {
                    replacement = "DankPyon_IronIngot";
                }
                if (replacement == null && IsOutOfContextBlueprint(thing.def))
                {
                    ReplaceThingByMarketValue(thing, "Gold");
                    continue;
                }
                if (replacement == null && IsOfficialModernItem(thing.def))
                {
                    ReplaceThingByMarketValue(thing, "Gold");
                    continue;
                }
                if (replacement != null)
                {
                    ReplaceThing(thing, replacement, multiplier);
                }
            }
        }

        internal static int MoveItemsOffConflictingLoadBlockers(Map map, Faction faction)
        {
            if (!IsSupportedHostileSettlement(map) || faction == null || faction != map.ParentFaction) return 0;
            StrongholdGenerationPlan plan = WorldComponent_Strongholds.Instance?.GetExisting(map.Parent.ID)?.generationPlan;
            int moved = 0;
            foreach (Thing item in map.listerThings.AllThings.Where(t => t.Spawned && t.def.category == ThingCategory.Item &&
                t.Faction != Faction.OfPlayer && t.OccupiedRect().Any(c => c.GetThingList(map).Any(blocker => blocker is Building &&
                    (blocker.def.building?.isNaturalRock == true || blocker.def.building?.isResourceRock == true ||
                     blocker.Faction == faction && StrongholdCenterPlanner.StructuralWall(blocker.def)) &&
                    GenSpawn.SpawningWipes(blocker.def, t.def)))).ToList())
            {
                IntVec3 original = item.Position;
                CellRect area = plan?.baseRect.Contains(original) == true ? plan.baseRect : CellRect.WholeMap(map);
                bool placed = false;
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(original, 65f, false))
                {
                    CellRect footprint = GenAdj.OccupiedRect(cell, item.Rotation, item.def.size);
                    if (footprint.Any(c => !c.InBounds(map) || !area.Contains(c) || !c.Standable(map) ||
                        c.GetEdifice(map) != null || c.GetFirstItem(map) != null || c.GetFirstPawn(map) != null)) continue;
                    item.DeSpawn();
                    try { GenSpawn.Spawn(item, cell, map, item.Rotation); placed = true; moved++; }
                    catch (Exception exception)
                    {
                        if (!item.Spawned) GenSpawn.Spawn(item, original, map, item.Rotation);
                        Log.Warning($"[EES Medieval Patch] Retained {item.ThingID} after load-conflict relocation failed: {exception}");
                    }
                    break;
                }
                if (!placed)
                    Log.Warning($"[EES Medieval Patch] Retained conflicting item {item.ThingID} at {original}; no safe cell in the same reward area. Native reload can refund the overlapped rock or own wall.");
            }
            if (moved > 0) Log.Message($"[EES Medieval Patch] Relocated {moved} exact item/rock-or-own-wall conflicts before ledger finalization; kept item and building contents for native reload.");
            return moved;
        }

        internal static void RecalculateVaultValue(Map map, StrongholdData data)
        {
            if (map == null || data == null || !IsMedieval(map.ParentFaction)) return;
            MapComponent_StrongholdSiege siege = map.GetComponent<MapComponent_StrongholdSiege>();
            CellRect vault = data.generationPlan?.vaultRect ?? siege?.eesVaultRect ?? CellRect.Empty;
            if (vault.Width <= 0 || vault.Height <= 0) return;

            HashSet<int> seen = new HashSet<int>();
            float value = 0f;
            foreach (Thing thing in StrongholdZoneUtility.RecoverableItems(map, vault))
                if (seen.Add(thing.thingIDNumber)) value += Math.Max(0f, thing.MarketValue * thing.stackCount);
            data.vaultValue = value;
            data.vaultValueAnnounced = false;
            Log.Message($"[EES Medieval Patch] Final vault value recalculated: faction={map.ParentFaction?.def?.defName}, rect={vault}, value={value:0.##}.");
        }

        private static float AddVaultHolderValue(Thing holderThing, HashSet<int> seen)
        {
            if (!(holderThing is IThingHolder holder)) return 0f;
            ThingOwner owner = holder.GetDirectlyHeldThings();
            if (owner == null) return 0f;
            float value = 0f;
            foreach (Thing thing in owner) value += AddVaultThingValue(thing, seen);
            return value;
        }

        private static float AddVaultThingValue(Thing thing, HashSet<int> seen)
        {
            if (thing == null || !seen.Add(thing.thingIDNumber)) return 0f;
            float value = thing.def?.category == ThingCategory.Item ? thing.MarketValue * thing.stackCount : 0f;
            if (thing is IThingHolder holder)
            {
                ThingOwner owner = holder.GetDirectlyHeldThings();
                if (owner != null) foreach (Thing child in owner) value += AddVaultThingValue(child, seen);
            }
            return value;
        }

        private static bool IsOutOfContextBlueprint(ThingDef def)
        {
            if (def == null || def.category != ThingCategory.Item || string.IsNullOrEmpty(def.defName))
            {
                return false;
            }

            // Research books and construction blueprints can also carry medieval content.
            CompProperties_Techprint techprint = def.GetCompProperties<CompProperties_Techprint>();
            return techprint?.project != null && techprint.project.techLevel > TechLevel.Medieval;
        }

        internal static bool HasValidVaultEnclosure(Map map, CellRect rect)
        {
            if (map == null || rect.Width < 3 || rect.Height < 3)
            {
                return false;
            }

            CellRect clipped = rect.ClipInsideMap(map);
            int edgeCount = 0;
            int enclosedCount = 0;
            int doorCount = 0;
            foreach (IntVec3 cell in clipped.EdgeCells)
            {
                edgeCount++;
                Building edifice = cell.GetEdifice(map);
                if (edifice?.def == null || edifice.Destroyed)
                {
                    continue;
                }

                if (edifice.def.IsDoor)
                {
                    doorCount++;
                    enclosedCount++;
                }
                else if (edifice.def.passability == Traversability.Impassable)
                {
                    enclosedCount++;
                }
            }

            return edgeCount > 0 && doorCount > 0 && enclosedCount == edgeCount;
        }

        internal static bool TryRefogVaultInterior(Map map, CellRect rect, out int foggedInteriorCells)
        {
            foggedInteriorCells = 0;
            if (!HasValidVaultEnclosure(map, rect))
            {
                return false;
            }

            map.regionAndRoomUpdater.TryRebuildDirtyRegionsAndRooms();

            CellRect clipped = rect.ClipInsideMap(map);
            CellRect interior = clipped.ContractedBy(1);
            if (interior.Width < 1 || interior.Height < 1)
            {
                return false;
            }

            HashSet<IntVec3> wallCells = new HashSet<IntVec3>();
            HashSet<IntVec3> doorCells = new HashSet<IntVec3>();
            HashSet<IntVec3> doorApproachCells = new HashSet<IntVec3>();
            bool hasInteriorSpace = false;
            bool doorApproachReachable = false;
            var indoorRooms = new Dictionary<Room, bool>();
            foreach (IntVec3 cell in interior.Cells)
            {
                if (StrongholdIndoorFog.IsInteriorSpace(map, cell, indoorRooms))
                {
                    hasInteriorSpace = true;
                    break;
                }
            }

            foreach (IntVec3 edgeCell in clipped.EdgeCells)
            {
                Building edifice = edgeCell.GetEdifice(map);
                if (edifice == null)
                {
                    continue;
                }

                foreach (IntVec3 occupied in edifice.OccupiedRect().Cells)
                {
                    if (edifice.def.IsDoor)
                    {
                        doorCells.Add(occupied);
                    }
                    else
                    {
                        wallCells.Add(occupied);
                    }
                }
            }

            foreach (IntVec3 doorCell in doorCells)
            {
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 adjacent = doorCell + direction;
                    if (!adjacent.InBounds(map) || adjacent.GetEdifice(map) != null)
                    {
                        continue;
                    }

                    if (interior.Contains(adjacent))
                    {
                        doorApproachCells.Add(adjacent);
                    }
                    else if (!clipped.Contains(adjacent) && adjacent.Standable(map) &&
                        map.reachability.CanReachMapEdge(adjacent,
                            TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly, false, false, false, true, false)))
                    {
                        doorApproachCells.Add(adjacent);
                        doorApproachReachable = true;
                    }
                }
            }

            if (!hasInteriorSpace || doorCells.Count == 0 || !doorApproachReachable)
            {
                return false;
            }

            foreach (IntVec3 cell in interior.Cells)
            {
                if (StrongholdIndoorFog.IsInteriorSpace(map, cell, indoorRooms))
                    map.fogGrid.Refog(new CellRect(cell.x, cell.z, 1, 1));
            }
            foreach (IntVec3 cell in wallCells.Concat(doorCells).Concat(clipped.EdgeCells).Concat(doorApproachCells))
            {
                if (cell.InBounds(map)) map.fogGrid.Unfog(cell);
            }
            StrongholdIndoorFog.RevealExterior(map);

            foggedInteriorCells = interior.Cells.Count(cell => StrongholdIndoorFog.IsInteriorSpace(map, cell, indoorRooms) && map.fogGrid.IsFogged(cell));
            int foggedBoundaryCells = clipped.EdgeCells.Count(cell => map.fogGrid.IsFogged(cell));
            Log.Message($"[EES Medieval Patch] Vault interior refog: rawRect={rect}, interior={interior}, " +
                $"wallCells={wallCells.Count}, doorCells={doorCells.Count}, foggedInteriorCells={foggedInteriorCells}, " +
                $"foggedBoundaryCells={foggedBoundaryCells}, doorApproachReachable={doorApproachReachable.ToString().ToLowerInvariant()}.");
            return foggedBoundaryCells == 0;
        }

        internal static void DiscardOrphanVault(Map map, MapComponent_StrongholdSiege siege)
        {
            if (map == null || siege == null || siege.eesVaultRect.Width < 2)
            {
                return;
            }

            CellRect orphanRect = siege.eesVaultRect.ClipInsideMap(map);
            foreach (IntVec3 cell in orphanRect.Cells)
            {
                if (map.roofGrid.RoofAt(cell) == RoofDefOf.RoofRockThick)
                {
                    map.roofGrid.SetRoof(cell, null);
                }
                map.fogGrid.Unfog(cell);
            }
            siege.eesVaultRect = CellRect.Empty;
            Log.Warning($"[EES Medieval Patch] Removed an orphan EES vault marker at {orphanRect.CenterCell}; no enclosed vault was generated.");
        }

        internal static void RemoveEmptyMinifiedThings(Map map, Faction faction, CellRect area)
        {
            if (map == null || !IsMedieval(faction))
            {
                return;
            }

            HashSet<MinifiedThing> emptyMinifiedThings = new HashSet<MinifiedThing>();
            foreach (IntVec3 cell in area.Cells)
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                foreach (Thing thing in cell.GetThingList(map))
                {
                    if (thing is MinifiedThing minified && minified.InnerThing == null)
                    {
                        emptyMinifiedThings.Add(minified);
                    }
                }
            }

            foreach (MinifiedThing minified in emptyMinifiedThings)
            {
                if (!minified.Destroyed)
                {
                    minified.Destroy(DestroyMode.Vanish);
                }
            }
        }

        private static bool IsOfficialModernItem(ThingDef def)
        {
            string packageId = def?.modContentPack?.PackageId;
            return def != null && def.category == ThingCategory.Item && def.techLevel > TechLevel.Medieval &&
                packageId != null && packageId.StartsWith("ludeon.rimworld", StringComparison.OrdinalIgnoreCase);
        }

        private static void ReplaceThingByMarketValue(Thing oldThing, string replacementDefName)
        {
            ThingDef replacementDef = DefDatabase<ThingDef>.GetNamedSilentFail(replacementDefName);
            if (replacementDef == null || replacementDef.BaseMarketValue <= 0f)
            {
                return;
            }

            float unitValue = Math.Max(1f, oldThing.MarketValue);
            int multiplier = Math.Max(1, (int)Math.Round(unitValue / replacementDef.BaseMarketValue));
            ReplaceThing(oldThing, replacementDefName, multiplier);
        }

        private static void ReplaceThing(Thing oldThing, string replacementDefName, int multiplier)
        {
            ThingDef replacementDef = DefDatabase<ThingDef>.GetNamedSilentFail(replacementDefName);
            Map map = oldThing.Map;
            IntVec3 position = oldThing.Position;
            int remaining = Math.Max(1, oldThing.stackCount * multiplier);
            if (replacementDef == null || map == null)
            {
                return;
            }

            List<Thing> replacements = new List<Thing>();
            while (remaining > 0)
            {
                Thing replacement = ThingMaker.MakeThing(replacementDef);
                replacement.stackCount = Math.Min(remaining, Math.Max(1, replacementDef.stackLimit));
                remaining -= replacement.stackCount;
                // Avoid merging until every replacement is placed, so a failed batch can be rolled back.
                if (!GenPlace.TryPlaceThing(replacement, position, map, ThingPlaceMode.Near,
                    extraValidator: cell => !cell.GetThingList(map).Any(t => t.CanStackWith(replacement))))
                {
                    foreach (Thing placed in replacements) placed.Destroy(DestroyMode.Vanish);
                    if (!replacement.Destroyed) replacement.Destroy(DestroyMode.Vanish);
                    Log.Warning($"[EES Medieval Patch] Retained {oldThing.def.defName} x{oldThing.stackCount}: replacement {replacementDefName} could not be placed at {position}.");
                    return;
                }
                replacements.Add(replacement);
            }
            oldThing.Destroy(DestroyMode.Vanish);
        }

        internal static bool IsProtectedStrongholdDef(ThingDef def)
        {
            string packageId = def?.modContentPack?.PackageId;
            return def?.defName?.StartsWith("EES_", StringComparison.OrdinalIgnoreCase) == true ||
                string.Equals(packageId, "wowgag.EvolvingEnemyStrongholds", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(packageId, "Gideon.CastleWalls", StringComparison.OrdinalIgnoreCase) ||
                packageId?.StartsWith("Phonicmas.RimDark.", StringComparison.OrdinalIgnoreCase) == true ||
                packageId?.StartsWith("Local.RimDark", StringComparison.OrdinalIgnoreCase) == true ||
                packageId?.StartsWith("Local.EvolvingEnemyStrongholds", StringComparison.OrdinalIgnoreCase) == true ||
                packageId?.StartsWith("Local.EES", StringComparison.OrdinalIgnoreCase) == true;
        }

        internal static void AddMedievalKingdomsVaultGear(Map map, Faction faction, StrongholdData data, CellRect area)
        {
            if (map == null || data == null || !IsMedievalKingdomsNpcFaction(faction) || data.ContentTier < 3 ||
                !MedievalKingdomsVaultGear.TryGetValue(faction.def.defName, out string[] gearNames))
            {
                return;
            }

            List<ThingDef> gear = gearNames.Select(DefDatabase<ThingDef>.GetNamedSilentFail).Where(def => def != null).ToList();
            if (gear.Count == 0)
            {
                return;
            }

            Rand.PushState(Gen.HashCombineInt(data.settlementId, 902173));
            try
            {
                int attempts = data.tier >= 16 ? 5 : data.tier == 15 ? 4 : data.tier == 14 ? 3 :
                    data.ContentTier >= 7 ? 2 : 1;
                for (int i = 0; i < attempts; i++)
                {
                    if (!Rand.Chance(data.tier >= 14 ? 1f - 0.08f * i : VaultGearChance(data.ContentTier, i)))
                    {
                        continue;
                    }

                    List<IntVec3> cells = area.Cells.Where(cell => cell.InBounds(map) && GenGrid.Standable(cell, map) &&
                        cell.GetFirstItem(map) == null).ToList();
                    if (cells.Count == 0)
                    {
                        break;
                    }

                    ThingDef def = gear.RandomElement();
                    Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
                    CompQuality quality = (thing as ThingWithComps)?.TryGetComp<CompQuality>();
                    if (quality != null)
                    {
                        quality.SetQuality(data.tier >= 14 ? HighTierVaultGearQuality(data.tier) :
                            VaultGearQuality(data.ContentTier), ArtGenerationContext.Outsider);
                    }
                    GenSpawn.Spawn(thing, cells.RandomElement(), map, WipeMode.Vanish);
                }
            }
            finally
            {
                Rand.PopState();
            }
        }

        internal static void AddAstartesVaultGear(Map map, Faction faction, StrongholdData data, CellRect area)
        {
            if (map == null || data == null || data.tier < 11 || !IsMedieval(faction))
            {
                return;
            }

            List<ThingDef> armor = AstartesArmorPool(data.tier);
            List<ThingDef> melee = AstartesMeleePool(data.tier);
            List<IntVec3> cells = area.Cells.Where(cell => cell.InBounds(map) && GenGrid.Standable(cell, map) &&
                cell.GetFirstItem(map) == null).ToList();
            AstartesVaultRewardState state = AstartesVaultRewardState.Current;
            if ((armor.Count == 0 && melee.Count == 0) || cells.Count == 0 || state == null ||
                !state.TryMarkGenerated(data.settlementId))
            {
                return;
            }

            Rand.PushState(Gen.HashCombineInt(data.settlementId, 40851311));
            try
            {
                if (cells.Count > 0 && armor.Count > 0 && Rand.Chance(AstartesArmorChance(data.tier)))
                {
                    SpawnAstartesReward(armor.RandomElement(), AstartesRewardQuality(data.tier), map, cells);
                }
                if (cells.Count > 0 && melee.Count > 0 && Rand.Chance(AstartesMeleeChance(data.tier)))
                {
                    SpawnAstartesReward(melee.RandomElement(), AstartesRewardQuality(data.tier), map, cells);
                }
            }
            finally
            {
                Rand.PopState();
            }
        }

        private static List<ThingDef> AstartesArmorPool(int tier)
        {
            List<string> names = new List<string>();
            if (ResearchFinished("BEWH_PowerArmorMkVII"))
            {
                names.AddRange(MkViiArmor);
            }
            if (tier >= 12 && ResearchFinished("BEWH_PowerArmorMkX"))
            {
                names.AddRange(MkXArmor);
                if (tier >= 13)
                {
                    names.AddRange(MkXChaplainArmor);
                }
            }
            return names.Select(DefDatabase<ThingDef>.GetNamedSilentFail)
                .Where(def => def?.apparel != null).ToList();
        }

        private static List<ThingDef> AstartesMeleePool(int tier)
        {
            if (!ResearchFinished("BEWH_AstartesWeapons"))
            {
                return new List<ThingDef>();
            }
            int count = tier >= 12 ? AstartesMelee.Length : 1;
            return AstartesMelee.Take(count).Select(DefDatabase<ThingDef>.GetNamedSilentFail)
                .Where(def => def != null && def.IsMeleeWeapon).ToList();
        }

        private static bool ResearchFinished(string defName)
        {
            ResearchProjectDef research = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defName);
            return research != null && research.IsFinished;
        }

        private static float AstartesArmorChance(int tier)
        {
            if (tier >= 16) return 1f;
            if (tier >= 15) return 0.95f;
            if (tier >= 14) return 0.85f;
            if (tier >= 13) return 0.75f;
            if (tier >= 12) return 0.55f;
            return 0.35f;
        }

        private static float AstartesMeleeChance(int tier)
        {
            if (tier >= 16) return 0.60f;
            if (tier >= 15) return 0.40f;
            if (tier >= 14) return 0.25f;
            if (tier >= 13) return 0.12f;
            if (tier >= 12) return 0.08f;
            return 0.05f;
        }

        private static QualityCategory AstartesRewardQuality(int tier)
        {
            float roll = Rand.Value;
            if (tier >= 14)
            {
                float legendary = tier >= 16 ? 0.40f : tier == 15 ? 0.25f : 0.15f;
                return roll < legendary ? QualityCategory.Legendary : QualityCategory.Masterwork;
            }
            if (tier >= 13)
            {
                return roll < 0.10f ? QualityCategory.Legendary :
                    roll < 0.55f ? QualityCategory.Masterwork : QualityCategory.Excellent;
            }
            if (tier >= 12)
            {
                return roll < 0.05f ? QualityCategory.Legendary :
                    roll < 0.35f ? QualityCategory.Masterwork : QualityCategory.Excellent;
            }
            return roll < 0.15f ? QualityCategory.Masterwork : QualityCategory.Excellent;
        }

        private static void SpawnAstartesReward(ThingDef def, QualityCategory quality, Map map, List<IntVec3> cells)
        {
            Thing thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            CompQuality compQuality = (thing as ThingWithComps)?.TryGetComp<CompQuality>();
            compQuality?.SetQuality(quality, ArtGenerationContext.Outsider);
            IntVec3 cell = cells.RandomElement();
            GenSpawn.Spawn(thing, cell, map, WipeMode.Vanish);
            cells.Remove(cell);
        }

        private static float VaultGearChance(int contentTier, int index)
        {
            if (contentTier >= 7) return index == 0 ? 1f : 0.65f;
            if (contentTier >= 6) return 0.8f;
            if (contentTier >= 5) return 0.6f;
            if (contentTier >= 4) return 0.4f;
            return 0.25f;
        }

        private static QualityCategory VaultGearQuality(int contentTier)
        {
            if (contentTier >= 7)
            {
                float roll = Rand.Value;
                return roll < 0.05f ? QualityCategory.Legendary :
                    roll < 0.35f ? QualityCategory.Masterwork : QualityCategory.Excellent;
            }
            if (contentTier >= 6) return Rand.Chance(0.3f) ? QualityCategory.Excellent : QualityCategory.Good;
            if (contentTier >= 5) return Rand.Chance(0.45f) ? QualityCategory.Good : QualityCategory.Normal;
            return Rand.Chance(0.25f) ? QualityCategory.Poor : QualityCategory.Normal;
        }

        private static QualityCategory HighTierVaultGearQuality(int tier)
        {
            float roll = Rand.Value;
            float legendary = tier >= 16 ? 0.35f : tier == 15 ? 0.20f : 0.10f;
            return roll < legendary ? QualityCategory.Legendary :
                roll < (tier >= 16 ? 0.85f : tier == 15 ? 0.75f : 0.65f)
                    ? QualityCategory.Masterwork : QualityCategory.Excellent;
        }

        internal static void RemoveModernBuildings(Map map, Faction faction, CellRect area)
        {
            if (map == null || !IsMedieval(faction))
            {
                return;
            }

            HashSet<string> blocked = new HashSet<string>
            {
                "CommsConsole", "GroundPenetratingScanner", "DeepDrill", "HiTechResearchBench", "MultiAnalyzer",
                "NutrientPasteDispenser", "NutrientPasteHopper", "Hopper", "BilliardsTable"
            };
            List<Thing> remove = new List<Thing>();
            foreach (IntVec3 cell in area.Cells)
            {
                if (cell.InBounds(map))
                {
                    remove.AddRange(cell.GetThingList(map).Where(t => t?.def != null &&
                        t.def.category == ThingCategory.Building && !IsProtectedStrongholdDef(t.def) &&
                        blocked.Contains(t.def.defName)));
                }
            }
            foreach (Thing thing in remove.Distinct().ToList())
            {
                if (!thing.Destroyed)
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
            }
        }

        internal static void NormalizeOuterRing(Map map, StrongholdData data, CellRect settlementRect)
        {
            Faction faction = map?.ParentFaction;
            if (!IsMedieval(faction) || UsesOpenPerimeter(faction) || data == null || data.tier >= 14)
            {
                return;
            }

            ThingDef barricade = DefDatabase<ThingDef>.GetNamedSilentFail("Barricade");
            if (barricade == null)
            {
                return;
            }

            ThingDef ceEmbrasure = StrongholdDefenseUtility.CeEmbrasure;
            ThingDef ceReinforcedEmbrasure = StrongholdDefenseUtility.CeReinforcedEmbrasure;
            CellRect ring = settlementRect.ExpandedBy(StrongholdAreaUtility.OuterRingOffsetFor(data));
            ReplaceRingCells(ring.EdgeCells, false);
            ReplaceRingCells(ring.ExpandedBy(1).EdgeCells, true);

            void ReplaceRingCells(IEnumerable<IntVec3> cells, bool outerWireRing)
            {
                foreach (IntVec3 cell in cells)
                {
                    if (!cell.InBounds(map))
                    {
                        continue;
                    }

                    List<Thing> things = cell.GetThingList(map).ToList();
                    foreach (Thing thing in things)
                    {
                        if (thing?.def?.defName == "BarbedWire")
                        {
                            thing.Destroy(DestroyMode.Vanish);
                            continue;
                        }
                        if (outerWireRing || (thing.def != ThingDefOf.Sandbags && thing.def != ceEmbrasure &&
                            thing.def != ceReinforcedEmbrasure &&
                            (thing.def != barricade || thing.Stuff == ThingDefOf.WoodLog)))
                        {
                            continue;
                        }

                        IntVec3 position = thing.Position;
                        thing.Destroy(DestroyMode.Vanish);
                        Thing replacement = ThingMaker.MakeThing(barricade, ThingDefOf.WoodLog);
                        replacement.SetFactionDirect(faction);
                        GenSpawn.Spawn(replacement, position, map, WipeMode.Vanish);
                    }
                }
            }
        }

        internal static bool BuildOpenOuterRing(Map map, StrongholdData data, CellRect settlementRect)
        {
            Faction faction = map?.ParentFaction;
            if (!UsesOpenPerimeter(faction) || data == null || settlementRect.Width < 5 || settlementRect.Height < 5)
                return false;

            ThingDef obstacle = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_CavalrySpike") ??
                DefDatabase<ThingDef>.GetNamedSilentFail("Barricade");
            if (obstacle == null)
            {
                Log.Warning("[EES Medieval Patch] Open perimeter has no cavalry spike or barricade definition.");
                return true;
            }
            ThingDef stuff = obstacle.defName == "DankPyon_CavalrySpike"
                ? DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_RawWood") ?? GenStuff.DefaultStuffFor(obstacle)
                : ThingDefOf.WoodLog;
            CellRect ring = CastleDefenseLayout.OpenRing(settlementRect,
                StrongholdAreaUtility.OuterRingOffsetFor(data), map.Size);
            IntVec3[] footprint = CastleDefenseLayout.OpenRingCells(ring).ToArray();
            ClearWallCells(map, footprint);
            int placed = 0;
            foreach (IntVec3 cell in footprint)
            {
                if (cell.GetThingList(map).Any(thing => thing.def == obstacle)) continue;
                SupportCastleEdgeCell(map, cell, obstacle);
                if (TryPlaceDefenseBuilding(map, faction, obstacle, stuff, cell)) placed++;
            }
            int missing = footprint.Count(cell => !cell.GetThingList(map).Any(thing => thing.def == obstacle));
            MapComponent_StrongholdSiege siege = map.GetComponent<MapComponent_StrongholdSiege>();
            if (siege != null) siege.openBase = true;
            Log.Message($"[EES Medieval Patch] Open perimeter: faction={faction.def.defName}, tier=T{data.tier}, layers=1, def={obstacle.defName}, newObstacles={placed}, missingCells={missing}, entranceWidth=5.");
            if (missing > 0)
                Log.Warning($"[EES Medieval Patch] Open perimeter retained existing buildings, pawns or unbuildable terrain at {missing} planned cells.");
            return true;
        }

        internal static bool BuildCastleOuterRing(Map map, StrongholdData data, CellRect settlementRect)
        {
            Faction faction = map?.ParentFaction;
            ThingDef wall = DefDatabase<ThingDef>.GetNamedSilentFail("JDS_WallsTwo");
            if (!IsMedieval(faction) || UsesOpenPerimeter(faction) || data == null || settlementRect.Width < 5 ||
                (data.tier < 14 && (IsFrenchRiverHarbor(map) || IsMongolCamp(map))) || wall == null)
            {
                return false;
            }
            if (HasCastleContour(map, data)) return BuildCastleContour(map, data);

            int offset = StrongholdAreaUtility.OuterRingOffsetFor(data);
            CellRect[] rings = CastleDefenseLayout.Rings(settlementRect, offset, data.tier, map.Size);
            if (data.tier >= 14 && rings.Length != 2)
                Log.Warning($"[EES Medieval Patch] T{data.tier} double wall needs eight cells of map margin; retaining single-wall fallback at {settlementRect}.");
            bool built = true;
            for (int layer = 0; layer < rings.Length; layer++)
                built &= BuildCastleRing(map, faction, data, rings[layer], wall, layer == 1);
            if (built && rings.Length == 2) BuildCastleBarbicans(map, faction, data, rings[0], rings[1]);
            return built;
        }

        internal static bool TryBuildHighTierCastlePerimeter(Map map, StrongholdData data, CellRect rect,
            out int buildings)
        {
            buildings = 0;
            ThingDef wall = DefDatabase<ThingDef>.GetNamedSilentFail("JDS_WallsTwo");
            if (UsesOpenPerimeter(map?.ParentFaction) || data == null || data.tier < 14 || wall == null || rect.Width < 12 || rect.Height < 12)
                return false;
            if (HasCastleContour(map, data))
            {
                BuildCastleContour(map, data);
                buildings = CastleContourPlanner.Cells(data.generationPlan.castleInnerPads.Concat(data.generationPlan.castleOuterPads))
                    .SelectMany(c => c.GetThingList(map)).OfType<Building>().Distinct().Count();
                return true;
            }
            CellRect inner = CastleDefenseLayout.Rings(rect,
                StrongholdAreaUtility.OuterRingOffsetFor(data), data.tier, map.Size)[0];
            bool built = BuildCastleRing(map, map.ParentFaction, data, inner, wall, false);
            buildings = inner.EdgeCells.SelectMany(cell => cell.GetThingList(map))
                .OfType<Building>().Distinct().Count();
            return built;
        }

        private static bool BuildCastleRing(Map map, Faction faction, StrongholdData data,
            CellRect ring, ThingDef wall, bool outer)
        {
            if (ring.Width < 12 || ring.Height < 12)
            {
                return false;
            }

            ThingDef castleGate = CastleGate();
            if (castleGate == null)
            {
                Log.Warning($"[EES Medieval Patch] Castle perimeter at {ring} lacks a valid JDS_Castle_Gate 3x2 Def; native-gate requirement is unmet, original objects retained without ordinary-door substitution.");
                return true; // Handled, not completed: prohibit Core's narrow-door fallback.
            }
            ThingDef fallbackWall = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_CastleWall") ?? ThingDefOf.Wall;
            int[] gateCenters = SelectCastleGateCenters(map, data, ring, outer, castleGate);
            EnsureCastleGateApproaches(map, ring, outer, fallbackWall, gateCenters);
            if (ring.EdgeCells.Any(cell => cell.GetThingList(map).Any(thing =>
                    thing.def?.defName == "JDS_WallsTwo" ||
                    thing.def?.defName == "JDS_I_Defense_Tower")) &&
                ring.EdgeCells.All(cell => HasOuterRingBuilding(map, cell)) &&
                ring.EdgeCells.Any(cell => cell.GetThingList(map).Any(IsDoorBuilding)) &&
                (castleGate == null || Enumerable.Range(0, 4).All(side =>
                    CastleDefenseLayout.GateFootprintAt(ring, side, gateCenters[side]).All(cell =>
                        cell.GetThingList(map).Any(thing => thing.def == castleGate)))))
            {
                foreach (Building_Door gate in map.listerThings.AllThings.OfType<Building_Door>().Where(gate =>
                    gate.def == castleGate && Enumerable.Range(0, 4).Any(side =>
                        gate.OccupiedRect() == CastleDefenseLayout.GateFootprintAt(ring, side, gateCenters[side]))).ToList())
                    EnsureCastleGateSupportWalls(map, faction, gate, fallbackWall);
                return true;
            }

            ClearWallCells(map, ring.EdgeCells.Concat(ring.ContractedBy(1).EdgeCells));
            ThingDef ceEmbrasure = StrongholdDefenseUtility.CeEmbrasure;
            ThingDef ceReinforcedEmbrasure = StrongholdDefenseUtility.CeReinforcedEmbrasure;
            foreach (IntVec3 cell in ring.EdgeCells)
            {
                foreach (Thing thing in cell.GetThingList(map).ToList())
                {
                    if ((MapGenerator.mapBeingGenerated == map || finalizingDefenseMap == map) &&
                        thing.Faction == faction && !IsCapturedNativeDefense(thing, map) &&
                        (thing.def == ThingDefOf.Sandbags || thing.def == ceEmbrasure ||
                        thing.def == ceReinforcedEmbrasure || thing.def.defName == "Barricade" ||
                        thing.def.defName == "BarbedWire"))
                    {
                        thing.Destroy(DestroyMode.Vanish);
                    }
                }
            }

            ThingDef straightTower = data.tier >= 6
                ? DefDatabase<ThingDef>.GetNamedSilentFail("JDS_I_Defense_Tower") : null;
            ThingDef cornerTower = data.tier >= 8
                ? DefDatabase<ThingDef>.GetNamedSilentFail("JDS_L_Defense_Tower") : null;
            ThingDef cornerWall = DefDatabase<ThingDef>.GetNamedSilentFail("JDS_Walls_L_connector");
            ThingDef granite = ThingDefOf.BlocksGranite;
            int towers = 0;
            int walls = 0;
            int doors = 0;

            // Place each real 3x2 gate once before the straight-wall sweep. The
            // complete occupied rectangle shares the wall's inward thickness.
            for (int side = 0; side < 4; side++)
            {
                CellRect footprint = CastleDefenseLayout.GateFootprintAt(ring, side, gateCenters[side]);
                ClearWallCells(map, footprint.Cells);
                foreach (IntVec3 cell in footprint) SupportCastleEdgeCell(map, cell, fallbackWall);
                bool exists = castleGate != null && footprint.All(cell => cell.GetThingList(map).Any(t => t.def == castleGate));
                if (exists || TryPlaceCastleFootprint(map, faction, castleGate, granite, footprint, CastleDefenseLayout.EdgeRotation(side)))
                {
                    if (!exists) doors++;
                    continue;
                }
                Log.Warning($"[EES Medieval Patch] Castle gate has no safe 3x2 footprint: side={side}, rect={footprint}; native-gate requirement is unmet, original objects retained without ordinary-door substitution.");
            }

            CellRect[] corners =
            {
                CellRect.FromLimits(ring.minX, ring.minZ, ring.minX + 1, ring.minZ + 1),
                CellRect.FromLimits(ring.maxX - 1, ring.minZ, ring.maxX, ring.minZ + 1),
                CellRect.FromLimits(ring.minX, ring.maxZ - 1, ring.minX + 1, ring.maxZ),
                CellRect.FromLimits(ring.maxX - 1, ring.maxZ - 1, ring.maxX, ring.maxZ)
            };
            for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
            {
                ClearWallCells(map, corners[cornerIndex].Cells);
                foreach (IntVec3 cell in corners[cornerIndex].Cells)
                {
                    foreach (Thing existing in cell.GetThingList(map).ToList())
                    {
                        if (cornerTower != null && (MapGenerator.mapBeingGenerated == map || finalizingDefenseMap == map) &&
                            existing.Faction == faction && existing.Faction != Faction.OfPlayer && existing is Building &&
                            !IsCapturedNativeDefense(existing, map) &&
                            StrongholdCenterPlanner.StructuralWall(existing.def) && !existing.def.IsDoor &&
                            !existing.OccupiedRect().Any(occupied => occupied.Roofed(map)) &&
                            existing.def.building?.isNaturalRock != true && existing.def.building?.isResourceRock != true)
                            existing.Destroy(DestroyMode.Vanish);
                    }
                    SupportCastleEdgeCell(map, cell, fallbackWall);
                }
                ThingDef piece = cornerTower ?? cornerWall;
                if (TryPlaceCastleFootprint(map, faction, piece, granite, corners[cornerIndex],
                    CastleDefenseLayout.CornerRotation(cornerIndex, piece == cornerTower)))
                {
                    if (piece == cornerTower) towers++;
                    else walls++;
                }
                else if (piece != cornerWall && TryPlaceCastleFootprint(map, faction, cornerWall,
                    granite, corners[cornerIndex], CastleDefenseLayout.CornerRotation(cornerIndex, false)))
                {
                    walls++;
                }
            }

            if (straightTower != null)
            {
                for (int side = 0; side < 4; side++)
                {
                    bool horizontal = side < 2;
                    int length = horizontal ? ring.Width : ring.Height;
                    foreach (int along in CastleDefenseLayout.TowerStarts(length, data.tier))
                    {
                        CellRect footprint = horizontal
                            ? CellRect.FromLimits(ring.minX + along, side == 0 ? ring.minZ : ring.maxZ - 1,
                                ring.minX + along + 1, side == 0 ? ring.minZ + 1 : ring.maxZ)
                            : CellRect.FromLimits(side == 2 ? ring.minX : ring.maxX - 1, ring.minZ + along,
                                side == 2 ? ring.minX + 1 : ring.maxX, ring.minZ + along + 1);
                        ClearWallCells(map, footprint.Cells);
                        foreach (IntVec3 cell in footprint.Cells)
                        {
                            SupportCastleEdgeCell(map, cell, fallbackWall);
                        }
                        if (TryPlaceCastleFootprint(map, faction, straightTower, granite, footprint,
                            CastleDefenseLayout.EdgeRotation(side)))
                        {
                            towers++;
                        }
                    }
                }
            }

            foreach (int z in new[] { ring.minZ, ring.maxZ })
            {
                for (int x = ring.minX; x <= ring.maxX; x++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (HasOuterRingBuilding(map, cell)) continue;
                    SupportCastleEdgeCell(map, cell, fallbackWall);
                    ThingDef cellStuff = map.terrainGrid.TerrainAt(cell) == TerrainDefOf.Bridge
                        ? ThingDefOf.WoodLog : granite;
                    int gateCenter = gateCenters[z == ring.minZ ? 0 : 1];
                    if (Math.Abs(x - gateCenter) <= 1)
                    {
                        continue;
                    }
                    int side = z == ring.minZ ? 0 : 1;
                    CellRect footprint = CastleDefenseLayout.WallFootprint(ring, side, x);
                    foreach (IntVec3 occupied in footprint) SupportCastleEdgeCell(map, occupied, fallbackWall);
                    if (TryPlaceCastleFootprint(map, faction, wall, granite, footprint, CastleDefenseLayout.EdgeRotation(side)))
                    {
                        walls++;
                    }
                    else if (TryPlaceDefenseBuilding(map, faction, fallbackWall, cellStuff, cell) ||
                        TryPlaceDefenseBuilding(map, faction, ThingDefOf.Wall, cellStuff, cell)) walls++;
                }
            }
            foreach (int x in new[] { ring.minX, ring.maxX })
            {
                for (int z = ring.minZ; z <= ring.maxZ; z++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (HasOuterRingBuilding(map, cell)) continue;
                    SupportCastleEdgeCell(map, cell, fallbackWall);
                    ThingDef cellStuff = map.terrainGrid.TerrainAt(cell) == TerrainDefOf.Bridge
                        ? ThingDefOf.WoodLog : granite;
                    int gateCenter = gateCenters[x == ring.minX ? 2 : 3];
                    if (Math.Abs(z - gateCenter) <= 1)
                    {
                        continue;
                    }
                    int side = x == ring.minX ? 2 : 3;
                    CellRect footprint = CastleDefenseLayout.WallFootprint(ring, side, z);
                    foreach (IntVec3 occupied in footprint) SupportCastleEdgeCell(map, occupied, fallbackWall);
                    if (TryPlaceCastleFootprint(map, faction, wall, granite, footprint, CastleDefenseLayout.EdgeRotation(side)))
                    {
                        walls++;
                    }
                    else if (TryPlaceDefenseBuilding(map, faction, fallbackWall, cellStuff, cell) ||
                        TryPlaceDefenseBuilding(map, faction, ThingDefOf.Wall, cellStuff, cell)) walls++;
                }
            }

            // An existing building on the outer edge may skip a two-deep wall
            // segment. Complete each native gate's actual end-wall requirements
            // explicitly; edge closure alone does not establish native support.
            foreach (Building_Door gate in map.listerThings.AllThings.OfType<Building_Door>().Where(gate =>
                gate.def == castleGate && Enumerable.Range(0, 4).Any(side =>
                    gate.OccupiedRect() == CastleDefenseLayout.GateFootprintAt(ring, side, gateCenters[side]))).ToList())
                EnsureCastleGateSupportWalls(map, faction, gate, fallbackWall);

            int openCells = ring.EdgeCells.Count(cell => !HasOuterRingBuilding(map, cell));
            int gateCells = ring.EdgeCells.Count(cell => cell.GetThingList(map).Any(IsDoorBuilding));
            int accessibleGates = Enumerable.Range(0, 4).Count(side => CastleGateApproachOpen(map, ring, side, gateCenters[side]));
            if (openCells > 0 || accessibleGates != 4)
            {
                Log.Warning($"[EES Medieval Patch] Castle outer ring at {ring.CenterCell}, tier=T{data.tier} has {openCells} open cells and {accessibleGates}/4 accessible full-depth entrances; check terrain and entrance access.");
            }
            int existingTowers = ring.EdgeCells.SelectMany(cell => cell.GetThingList(map))
                .Distinct().Count(thing => thing.def == straightTower || thing.def == cornerTower);
            Log.Message($"[EES Medieval Patch] Castle outer ring: tier=T{data.tier}, layer={(outer ? "outer" : "inner")}, walls={walls}, newGates={doors}, gateCells={gateCells}, accessibleGates={accessibleGates}, firingTowers={existingTowers}, newTowers={towers}, openCells={openCells}.");
            return true;
        }

        private static int[] SelectCastleGateCenters(Map map, StrongholdData data, CellRect ring, bool outer, ThingDef gateDef)
        {
            int[] centers = Enumerable.Range(0, 4).Select(side => CastleDefenseLayout.GateCenter(ring, side, outer)).ToArray();
            // Rectangular paired gates keep the barbican contract. The saved
            // high-tier contour already plans its own independent gate centers.
            if (data.tier >= 14 || !ConstructionContext(map)) return centers;
            var gates = map.listerThings.AllThings.OfType<Building_Door>()
                .Where(g => g.def == gateDef && g.Faction == map.ParentFaction).ToList();
            for (int side = 0; side < 4; side++)
            {
                int preferred = centers[side];
                int low = side < 2 ? ring.minX + 3 : ring.minZ + 3;
                int high = side < 2 ? ring.maxX - 3 : ring.maxZ - 3;
                var existing = gates.FirstOrDefault(g => g.Rotation == CastleDefenseLayout.EdgeRotation(side) &&
                    g.OccupiedRect() == CastleDefenseLayout.GateFootprintAt(ring, side,
                        side < 2 ? g.OccupiedRect().CenterCell.x : g.OccupiedRect().CenterCell.z));
                if (existing != null)
                {
                    centers[side] = side < 2 ? existing.OccupiedRect().CenterCell.x : existing.OccupiedRect().CenterCell.z;
                    continue;
                }
                foreach (int center in Enumerable.Range(low, high - low + 1)
                    .OrderBy(n => Math.Abs(n - preferred)).ThenBy(n => n))
                {
                    CellRect footprint = CastleDefenseLayout.GateFootprintAt(ring, side, center);
                    CellRect corridor = CastleDefenseLayout.GateCorridor(footprint, CastleDefenseLayout.EdgeRotation(side));
                    if (!corridor.Min.InBounds(map) || !corridor.Max.InBounds(map) ||
                        footprint.Any(c => c.GetThingList(map).Any(t => IsProtectedPerimeterOccupant(t, map))) ||
                        corridor.Any(c => !CanPrepareCastlePassageCell(map, c)) ||
                        !CastleGateSupportAvailable(map, gateDef, footprint, CastleDefenseLayout.EdgeRotation(side))) continue;
                    centers[side] = center;
                    if (center != preferred)
                        Log.Message($"[EES Medieval Patch] Planned single-wall native gate: side={side}, preferred={preferred}, actual={center}, footprint={footprint}; protected objects retained.");
                    break;
                }
            }
            return centers;
        }

        private static bool CastleGateSupportAvailable(Map map, ThingDef gateDef, CellRect footprint, Rot4 rotation)
        {
            foreach (IntVec3 anchor in footprint.ExpandedBy(1))
            {
                if (GenAdj.OccupiedRect(anchor, rotation, gateDef.size) != footprint) continue;
                return DoorUtility.WallRequirementCells(gateDef, anchor, rotation).All(cell =>
                    cell.InBounds(map) && (DoorUtility.EncapsulatingWallAt(cell, map) ||
                        !cell.GetThingList(map).Any(t => IsProtectedPerimeterOccupant(t, map))));
            }
            return false;
        }

        private static bool EnsureCastleGateSupportWalls(Map map, Faction faction, Building_Door gate, ThingDef wall)
        {
            if (!ConstructionContext(map) || gate == null || gate.Destroyed || gate.Map != map ||
                gate.def != CastleGate() || gate.Faction != faction || IsCapturedNativeDefense(gate, map)) return false;
            var required = DoorUtility.WallRequirementCells(gate.def, gate.Position, gate.Rotation).ToList();
            foreach (IntVec3 cell in required)
            {
                if (!cell.InBounds(map) || DoorUtility.EncapsulatingWallAt(cell, map)) continue;
                // Keep actual existing supporting walls and all protected content.
                // Clear/move only the normal generation-eligible occupants.
                ClearWallCells(map, new[] { cell });
                SupportCastleEdgeCell(map, cell, wall);
                TryPlaceDefenseBuilding(map, faction, wall, ThingDefOf.BlocksGranite, cell);
            }
            var missing = required.Where(cell => !DoorUtility.EncapsulatingWallAt(cell, map)).ToList();
            if (missing.Count > 0)
                Log.Warning($"[EES Medieval Patch] Castle gate {gate.ThingID} at {gate.Position} lacks native support walls at {string.Join(",", missing)}; protected objects retained, gate closure is not complete.");
            return missing.Count == 0;
        }

        private static void BuildCastleBarbicans(Map map, Faction faction, StrongholdData data,
            CellRect inner, CellRect outer)
        {
            ThingDef wall = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_CastleWall") ?? ThingDefOf.Wall;
            IntVec3[] walls = CastleDefenseLayout.ChamberWalls(inner, outer).ToArray();
            ClearWallCells(map, walls);
            foreach (IntVec3 cell in walls)
            {
                SupportCastleEdgeCell(map, cell, wall);
                ThingDef stuff = map.terrainGrid.TerrainAt(cell) == TerrainDefOf.Bridge
                    ? ThingDefOf.WoodLog : ThingDefOf.BlocksGranite;
                TryPlaceDefenseBuilding(map, faction, wall, stuff, cell);
            }

            int connected = 0;
            for (int side = 0; side < 4; side++)
            {
                int center = CastleDefenseLayout.GateCenter(inner, side, false);
                List<IntVec3> approach = new List<IntVec3>();
                for (int depth = 1; depth < CastleDefenseLayout.WallGap; depth++)
                    for (int along = center - 1; along <= center + 5; along++)
                        approach.Add(CastleDefenseLayout.SideCell(outer, side, along, depth));
                ClearWallCells(map, approach);
                RelocateLooseDefenseBlockers(map, approach);
                foreach (IntVec3 cell in approach) SupportCastleEdgeCell(map, cell, wall);
                IntVec3 from = CastleDefenseLayout.SideCell(outer, side, center + 4);
                IntVec3 to = CastleDefenseLayout.SideCell(inner, side, center);
                if (from.GetThingList(map).Any(IsDoorBuilding) &&
                    to.GetThingList(map).Any(IsDoorBuilding) &&
                    map.reachability.CanReach(from, to, PathEndMode.OnCell,
                        TraverseParms.For(TraverseMode.PassDoors, Danger.Deadly))) connected++;
            }
            List<IntVec3> gapCells = walls.Where(cell => !HasOuterRingBuilding(map, cell)).Distinct().ToList();
            int gaps = gapCells.Count;
            Log.Message($"[EES Medieval Patch] Barbicans: tier=T{data.tier}, layers=2, wallGap={CastleDefenseLayout.WallGap}, connectorOpenCells={gaps}, connectedGatePairs={connected}/4.");
            if (gaps != 0 || connected != 4)
                Log.Warning($"[EES Medieval Patch] Incomplete barbican at {inner.CenterCell}: connectorOpenCells={gaps}, connectedGatePairs={connected}/4, gapCells={string.Join(",", gapCells)}; existing buildings and pawns were preserved.");
        }

        private static void EnsureCastleGateApproaches(Map map, CellRect ring, bool outer, ThingDef fallbackWall, int[] gateCenters)
        {
            for (int side = 0; side < 4; side++)
            {
                CellRect footprint = CastleDefenseLayout.GateFootprintAt(ring, side, gateCenters[side]);
                var approaches = new HashSet<IntVec3>(CastleDefenseLayout.GateCorridor(footprint,
                    CastleDefenseLayout.EdgeRotation(side)).Where(cell => cell.InBounds(map) && !footprint.Contains(cell)));
                foreach (Thing blocker in approaches.SelectMany(cell => cell.GetThingList(map)).Distinct().ToList())
                    if (!blocker.Destroyed && IsNaturalBlocker(blocker)) ClearNaturalBlocker(map, blocker, approaches);
                RelocateLooseDefenseBlockers(map, approaches);
                foreach (IntVec3 cell in approaches)
                    if (!cell.Walkable(map)) SupportCastleEdgeCell(map, cell, fallbackWall);
                if (!CastleGateApproachOpen(map, ring, side, gateCenters[side]))
                    Log.Warning($"[EES Medieval Patch] Castle gate approach remains blocked at {footprint}; side={side}, protected contents retained.");
            }
        }

        private static bool CastleGateApproachOpen(Map map, CellRect ring, int side, int center)
        {
            return Enumerable.Range(center - 1, 3).All(along => new[] { -1, 2 }.All(depth =>
            {
                IntVec3 cell = CastleDefenseLayout.SideCell(ring, side, along, depth);
                return cell.InBounds(map) && cell.Walkable(map);
            }));
        }

        private static bool IntersectsCastleGateEntrance(Map map, CellRect footprint)
        {
            return map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                .Where(thing => thing.def.defName == "JDS_Castle_Gate")
                .Any(gate => footprint.Overlaps(CastleDefenseLayout.GateCorridor(gate.OccupiedRect(), gate.Rotation)));
        }

        private static bool HasOuterRingBuilding(Map map, IntVec3 cell)
        {
            return cell.GetThingList(map).Any(thing => thing is Building &&
                (IsDoorBuilding(thing) || thing.def.passability == Traversability.Impassable ||
                    thing.def.building?.IsTurret == true));
        }

        private static bool IsDoorBuilding(Thing thing)
        {
            return thing is Building_Door || thing?.def?.IsDoor == true;
        }

        private static void SupportCastleEdgeCell(Map map, IntVec3 cell, ThingDef fallbackWall)
        {
            if (map == null || !cell.InBounds(map) ||
                (MapGenerator.mapBeingGenerated != map && finalizingDefenseMap != map) ||
                !IsSupportedHostileSettlement(map)) return;
            var plan = WorldComponent_Strongholds.Instance?.GetExisting(map.Parent.ID)?.generationPlan;
            if (plan?.legacyMap == true) return;
            TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
            TerrainDef foundation = DefDatabase<TerrainDef>.GetNamedSilentFail("EES_MedievalCastleFoundation");
            bool weakDryTerrain = terrain != null && !terrain.IsWater && !terrain.IsIce;
            // Only an actual new castle's wall/entrance footprint receives a
            // stone footing over deep water. In 1.6 BridgeBase occupies the
            // separate foundation layer and retains the native base terrain.
            bool waterFooting = terrain?.IsWater == true && !terrain.IsIce &&
                plan?.centerBuilt == true && plan.centerKind == "castle";
            if (fallbackWall != null && foundation != null && terrain != foundation &&
                (terrain?.affordances?.Contains(TerrainAffordanceDefOf.Bridgeable) == true || terrain?.bridge == true || weakDryTerrain || waterFooting) &&
                cell.GetEdifice(map) == null && !terrain.affordances.Contains(TerrainAffordanceDefOf.Heavy))
            {
                map.terrainGrid.SetTerrain(cell, foundation);
            }
        }

        private static bool TryPlaceCastleFootprint(Map map, Faction faction, ThingDef def, ThingDef stuff,
            CellRect footprint, Rot4 rotation)
        {
            if (def == null) return false;
            for (int x = footprint.minX - 1; x <= footprint.maxX + 1; x++)
            {
                for (int z = footprint.minZ - 1; z <= footprint.maxZ + 1; z++)
                {
                    IntVec3 anchor = new IntVec3(x, 0, z);
                    CellRect occupied = GenAdj.OccupiedRect(anchor, rotation, def.size);
                    if (occupied.minX == footprint.minX && occupied.maxX == footprint.maxX &&
                        occupied.minZ == footprint.minZ && occupied.maxZ == footprint.maxZ)
                    {
                        return TryPlaceDefenseBuilding(map, faction, def, stuff, anchor, rotation);
                    }
                }
            }
            return false;
        }

        internal static void AddTieredOuterDefenses(Map map, StrongholdData data, CellRect settlementRect)
        {
            Faction faction = map?.ParentFaction;
            if (!IsMedieval(faction) || UsesOpenPerimeter(faction) || data == null || data.tier < 4 ||
                (data.tier < 14 && IsFrenchRiverHarbor(map)))
            {
                return;
            }

            int density = data.tier >= 11 ? 12 : data.tier >= 8 ? 8 : 4;
            int offset = StrongholdAreaUtility.OuterRingOffsetFor(data);
            int outerOffset = data.tier >= 14 ? CastleDefenseLayout.WallGap : offset;
            CellRect defenseRing = settlementRect.ExpandedBy(outerOffset + 5).ClipInsideMap(map);
            ThingDef trap = ThingDefOf.TrapSpike;
            ThingDef barricade = DefDatabase<ThingDef>.GetNamedSilentFail("Barricade");
            int traps = PlaceEvenly(trap, ThingDefOf.WoodLog, HasCastleContour(map, data)
                ? CastleContourPlanner.ExteriorCells(map, data.generationPlan, 5) : defenseRing.EdgeCells.ToList(), density, 1);
            int barricades = PlaceEvenly(barricade, ThingDefOf.WoodLog,
                HasCastleContour(map, data) ? CastleContourPlanner.ExteriorCells(map, data.generationPlan, 6) :
                defenseRing.ExpandedBy(1).ClipInsideMap(map).EdgeCells.ToList(), density, 2);

            int walls = 0;
            if (data.tier >= 8 && data.tier < 14 && !IsMongolCamp(map))
            {
                CellRect secondLine = settlementRect.ExpandedBy(offset + 3).ClipInsideMap(map);
                int segmentLength = data.tier >= 11 ? 8 : 5;
                StrongholdDefenseUtility.ResolveWall(map, faction, data.tier, out ThingDef wallDef, out ThingDef wallStuff);
                foreach (IntVec3 cell in secondLine.EdgeCells)
                {
                    bool nearHorizontalCorner = cell.x - secondLine.minX < segmentLength ||
                        secondLine.maxX - cell.x < segmentLength;
                    bool nearVerticalCorner = cell.z - secondLine.minZ < segmentLength ||
                        secondLine.maxZ - cell.z < segmentLength;
                    if ((cell.z == secondLine.minZ || cell.z == secondLine.maxZ) && !nearHorizontalCorner ||
                        (cell.x == secondLine.minX || cell.x == secondLine.maxX) && !nearVerticalCorner)
                    {
                        continue;
                    }
                    if (TryPlaceDefenseBuilding(map, faction, wallDef, wallStuff, cell))
                    {
                        walls++;
                    }
                }
            }

            Log.Message($"[EES Medieval Patch] Tiered outer defenses: faction={faction.def.defName}, tier=T{data.tier}, " +
                $"traps={traps}, barricades={barricades}, secondLineWalls={walls}.");

            int PlaceEvenly(ThingDef def, ThingDef stuff, List<IntVec3> cells, int count, int phase)
            {
                if (def == null || cells.Count == 0 || count <= 0)
                {
                    return 0;
                }
                int placed = 0;
                for (int i = 0; i < count; i++)
                {
                    int index = ((i * cells.Count) / count + phase * Math.Max(1, cells.Count / (count * 3))) % cells.Count;
                    if (TryPlaceDefenseBuilding(map, faction, def, stuff, cells[index]))
                    {
                        placed++;
                    }
                }
                return placed;
            }
        }

        private static bool TryPlaceDefenseBuilding(Map map, Faction faction, ThingDef def, ThingDef stuff,
            IntVec3 cell, Rot4 rotation = default(Rot4))
        {
            if (!ConstructionContext(map) || def == null || !cell.InBounds(map) || !GenConstruct.CanBuildOnTerrain(def, cell, map, rotation))
            {
                return false;
            }

            CellRect footprint = GenAdj.OccupiedRect(cell, rotation, def.size);
            // CanBuildOnTerrain already checks the whole footprint from the real anchor.
            if (footprint.Any(occupied => !occupied.InBounds(map)))
            {
                return false;
            }
            if (def.defName != "JDS_Castle_Gate" && IntersectsCastleGateEntrance(map, footprint)) return false;
            var occupants = footprint.SelectMany(occupied => occupied.GetThingList(map)).Distinct().ToList();
            // GenSpawn.WipeMode.Vanish also wipes items and plants. Reject
            // protected content before moving anything or creating a building.
            var wiped = occupants.Where(thing => GenSpawn.SpawningWipes(def, thing.def)).ToList();
            if (wiped.Any(thing => IsProtectedSpawnWipe(thing, faction))) return false;
            var blockers = occupants
                .Where(thing => thing.def.category == ThingCategory.Building || thing is Blueprint || thing is Frame).Distinct().ToList();
            if (blockers.Any(thing => !CanRelocateDefenseBlocker(thing, map, faction) && !CanRelocateLooseDefenseBlocker(thing, map))) return false;
            foreach (Plant plant in wiped.OfType<Plant>().Where(plant => plant.AllComps?.Count > 0))
                if (!RelocateWildLightPlant(map, plant, new HashSet<IntVec3>(footprint))) return false;
            foreach (Thing item in wiped.Where(thing => thing.def.category == ThingCategory.Item))
                if (!RelocateConstructionItem(map, item, new HashSet<IntVec3>(footprint))) return false;
            foreach (Thing blocker in blockers)
            {
                var reservedDefense = ReservedCastleConstructionCells(map);
                bool outdoors = blocker.OccupiedRect().All(point => !point.Roofed(map));
                if ((!StrongholdCenterPlanner.FindOutsideCell(map, blocker.def, blocker.Rotation, blocker.Position, footprint, out IntVec3 destination) ||
                    GenAdj.OccupiedRect(destination, blocker.Rotation, blocker.def.size).Any(point =>
                        reservedDefense.Contains(point) || outdoors && point.Roofed(map)) ||
                    IntersectsCastleGateEntrance(map, GenAdj.OccupiedRect(destination, blocker.Rotation, blocker.def.size))) &&
                    !FindLooseOutsideCell(map, blocker, footprint, out destination)) return false;
                IntVec3 original = blocker.Position;
                Rot4 originalRotation = blocker.Rotation;
                blocker.DeSpawn();
                try { SpawnRelocatedConstructionThing(blocker, destination, map, originalRotation); }
                catch { if (!blocker.Spawned) SpawnRelocatedConstructionThing(blocker, original, map, originalRotation); throw; }
            }

            foreach (Pawn pawn in footprint.SelectMany(occupied => occupied.GetThingList(map)).OfType<Pawn>().Distinct().ToList())
            {
                if (MapGenerator.mapBeingGenerated != map ||
                    !(pawn.Faction == faction || pawn.Faction == null && pawn.RaceProps.Animal) || pawn.IsPrisoner || pawn.Dead ||
                    !StrongholdCenterPlanner.FindOutsideCell(map, pawn.def, pawn.Rotation, pawn.Position, footprint, out IntVec3 safe)) return false;
                pawn.Position = safe;
                pawn.Notify_Teleported(endCurrentJob: false);
            }

            ThingDef actualStuff = def.MadeFromStuff ? (stuff ?? GenStuff.DefaultStuffFor(def)) : null;
            try
            {
                Thing thing = ThingMaker.MakeThing(def, actualStuff);
                thing.SetFactionDirect(faction);
                GenSpawn.Spawn(thing, cell, map, rotation, WipeMode.Vanish);
                StrongholdFeatureEffects.RegisterGeneratedTurret(thing);
                return true;
            }
            catch (Exception exception)
            {
                Log.WarningOnce($"[EES Medieval Patch] Could not place medieval defense {def.defName}: {exception.Message}",
                    Gen.HashCombineInt(def.shortHash, 742991));
                return false;
            }
        }

        private static bool IsProtectedSpawnWipe(Thing thing, Faction faction)
        {
            return thing.questTags?.Count > 0 || thing.Faction == Faction.OfPlayer ||
                thing.Faction != null && thing.Faction != faction ||
                thing is Plant plant && (plant.sown || plant.Faction != null || plant.GetType() != typeof(Plant) ||
                    plant.AllComps?.Count > 0 && !CanRelocateWildLightPlant(plant, plant.Map));
        }

        internal static bool IsProtectedPerimeterOccupant(Thing thing, Map map)
        {
            if (thing == null || thing.Destroyed) return false;
            if (!ConstructionContext(map) || thing.Map != map) return true;
            Faction faction = map.ParentFaction;
            if (thing is Pawn pawn)
                return MapGenerator.mapBeingGenerated != map ||
                    !(pawn.Faction == faction || pawn.Faction == null && pawn.RaceProps.Animal) || pawn.IsPrisoner || pawn.Dead;
            // Score the same clear-then-place chain that constructs the wall.
            // A natural-rock flag is not permission to remove an ore or owned rock.
            if (thing.def.building?.isResourceRock == true) return true;
            if (IsNaturalBlocker(thing)) return !CanClearNaturalBlocker(map, thing);
            if (GenSpawn.SpawningWipes(ThingDefOf.Wall, thing.def) && IsProtectedSpawnWipe(thing, faction)) return true;
            if (thing.def.category == ThingCategory.Building || thing is Blueprint || thing is Frame)
                return !CanRelocateDefenseBlocker(thing, map, faction) && !CanRelocateLooseDefenseBlocker(thing, map);
            // Eligible loose items still require a free destination at construction
            // time. A zero score is a planning result, not a placement guarantee.
            return false;
        }

        private static bool IsCapturedNativeDefense(Thing thing, Map map)
        {
            var plan = map?.Parent is Settlement site
                ? WorldComponent_Strongholds.Instance?.GetExisting(site.ID)?.generationPlan : null;
            return thing != null && plan?.nativeDefenseIds?.Contains(thing.thingIDNumber) == true;
        }

        internal static bool CanRelocateDefenseBlocker(Thing thing, Map map, Faction faction)
        {
            bool generation = ConstructionContext(map);
            if (!generation || thing is not Building || thing is Blueprint || thing is Frame || thing.questTags?.Count > 0 ||
                thing.TryGetComp<CompAssignableToPawn>()?.AssignedPawnsForReading.Count > 0 ||
                thing.def.building?.isResourceRock == true || IsCapturedNativeDefense(thing, map) ||
                thing.Faction == Faction.OfPlayer || thing.Faction != null && thing.Faction != faction ||
                IsProtectedStrongholdDef(thing.def) || thing.def.IsDoor ||
                thing.def.passability == Traversability.Impassable) return false;
            if (thing.def.building?.IsTurret == true && thing.Faction == faction) return true;
            // Natural MO beehives are standable, valuable one-cell buildings.
            // Keep their Thing/components when a new castle wall crosses them,
            // including hives beneath a native town roof.
            if (thing.def.defName is "Sarcophagus" or "DankPyon_BeeHive") return true;
            if (thing is IThingHolder || thing.OccupiedRect().Any(cell => cell.Roofed(map))) return false;
            // These known nonstructural ruins/deposits keep their original Thing and components when moved.
            return thing.def.building?.isInert == true || thing.def.defName is
                "DankPyon_RuinedBatteringRam" or "DankPyon_RuinedSiegeTower" or
                "DankPyon_AncientRuinedBanner" or "DankPyon_AncientRuinedCart" or
                "DankPyon_AncientRuinedCrate" or "DankPyon_AncientRuinedBarrel" or
                "DankPyon_RustedSwords" or "DankPyon_RustedHelmet" or "DankPyon_RustedHauberk" or
                "DankPyon_AncientFootman" or "DankPyon_TarPit" or "SteamGeyser" or
                "Table2x2c" or "Table1x2c" or "Table1x2cSmall" or "Sarcophagus" ||
                thing.def.passability == Traversability.PassThroughOnly && thing.OccupiedRect().Cells.All(cell => !cell.Roofed(map));
        }

        private static bool CanRelocateLooseDefenseBlocker(Thing thing, Map map)
        {
            bool generation = ConstructionContext(map);
            if (!generation || thing is not Building || thing is IThingHolder || thing.questTags?.Count > 0 ||
                thing.TryGetComp<CompAssignableToPawn>()?.AssignedPawnsForReading.Count > 0 ||
                thing.def.building?.isResourceRock == true || IsCapturedNativeDefense(thing, map) ||
                thing.Faction == Faction.OfPlayer || thing.Faction != null && thing.Faction != map.ParentFaction ||
                StrongholdCenterPlanner.StructuralWall(thing.def) && thing.OccupiedRect().Any(cell => cell.Roofed(map)) ||
                (IsProtectedStrongholdDef(thing.def) && !IsLooseDefenseStructure(thing)) ||
                (thing.def.building?.IsTurret == true && !IsLooseDefenseStructure(thing)) ||
                (!IsLooseDefenseStructure(thing) && !IsDoorBuilding(thing) && thing.OccupiedRect().Any(cell => cell.Roofed(map))))
            {
                return false;
            }

            if (IsDoorBuilding(thing)) return true;
            return IsLooseDefenseStructure(thing) || thing.def.building?.isInert == true || thing.def.defName is
                "DankPyon_AncientFootman" or "DankPyon_AncientRuinedBanner" or
                "DankPyon_AncientRuinedCart" or "DankPyon_AncientRuinedCrate" or
                "DankPyon_AncientRuinedBarrel" or "DankPyon_RustedSwords" or
                "DankPyon_RustedHelmet" or "DankPyon_RustedHauberk" or
                "DankPyon_TarPit" or "SteamGeyser" or
                "Table2x2c" or "Table1x2c" or "Table1x2cSmall" ||
                thing.def.passability == Traversability.PassThroughOnly;
        }

        private static bool IsLooseDefenseStructure(Thing thing)
        {
            string defName = thing?.def?.defName;
            return string.Equals(defName, "Wall", StringComparison.OrdinalIgnoreCase) ||
                defName?.EndsWith("Wall", StringComparison.OrdinalIgnoreCase) == true ||
                string.Equals(defName, "JDS_Defense_Tower", StringComparison.OrdinalIgnoreCase);
        }

        private static int RelocateLooseDefenseBlockers(Map map, IEnumerable<IntVec3> cells, bool reportBlocked = false)
        {
            var reserved = new CellRect(cells.Min(cell => cell.x), cells.Min(cell => cell.z),
                cells.Max(cell => cell.x) - cells.Min(cell => cell.x) + 1,
                cells.Max(cell => cell.z) - cells.Min(cell => cell.z) + 1);
            int relocated = 0;
            int retained = 0;
            foreach (Thing blocker in cells.SelectMany(cell => cell.GetThingList(map))
                .Where(thing => thing is Building && !thing.Destroyed).Distinct().ToList())
            {
                if ((!CanRelocateLooseDefenseBlocker(blocker, map) &&
                     !CanRelocateDefenseBlocker(blocker, map, map.ParentFaction)) ||
                    !FindLooseOutsideCell(map, blocker, reserved, out IntVec3 destination))
                {
                    // An ineligible object or failed safe relocation is not
                    // permission to destroy furniture, ruins or held contents.
                    retained++;
                    continue;
                }
                IntVec3 original = blocker.Position;
                Rot4 originalRotation = blocker.Rotation;
                blocker.DeSpawn();
                try
                {
                    SpawnRelocatedConstructionThing(blocker, destination, map, originalRotation);
                    relocated++;
                }
                catch (Exception exception)
                {
                    if (!blocker.Spawned) SpawnRelocatedConstructionThing(blocker, original, map, originalRotation);
                    Log.Warning($"[EES Medieval Patch] Retained {blocker.ThingID} after loose defense relocation failed: {exception}");
                }
            }
            if (reportBlocked && retained > 0)
                Log.Message($"[EES Medieval Patch] Preserved {retained} existing protected or unrelocatable buildings in the exact construction footprint; placement must retain explicit blockage.");
            return relocated;
        }

        private static bool FindLooseOutsideCell(Map map, Thing thing, CellRect reserved, out IntVec3 cell)
        {
            var plan = map.Parent is Settlement site ? WorldComponent_Strongholds.Instance?.GetExisting(site.ID)?.generationPlan : null;
            var reservedDefense = ReservedCastleConstructionCells(map);
            bool outdoors = thing.OccupiedRect().All(point => !point.Roofed(map));
            // GenRadial has a finite pattern, even when another mod expands it.
            // This generation-only fallback must also find safe cells beyond
            // that pattern without logging an invalid radius or truncating it.
            foreach (IntVec3 candidate in map.AllCells.OrderBy(candidate =>
                (candidate - thing.Position).LengthHorizontalSquared))
            {
                CellRect footprint = GenAdj.OccupiedRect(candidate, thing.Rotation, thing.def.size);
                if (footprint.Overlaps(reserved.ExpandedBy(2))) continue;
                // Moving an eligible ruin into a future wall or beneath a native
                // roof makes the next construction step irreparably blocked.
                if (footprint.Any(point => reservedDefense.Contains(point) || point.InBounds(map) && outdoors && point.Roofed(map))) continue;
                if (plan?.centerReserved == true && footprint.Overlaps(plan.centerRect.ExpandedBy(1)) ||
                    IntersectsCastleGateEntrance(map, footprint)) continue;
                bool clear = true;
                foreach (IntVec3 point in footprint)
                {
                    if (!point.InBounds(map) || !point.Standable(map) || point.GetEdifice(map) != null ||
                        point.GetFirstPawn(map) != null || point.GetFirstItem(map) != null)
                    {
                        clear = false;
                        break;
                    }
                }
                if (!clear) continue;
                cell = candidate;
                return true;
            }
            cell = IntVec3.Invalid;
            return false;
        }

        private static HashSet<IntVec3> ReservedCastleConstructionCells(Map map)
        {
            var plan = map?.Parent is Settlement site ? WorldComponent_Strongholds.Instance?.GetExisting(site.ID)?.generationPlan : null;
            var cells = new HashSet<IntVec3>();
            if (!ConstructionContext(map) || !CastleContourPlanner.HasPlan(plan, map)) return cells;
            cells.UnionWith(CastleContourPlanner.Cells(plan.castleInnerPads.Concat(plan.castleOuterPads)));
            cells.UnionWith(CastleContourPlanner.ChamberWalls(plan));
            cells.UnionWith(CastleContourPlanner.ChamberPassage(plan));
            for (int layer = 0; layer < 2; layer++)
            for (int side = 0; side < 4; side++)
                cells.UnionWith(CastleDefenseLayout.GateCorridor(CastleContourPlanner.Gate(plan, layer, side), CastleDefenseLayout.EdgeRotation(side)));
            return cells;
        }

        [ThreadStatic]
        internal static Map finalizingDefenseMap;

        internal static int AddFallbackMedievalTurrets(Map map, Faction faction, StrongholdData data, CellRect rect)
        {
            if (map == null || faction == null || data == null || !IsMedieval(faction) || rect.Width < 3)
            {
                return 0;
            }

            string[] candidates = data.tier >= 8
                ? new[] { "DankPyon_Turret_Trebuchet", "DankPyon_Turret_RepeaterBallista", "DankPyon_Turret_Ballista",
                    "VFEM2_Turret_WallMountedArquebus", "VFEM2_Turret_WallMountedArbalest", "DankPyon_Turret_Scorpio" }
                : data.tier >= 5
                    ? new[] { "DankPyon_Turret_Ballista", "VFEM2_Turret_WallMountedArbalest", "DankPyon_Turret_Scorpio" }
                    : new[] { "DankPyon_Turret_Scorpio", "VFEM2_Turret_WallMountedArbalest" };
            List<ThingDef> turrets = candidates.Select(DefDatabase<ThingDef>.GetNamedSilentFail)
                .Where(def => def != null).ToList();
            if (turrets.Count == 0)
            {
                Log.Error($"[EES Medieval Patch] No medieval fallback turret def is available for faction={faction.def.defName}, tier=T{data.tier}.");
                return 0;
            }

            int desired = data.tier >= 11 ? 4 : data.tier >= 8 ? 3 : data.tier >= 5 ? 2 : 1;
            HashSet<string> turretNames = new HashSet<string>(candidates, StringComparer.Ordinal);
            int existing = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                .Count(thing => thing.Faction == faction && turretNames.Contains(thing.def?.defName));
            int remaining = Math.Max(0, desired - existing);
            if (remaining == 0)
            {
                return 0;
            }

            int ringOffset = StrongholdAreaUtility.OuterRingOffsetFor(data);
            List<IntVec3> placementCells = new List<IntVec3>();
            if (HasCastleContour(map, data))
                foreach (int distance in new[] { 5, 3, 1 }) placementCells.AddRange(CastleContourPlanner.ExteriorCells(map, data.generationPlan, distance));
            foreach (int distance in new[] { ringOffset + 5, ringOffset + 3, ringOffset + 1, 0 })
            {
                if (HasCastleContour(map, data)) break;
                CellRect candidateRing = distance == 0
                    ? rect
                    : rect.ExpandedBy(distance).ClipInsideMap(map);
                placementCells.AddRange(candidateRing.EdgeCells);
            }

            int placed = 0;
            List<string> placedDefs = new List<string>();
            foreach (IntVec3 cell in placementCells.Distinct())
            {
                foreach (ThingDef turret in turrets)
                {
                    if (!TryPlaceDefenseBuilding(map, faction, turret, null, cell))
                    {
                        continue;
                    }

                    placed++;
                    placedDefs.Add(turret.defName);
                    break;
                }
                if (placed >= remaining)
                {
                    break;
                }
            }

            Log.Message($"[EES Medieval Patch] Fallback medieval turrets: faction={faction.def.defName}, tier=T{data.tier}, " +
                $"candidates={string.Join(",", turrets.Select(def => def.defName))}, existing={existing}, " +
                $"placed={placed}, placedDefs={string.Join(",", placedDefs)}, desired={desired}.");
            return placed;
        }

        internal static void EnsureMedievalOuterDefenses(Map map, Faction faction, StrongholdData data, CellRect rect)
        {
            if (map == null || faction == null || data == null || !IsMedieval(faction) || rect.Width < 5)
            {
                return;
            }

            if (BuildOpenOuterRing(map, data, rect)) return;
            BuildCastleOuterRing(map, data, rect);
            NormalizeOuterRing(map, data, rect);
            AddTieredOuterDefenses(map, data, rect);
            AddFallbackMedievalTurrets(map, faction, data, rect);
        }

        internal static int AddMedievalRoleFacilities(Map map, Faction faction, StrongholdData data,
            CellRect interior, HashSet<IntVec3> occupied)
        {
            if (map == null || data == null || !IsMedieval(faction))
            {
                return 0;
            }

            string role = data.roleDefName ?? string.Empty;
            List<string> facilities = new List<string>();
            if (role.IndexOf("supply", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                facilities.AddRange(new[]
                {
                    "Campfire", "DankPyon_RusticBarrelClosed", "DankPyon_RusticBarrelClosed",
                    "DankPyon_RusticChest", "DankPyon_IceChest"
                });
            }
            else if (role.IndexOf("black", StringComparison.OrdinalIgnoreCase) >= 0 ||
                role.IndexOf("market", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                facilities.AddRange(new[]
                {
                    "DankPyon_TableMarket_MeleeWeapons", "DankPyon_TableMarket_Bow",
                    "DankPyon_RusticChest", "DankPyon_RoyalChest"
                });
            }
            else if (role.IndexOf("prison", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                facilities.AddRange(new[]
                {
                    "VFEM2_TrainingDummy", "VFEM2_FurBed", "DankPyon_RusticChest", "Campfire"
                });
            }
            else if (role.IndexOf("psychic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                role.IndexOf("shrine", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                facilities.AddRange(new[]
                {
                    "DankPyon_ArmorStatue", "DankPyon_Brazier1x1c", "DankPyon_Brazier1x1c",
                    "DankPyon_RoyalThrone"
                });
            }
            else if (role.IndexOf("noble", StringComparison.OrdinalIgnoreCase) >= 0 ||
                role.IndexOf("estate", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                facilities.AddRange(new[]
                {
                    "DankPyon_RoyalThrone", "DankPyon_RoyalChest", "VFEM2_DoubleFurBed",
                    "VFEM2_StandingBanner", "DankPyon_Brazier1x1c"
                });
            }
            else if (role.IndexOf("command", StringComparison.OrdinalIgnoreCase) >= 0 ||
                role.IndexOf("fortress", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                facilities.AddRange(new[]
                {
                    "VFEM2_ResearchBoard", "DankPyon_ScribeTable", "VFEM2_StandingBanner",
                    "VFEM2_ArcheryTarget", "VFEM2_TrainingDummy", "DankPyon_Brazier1x1c"
                });
            }

            int limit = data.tier >= 11 ? facilities.Count : data.tier >= 8 ? Math.Min(5, facilities.Count) :
                data.tier >= 5 ? Math.Min(4, facilities.Count) : Math.Min(3, facilities.Count);
            int placed = 0;
            for (int i = 0; i < limit; i++)
            {
                ThingDef facility = StrongholdCultureCenters.FurnitureFor(facilities[i], data.tier);
                if (facility == null) continue;
                ThingDef stuff = facility.defName.IndexOf("Barrel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    facility.defName.IndexOf("Chest", StringComparison.OrdinalIgnoreCase) >= 0
                    ? ThingDefOf.WoodLog : null;
                if (TryPlaceBuilding(map, faction, interior, facility.defName, stuff, occupied))
                {
                    placed++;
                }
            }
            return placed;
        }

        private static bool TryPlaceBuilding(Map map, Faction faction, CellRect area, string defName,
            ThingDef stuff = null, HashSet<IntVec3> occupied = null)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                return false;
            }

            IntVec3 center = area.CenterCell;
            Rot4 rotation = Rot4.North;
            foreach (IntVec3 cell in area.Cells.OrderBy(c => Math.Abs(c.x - center.x) + Math.Abs(c.z - center.z)))
            {
                CellRect footprint = GenAdj.OccupiedRect(cell, rotation, def.size);
                if (footprint.Cells.Any(c => !area.Contains(c)) || !GenConstruct.CanBuildOnTerrain(def, cell, map, rotation) ||
                    footprint.Cells.Any(c => !c.InBounds(map) || !GenGrid.Standable(c, map) ||
                        occupied?.Contains(c) == true || GridsUtility.GetEdifice(c, map) != null ||
                        c.GetThingList(map).Any(t => t is Pawn || t.def.category == ThingCategory.Building)))
                {
                    continue;
                }

                Thing building = ThingMaker.MakeThing(def, def.MadeFromStuff ? (stuff ?? GenStuff.DefaultStuffFor(def)) : null);
                building.SetFactionDirect(faction);
                GenSpawn.Spawn(building, cell, map, rotation, WipeMode.Vanish);
                if (occupied != null)
                {
                    foreach (IntVec3 usedCell in footprint.Cells)
                    {
                        occupied.Add(usedCell);
                    }
                }
                return true;
            }
            return false;
        }
    }

    [HarmonyPatch]
    internal static class MedievalRationsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(StrongholdGarrisonUtility), "GiveSiegeRations");
            yield return AccessTools.Method(AccessTools.TypeByName("EvolvingEnemyStrongholds.SupplyCaravanUtility"), "MakeCargo");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            MethodInfo selector = __originalMethod.GetParameters()[0].ParameterType == typeof(Pawn)
                ? AccessTools.Method(typeof(MedievalUtility), nameof(MedievalUtility.RationDefName), new[] { typeof(Pawn) })
                : AccessTools.Method(typeof(MedievalUtility), nameof(MedievalUtility.RationDefName), new[] { typeof(Faction) });

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ldstr && instruction.operand as string == "MealSurvivalPack")
                {
                    instruction.opcode = OpCodes.Ldarg_0;
                    instruction.operand = null;
                    yield return instruction;
                    yield return new CodeInstruction(OpCodes.Call, selector);
                }
                else
                {
                    yield return instruction;
                }
            }
        }
    }

    [HarmonyPatch]
    internal static class MedievalEraPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(StrongholdScoreUtility), "EraFor");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = instructions.ToList();
            FieldInfo techLevel = AccessTools.Field(typeof(FactionDef), nameof(FactionDef.techLevel));
            MethodInfo effectiveTech = AccessTools.Method(typeof(MedievalUtility), nameof(MedievalUtility.StrongholdGrowthTechLevel));
            foreach (CodeInstruction code in codes)
            {
                if (code.opcode == OpCodes.Ldfld && Equals(code.operand, techLevel))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = effectiveTech;
                }
            }
            for (int i = 4; i < codes.Count; i++)
            {
                ConstructorInfo ctor = codes[i].operand as ConstructorInfo;
                if (codes[i].opcode == OpCodes.Newobj && ctor?.DeclaringType?.Name == "EraSpec" &&
                    LdcValue(codes[i - 4]) == 3 && LdcValue(codes[i - 3]) == 4 &&
                    LdcValue(codes[i - 2]) == 5 && LdcValue(codes[i - 1]) == 6)
                {
                    SetLdc(codes[i - 4], 1);
                    SetLdc(codes[i - 3], 3);
                    SetLdc(codes[i - 2], 13);
                    SetLdc(codes[i - 1], 13);
                    return codes;
                }
            }

            Log.Error("[EES Medieval Patch] EraFor pattern was not found; medieval T13 growth was not applied.");
            return codes;
        }

        private static int? LdcValue(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Ldc_I4_M1) return -1;
            if (instruction.opcode == OpCodes.Ldc_I4_0) return 0;
            if (instruction.opcode == OpCodes.Ldc_I4_1) return 1;
            if (instruction.opcode == OpCodes.Ldc_I4_2) return 2;
            if (instruction.opcode == OpCodes.Ldc_I4_3) return 3;
            if (instruction.opcode == OpCodes.Ldc_I4_4) return 4;
            if (instruction.opcode == OpCodes.Ldc_I4_5) return 5;
            if (instruction.opcode == OpCodes.Ldc_I4_6) return 6;
            if (instruction.opcode == OpCodes.Ldc_I4_7) return 7;
            if (instruction.opcode == OpCodes.Ldc_I4_8) return 8;
            if (instruction.opcode == OpCodes.Ldc_I4_S) return Convert.ToInt32(instruction.operand);
            if (instruction.opcode == OpCodes.Ldc_I4) return (int)instruction.operand;
            return null;
        }

        private static void SetLdc(CodeInstruction instruction, int value)
        {
            instruction.opcode = OpCodes.Ldc_I4;
            instruction.operand = value;
        }
    }

    [HarmonyPatch(typeof(StrongholdScoreUtility), nameof(StrongholdScoreUtility.EraSaturation))]
    internal static class MedievalSaturationPatch
    {
        private static void Postfix(Faction faction, ref float __result)
        {
            if (MedievalUtility.IsMedieval(faction))
            {
                __result = 1f;
            }
        }
    }

    [HarmonyPatch]
    internal static class RatkinStrongholdTechPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(StrongholdScoreUtility), "EraSaturation");
            yield return AccessTools.Method(typeof(GarrisonGroupUtility), "EliteTierEligible");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo techLevel = AccessTools.Field(typeof(FactionDef), nameof(FactionDef.techLevel));
            MethodInfo effectiveTech = AccessTools.Method(typeof(MedievalUtility), nameof(MedievalUtility.StrongholdTechLevel));
            foreach (CodeInstruction code in instructions)
            {
                if (code.opcode == OpCodes.Ldfld && Equals(code.operand, techLevel))
                {
                    code.opcode = OpCodes.Call;
                    code.operand = effectiveTech;
                }
                yield return code;
            }
        }
    }

    internal static class MedievalHighTierScale
    {
        internal static int HighTierReinforcements(int tier)
        {
            return tier >= 16 ? 28 : tier == 15 ? 24 : tier == 14 ? 20 : 0;
        }

        internal static int ExtraExpansion(int tier)
        {
            return tier >= 16 ? 12 : tier == 15 ? 8 : tier == 14 ? 4 : 0;
        }

        internal static int ExtraGarrison(int tier)
        {
            return tier >= 16 ? 54 : tier == 15 ? 34 : tier == 14 ? 16 : 0;
        }

        internal static float RewardMultiplier(int tier)
        {
            return tier >= 16 ? 3.2f : tier == 15 ? 2.35f : tier == 14 ? 1.6f : 1f;
        }

        internal static QualityCategory GearQualityFloor(int tier, bool elite, float roll)
        {
            if (elite)
                return tier >= 16 && roll < 0.25f ? QualityCategory.Legendary :
                    tier >= 15 ? QualityCategory.Masterwork : QualityCategory.Excellent;
            return tier >= 16 && roll < 0.25f ? QualityCategory.Masterwork :
                tier >= 15 ? QualityCategory.Excellent : QualityCategory.Good;
        }

        internal static void ImproveGear(Pawn pawn, int tier, bool elite)
        {
            if (pawn == null || tier < 14) return;
            float roll = (unchecked((uint)Gen.HashCombineInt(pawn.thingIDNumber, tier)) >> 8) / 16777216f;
            QualityCategory floor = GearQualityFloor(tier, elite, roll);
            Improve(pawn.equipment?.Primary);
            if (pawn.apparel != null)
                foreach (Apparel apparel in pawn.apparel.WornApparel) Improve(apparel);

            void Improve(ThingWithComps thing)
            {
                CompQuality quality = thing?.TryGetComp<CompQuality>();
                if (quality != null && quality.Quality < floor)
                    quality.SetQuality(floor, ArtGenerationContext.Outsider);
            }
        }
    }

    [HarmonyPatch(typeof(StrongholdAreaUtility), nameof(StrongholdAreaUtility.ExpandArea))]
    internal static class MedievalHighTierAreaPatch
    {
        private static void Postfix(Map map, StrongholdData data, ref CellRect __result)
        {
            int extra = MedievalHighTierScale.ExtraExpansion(data?.tier ?? 0);
            if (extra == 0 || !MedievalUtility.IsMedieval(map?.ParentFaction) || __result.Width < 5)
            {
                return;
            }

            CellRect bounds = GenGrid.BoundsRect(map, 14);
            __result = CellRect.FromLimits(
                Math.Max(bounds.minX, __result.minX - extra),
                Math.Max(bounds.minZ, __result.minZ - extra),
                Math.Min(bounds.maxX, __result.maxX + extra),
                Math.Min(bounds.maxZ, __result.maxZ + extra));
            MapGenerator.SetVar("SettlementRect", __result);
        }
    }

    [HarmonyPatch]
    internal static class MedievalHighTierMilitaryPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(StrongholdScoreUtility), "TierMilBase");
        }

        private static void Postfix(int tier, ref int __result)
        {
            if (tier > 13)
            {
                __result += MedievalHighTierScale.ExtraGarrison(tier) * 110;
            }
        }
    }

    [HarmonyPatch(typeof(StrongholdZoneUtility), nameof(StrongholdZoneUtility.EstimateVaultValue))]
    internal static class MedievalHighTierVaultEstimatePatch
    {
        private static void Postfix(StrongholdData data, ref float __result)
        {
            if (data?.tier > 13)
            {
                __result *= MedievalHighTierScale.RewardMultiplier(data.tier);
            }
        }
    }

    [HarmonyPatch(typeof(StrongholdZoneUtility), nameof(StrongholdZoneUtility.FillVault))]
    internal static class MedievalHighTierVaultPatch
    {
        private static void Prefix(StrongholdData data, ref float extraMul)
        {
            if (data?.tier > 13)
            {
                extraMul *= MedievalHighTierScale.RewardMultiplier(data.tier);
            }
        }
    }

    [HarmonyPatch(typeof(GarrisonGroupUtility), nameof(GarrisonGroupUtility.DecidePlan))]
    internal static class MedievalHighTierElitePlanPatch
    {
        private static void Postfix(StrongholdData __0, Settlement __1)
        {
            StrongholdData data = __0;
            Settlement settlement = __1;
            Faction faction = settlement?.Faction;
            if (data?.tier < 14 || !MedievalUtility.IsMedieval(faction) || data.groupPlanForced ||
                !GarrisonGroupUtility.IsApplicable(faction, data))
            {
                return;
            }

            data.hasElite = true;
            data.eliteCount = data.tier >= 16 ? 12 : data.tier == 15 ? 10 : 8;
        }
    }

    [HarmonyPatch(typeof(StrongholdScoreUtility), nameof(StrongholdScoreUtility.ValidRolesFor))]
    internal static class MedievalRolesPatch
    {
        private static void Postfix(Faction faction, int tier, ref List<string> __result)
        {
            if (!MedievalUtility.IsMedieval(faction))
            {
                return;
            }

            int contentTier = StrongholdScoreUtility.ContentTier(tier);
            __result = new List<string> { "SupplyDepot", "PrisonCamp" };
            if (contentTier >= 2)
            {
                __result.Add("BlackMarketHub");
                __result.Add("PsychicShrine");
            }
            if (contentTier >= 3)
            {
                __result.Add("NobleEstate");
            }
            if (contentTier >= 4)
            {
                __result.Add("CommandFortress");
            }
        }
    }

    [HarmonyPatch(typeof(StrongholdScoreUtility), nameof(StrongholdScoreUtility.DecideFacilities))]
    internal static class MedievalFacilitiesPatch
    {
        private static void Postfix(StrongholdData data, Settlement settlement)
        {
            if (data != null && MedievalUtility.IsMedieval(settlement?.Faction))
            {
                data.hasAmmoDepot = data.tier >= 4;
                data.hasMortarBattery = data.tier >= 8;
                data.hasCommsRoom = false;
            }
        }
    }

    [HarmonyPatch(typeof(MedievalOverhaulCompat), nameof(MedievalOverhaulCompat.IsEmpireOwnTurret))]
    internal static class MedievalTurretOwnershipPatch
    {
        private static void Postfix(Faction faction, ModContentPack turretMod, ref bool __result)
        {
            if (!__result && MedievalUtility.IsMedieval(faction) &&
                (turretMod?.PackageId?.ToLowerInvariant() == MedievalUtility.MedievalOverhaulPackageId ||
                 turretMod?.PackageId?.ToLowerInvariant() == MedievalUtility.VfeMedieval2PackageId ||
                 string.Equals(turretMod?.PackageId, "Gideon.CastleWalls", StringComparison.OrdinalIgnoreCase)))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch]
    internal static class MedievalTurretPoolPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("EvolvingEnemyStrongholds.StrongholdGarrisonUtility"), "BuildTurretPool");
        }

        private static void Postfix(Faction faction, IList ownPool, IList officialPool)
        {
            if (!MedievalUtility.IsMedieval(faction))
            {
                return;
            }

            // Bind the completed out values directly; __args can write its initial null values back.
            Filter(ownPool, faction);
            Filter(officialPool, faction);
        }

        private static void Filter(IList candidates, Faction faction)
        {
            if (candidates == null)
            {
                return;
            }
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                object candidate = candidates[i];
                ThingDef def = candidate == null ? null :
                    AccessTools.Field(candidate.GetType(), "def")?.GetValue(candidate) as ThingDef;
                if (MedievalUtility.SkipWallTowerForOpenPerimeter(faction, def) ||
                    def?.defName == "JDS_I_Defense_Tower" || def?.defName == "JDS_L_Defense_Tower" || def?.defName == "JDS_T_Defense_Tower")
                {
                    candidates.RemoveAt(i);
                    continue;
                }
                if (MedievalUtility.IsProtectedStrongholdDef(def)) continue;
                bool unsupportedVfeTurret =
                    def?.modContentPack?.PackageId?.ToLowerInvariant() == MedievalUtility.VfeMedieval2PackageId &&
                    !MedievalUtility.VfeTurretDefNames.Contains(def.defName);
                if (def == null || def.techLevel > TechLevel.Medieval || MedievalUtility.RequiresPower(def) ||
                    unsupportedVfeTurret)
                {
                    candidates.RemoveAt(i);
                }
            }
        }
    }

    [HarmonyPatch]
    internal static class RegisteredCultureTurretSafetyPatch
    {
        private struct PatchState
        {
            internal bool Applies;
            internal bool WasOverlay;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(StrongholdGarrisonUtility), "AddTurrets");
        }

        private static void Prefix(Faction faction, StrongholdData data, out PatchState __state)
        {
            Faction effectiveFaction = faction ?? Find.CurrentMap?.ParentFaction;
            __state = new PatchState
            {
                Applies = data != null &&
                    SettlementCultureRegistry.IsRegisteredFaction(effectiveFaction?.def?.defName),
                WasOverlay = data?.isOverlayBase == true
            };
            if (__state.Applies && __state.WasOverlay)
            {
                data.isOverlayBase = false;
            }
        }

        private static void Postfix(StrongholdData data, PatchState __state)
        {
            if (__state.Applies && data != null)
            {
                data.isOverlayBase = __state.WasOverlay;
            }
        }

        private static Exception Finalizer(Exception __exception, StrongholdData data, PatchState __state)
        {
            if (__state.Applies && data != null)
            {
                data.isOverlayBase = __state.WasOverlay;
            }
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class MedievalGarrisonEstimatePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(StrongholdGarrisonUtility), "EstimateGarrison",
                new[] { typeof(StrongholdData), typeof(Settlement), typeof(int).MakeByRefType(), typeof(int).MakeByRefType() });
        }

        private static void Postfix(StrongholdData data, Settlement settlement, ref int direct, ref int mortarMax)
        {
            if (!MedievalUtility.IsMedieval(settlement?.Faction))
            {
                return;
            }

            StrongholdGarrisonUtility.EstimateGarrison(data?.tier ?? 1, false, out int directWithoutOverlay, out _);
            direct = Math.Min(direct, directWithoutOverlay) + MedievalHighTierScale.HighTierReinforcements(data?.tier ?? 0);
            mortarMax = 0;
        }
    }

    internal static class MedievalGarrisonPopulationLimit
    {
        internal static readonly int[] CapsByTier =
        {
            0, 10, 15, 19, 24, 30, 36, 42, 48, 54, 60, 66, 72, 78, 96, 116, 138
        };

        [ThreadStatic]
        private static bool active;

        [ThreadStatic]
        private static int remainingSlots;

        internal static void Begin(Map map, Faction faction, StrongholdData data)
        {
            active = map != null && faction != null && data != null && MedievalUtility.IsMedieval(faction);
            if (!active)
            {
                remainingSlots = 0;
                return;
            }

            int tier = Math.Max(1, Math.Min(16, data.tier));
            int existing = StrongholdPawnOwnership.Population(map, faction, FinalStandUtility.Active(data)).Count;
            // ReinforceGarrison creates the commander and specialist crew first.
            // They are already included in existing; do not reserve them twice.
            remainingSlots = Math.Max(0, StrongholdGarrisonUtility.PopulationLimit(tier) +
                FinalStandUtility.PopulationBonus(data) - existing);
        }

        internal static void End()
        {
            active = false;
            remainingSlots = 0;
        }

        internal static IEnumerable<Pawn> Limit(IEnumerable<Pawn> pawns)
        {
            if (!active || pawns == null)
            {
                return pawns;
            }
            return LimitedPawns(pawns);
        }

        private static IEnumerable<Pawn> LimitedPawns(IEnumerable<Pawn> pawns)
        {
            // Enumerate the completed worker batch even when no slots remain, so
            // unused owners can follow native world-pawn handling.
            foreach (Pawn pawn in pawns)
            {
                if (pawn == null) continue;
                if (remainingSlots <= 0)
                {
                    // Group workers generate a whole batch before yielding its first pawn.
                    // Return unused members to vanilla world-pawn handling instead of abandoning them.
                    if (!pawn.Spawned && !pawn.Destroyed && !pawn.Discarded && pawn.ParentHolder == null &&
                        pawn.GetLord() == null && !Find.WorldPawns.Contains(pawn))
                        StrongholdPawnOwnership.ReturnUnused(pawn);
                    continue;
                }
                int people = StrongholdPawnOwnership.FitGroup(pawn, remainingSlots);
                remainingSlots -= people;
                yield return pawn;
            }
        }
    }

    [HarmonyPatch]
    internal static class MedievalAddReinforcementsLimitPatch
    {
        [ThreadStatic]
        private static HashSet<Pawn> generatedPawns;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(StrongholdGarrisonUtility), "AddReinforcements");
        }

        private static void Prefix(Map map, Faction faction, StrongholdData data, out HashSet<int> __state)
        {
            __state = map != null && MedievalUtility.IsMedieval(faction) && data?.tier >= 14
                ? new HashSet<int>(map.mapPawns.AllPawnsSpawned.Select(pawn => pawn.thingIDNumber)) : null;
            generatedPawns = new HashSet<Pawn>();
            MedievalGarrisonPopulationLimit.Begin(map, faction, data);
        }

        private static void Postfix(Map map, Faction faction, StrongholdData data, HashSet<int> __state)
        {
            if (__state == null) return;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                if (pawn.Faction == faction && !__state.Contains(pawn.thingIDNumber))
                    foreach (var member in StrongholdPawnOwnership.Group(pawn))
                        MedievalHighTierScale.ImproveGear(member, data.tier, false);
        }

        private static Exception Finalizer(Exception __exception)
        {
            if (generatedPawns != null)
            {
                var owned = StrongholdPawnOwnership.DeepOwned(generatedPawns.Where(p => p != null &&
                    (p.Spawned || p.ParentHolder != null || p.GetLord() != null || Find.WorldPawns.Contains(p))));
                foreach (Pawn pawn in generatedPawns)
                {
                    if (pawn == null || pawn.Spawned || pawn.Destroyed || pawn.Discarded || pawn.ParentHolder != null ||
                        pawn.GetLord() != null || Find.WorldPawns.Contains(pawn) || owned.Contains(pawn)) continue;
                    StrongholdPawnOwnership.ReturnUnused(pawn);
                }
                generatedPawns = null;
            }
            MedievalGarrisonPopulationLimit.End();
            return __exception;
        }

        internal static void TrackGeneratedPawn(Pawn pawn)
        {
            if (generatedPawns != null && pawn != null) generatedPawns.Add(pawn);
        }
    }

    internal static class MedievalNarrativePawnScope
    {
        internal sealed class Frame
        {
            internal Frame previous;
            internal readonly HashSet<Pawn> actors = new HashSet<Pawn>();
        }

        [ThreadStatic] internal static Frame current;

        internal static bool Eligible()
        {
            Map map = MapGenerator.mapBeingGenerated ?? MedievalUtility.finalizingDefenseMap;
            StrongholdData data = map?.Parent is Settlement site ?
                WorldComponent_Strongholds.Instance?.GetExisting(site.ID) : null;
            return MedievalUtility.IsSupportedHostileSettlement(map) && data != null &&
                data.generationPlan?.legacyMap != true && StrongholdCenterPlanner.Eligible(map, data);
        }

        internal static void End(Frame frame)
        {
            if (frame == null) return;
            current = frame.previous;
            foreach (Pawn actor in frame.actors)
            {
                if (actor.Spawned || actor.Destroyed || actor.Discarded || actor.ParentHolder != null ||
                    actor.GetLord() != null || Find.WorldPawns.Contains(actor)) continue;
                Find.WorldPawns.PassToWorld(actor, PawnDiscardDecideMode.Discard);
            }
        }
    }

    // Random tale actors supply copied grammar data, rather than real relatives.
    // Vanilla's default request can reuse a world pawn or link an unsaved actor
    // to the garrison while naming a newly generated legendary weapon.
    [HarmonyPatch(typeof(TaleData_Pawn), nameof(TaleData_Pawn.GenerateRandom))]
    internal static class MedievalRandomNarrativePawnPatch
    {
        private static void Prefix(out MedievalNarrativePawnScope.Frame __state)
        {
            __state = MedievalNarrativePawnScope.current != null || MedievalNarrativePawnScope.Eligible()
                ? new MedievalNarrativePawnScope.Frame { previous = MedievalNarrativePawnScope.current } : null;
            if (__state != null) MedievalNarrativePawnScope.current = __state;
        }

        private static void Postfix(TaleData_Pawn __result, MedievalNarrativePawnScope.Frame __state)
        {
            if (__state != null && __result?.pawn != null && __state.actors.Contains(__result.pawn))
                __result.pawn = null; // GetRules reads the copied name, kind, age and other tale fields.
        }

        private static Exception Finalizer(Exception __exception, MedievalNarrativePawnScope.Frame __state)
        {
            MedievalNarrativePawnScope.End(__state);
            return __exception;
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    internal static class MedievalNarrativePawnRequestPatch
    {
        private static void Prefix(ref PawnGenerationRequest request)
        {
            if (MedievalNarrativePawnScope.current == null) return;
            request.ForceGenerateNewPawn = true;
            request.CanGeneratePawnRelations = false;
        }

        private static void Postfix(Pawn __result)
        {
            if (__result != null) MedievalNarrativePawnScope.current?.actors.Add(__result);
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    internal static class MedievalGeneratedPawnTrackingPatch
    {
        private static void Postfix(Pawn __result) => MedievalAddReinforcementsLimitPatch.TrackGeneratedPawn(__result);
    }

    [HarmonyPatch(typeof(PawnGroupMakerUtility), nameof(PawnGroupMakerUtility.GeneratePawns),
        new[] { typeof(PawnGroupMakerParms), typeof(bool) })]
    internal static class MedievalGeneratedPawnLimitPatch
    {
        private static void Postfix(ref IEnumerable<Pawn> __result)
        {
            __result = MedievalGarrisonPopulationLimit.Limit(__result);
        }
    }

    [HarmonyPatch]
    internal static class MedievalHighTierEliteGearPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(GarrisonGroupUtility), "UpgradeEliteGear");
        }

        private static void Postfix(Pawn p, int id)
        {
            if (!MedievalUtility.IsMedieval(p?.Faction)) return;
            StrongholdData data = WorldComponent_Strongholds.Instance?.GetExisting(id);
            if (data != null) MedievalHighTierScale.ImproveGear(p, data.tier, true);
        }
    }

    internal static class MedievalSecondEliteSquad
    {
        private const int RollSalt = 1500450271;
        private const int SizeSalt = 1500450272;

        [ThreadStatic]
        internal static bool Active;

        [ThreadStatic]
        internal static int FirstSquadCount;

        [ThreadStatic]
        internal static int SecondSquadCount;

        [ThreadStatic]
        internal static CellRect DefenseRect;

        [ThreadStatic]
        internal static bool CreatingSecondLord;

        internal static bool TryBegin(Faction faction, StrongholdData data, CellRect rect, int availablePawnCount)
        {
            End();
            if (!MedievalUtility.IsMedieval(faction) || data == null || !data.hasElite || data.eliteCount <= 0 ||
                availablePawnCount - data.eliteCount < 3)
            {
                return false;
            }

            float chance = data.tier >= 14 ? 1f : data.tier >= 13 ? 0.55f : data.tier == 12 ? 0.35f : data.tier == 11 ? 0.20f : 0f;
            if (chance <= 0f || StableFraction(data.settlementId, RollSalt) >= chance)
            {
                return false;
            }

            Active = true;
            FirstSquadCount = data.eliteCount;
            int desiredSecondSquad = data.tier >= 16 ? 9 : data.tier == 15 ? 7 : data.tier == 14 ? 5 :
                StableFraction(data.settlementId, SizeSalt) < 0.5f ? 3 : 4;
            SecondSquadCount = Math.Min(desiredSecondSquad, availablePawnCount - data.eliteCount);
            DefenseRect = rect;
            data.eliteCount += SecondSquadCount;
            return true;
        }

        internal static void End()
        {
            Active = false;
            FirstSquadCount = 0;
            SecondSquadCount = 0;
            DefenseRect = default(CellRect);
            CreatingSecondLord = false;
        }

        internal static IntVec3 FindSecondHoldPoint(Map map)
        {
            IntVec3 candidate = DefenseRect.Width > 0
                ? new IntVec3(DefenseRect.minX + DefenseRect.Width * 3 / 4, 0, DefenseRect.CenterCell.z)
                : map.Center;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(candidate, 12f, true))
            {
                if (cell.InBounds(map) && cell.Standable(map))
                {
                    return cell;
                }
            }
            return map.Center;
        }

        private static float StableFraction(int settlementId, int salt)
        {
            uint value = (uint)Gen.HashCombineInt(settlementId, salt);
            unchecked
            {
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
            }
            return (value >> 8) / 16777216f;
        }
    }

    [HarmonyPatch(typeof(GarrisonGroupUtility), nameof(GarrisonGroupUtility.ApplyGroups))]
    internal static class MedievalSecondElitePlanPatch
    {
        private static void Prefix(Faction faction, StrongholdData d, CellRect rect, List<Pawn> bPawns, out int __state)
        {
            __state = d?.eliteCount ?? 0;
            MedievalSecondEliteSquad.TryBegin(faction, d, rect, bPawns?.Count ?? 0);
        }

        private static Exception Finalizer(Exception __exception, StrongholdData d, int __state)
        {
            if (MedievalSecondEliteSquad.Active && d != null)
            {
                d.eliteCount = __state;
            }
            MedievalSecondEliteSquad.End();
            return __exception;
        }
    }

    [HarmonyPatch(typeof(LordMaker), nameof(LordMaker.MakeNewLord),
        new[] { typeof(Faction), typeof(LordJob), typeof(Map), typeof(IEnumerable<Pawn>) })]
    internal static class MedievalSecondEliteLordPatch
    {
        private static void Prefix(Faction faction, LordJob lordJob, Map map, ref IEnumerable<Pawn> startingPawns)
        {
            if (!MedievalSecondEliteSquad.Active || MedievalSecondEliteSquad.CreatingSecondLord ||
                !(lordJob is LordJob_EESDefendGroup groupJob) ||
                groupJob.groupType != (int)GarrisonGroupType.Elite || startingPawns == null)
            {
                return;
            }

            List<Pawn> elites = startingPawns.ToList();
            int firstCount = Math.Min(MedievalSecondEliteSquad.FirstSquadCount, elites.Count);
            int secondCount = Math.Min(MedievalSecondEliteSquad.SecondSquadCount, elites.Count - firstCount);
            if (firstCount <= 0 || secondCount < 3)
            {
                return;
            }

            List<Pawn> firstSquad = elites.Take(firstCount).ToList();
            List<Pawn> secondSquad = elites.Skip(firstCount).Take(secondCount).ToList();
            startingPawns = firstSquad;

            IntVec3 holdPoint = MedievalSecondEliteSquad.FindSecondHoldPoint(map);
            LordJob secondJob = new LordJob_EESDefendGroup(
                faction, holdPoint, GarrisonGroupType.Elite, 0f, secondSquad.Count, 2000000000);
            try
            {
                MedievalSecondEliteSquad.CreatingSecondLord = true;
                LordMaker.MakeNewLord(faction, secondJob, map, secondSquad);
            }
            finally
            {
                MedievalSecondEliteSquad.CreatingSecondLord = false;
            }
        }
    }

    [HarmonyPatch(typeof(CECompat), nameof(CECompat.PickLootAmmo))]
    internal static class MedievalCeLootPatch
    {
        private static bool Prefix(Faction faction, ref List<ThingDef> __result)
        {
            if (!MedievalUtility.IsMedieval(faction))
            {
                return true;
            }
            __result = new List<ThingDef>();
            return false;
        }
    }

    [HarmonyPatch(typeof(CECompat), nameof(CECompat.ApplyCeGarrison))]
    internal static class MedievalCeGarrisonPatch
    {
        private static bool Prefix(Faction faction)
        {
            return !MedievalUtility.IsMedieval(faction);
        }
    }

    [HarmonyPatch(typeof(CommanderEffects), nameof(CommanderEffects.AssignType))]
    internal static class MedievalCommanderTypePatch
    {
        private static readonly HashSet<string> Allowed = new HashSet<string>
        {
            "Commander", "Marksman", "Fanatic", "Assault", "Warlord"
        };

        private static void Postfix(StrongholdData data, Settlement settlement)
        {
            if (data != null && data.hasCommander && MedievalUtility.IsMedieval(settlement?.Faction) &&
                !Allowed.Contains(data.commanderType.ToString()))
            {
                data.commanderType = CommanderType.Assault;
            }
        }
    }

    [HarmonyPatch(typeof(StrongholdDefenseUtility), nameof(StrongholdDefenseUtility.ResolveWall))]
    internal static class MedievalWallPatch
    {
        private static void Postfix(Faction faction, int tier, ref ThingDef wallDef, ref ThingDef stuff)
        {
            if (!MedievalUtility.IsMedieval(faction))
            {
                return;
            }

            ThingDef granite = ThingDefOf.BlocksGranite;
            if (MedievalUtility.IsVfeClan(faction) && tier < 5)
            {
                ThingDef palisade = DefDatabase<ThingDef>.GetNamedSilentFail("VFEM2_Palisade");
                if (palisade != null)
                {
                    wallDef = palisade;
                    stuff = ThingDefOf.WoodLog;
                    return;
                }
            }

            if (MedievalUtility.IsVfeEnemySettlement(faction) && tier >= 5)
            {
                ThingDef vfeCastleWall = DefDatabase<ThingDef>.GetNamedSilentFail("VFEM2_CastleWall");
                if (vfeCastleWall != null)
                {
                    wallDef = vfeCastleWall;
                    stuff = granite;
                    return;
                }
            }

            ThingDef castleWall = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_CastleWall");
            if (tier >= 5 && castleWall != null)
            {
                wallDef = castleWall;
                stuff = granite;
            }
            else if (wallDef != ThingDefOf.Wall && wallDef?.modContentPack?.PackageId?.ToLowerInvariant() != MedievalUtility.MedievalOverhaulPackageId)
            {
                wallDef = ThingDefOf.Wall;
                stuff = granite;
            }
        }
    }

    [HarmonyPatch]
    internal static class MedievalSecureVaultPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("EvolvingEnemyStrongholds.StrongholdLayoutGenerator"), "SecureVault");
        }

        private static bool Prefix(Faction faction)
        {
            return !MedievalUtility.IsMedieval(faction);
        }
    }

    [HarmonyPatch]
    internal static class MedievalFloorPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            Type layout = AccessTools.TypeByName("EvolvingEnemyStrongholds.StrongholdLayoutGenerator");
            yield return AccessTools.Method(layout, "LayFloor");
            yield return AccessTools.Method(layout, "CarveDoorOutward");
        }

        private static void Prefix(Map map, ref TerrainDef floor)
        {
            if (floor?.defName == "Concrete" && MedievalUtility.IsMedieval(map?.ParentFaction))
            {
                floor = MedievalUtility.MedievalFloor() ?? floor;
            }
        }
    }

    [HarmonyPatch]
    internal static class MedievalZoneFloorPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("EvolvingEnemyStrongholds.StrongholdZoneUtility"), "LayFloor");
        }

        private static bool Prefix(Map map, CellRect core)
        {
            if (!MedievalUtility.IsMedieval(map?.ParentFaction))
            {
                return true;
            }

            TerrainDef floor = MedievalUtility.MedievalFloor();
            if (floor != null)
            {
                foreach (IntVec3 cell in core.Cells)
                {
                    if (cell.InBounds(map))
                    {
                        map.terrainGrid.SetTerrain(cell, floor);
                    }
                }
            }
            return false;
        }
    }

    [HarmonyPatch]
    internal static class MedievalLayoutDoorPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("EvolvingEnemyStrongholds.StrongholdLayoutGenerator"), "BuildEnclosedRoom");
        }

        private static void Prefix(Faction faction, ref ThingDef doorDef, ref ThingDef doorStuff)
        {
            if (MedievalUtility.IsMedieval(faction))
            {
                doorDef = MedievalUtility.MedievalDoor();
                doorStuff = doorDef.MadeFromStuff ? ThingDefOf.BlocksGranite : null;
            }
        }
    }

    [HarmonyPatch]
    internal static class MedievalZoneDoorPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("EvolvingEnemyStrongholds.StrongholdZoneUtility"), "SpawnDoor");
        }

        private static void Prefix(Faction faction, ref ThingDef doorDef, ref ThingDef doorStuff)
        {
            if (MedievalUtility.IsMedieval(faction))
            {
                doorDef = MedievalUtility.MedievalDoor();
                doorStuff = doorDef.MadeFromStuff ? ThingDefOf.BlocksGranite : null;
            }
        }
    }

    [HarmonyPatch]
    internal static class MedievalRoleFacilitiesPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("EvolvingEnemyStrongholds.StrongholdZoneUtility"), "PlaceFacilitiesAndPrisoners");
        }

        private static bool Prefix(Map map, Faction faction, StrongholdData data, CellRect interior,
            HashSet<IntVec3> occupied, ref int __result)
        {
            if (!MedievalUtility.IsMedieval(faction))
            {
                return true;
            }
            data.hasCommsRoom = false;
            data.hasAmmoDepot = data.tier >= 4;
            data.hasMortarBattery = data.tier >= 8;
            __result = MedievalUtility.AddMedievalRoleFacilities(map, faction, data, interior, occupied);
            return false;
        }
    }

    [HarmonyPatch]
    internal static class MedievalRoleBuildingsPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("EvolvingEnemyStrongholds.StrongholdZoneUtility"), "PlaceRequiredRoleBuildings");
        }

        private static void Postfix(Map map, Faction faction, StrongholdData data, CellRect interior)
        {
            MedievalUtility.RemoveModernBuildings(map, faction, interior);
        }
    }

    [HarmonyPatch(typeof(StrongholdAreaUtility), nameof(StrongholdAreaUtility.BuildOuterRing))]
    internal static class MedievalOuterRingPatch
    {
        private static bool Prefix(Map map, StrongholdData data, CellRect settlementRect)
        {
            if (!MedievalUtility.IsSupportedHostileSettlement(map))
            {
                return true;
            }
            if (MedievalUtility.BuildOpenOuterRing(map, data, settlementRect)) return false;
            if (data?.tier < 14 && MedievalUtility.IsFrenchRiverHarbor(map))
            {
                MedievalUtility.BuildFrenchHarborOuterDefenses(map, data, settlementRect);
                return false;
            }
            MedievalUtility.ClearOuterDefenseFootprint(map, data, settlementRect);
            return !MedievalUtility.BuildCastleOuterRing(map, data, settlementRect);
        }

        private static void Postfix(Map map, StrongholdData data, CellRect settlementRect)
        {
            if (!MedievalUtility.IsSupportedHostileSettlement(map) || MedievalUtility.UsesOpenPerimeter(map?.ParentFaction) ||
                (data?.tier < 14 && MedievalUtility.IsFrenchRiverHarbor(map)))
            {
                return;
            }
            MedievalUtility.NormalizeOuterRing(map, data, settlementRect);
            MedievalUtility.AddTieredOuterDefenses(map, data, settlementRect);
        }
    }

    [HarmonyPatch(typeof(StrongholdAreaUtility), nameof(StrongholdAreaUtility.BuildPerimeter))]
    internal static class MedievalPerimeterPatch
    {
        private static bool Prefix(Map map, StrongholdData data, CellRect rect, ref int __result)
        {
            if (!MedievalUtility.IsSupportedHostileSettlement(map))
            {
                return true;
            }
            if (MedievalUtility.UsesOpenPerimeter(map.ParentFaction))
            {
                __result = 0;
                return false;
            }
            if (MedievalUtility.TryBuildHighTierCastlePerimeter(map, data, rect, out __result))
                return false;
            if (MedievalUtility.IsFrenchRiverHarbor(map))
            {
                __result = MedievalUtility.BuildFrenchHarborPerimeter(map, data, rect, true);
                MapComponent_StrongholdSiege siege = map.GetComponent<MapComponent_StrongholdSiege>();
                if (siege != null)
                {
                    siege.openBase = true;
                }
                Log.Message($"[EES Medieval Patch] France river mode: replaced rectangular BuildPerimeter with an open harbor perimeter at {rect.CenterCell}.");
                return false;
            }
            if (MedievalUtility.IsMongolCamp(map))
            {
                __result = MedievalUtility.BuildMongolSegmentedPerimeter(map, data, rect);
                MapComponent_StrongholdSiege siege = map.GetComponent<MapComponent_StrongholdSiege>();
                if (siege != null)
                {
                    siege.openBase = true;
                }
                return false;
            }
            MedievalUtility.ClearWallFootprint(map, rect);
            return true;
        }
    }

    [HarmonyPatch(typeof(StrongholdAreaUtility), nameof(StrongholdAreaUtility.ResealPerimeter))]
    internal static class MedievalResealPerimeterPatch
    {
        private static bool Prefix(Map map, StrongholdData data, CellRect rect, ref int __result)
        {
            if (!MedievalUtility.IsSupportedHostileSettlement(map))
            {
                return true;
            }
            if (MedievalUtility.UsesOpenPerimeter(map.ParentFaction))
            {
                __result = 0;
                return false;
            }
            if (MedievalUtility.TryBuildHighTierCastlePerimeter(map, data, rect, out __result))
                return false;
            if (MedievalUtility.IsFrenchRiverHarbor(map))
            {
                __result = MedievalUtility.BuildFrenchHarborPerimeter(map, data, rect, false);
                return false;
            }
            if (MedievalUtility.IsMongolCamp(map))
            {
                __result = MedievalUtility.BuildMongolSegmentedPerimeter(map, data, rect);
                return false;
            }
            MedievalUtility.ClearWallFootprint(map, rect);
            return true;
        }
    }

    [HarmonyPatch]
    internal static class MedievalVaultSitePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("EvolvingEnemyStrongholds.StrongholdLayoutGenerator"),
                "FindVaultSpotOverlay");
        }

        private static void Postfix(Map map, CellRect baseRect, int vh, ref CellRect __result)
        {
            if (MedievalUtility.IsSupportedHostileSettlement(map))
            {
                __result = MedievalUtility.FindValidVaultRect(map, baseRect, vh, __result);
            }
        }
    }

    [HarmonyPatch(typeof(StrongholdZoneUtility), nameof(StrongholdZoneUtility.FillVault))]
    internal static class MedievalVaultLootPatch
    {
        private static void Postfix(Map map, Faction faction, StrongholdData data, CellRect vaultInterior)
        {
            MedievalUtility.RemoveEmptyMinifiedThings(map, faction, vaultInterior);
            MedievalUtility.ReplaceModernThings(map, faction, vaultInterior);
            MedievalUtility.AddMedievalKingdomsVaultGear(map, faction, data, vaultInterior);
            MedievalUtility.AddAstartesVaultGear(map, faction, data, vaultInterior);
        }
    }

    [HarmonyPatch]
    internal static class MedievalRoleLootPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("EvolvingEnemyStrongholds.StrongholdZoneUtility"), "PlaceRoleShelfItems");
        }

        private static void Postfix(Map map, Faction faction, CellRect interior)
        {
            MedievalUtility.ReplaceModernThings(map, faction, interior);
        }
    }

    [HarmonyPatch]
    internal static class MedievalStoreroomLootPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("EvolvingEnemyStrongholds.StrongholdLayoutGenerator"), "FurnishStoreroom");
        }

        private static void Postfix(Map map, Faction faction, CellRect room)
        {
            MedievalUtility.ReplaceModernThings(map, faction, room);
            MedievalUtility.RemoveModernBuildings(map, faction, room);
        }
    }

    [HarmonyPatch]
    internal static class MedievalOverlayVaultLootPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("EvolvingEnemyStrongholds.StrongholdLayoutGenerator"), "FurnishVault");
        }

        private static void Postfix(Map map, Faction faction, CellRect room)
        {
            MedievalUtility.RemoveEmptyMinifiedThings(map, faction, room);
            MedievalUtility.ReplaceModernThings(map, faction, room);
        }
    }

    [HarmonyPatch]
    internal static class MedievalOrphanVaultRefogPatch
    {
        private static MethodBase TargetMethod()
        {
            Type refogPatch = AccessTools.TypeByName("EvolvingEnemyStrongholds.Patch_MapGenerator_GenerateMap_Refog");
            return AccessTools.Method(refogPatch, "RefogVault");
        }

        private static bool Prefix(Map map, MapComponent_StrongholdSiege siege, ref int __result)
        {
            Faction faction = map?.ParentFaction;
            if (!(map?.Parent is Settlement) || !MedievalUtility.NeedsWholeMapSettlementCleanup(faction) ||
                !faction.HostileTo(Faction.OfPlayer))
            {
                return true;
            }

            if (MedievalUtility.TryRefogVaultInterior(map, siege?.eesVaultRect ?? CellRect.Empty, out int foggedInteriorCells))
            {
                __result = foggedInteriorCells;
            }
            else if (siege != null)
            {
                Settlement settlement = map.Parent as Settlement;
                StrongholdData data = settlement == null
                    ? null
                    : WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
                if (data != null && MedievalUtility.TryBuildRegisteredMedievalVault(
                    map, faction, data, CellRect.Empty, true) &&
                    MedievalUtility.TryRefogVaultInterior(map, siege.eesVaultRect, out foggedInteriorCells))
                {
                    __result = foggedInteriorCells;
                    return false;
                }

                MedievalUtility.DiscardOrphanVault(map, siege);
                __result = 0;
            }
            else
            {
                MedievalUtility.DiscardOrphanVault(map, siege);
                __result = 0;
            }
            return false;
        }
    }

    [HarmonyPatch]
    internal static class MedievalRiverBaseRefogPatch
    {
        private static MethodBase TargetMethod()
        {
            Type refogPatch = AccessTools.TypeByName("EvolvingEnemyStrongholds.Patch_MapGenerator_GenerateMap_Refog");
            return AccessTools.Method(refogPatch, "Postfix");
        }

        private static bool Prefix(Map __0)
        {
            Map map = __0;
            if (!MedievalUtility.IsSupportedHostileSettlement(map))
            {
                return true;
            }

            MapComponent_StrongholdSiege siege = map.GetComponent<MapComponent_StrongholdSiege>();
            if (siege?.eesGenerated != true) return true;
            CellRect baseRect = siege?.eesBaseRect ?? CellRect.Empty;
            bool keepWholeBaseRefog = !MedievalUtility.UsesOpenPerimeter(map.ParentFaction) &&
                (baseRect.Width < 5 || (!MedievalUtility.IsFrenchRiverHarbor(map) &&
                !MedievalUtility.HasRiverCrossing(map, baseRect) &&
                MedievalUtility.HasClosedPerimeter(map, baseRect)));
            MedievalUtility.LogRegisteredGenerationSummary(map, siege, baseRect, !keepWholeBaseRefog);
            if (keepWholeBaseRefog)
            {
                StrongholdIndoorFog.RefogRooms(map, CellRect.WholeMap(map));
                return false;
            }

            if (!MedievalUtility.TryRefogVaultInterior(map, siege.eesVaultRect, out _))
            {
                MedievalUtility.DiscardOrphanVault(map, siege);
            }
            siege.openBase = true;
            StrongholdIndoorFog.RefogRooms(map, CellRect.WholeMap(map));
            Log.Message($"[EES Medieval Patch] Indoor-only fog for an open or river-split medieval stronghold at {baseRect.CenterCell}; outdoor space, walls and doors remain visible.");
            return false;
        }
    }

    [HarmonyPatch]
    internal static class MedievalRegisteredTemplatePatch
    {
        private sealed class GenerationState
        {
            internal string TemplateDefName;
            internal HashSet<int> ExistingBuildingIds;
        }

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("KCSG.SettlementGenUtils"), "Generate");
        }

        private static void Prefix(ref ResolveParams __0, Map __1, object __2, out GenerationState __state)
        {
            Map map = __1;
            Def layout = __2 as Def;
            __state = null;
            var data = map?.Parent is Settlement settlement
                ? WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID) : null;
            if (MapGenerator.mapBeingGenerated != map || !MedievalUtility.IsSupportedHostileSettlement(map) ||
                !StrongholdCenterPlanner.Eligible(map, data) || data.generationPlan?.legacyMap == true ||
                data.generationPlan?.centerBuilt == true ||
                !SettlementCultureRegistry.TryGetProfile(layout?.defName, map.ParentFaction.def.defName, out _)) return;

            MedievalUtility.RecordRegisteredTemplate(map, layout.defName);
            // KCSG still generates its complete town when dry-site preparation fails.
            // Capture that town as well so the postfix calibrates its actual bounds
            // and reserves the center before later native scatter can occupy it.
            __state = new GenerationState
            {
                TemplateDefName = layout.defName,
                ExistingBuildingIds = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                    .Select(thing => thing.thingIDNumber).ToHashSet()
            };
            if (MedievalUtility.TryPrepareRegisteredTownSite(map, layout.defName, ref __0))
                MedievalUtility.ClearRegisteredTownNaturalBlockers(map, __0.rect);
        }

        private static void Postfix(Map __1, GenerationState __state)
        {
            if (__state != null)
            {
                MedievalUtility.CalibrateRegisteredTownRect(__1, __state.TemplateDefName, __state.ExistingBuildingIds);
            }
        }
    }

    [HarmonyPatch]
    internal static class MedievalRegisteredStrongholdRectPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(StrongholdAreaUtility), "GetRect");
        }

        private static void Postfix(Map map, ref CellRect __result)
        {
            if (MedievalUtility.TryGetRegisteredTownRect(map, out CellRect selected))
            {
                __result = selected;
            }
        }
    }

    [HarmonyPatch]
    internal static class MedievalKcsgStuffPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("KCSG.SymbolUtils"), "GenerateBuildingAt");
        }

        private static void Prefix(object[] __args)
        {
            Map map = __args?.OfType<Map>().FirstOrDefault();
            if (!MedievalUtility.IsMedieval(map?.ParentFaction))
            {
                return;
            }

            object symbol = __args.FirstOrDefault(arg => arg?.GetType().FullName == "KCSG.SymbolDef");
            FieldInfo thingDefField = symbol == null ? null : AccessTools.Field(symbol.GetType(), "thingDef");
            FieldInfo stuffDefField = symbol == null ? null : AccessTools.Field(symbol.GetType(), "stuffDef");
            ThingDef thingDef = thingDefField?.GetValue(symbol) as ThingDef;
            if ((thingDef?.defName == "TorchWallLamp" || thingDef?.defName == "TorchLamp") &&
                thingDef.MadeFromStuff && stuffDefField?.GetValue(symbol) == null)
            {
                stuffDefField.SetValue(symbol, GenStuff.DefaultStuffFor(thingDef));
            }
        }
    }

    [HarmonyPatch(typeof(ThingMaker), nameof(ThingMaker.MakeThing))]
    internal static class MedievalTorchThingMakerPatch
    {
        private static void Prefix(ThingDef def, ref ThingDef stuff)
        {
            if ((def?.defName == "TorchWallLamp" || def?.defName == "TorchLamp") &&
                def.MadeFromStuff && stuff == null)
            {
                stuff = GenStuff.DefaultStuffFor(def);
            }
        }
    }

    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateContentsIntoMap))]
    [HarmonyPriority(Priority.Last)]
    internal static class MedievalFinalSettlementCleanupPatch
    {
        private static void Postfix(Map map)
        {
            Settlement settlement = map?.Parent as Settlement;
            Faction faction = settlement?.Faction;
            if (!MedievalUtility.IsSupportedHostileSettlement(map))
            {
                return;
            }

            if (MedievalUtility.NeedsWholeMapSettlementCleanup(faction))
            {
                CellRect wholeMap = new CellRect(0, 0, map.Size.x, map.Size.z);
                MedievalUtility.RemoveEmptyMinifiedThings(map, faction, wholeMap);
                MedievalUtility.ReplaceModernThings(map, faction, wholeMap);
                MedievalUtility.RemoveModernBuildings(map, faction, wholeMap);
                MedievalUtility.ClearFranceIndoorNaturalBlockers(map);
            }

            MapComponent_StrongholdSiege siege = map.GetComponent<MapComponent_StrongholdSiege>();
            StrongholdData data = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
            if (data == null && siege != null)
            {
                data = AccessTools.Field(typeof(MapComponent_StrongholdSiege), "cachedData")?.GetValue(siege) as StrongholdData;
            }

            CellRect baseRect = siege?.eesBaseRect ?? CellRect.Empty;
            if (baseRect.Width < 5 && MedievalUtility.TryGetRegisteredTownRect(map, out CellRect registeredRect))
            {
                baseRect = registeredRect;
            }
            Map previousDefenseMap = MedievalUtility.finalizingDefenseMap;
            try
            {
                MedievalUtility.finalizingDefenseMap = map;
                MedievalUtility.EnsureMedievalOuterDefenses(map, faction, data, baseRect);
            }
            finally { MedievalUtility.finalizingDefenseMap = previousDefenseMap; }
            MedievalUtility.MoveItemsOffConflictingLoadBlockers(map, faction);
            MedievalUtility.RecalculateVaultValue(map, data);
        }
    }

    [HarmonyPatch]
    internal static class MedievalFactionSettlementCleanupPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(GenStep_Settlement), "ScatterAt");
        }

        private static void Postfix(Map map)
        {
            Settlement settlement = map?.Parent as Settlement;
            Faction faction = settlement?.Faction;
            if (!MedievalUtility.IsSupportedHostileSettlement(map) ||
                !MedievalUtility.NeedsWholeMapSettlementCleanup(faction))
            {
                return;
            }

            CellRect wholeMap = new CellRect(0, 0, map.Size.x, map.Size.z);
            MedievalUtility.ReplaceModernThings(map, faction, wholeMap);
            MedievalUtility.RemoveModernBuildings(map, faction, wholeMap);
            MedievalUtility.RecalculateVaultValue(map, WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID));
        }
    }

    [HarmonyPatch(typeof(StrongholdGarrisonUtility), nameof(StrongholdGarrisonUtility.ComputeBaseRect))]
    internal static class MedievalRegisteredOverlayRectPatch
    {
        private static void Postfix(Map map, ref CellRect __result)
        {
            if (MedievalUtility.TryGetRegisteredTownRect(map, out CellRect selected) && __result != selected)
            {
                Log.Message($"[EES Medieval Patch] Aligned EES overlay base rect with registered town: computedRect={__result}, selectedRect={selected}.");
                __result = selected;
            }
        }
    }

}
