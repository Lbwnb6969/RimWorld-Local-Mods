using System.Collections.Generic;
using System.Linq;
using System;
using Core40k;
using HarmonyLib;
using RimWorld;
using RimWorld.QuestGen;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimDark.MastersOfMankind
{
    [StaticConstructorOnStartup]
    internal static class RankStatOffsetGuard
    {
        static RankStatOffsetGuard()
        {
            var harmony = new Harmony("Local.RimDark.MastersOfMankind.RankStatOffsetGuard");
            harmony.Patch(
                AccessTools.Method(typeof(CompRankInfo), nameof(CompRankInfo.GetStatOffset)),
                prefix: new HarmonyMethod(typeof(RankStatOffsetGuard), nameof(Prefix)));
            harmony.Patch(
                AccessTools.Method(typeof(CompRankInfo), nameof(CompRankInfo.GetStatFactor)),
                prefix: new HarmonyMethod(typeof(RankStatOffsetGuard), nameof(FactorPrefix)));
        }

        private static bool Prefix(CompRankInfo __instance, StatDef stat, ref float __result)
        {
            if (__instance == null || stat == null) return true;
            lock (__instance)
            {
                Dictionary<StatDef, float> cache = __instance.CachedStatOffset;
                if (cache.TryGetValue(stat, out __result)) return false;
                float offset = 0f;
                foreach (RankDef rank in __instance.UnlockedRanks)
                {
                    if (rank?.statOffsets != null)
                        offset += StatUtility.GetStatOffsetFromList(rank.statOffsets, stat);
                }
                // Core40k replaces these dictionaries when ranks change.
                cache[stat] = offset;
                __result = offset;
            }
            return false;
        }

        private static bool FactorPrefix(CompRankInfo __instance, StatDef stat, ref float __result)
        {
            if (__instance == null || stat == null) return true;
            lock (__instance)
            {
                Dictionary<StatDef, float> cache = __instance.CachedStatFactor;
                if (cache.TryGetValue(stat, out __result)) return false;
                float factor = 1f;
                foreach (RankDef rank in __instance.UnlockedRanks)
                {
                    if (rank?.statFactors != null)
                        factor *= StatUtility.GetStatFactorFromList(rank.statFactors, stat);
                }
                cache[stat] = factor;
                __result = factor;
            }
            return false;
        }
    }

    public sealed class QuestNode_ResearchFinishedOrFail : QuestNode
    {
        public SlateRef<ResearchProjectDef> research;

        protected override bool TestRunInt(Slate slate)
        {
            ResearchProjectDef project = research.GetValue(slate);
            return project != null && project.IsFinished;
        }

        protected override void RunInt()
        {
        }
    }

    public sealed class PawnKindChance
    {
        public PawnKindDef pawnKindDef;
        public float chance;
    }

    public sealed class CompProperties_AbilityEmperorLOTD : CompProperties_AbilityEffect
    {
        public float psychicSensitivityExtraSummmonFactor = 0.5f;
        public List<PawnKindChance> pawnKindDefs;
        public int summonLimit = -1;

        public CompProperties_AbilityEmperorLOTD()
        {
            compClass = typeof(CompAbilityEffect_EmperorLOTD);
        }
    }

    public sealed class CompAbilityEffect_EmperorLOTD : CompAbilityEffect
    {
        private new CompProperties_AbilityEmperorLOTD Props
        {
            get { return (CompProperties_AbilityEmperorLOTD)props; }
        }

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            int count = Mathf.Max(1, Mathf.FloorToInt(parent.pawn.GetStatValue(StatDefOf.PsychicSensitivity) /
                Props.psychicSensitivityExtraSummmonFactor));
            if (Props.summonLimit >= 0)
                count = Mathf.Min(count, Props.summonLimit);

            HediffDef duration = DefDatabase<HediffDef>.GetNamed(
                count == 1 ? "GW_Ruler_EmperorLOTD_6Hours" :
                count == 2 ? "GW_Ruler_EmperorLOTD_12Hours" : "GW_Ruler_EmperorLOTD_24Hours");

            float totalWeight = Props.pawnKindDefs.Sum(entry => entry.chance);
            for (int i = 0; i < count; i++)
            {
                float roll = Rand.Value * totalWeight;
                PawnKindDef kind = Props.pawnKindDefs[Props.pawnKindDefs.Count - 1].pawnKindDef;
                foreach (PawnKindChance entry in Props.pawnKindDefs)
                {
                    roll -= entry.chance;
                    if (roll <= 0f)
                    {
                        kind = entry.pawnKindDef;
                        break;
                    }
                }

                Pawn pawn = PawnGenerator.GeneratePawn(kind, Faction.OfPlayer);
                GenSpawn.Spawn(pawn, target.Cell, parent.pawn.Map);
                pawn.apparel.LockAll();
                pawn.health.AddHediff(duration);
                pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Follow, parent.pawn), JobCondition.Incompletable);
            }

            base.Apply(target, dest);
        }
    }

    public sealed class HediffCompProperties_DespawnAfterRemoved : HediffCompProperties
    {
        public HediffCompProperties_DespawnAfterRemoved()
        {
            compClass = typeof(HediffComp_DespawnAfterRemoved);
        }
    }

    public sealed class HediffComp_DespawnAfterRemoved : HediffComp
    {
        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            if (Pawn.Spawned)
                Pawn.DeSpawn();
        }
    }

    public sealed class CompProperties_EquippableGiveHediff : CompProperties
    {
        public HediffDef hediffDef;

        public CompProperties_EquippableGiveHediff()
        {
            compClass = typeof(CompEquippableGiveHediff);
        }
    }

    public sealed class CompEquippableGiveHediff : ThingComp
    {
        private CompProperties_EquippableGiveHediff Props
        {
            get { return (CompProperties_EquippableGiveHediff)props; }
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            if (!pawn.health.hediffSet.HasHediff(Props.hediffDef))
                pawn.health.AddHediff(Props.hediffDef);
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(Props.hediffDef);
            if (hediff != null)
                pawn.health.RemoveHediff(hediff);
        }
    }

    public sealed class HediffCompProperties_XpOnKill : HediffCompProperties
    {
        public bool isDrainAtRandom;
        public float drainPercent = 0.05f;

        public HediffCompProperties_XpOnKill()
        {
            compClass = typeof(HediffComp_XpOnKill);
        }
    }

    public sealed class HediffComp_XpOnKill : HediffComp
    {
        private HediffCompProperties_XpOnKill Props
        {
            get { return (HediffCompProperties_XpOnKill)props; }
        }

        public override void Notify_KilledPawn(Pawn victim, DamageInfo? dinfo)
        {
            base.Notify_KilledPawn(victim, dinfo);
            if (Pawn.skills == null || victim.skills == null)
                return;

            List<SkillRecord> eligible = victim.skills.skills.Where(skill => !skill.TotallyDisabled && skill.XpTotalEarned > 0f).ToList();
            if (Props.isDrainAtRandom && eligible.Count > 1)
                eligible = new List<SkillRecord> { eligible.RandomElement() };

            foreach (SkillRecord source in eligible)
            {
                SkillRecord target = Pawn.skills.GetSkill(source.def);
                if (!target.TotallyDisabled)
                    target.Learn(source.XpTotalEarned * Props.drainPercent, true);
            }
        }
    }

    public sealed class ApparelSetEffect
    {
        public List<ThingDef> allApparels;
        public HediffDef hediff;
    }

    public sealed class CompProperties_ApparelSet : CompProperties
    {
        public ApparelSetEffect apparelSetEffect;

        public CompProperties_ApparelSet()
        {
            compClass = typeof(CompApparelSet);
        }
    }

    public sealed class CompApparelSet : ThingComp
    {
        private CompProperties_ApparelSet Props
        {
            get { return (CompProperties_ApparelSet)props; }
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            Sync(pawn);
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);
            Sync(pawn);
        }

        private void Sync(Pawn pawn)
        {
            ApparelSetEffect effect = Props.apparelSetEffect;
            bool complete = effect.allApparels.All(def => pawn.apparel.WornApparel.Any(item => item.def == def));
            Hediff current = pawn.health.hediffSet.GetFirstHediffOfDef(effect.hediff);
            if (complete && current == null)
                pawn.health.AddHediff(effect.hediff);
            else if (!complete && current != null)
                pawn.health.RemoveHediff(current);
        }
    }

    public sealed class DefModExtension_ApparelRestriction : DefModExtension
    {
        public bool isRestrictStat;
        public StatDef statDef;
        public string reqMode;
        public float value;
    }

    [StaticConstructorOnStartup]
    internal static class ApparelRestrictionPatch
    {
        static ApparelRestrictionPatch()
        {
            new Harmony("Local.RimDark.MastersOfMankind").Patch(
                AccessTools.Method(typeof(ApparelUtility), nameof(ApparelUtility.HasPartsToWear)),
                postfix: new HarmonyMethod(typeof(ApparelRestrictionPatch), nameof(Postfix)));
        }

        private static void Postfix(Pawn p, ThingDef apparel, ref bool __result)
        {
            if (!__result)
                return;

            DefModExtension_ApparelRestriction restriction = apparel.GetModExtension<DefModExtension_ApparelRestriction>();
            if (restriction != null && restriction.isRestrictStat && restriction.reqMode == "GreaterEqual")
                __result = p.GetStatValue(restriction.statDef) >= restriction.value;
        }
    }

}
