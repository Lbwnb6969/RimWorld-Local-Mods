#if WAYLITH
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace Local.StartupLogFixes
{
    public sealed partial class StartupLogFixesMod
    {
        static Func<ThingDef,ThingDef> replacementMainDef;
        static FieldInfo rimDarkChoices, rimDarkAnnouncedVariants;
        static readonly AccessTools.FieldRef<ThingFilter,List<ThingDef>> explicitThingDefs=AccessTools.FieldRefAccess<ThingFilter,List<ThingDef>>("thingDefs");
        static readonly AccessTools.FieldRef<ThingFilter,List<ThingDef>> excludedThingDefs=AccessTools.FieldRefAccess<ThingFilter,List<ThingDef>>("disallowedThingDefs");
        static void PatchWaylithContent(Harmony harmony)
        {
            var customization=AccessTools.TypeByName("Core40k.CompGeneCustomization");
            if(customization!=null) {
                rimDarkChoices=AccessTools.Field(customization,"choices");
                rimDarkAnnouncedVariants=AccessTools.Field(customization,"announcedVariants");
                TryPatch(harmony,AccessTools.Method(customization,"PostExposeData"),"RimDark legacy gene collections before job PostLoadInit",postfixName:nameof(RimDarkGeneCollections));
            }
            var utils=AccessTools.TypeByName("ReplaceLib.Utils");
            var getMain=utils?.GetMethods(BindingFlags.Public|BindingFlags.Static).SingleOrDefault(m=>m.Name=="GetMainDef"&&m.ReturnType==typeof(ThingDef)&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType==typeof(ThingDef));
            if(getMain!=null) {
                replacementMainDef=(Func<ThingDef,ThingDef>)Delegate.CreateDelegate(typeof(Func<ThingDef,ThingDef>),getMain);
                TryPatch(harmony,AccessTools.Method("ReplaceLib.Startup:Debug"),"canonical recipe ingredient definitions",prefixName:nameof(CanonicalizeRecipes));
            }
            TryPatch(harmony,AccessTools.Method(typeof(ThingFilter),nameof(ThingFilter.ExposeData)),"obsolete artillery filter entries",prefixName:nameof(RemoveObsoleteArtilleryFilter));
            TryPatch(harmony,AccessTools.Method("ProcessorFramework.WorkGiver_FillProcessor:HasJobOnThing"),"processor exclusive input availability",prefixName:nameof(ProcessorAvailability));
            TryPatch(harmony,AccessTools.Method("ProcessorFramework.WorkGiver_FillProcessor:JobOnThing"),"processor nonzero input job",postfixName:nameof(ProcessorJobCount));
            TryPatch(harmony,AccessTools.Method("ProcessorFramework.JobDriver_FillProcessor:TryMakePreToilReservations"),"processor reservation recheck",prefixName:nameof(ProcessorReservations));
            var weaponCache=AccessTools.TypeByName("MuzzleFlash.WeaponPositionCache");
            if(weaponCache!=null&&weaponCache.IsAbstract)TryPatch(harmony,AccessTools.Method(typeof(Game),"ExposeSmallComponents"),"empty retired muzzle-flash cache",prefixName:nameof(RemoveEmptyWeaponCache));
            if(AccessTools.TypeByName("Analyzer.GameComponent_Analyzer")==null)TryPatch(harmony,AccessTools.Method(typeof(Game),"ExposeSmallComponents"),"empty retired Analyzer component",prefixName:nameof(RemoveEmptyAnalyzerComponent));
            if(AccessTools.TypeByName("Thek_GuardingPawns.MapComponent_GuardingPawns")==null) {
                TryPatch(harmony,AccessTools.Method(typeof(Map),"ExposeComponents"),"empty unavailable Guarding Pawns component",prefixName:nameof(RemoveEmptyGuardingComponent));
                TryPatch(harmony,AccessTools.Method(typeof(ThingFilter),nameof(ThingFilter.ExposeData)),"unavailable Guarding Pawns filter definitions",prefixName:nameof(RemoveMissingGuardingFilter));
            }
        }
        public static void RimDarkGeneCollections(object __instance)
        {
            // Older saves lack the new customization lists. Scribe sets them
            // to null during LoadingVars; job/gene PostLoadInit can read them
            // before this component's own PostLoadInit initializes them.
            if(Scribe.mode!=LoadSaveMode.LoadingVars)return;
            if(rimDarkChoices.GetValue(__instance)==null)rimDarkChoices.SetValue(__instance,Activator.CreateInstance(rimDarkChoices.FieldType));
            if(rimDarkAnnouncedVariants.GetValue(__instance)==null)rimDarkAnnouncedVariants.SetValue(__instance,Activator.CreateInstance(rimDarkAnnouncedVariants.FieldType));
        }
        public static void CanonicalizeRecipes()
        {
            int replaced=0;
            foreach(var recipe in DefDatabase<RecipeDef>.AllDefsListForReading) {
                var filters=new HashSet<ThingFilter>();
                if(recipe.defaultIngredientFilter!=null)filters.Add(recipe.defaultIngredientFilter);
                if(recipe.fixedIngredientFilter!=null)filters.Add(recipe.fixedIngredientFilter);
                if(recipe.ingredients!=null)foreach(var ingredient in recipe.ingredients)if(ingredient.filter!=null)filters.Add(ingredient.filter);
                foreach(var filter in filters) {
                    foreach(var old in filter.AllowedThingDefs.ToList()) {
                        var main=replacementMainDef(old);
                        if(main!=null&&main!=old){filter.SetAllow(old,false);filter.SetAllow(main,true);replaced++;}
                    }
                    foreach(var list in new[]{explicitThingDefs(filter),excludedThingDefs(filter)}) {
                        if(list==null)continue;
                        for(int i=0;i<list.Count;i++) {
                            var main=replacementMainDef(list[i]);
                            if(main!=null&&main!=list[i]){list[i]=main;replaced++;}
                        }
                    }
                }
            }
            if(replaced>0)Log.Message("[StartupLog Compatibility] Replaced "+replaced+" stale recipe ingredient references with ReplaceLib canonical definitions.");
        }
        public static void RemoveObsoleteArtilleryFilter()
        {
            if(Scribe.mode!=LoadSaveMode.LoadingVars)return;
            var root=Scribe.loader.curXmlParent;var allowed=root?.SelectSingleNode("allowedDefs");
            if(allowed==null)return;
            int removed=0;
            foreach(XmlNode item in allowed.ChildNodes.Cast<XmlNode>().ToList()) {
                if(item.Name!="li")continue;
                string name=item.InnerText;
                if(name.StartsWith("VFES_WorldArtillery_",StringComparison.Ordinal)&&DefDatabase<ThingDef>.GetNamedSilentFail(name)==null){allowed.RemoveChild(item);removed++;}
            }
            if(removed>0)Log.Message("[StartupLog Compatibility] Removed "+removed+" unavailable legacy world-artillery entries from a saved ThingFilter; active shell definitions retained.");
        }
        public static bool ProcessorAvailability(Pawn pawn,Thing t,bool forced,ref bool __result)
        {
            if(pawn.CanReserve(t,1,-1,null,forced))return true;
            __result=false;return false;
        }
        public static void ProcessorJobCount(ref Job __result)
        {
            if(__result!=null&&__result.count<=0)__result=null;
        }
        public static bool ProcessorReservations(JobDriver __instance,ref bool __result)
        {
            var job=__instance.job;
            if(job.count>0&&__instance.pawn.CanReserve(job.targetA,1,-1)&&__instance.pawn.CanReserve(job.targetB,1,job.count))return true;
            __result=false;return false;
        }
        public static void RemoveEmptyWeaponCache()
        {
            if(Scribe.mode!=LoadSaveMode.LoadingVars)return;
            var components=Scribe.loader.curXmlParent?.SelectSingleNode("components");
            if(components==null)return;
            foreach(XmlNode node in components.ChildNodes.Cast<XmlNode>().ToList()) {
                if(node.Attributes?["Class"]?.Value=="MuzzleFlash.WeaponPositionCache"&&!node.HasChildNodes) {
                    components.RemoveChild(node);
                    Log.Message("[StartupLog Compatibility] Removed an empty legacy MuzzleFlash.WeaponPositionCache entry; current cache is abstract and contains no saved state.");
                }
            }
        }
        public static void RemoveEmptyGuardingComponent()
        {
            if(Scribe.mode!=LoadSaveMode.LoadingVars)return;
            var components=Scribe.loader.curXmlParent?.SelectSingleNode("components");
            if(components==null)return;
            foreach(XmlNode node in components.ChildNodes.Cast<XmlNode>().ToList()) {
                if(node.Attributes?["Class"]?.Value!="Thek_GuardingPawns.MapComponent_GuardingPawns")continue;
                // Only the two known empty dictionaries can be discarded.
                if(node.SelectNodes(".//li").Count!=0)continue;
                if(node.ChildNodes.Cast<XmlNode>().Any(n=>n.Name!="GuardingP_GuardJobsExtraOptions"&&n.Name!="GuardingP_PreviousPatrolSpotPassedByPawn"))continue;
                components.RemoveChild(node);
                Log.Message("[StartupLog Compatibility] Removed the empty unavailable Guarding Pawns component from the loaded copy.");
            }
        }
        public static void RemoveEmptyAnalyzerComponent()
        {
            if(Scribe.mode!=LoadSaveMode.LoadingVars)return;
            var components=Scribe.loader.curXmlParent?.SelectSingleNode("components");
            if(components==null)return;
            foreach(XmlNode node in components.ChildNodes.Cast<XmlNode>().ToList()) {
                if(node.Attributes?["Class"]?.Value=="Analyzer.GameComponent_Analyzer"&&!node.HasChildNodes) {
                    components.RemoveChild(node);
                    Log.Message("[StartupLog Compatibility] Removed the empty retired Dubs Analyzer component from the loaded copy.");
                }
            }
        }
        public static void RemoveMissingGuardingFilter()
        {
            if(Scribe.mode!=LoadSaveMode.LoadingVars)return;
            var allowed=Scribe.loader.curXmlParent?.SelectSingleNode("allowedDefs");
            if(allowed==null)return;
            int removed=0;
            foreach(XmlNode item in allowed.ChildNodes.Cast<XmlNode>().ToList()) {
                string n=item.InnerText;
                if(item.Name=="li"&&(n.StartsWith("GuardingP_",StringComparison.Ordinal)||n.StartsWith("Blueprint_GuardingP_",StringComparison.Ordinal)||n.StartsWith("Frame_GuardingP_",StringComparison.Ordinal))&&DefDatabase<ThingDef>.GetNamedSilentFail(n)==null){allowed.RemoveChild(item);removed++;}
            }
            if(removed>0)Log.Message("[StartupLog Compatibility] Removed "+removed+" unavailable Guarding Pawns definitions from a saved ThingFilter.");
        }
    }
}
#endif
