using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace Local.StartupLogFixes
{
    public sealed partial class StartupLogFixesMod
    {
        public static IEnumerable<CodeInstruction> ModWarningsTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            MethodInfo add = AccessTools.Method(typeof(Dictionary<string, string>), "Add",
                new[] { typeof(string), typeof(string) });
            var matches = code.Where(instruction => instruction.Calls(add)).ToList();
            if (matches.Count != 1)
            {
                Log.Warning("[StartupLog Compatibility] Mod warning merge was not installed: expected one Dictionary.Add call, found " + matches.Count + ".");
                return code;
            }
            matches[0].opcode = OpCodes.Call;
            matches[0].operand = AccessTools.Method(typeof(StartupLogFixesMod), nameof(MergeModWarning));
            return code;
        }

        public static void MergeModWarning(Dictionary<string, string> warnings, string packageId, string warning)
        {
            if (!warnings.TryGetValue(packageId, out string existing))
            {
                warnings.Add(packageId, warning);
                return;
            }
            // Merge only this duplicate insertion. All other exceptions and
            // every other package's dependency/order warnings remain intact.
            warnings[packageId] = string.IsNullOrEmpty(existing) ? warning :
                string.IsNullOrEmpty(warning) ? existing : existing + Environment.NewLine + warning;
        }
    }
}
