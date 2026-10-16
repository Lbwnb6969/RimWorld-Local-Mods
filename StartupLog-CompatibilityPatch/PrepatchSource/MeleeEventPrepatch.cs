using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Prepatcher;
using UnityEngine;

namespace Local.StartupLogFixes
{
    public static class MeleeEventPrepatch
    {
        [FreePatchAll]
        public static bool RewriteEvents(object module)
        {
            if ((string)Read(Read(Read(module, "Assembly"), "Name"), "Name") != "zAnimationMod") return false;
            object eventBase = Items(module, "Types").FirstOrDefault(type => (string)Read(type, "FullName") == "AM.Events.EventBase");
            if (eventBase == null || (string)Read(Read(eventBase, "BaseType"), "FullName") != "UnityEngine.ScriptableObject") return false;
            var factories = Items(eventBase, "Methods").Where(method => (string)Read(method, "Name") == ".cctor")
                .Concat(Items(eventBase, "NestedTypes").SelectMany(type => Items(type, "Methods")).Where(method =>
                    ((string)Read(method, "Name")).StartsWith("<.cctor>", StringComparison.Ordinal) &&
                    (string)Read(Read(method, "ReturnType"), "FullName") == "AM.Events.EventBase" && !Items(method, "Parameters").Any())).ToList();
            var replacements = new List<object>();
            int nativeCalls = 0;
            foreach (object factory in factories)
            foreach (object instruction in Items(Read(factory, "Body"), "Instructions"))
            {
                if (Read(Read(instruction, "OpCode"), "Code").ToString() != "Call") continue;
                object call = Read(instruction, "Operand");
                var parameters = Items(call, "Parameters").ToList();
                if (parameters.Count != 1 || (string)Read(Read(parameters[0], "ParameterType"), "FullName") != "System.Type" ||
                    (string)Read(call, "Name") != "CreateInstance") continue;
                string owner = (string)Read(Read(call, "DeclaringType"), "FullName");
                if (owner == "System.Activator") replacements.Add(instruction);
                if (owner == "UnityEngine.ScriptableObject") nativeCalls++;
            }
            if (replacements.Count == 0 && nativeCalls == 2) return false;
            if (factories.Count != 2 || replacements.Count != 2)
                throw new InvalidOperationException("Melee Animations event factories changed; retained original assembly.");
            object creation = module.GetType().GetMethod("ImportReference", new[] { typeof(MethodBase) }).Invoke(module,
                new object[] { typeof(ScriptableObject).GetMethod("CreateInstance", new[] { typeof(Type) }) });
            foreach (object instruction in replacements) instruction.GetType().GetProperty("Operand").SetValue(instruction, creation, null);
            return true;
        }

        // Prepatcher's Cecil types are internal to Harmony; keep this optional assembly independent of its private ABI.
        private static object Read(object value, string name)
        {
            return value.GetType().GetProperty(name).GetValue(value, null);
        }

        private static IEnumerable<object> Items(object value, string name)
        {
            return ((IEnumerable)Read(value, name)).Cast<object>();
        }
    }
}
