using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using EvolvingEnemyStrongholds;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace EESMedievalPatch;

// GenSpawn otherwise rerolls this flag even when restoring a serialized
// rotation. Exact guarded construction relocations also keep their original
// orientation; normal new spawns and unrelated maps retain the native branch.
[HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.Spawn), new[]
{
    typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool)
})]
internal static class Patch_PreserveStrongholdLoadedRotation
{
    internal static bool ShouldRandomize(bool original, Thing newThing, Map map, bool respawningAfterLoad)
    {
        if (original && MedievalUtility.IsConstructionRelocation(newThing, map)) return false;
        if (!original || !respawningAfterLoad || newThing is not Building ||
            map?.Parent is not Settlement site || site.Faction == null || site.Faction.IsPlayer ||
            WorldComponent_Strongholds.Instance?.GetExisting(site.ID) == null) return original;
        return false;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var field = AccessTools.Field(typeof(ThingDef), nameof(ThingDef.randomizeRotationOnSpawn));
        var policy = AccessTools.Method(typeof(Patch_PreserveStrongholdLoadedRotation), nameof(ShouldRandomize));
        var result = new List<CodeInstruction>();
        int matches = 0;
        foreach (var instruction in instructions)
        {
            result.Add(instruction);
            if (!instruction.LoadsField(field)) continue;
            matches++;
            result.Add(new CodeInstruction(OpCodes.Ldarg_0));
            result.Add(new CodeInstruction(OpCodes.Ldarg_2));
            result.Add(new CodeInstruction(OpCodes.Ldarg_S, (byte)5));
            result.Add(new CodeInstruction(OpCodes.Call, policy));
        }
        if (matches != 1) throw new InvalidOperationException("Expected exactly one GenSpawn randomizeRotationOnSpawn branch; found " + matches);
        return result;
    }
}
