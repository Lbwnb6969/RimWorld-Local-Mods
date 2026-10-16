using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class StrongholdCultureCenters
{
    private static readonly Dictionary<string, (string Style, string Floor)> Profiles = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        { "BA_France_NobleHouse", ("courtyardKeep", "DankPyon_Floor_Versailles_Limestone") },
        { "BA_HRE_NobleHouse", ("greatHallKeep", "DankPyon_Floor_RusticWoodFloor_DarkWood") },
        { "BA_Poland_NobleHouse", ("greatHallKeep", "FlagstoneSlate") },
        { "BA_Byzantium_NobleHouse", ("palaceCourts", "DankPyon_Floor_Lys_Blue") },
        { "BA_Turkic_NobleHouse", ("palaceCourts", "FlagstoneSandstone") },
        { "BA_Moor_NobleHouse", ("palaceCourts", "DankPyon_Floor_Lys_Green") },
        { "BA_Portugal_NobleHouse", ("courtyardKeep", "FlagstoneGranite") },
        { "BA_Spain_NobleHouse", ("palaceCourts", "DankPyon_Floor_Lys_Red") },
        { "BA_Mongol_NobleHouse", ("greatHallKeep", "DankPyon_Floor_RusticWoodFloor_Wood") }
    };

    private static readonly HashSet<string> NativeTemplates = new HashSet<string>(StringComparer.Ordinal)
    {
        "BA_HRETown", "BA_PolishTown", "BA_ByzantiumTown", "BA_TurkishTown", "BA_IberiaTown", "BA_MongolTown",
        "EESMK_Byzantium_AccessibleConstantinopleTown"
    };

    public static string StyleFor(Faction faction)
    {
        return faction?.def != null && Profiles.TryGetValue(faction.def.defName, out var profile) ? profile.Style : "courtyardKeep";
    }

    public static string StyleFor(Faction faction, string role, int variant)
    {
        var primary = StyleFor(faction);
        if (primary == "courtyardKeep")
            primary = role switch
            {
                "NobleEstate" or "ResearchOutpost" => "palaceCourts",
                "CommandFortress" or "PrisonCamp" or "AmmoDepot" or "Mine" => "greatHallKeep",
                _ => primary
            };
        if (variant != 1 || faction?.def?.humanlikeFaction != true) return primary;
        // Saved style names distinguish the new alternative from older unbuilt
        // plans, whose native castle reuse and chosen style must remain intact.
        return primary == "courtyardKeep" ? "palaceCourtsAlternative" : "courtyardKeepAlternative";
    }

    public static TerrainDef FloorFor(Faction faction, TerrainDef fallback)
    {
        return faction?.def != null && Profiles.TryGetValue(faction.def.defName, out var profile) ?
            DefDatabase<TerrainDef>.GetNamedSilentFail(profile.Floor) ?? fallback : fallback;
    }

    public static bool PreserveNativeTemplate(Faction faction, string template)
    {
        return faction?.def != null && Profiles.ContainsKey(faction.def.defName) && template != null && NativeTemplates.Contains(template);
    }

    // These exact faction/layout pairs are supplied by the installed native
    // CustomGenOption.chooseFromlayouts, rather than chooseFromSettlements.
    public static bool IsNativeCastleLayout(Faction faction, string template)
    {
        string family = faction?.def?.defName switch
        {
            "DankPyon_NobleHouseFierce" => "Amboise",
            "DankPyon_NobleHouseCivil2" => "Soren",
            "DankPyon_NobleHouseCivil" => "Oswin",
            "DankPyon_NobleHouseSavage" => "Hesse",
            _ => null
        };
        return family != null && (template == "DankPyon_Settlement1_" + family ||
            template == "DankPyon_Settlement2_" + family || template == "DankPyon_Settlement3_" + family);
    }

    // Resolve only new furniture requests. Native furniture is only downgraded during fresh low-tier map generation.
    public static ThingDef FurnitureFor(string name, int tier)
    {
        var fallback = name switch
        {
            "DankPyon_RoyalCloset" => ("DankPyon_RusticDresser", "Dresser"),
            "DankPyon_RoyalBookshelf" => ("DankPyon_RusticBookshelf", "Shelf"),
            "DankPyon_RoyalArmchair" => ("DankPyon_RusticChair", "DiningChair"),
            "DankPyon_RoyalThrone" => ("DankPyon_RusticChair", "DiningChair"),
            "DankPyon_RoyalTable2x2c" => ("DankPyon_Table2x2c", "Table1x2c"),
            "DankPyon_RoyalTable2x4c" => ("DankPyon_Table2x4c", "Table2x4c"),
            "DankPyon_RoyalEndTable" => ("DankPyon_RusticEndTable", "EndTable"),
            "DankPyon_RoyalDresser" => ("DankPyon_RusticDresser", "Dresser"),
            "DankPyon_RoyalTudorBed" => ("DankPyon_Bed_RusticDouble_Red", "DoubleBed"),
            "DankPyon_RoyalChest" => ("DankPyon_RusticChest", "Shelf"),
            _ => (null, null)
        };
        var requested = DefDatabase<ThingDef>.GetNamedSilentFail(name);
        if (fallback.Item1 == null || tier >= 10 && requested != null) return requested;
        return DefDatabase<ThingDef>.GetNamedSilentFail(fallback.Item1) ??
            DefDatabase<ThingDef>.GetNamedSilentFail(fallback.Item2);
    }

    public static ThingDef NativeFurnitureFallback(ThingDef current, int tier)
    {
        if (current == null || tier >= 10) return null;
        string name = current.defName ?? string.Empty;
        string request = null;
        if (name.StartsWith("DankPyon_RoyalArmchair", StringComparison.Ordinal)) request = "DankPyon_RoyalArmchair";
        else if (name.StartsWith("DankPyon_RoyalBookshelf", StringComparison.Ordinal)) request = "DankPyon_RoyalBookshelf";
        else if (name.StartsWith("DankPyon_RoyalTable2x2c", StringComparison.Ordinal)) request = "DankPyon_RoyalTable2x2c";
        else if (name.StartsWith("DankPyon_RoyalTable2x4c", StringComparison.Ordinal)) request = "DankPyon_RoyalTable2x4c";
        else if (name.StartsWith("DankPyon_RoyalEndTable", StringComparison.Ordinal)) request = "DankPyon_RoyalEndTable";
        else if (name.StartsWith("DankPyon_RoyalTudorBed", StringComparison.Ordinal)) request = "DankPyon_RoyalTudorBed";
        else if (name.StartsWith("DankPyon_RoyalDresser", StringComparison.Ordinal)) request = "DankPyon_RoyalDresser";
        else if (name.StartsWith("DankPyon_RoyalCloset", StringComparison.Ordinal)) request = "DankPyon_RoyalCloset";
        if (request == null) return null;
        ThingDef replacement = FurnitureFor(request, tier);
        return replacement != null && replacement.defName != current.defName ? replacement : null;
    }
}
