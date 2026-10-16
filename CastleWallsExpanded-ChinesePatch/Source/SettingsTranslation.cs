using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;

namespace CastleWallsExpandedChinese
{
    [StaticConstructorOnStartup]
    public static class SettingsTranslation
    {
        private static readonly Dictionary<string, string> Translations = new Dictionary<string, string>
        {
            { "Castle Walls Expanded", "城堡墙扩展" },
            { "Full game reload is required after ANY change here!", "更改此处的任何设置后都需要完全重启游戏！" },
            { "Content Options", "内容选项" },
            { "Add expanded castle content?", "启用扩展城堡内容？" },
            { "Add experimental diagonal castle walls (with amateurish textures)?", "启用实验性斜向城墙（使用较为粗糙的贴图）？" },
            { "Balancing Options", "平衡选项" },
            { "Use the castle walls material cost multiplier?", "启用城墙材料消耗倍率？" },
            { "Castle walls material cost multiplier: {0:F2}x", "城墙材料消耗倍率：{0:F2}x" },
            { "Use the defense tower steel refuel cost multiplier?", "启用防御塔钢铁补充消耗倍率？" },
            { "Defense tower steel refuel cost multiplier: {0:F2}x", "防御塔钢铁补充消耗倍率：{0:F2}x" },
            { "(A value of 0x will remove the refueling requirement entirely)", "（设为 0x 将完全移除补充材料需求）" },
            { "Add an electricity requirement for defense towers?", "为防御塔添加供电需求？" },
            { "Electricity requirement for defense towers: {0}W", "防御塔耗电：{0}W" },
            { "Add an electricity requirement for gates? (Quicker open speed when powered!)", "为城堡大门添加供电需求？（通电时开启更快！）" },
            { "Electricity requirement for gates: {0}W", "城堡大门耗电：{0}W" },
            { "Use the gate open speed multiplier?", "启用城堡大门开启速度倍率？" },
            { "Gate open speed multiplier: {0:F2}x", "城堡大门开启速度倍率：{0:F2}x" },
            { "Remove maintenance requirement from defense towers?", "移除防御塔的维护需求？" },
            { "Lighting Options", "照明选项" },
            { "Add torch lights to castle walls?", "为城堡墙添加火炬照明？" },
            { "Add torch lights to castle non-defense towers?", "为非防御型城堡塔楼添加火炬照明？" },
            { "Add torch lights to castle defense towers?", "为城堡防御塔添加火炬照明？" },
            { "Add torch lights to castle gates?", "为城堡大门添加火炬照明？" }
        };

        static SettingsTranslation()
        {
            var targetType = AccessTools.TypeByName("CastleWallsExpanded.CastleWallsExpandedMod");
            if (targetType == null)
            {
                return;
            }

            var harmony = new Harmony("Local.CastleWallsExpanded.ChineseSimplified");
            var transpiler = new HarmonyMethod(typeof(SettingsTranslation), nameof(TranslateStrings));

            Patch(harmony, AccessTools.DeclaredMethod(targetType, "SettingsCategory"), transpiler);
            Patch(harmony, AccessTools.DeclaredMethod(targetType, "DoSettingsWindowContents"), transpiler);
        }

        private static void Patch(Harmony harmony, System.Reflection.MethodBase original, HarmonyMethod transpiler)
        {
            if (original != null)
            {
                harmony.Patch(original, transpiler: transpiler);
            }
        }

        public static IEnumerable<CodeInstruction> TranslateStrings(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var instruction in instructions)
            {
                bool translatedLiteral = instruction.opcode == OpCodes.Ldstr &&
                    instruction.operand is string source &&
                    Translations.ContainsKey(source);
                yield return instruction;
                if (translatedLiteral)
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(SettingsTranslation), nameof(TranslateString)));
            }
        }

        public static string TranslateString(string source)
        {
            if (LanguageDatabase.activeLanguage?.folderName?.StartsWith("ChineseSimplified",
                    System.StringComparison.Ordinal) == true && Translations.TryGetValue(source, out var translated))
                return translated;
            return source;
        }
    }
}
