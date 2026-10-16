using System.Linq;
using Verse;

namespace EESMedievalPatch;

internal static partial class MedievalUtility
{
    // Only new-generation gate/chamber passage cells use this extra check.
    // A zero protection score alone does not mean an occupant is standable or
    // handled by the actual passage clear/relocation chain (e.g. VCE bananas).
    internal static bool CanPrepareCastlePassageCell(Map map, IntVec3 cell)
    {
        if (!ConstructionContext(map) || !cell.InBounds(map)) return false;
        var things = cell.GetThingList(map);
        if (things.Any(t => IsProtectedPerimeterOccupant(t, map))) return false;
        if (cell.Standable(map)) return true;
        return things.Where(t => t is not Pawn && t.def.passability != Traversability.Standable)
            .All(t => CanClearNaturalBlocker(map, t) ||
                CanRelocateDefenseBlocker(t, map, map.ParentFaction) || CanRelocateLooseDefenseBlocker(t, map));
    }
}
