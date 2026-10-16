using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public enum FinalStandOutcomeKind
{
    PendingDecision,
    PlayerConquest,
    NpcLoss,
    Vassalized,
    Ceded,
    PassiveRemoval
}

public enum FinalStandPhase
{
    Candidate,
    Active,
    Triggered,
    Resolved,
    Cancelled
}

// A stable location/faction identity survives replacement of the Settlement object.
public sealed class FinalStandOutcome : IExposable
{
    public string memberKey;
    public int settlementId = -1;
    public int factionLoadId = -1;
    public string layerId;
    public int tileId = -1;
    public int destroyedSettlementId = -1;
    public int alliedFactionLoadId = -1;
    public int outcomeTick = -1;
    public bool playerVictoryConfirmed;
    public FinalStandOutcomeKind kind;
    public int sourceTier;
    public int sourceMilitaryPower;
    public List<string> sourceCombatKindDefs = new List<string>();

    // The identity is deliberately independent of Settlement.ID. A rebuilt settlement
    // at the same layer/tile/faction must remain the same historical member, while a
    // replacement at another location must not inherit a campaign without explicit
    // lineage supplied by a caller.
    public static string KeyFor(string layerId, int tileId, int factionLoadId) =>
        string.IsNullOrEmpty(layerId) || tileId < 0 || factionLoadId < 0
            ? null : layerId + ":" + tileId + ":" + factionLoadId;

    public static string KeyFor(Settlement settlement) => settlement?.Faction == null || !settlement.Tile.Valid
        ? null : KeyFor(settlement.Tile.Layer.GetUniqueLoadID(), settlement.Tile.tileId, settlement.Faction.loadID);

    public static FinalStandOutcome Capture(Settlement settlement, FinalStandOutcomeKind kind, bool playerVictory = false)
    {
        string key = KeyFor(settlement);
        if (key == null) return null;
        var data = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
        return new FinalStandOutcome
        {
            memberKey = key,
            settlementId = settlement.ID,
            factionLoadId = settlement.Faction.loadID,
            layerId = settlement.Tile.Layer.GetUniqueLoadID(),
            tileId = settlement.Tile.tileId,
            outcomeTick = Find.TickManager?.TicksGame ?? -1,
            playerVictoryConfirmed = playerVictory,
            kind = kind,
            sourceTier = data?.tier ?? 0,
            sourceMilitaryPower = data?.militaryPower ?? 0,
            sourceCombatKindDefs = settlement.Faction.def.pawnGroupMakers?
                .Where(g => g?.kindDef == PawnGroupKindDefOf.Combat).SelectMany(g => g.options ?? new List<PawnGenOption>())
                .Where(o => o?.kind != null).Select(o => o.kind.defName).Distinct().OrderBy(n => n).Take(32).ToList() ?? new List<string>()
        };
    }

    public FinalStandOutcome WithKind(FinalStandOutcomeKind nextKind) => new FinalStandOutcome
    {
        memberKey = memberKey, settlementId = settlementId, factionLoadId = factionLoadId,
        layerId = layerId, tileId = tileId, destroyedSettlementId = destroyedSettlementId,
        alliedFactionLoadId = alliedFactionLoadId, outcomeTick = outcomeTick,
        playerVictoryConfirmed = playerVictoryConfirmed, kind = nextKind,
        sourceTier = sourceTier, sourceMilitaryPower = sourceMilitaryPower,
        sourceCombatKindDefs = new List<string>(sourceCombatKindDefs ?? new List<string>())
    };

    public void ExposeData()
    {
        Scribe_Values.Look(ref memberKey, "memberKey");
        Scribe_Values.Look(ref settlementId, "settlementId", -1);
        Scribe_Values.Look(ref factionLoadId, "factionLoadId", -1);
        Scribe_Values.Look(ref layerId, "layerId");
        Scribe_Values.Look(ref tileId, "tileId", -1);
        Scribe_Values.Look(ref destroyedSettlementId, "destroyedSettlementId", -1);
        Scribe_Values.Look(ref alliedFactionLoadId, "alliedFactionLoadId", -1);
        Scribe_Values.Look(ref outcomeTick, "outcomeTick", -1);
        Scribe_Values.Look(ref playerVictoryConfirmed, "playerVictoryConfirmed", false);
        Scribe_Values.Look(ref kind, "kind", FinalStandOutcomeKind.PassiveRemoval);
        Scribe_Values.Look(ref sourceTier, "sourceTier", 0);
        Scribe_Values.Look(ref sourceMilitaryPower, "sourceMilitaryPower", 0);
        Scribe_Collections.Look(ref sourceCombatKindDefs, "sourceCombatKindDefs", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit) sourceCombatKindDefs ??= new List<string>();
    }
}
