using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Local.StartupLogFixes
{
    public sealed partial class StartupLogFixesMod
    {
        private static Type meleeEventBaseType;

        private static void PatchMeleeAnimationEventFactories(Harmony harmony)
        {
            meleeEventBaseType = AccessTools.TypeByName("AM.Events.EventBase");
            if (meleeEventBaseType == null || !typeof(ScriptableObject).IsAssignableFrom(meleeEventBaseType)) return;
            foreach (Type closure in meleeEventBaseType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(closure))
            {
                if (method.ReturnType == meleeEventBaseType && method.GetParameters().Length == 0 &&
                    method.Name.StartsWith("<.cctor>", StringComparison.Ordinal))
                {
                    var activation = AccessTools.Method(typeof(Activator), nameof(Activator.CreateInstance), new[] { typeof(Type) });
                    if (!PatchProcessor.GetOriginalInstructions(method).Any(instruction => instruction.Calls(activation))) continue;
                    TryPatch(harmony, method, "Melee Animations saved event creation",
                        transpilerName: nameof(MeleeAnimationEventFactoryTranspiler));
                }
            }
        }

        public static IEnumerable<CodeInstruction> MeleeAnimationEventFactoryTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = instructions.ToList();
            MethodInfo activation = AccessTools.Method(typeof(Activator), nameof(Activator.CreateInstance), new[] { typeof(Type) });
            if (original.Count(instruction => instruction.Calls(activation)) != 1)
                throw new InvalidOperationException("Melee Animations event factory changed; expected exactly one Activator.CreateInstance(Type) call.");
            MethodInfo replacement = AccessTools.Method(typeof(StartupLogFixesMod), nameof(CreateMeleeAnimationEvent));
            foreach (CodeInstruction instruction in original)
            {
                if (instruction.Calls(activation))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                }
            }
            return original;
        }

        public static object CreateMeleeAnimationEvent(Type type)
        {
            if (meleeEventBaseType != null && type != null && type.IsSubclassOf(meleeEventBaseType))
                return ScriptableObject.CreateInstance(type);
            return Activator.CreateInstance(type);
        }
    }
}
