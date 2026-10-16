using System.Reflection;
using HarmonyLib;
using Verse;

namespace Local.MThings.Geysers.ChineseSimplified
{
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        static Bootstrap()
        {
            new Harmony("local.mthings.geysers.chinesesimplified").PatchAll(typeof(Bootstrap).Assembly);
        }
    }

    [HarmonyPatch]
    internal static class SettingsCategoryPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method("MThings.Geysers.MThingsGeysersMod:SettingsCategory");
        }

        private static void Postfix(ref string __result)
        {
            const string key = "MThingsGeysers_Settings_Category";
            if (LanguageDatabase.activeLanguage?.HaveTextForKey(key) == true)
                __result = key.Translate();
        }
    }
}
