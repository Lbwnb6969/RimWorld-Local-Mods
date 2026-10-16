using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace WorldTileMutatorEditor
{
    [StaticConstructorOnStartup]
    public static class WorldTileMutatorEditorMod
    {
        static WorldTileMutatorEditorMod()
        {
            new Harmony("Local.WorldTileMutatorEditor").PatchAll(typeof(WorldTileMutatorEditorMod).Assembly);
        }
    }

    [HarmonyPatch(typeof(WorldInspectPane), nameof(WorldInspectPane.DoWindowContents))]
    public static class WorldInspectPanePatch
    {
        public static void Postfix(WorldInspectPane __instance)
        {
            PlanetTile selectedTile = Find.WorldSelector != null ? Find.WorldSelector.SelectedTile : PlanetTile.Invalid;
            if (!selectedTile.Valid)
                return;

            Rect buttonRect = new Rect(12f, __instance.windowRect.height - 46f, __instance.windowRect.width - 24f, 32f);
            if (Widgets.ButtonText(buttonRect, "WTME.OpenWorldEditor".Translate()))
            {
                Find.WindowStack.Add(new TileEditorWindow(selectedTile.Tile));
            }
            TooltipHandler.TipRegion(buttonRect, "WTME.OpenWorldEditorDesc".Translate());
        }
    }

    public sealed class TileEditorWindow : Window
    {
        private readonly Tile tile;
        private readonly PlanetTile planetTile;
        private string search = string.Empty;
        private Vector2 scroll;
        private List<TileMutatorDef> candidateDefs = new List<TileMutatorDef>();
        private List<TileMutatorDef> visibleDefs = new List<TileMutatorDef>();
        private readonly Dictionary<TileMutatorDef, string> candidateLabels = new Dictionary<TileMutatorDef, string>();
        private List<TileMutatorDef> previousMutators = new List<TileMutatorDef>();
        private Hilliness previousHilliness;
        private string previousSearch;

        private static readonly System.Reflection.FieldInfo HillinessLabelField = AccessTools.Field(typeof(Tile), "hillinessLabelCached");
        private static readonly System.Reflection.FieldInfo SecondaryBiomeFlagField = AccessTools.Field(typeof(Tile), "tmpHasSecondaryBiome");
        private static readonly System.Reflection.FieldInfo SecondaryBiomeField = AccessTools.Field(typeof(Tile), "tmpSecondaryBiome");

        public TileEditorWindow(Tile tile)
        {
            this.tile = tile;
            planetTile = tile.tile;
            forcePause = false;
            absorbInputAroundWindow = true;
            doCloseX = true;
            RefreshCandidates();
        }

        public override Vector2 InitialSize => new Vector2(760f, 680f);

        private bool HasGeneratedMap => Find.Maps.Any(map => map.Tile == planetTile);

        public override void DoWindowContents(Rect inRect)
        {
            if (previousHilliness != CurrentHilliness || !previousMutators.SequenceEqual(tile.Mutators))
                RefreshCandidates();
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);
            listing.Label("WTME.Title".Translate());
            listing.Label(string.Format("WTME.TileSummary".Translate().ToString(), planetTile, tile.HillinessLabel, HasGeneratedMap));
            listing.GapLine();

            bool editable = !HasGeneratedMap;
            GUI.enabled = editable;
            listing.Label("WTME.Hilliness".Translate());
            foreach (Hilliness value in new[] { Hilliness.Flat, Hilliness.SmallHills, Hilliness.LargeHills, Hilliness.Mountainous, Hilliness.Impassable })
            {
                if (listing.RadioButton(HillinessUtility.GetLabelCap(value), CurrentHilliness == value))
                    SetHilliness(value);
            }
            GUI.enabled = true;
            if (!editable)
                listing.Label("WTME.ReadOnlyGenerated".Translate());

            listing.GapLine();
            listing.Label("WTME.CurrentMutators".Translate());
            foreach (TileMutatorDef mutator in tile.Mutators.ToList())
            {
                Rect row = listing.GetRect(28f);
                Widgets.Label(row.LeftPart(0.72f), mutator.Label(planetTile).CapitalizeFirst());
                if (editable && Widgets.ButtonText(row.RightPart(0.25f), "WTME.Remove".Translate()))
                {
                    tile.RemoveMutator(mutator);
                    InvalidateTileCaches();
                    RefreshCandidates();
                }
                TooltipHandler.TipRegion(row, new TipSignal(() => mutator.Description(planetTile), row.GetHashCode()));
                listing.Gap(2f);
            }

            listing.Label("WTME.Search".Translate());
            search = Widgets.TextField(listing.GetRect(28f), search);
            if (search != previousSearch) FilterCandidates();
            listing.GapLine();

            Rect listRect = listing.GetRect(230f);
            float contentHeight = visibleDefs.Count * 28f;
            Widgets.BeginScrollView(listRect, ref scroll, new Rect(0f, 0f, listRect.width - 16f, contentHeight));
            Listing_Standard candidates = new Listing_Standard();
            candidates.Begin(new Rect(0f, 0f, listRect.width - 16f, contentHeight));
            foreach (TileMutatorDef mutator in visibleDefs)
            {
                Rect row = candidates.GetRect(28f);
                if (row.yMax < scroll.y || row.y > scroll.y + listRect.height) continue;
                Widgets.Label(row.LeftPart(0.62f), candidateLabels[mutator].CapitalizeFirst());
                bool valid = IsValidCandidate(mutator);
                GUI.enabled = editable && valid;
                if (Widgets.ButtonText(row.RightPart(0.3f), valid ? "WTME.Add".Translate() : "WTME.Invalid".Translate()))
                    AddMutatorWithConfirmation(mutator);
                GUI.enabled = true;
                TooltipHandler.TipRegion(row, new TipSignal(() => valid ? mutator.Description(planetTile) : mutator.description, row.GetHashCode()));
            }
            candidates.End();
            Widgets.EndScrollView();
            listing.End();
        }

        private bool Available(TileMutatorDef mutator) => mutator.EverValid() && !tile.Mutators.Contains(mutator);

        private void RefreshCandidates()
        {
            candidateLabels.Clear();
            candidateDefs = DefDatabase<TileMutatorDef>.AllDefsListForReading.Where(Available).ToList();
            foreach (TileMutatorDef def in candidateDefs)
                candidateLabels[def] = IsValidCandidate(def)
                    ? def.Label(planetTile) : (def.label ?? def.defName);
            candidateDefs = candidateDefs.OrderBy(def => candidateLabels[def]).ToList();
            previousMutators = tile.Mutators.ToList();
            previousHilliness = CurrentHilliness;
            FilterCandidates();
        }

        private void FilterCandidates()
        {
            previousSearch = search;
            string needle = search.Trim().ToLowerInvariant();
            visibleDefs = candidateDefs.Where(def => needle.Length == 0 ||
                candidateLabels[def].ToLowerInvariant().Contains(needle) ||
                def.defName.ToLowerInvariant().Contains(needle)).ToList();
        }

        private void AddMutatorWithConfirmation(TileMutatorDef mutator)
        {
            List<TileMutatorDef> conflicts = tile.Mutators.Where(existing => HasCategoryConflict(existing, mutator)).ToList();
            if (conflicts.Count == 0)
            {
                ApplyMutator(mutator, conflicts);
                return;
            }

            string conflictText = string.Join(", ", conflicts.Select(x => x.Label(planetTile)));
            Find.WindowStack.Add(new Dialog_MessageBox(
                string.Format("WTME.ConflictText".Translate().ToString(), mutator.Label(planetTile), conflictText),
                "WTME.Confirm".Translate(), () => ApplyMutator(mutator, conflicts),
                "WTME.Cancel".Translate(), null, "WTME.ConflictTitle".Translate()));
        }

        private void ApplyMutator(TileMutatorDef mutator, List<TileMutatorDef> approvedConflicts)
        {
            if (HasGeneratedMap || !Available(mutator) || !IsValidCandidate(mutator)) return;
            List<TileMutatorDef> currentConflicts = tile.Mutators.Where(existing => HasCategoryConflict(existing, mutator)).ToList();
            if (currentConflicts.Any(existing => !approvedConflicts.Contains(existing))) return;
            foreach (TileMutatorDef existing in currentConflicts) tile.RemoveMutator(existing);
            tile.AddMutator(mutator);
            InvalidateTileCaches();
            RefreshCandidates();
            Find.World.renderer.SetAllLayersDirty();
        }

        private bool IsValidCandidate(TileMutatorDef mutator)
        {
            // River workers read Rivers[0] for their label and need an existing world river.
            if (mutator.Worker is TileMutatorWorker_River &&
                (!(tile is SurfaceTile surface) || surface.Rivers.NullOrEmpty())) return false;
            return mutator.IsValidTile(planetTile, tile.Layer);
        }

        private static bool HasCategoryConflict(TileMutatorDef left, TileMutatorDef right)
        {
            return (left.categories != null && right.categories != null && left.categories.Intersect(right.categories).Any()) ||
                (left.categories != null && right.overrideCategories != null && left.categories.Intersect(right.overrideCategories).Any()) ||
                (right.categories != null && left.overrideCategories != null && right.categories.Intersect(left.overrideCategories).Any());
        }

        private void SetHilliness(Hilliness value)
        {
            List<TileMutatorDef> invalidated = tile.Mutators.Where(mutator =>
            {
                Hilliness old = CurrentHilliness;
                object oldLabel = HillinessLabelField?.GetValue(tile);
                try
                {
                    tile.hilliness = value;
                    HillinessLabelField?.SetValue(tile, null);
                    // Placement membership/category checks do not apply to a retained mutator.
                    return (mutator.minHilliness != Hilliness.Undefined && (int)value < (int)mutator.minHilliness) ||
                        (mutator.maxHilliness != Hilliness.Undefined && (int)value > (int)mutator.maxHilliness) ||
                        (mutator.Worker != null && !mutator.Worker.IsValidTile(planetTile, tile.Layer));
                }
                finally { tile.hilliness = old; HillinessLabelField?.SetValue(tile, oldLabel); }
            }).ToList();

            if (invalidated.Count > 0)
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    string.Format("WTME.HillinessConflictText".Translate().ToString(), HillinessUtility.GetLabelCap(value), string.Join(", ", invalidated.Select(x => x.Label(planetTile)))),
                    "WTME.Confirm".Translate(), () => ApplyHilliness(value),
                    "WTME.Cancel".Translate(), null, "WTME.ConflictTitle".Translate()));
                return;
            }
            ApplyHilliness(value);
        }

        private void ApplyHilliness(Hilliness value)
        {
            if (HasGeneratedMap) return;
            tile.hilliness = value;
            InvalidateTileCaches();
            RefreshCandidates();
            Find.World.renderer.SetAllLayersDirty();
        }

        private void InvalidateTileCaches()
        {
            HillinessLabelField?.SetValue(tile, null);
            SecondaryBiomeFlagField?.SetValue(tile, null);
            SecondaryBiomeField?.SetValue(tile, null);
        }

        private Hilliness CurrentHilliness => tile.hilliness;
    }
}
