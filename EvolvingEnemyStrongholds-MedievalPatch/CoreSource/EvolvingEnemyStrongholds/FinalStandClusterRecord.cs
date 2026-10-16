using System.Collections.Generic;
using Verse;

namespace EvolvingEnemyStrongholds;

public class FinalStandClusterRecord : IExposable
{
    public int schemaVersion = 2;
    public FinalStandPhase phase = FinalStandPhase.Candidate;
    public string anchorLayerId;
    public int anchorTileId = -1;
    public List<string> memberKeys = new List<string>();
    public List<string> playerConquestKeys = new List<string>();
    public List<string> removedMemberKeys = new List<string>();
    public string targetMemberKey;
    public bool existingMapPending;
    public bool existingMapReinforcementAttempted;
    public int existingMapReinforcementPawnCount;
    public int existingMapReinforcementTick = -1;
    public string existingMapReinforcementReason;
    public int resolvedTick = -1;
    public string resolutionReason;
    public int clusterId = -1;
    public int factionLoadId = -1;
    public int anchorSettlementId = -1;
    public List<int> settlementIds = new List<int>();
    public List<int> destroyedSettlementIds = new List<int>();
    public int initialCount;
    public int battleStartedTick = -1;
    public bool triggered;
    public int targetSettlementId = -1;
    // Budgets only: no conquered/dead Pawn object is resurrected or kept alive in the world.
    public int reservePeople;
    public int reserveElites;
    public float reservePoints;
    public List<string> reserveKindDefs = new List<string>();

    public void ExposeData()
    {
        Scribe_Values.Look(ref schemaVersion, "schemaVersion", 1);
        Scribe_Values.Look(ref phase, "phase", FinalStandPhase.Candidate);
        Scribe_Values.Look(ref anchorLayerId, "anchorLayerId");
        Scribe_Values.Look(ref anchorTileId, "anchorTileId", -1);
        Scribe_Collections.Look(ref memberKeys, "memberKeys", LookMode.Value);
        Scribe_Collections.Look(ref playerConquestKeys, "playerConquestKeys", LookMode.Value);
        Scribe_Collections.Look(ref removedMemberKeys, "removedMemberKeys", LookMode.Value);
        Scribe_Values.Look(ref targetMemberKey, "targetMemberKey");
        Scribe_Values.Look(ref existingMapPending, "existingMapPending", false);
        Scribe_Values.Look(ref existingMapReinforcementAttempted, "existingMapReinforcementAttempted", false);
        Scribe_Values.Look(ref existingMapReinforcementPawnCount, "existingMapReinforcementPawnCount", 0);
        Scribe_Values.Look(ref existingMapReinforcementTick, "existingMapReinforcementTick", -1);
        Scribe_Values.Look(ref existingMapReinforcementReason, "existingMapReinforcementReason");
        Scribe_Values.Look(ref resolvedTick, "resolvedTick", -1);
        Scribe_Values.Look(ref resolutionReason, "resolutionReason");
        Scribe_Values.Look(ref clusterId, "clusterId", -1);
        Scribe_Values.Look(ref factionLoadId, "factionLoadId", -1);
        Scribe_Values.Look(ref anchorSettlementId, "anchorSettlementId", -1);
        Scribe_Collections.Look(ref settlementIds, "settlementIds", LookMode.Value);
        Scribe_Collections.Look(ref destroyedSettlementIds, "destroyedSettlementIds", LookMode.Value);
        Scribe_Values.Look(ref initialCount, "initialCount", 0);
        Scribe_Values.Look(ref battleStartedTick, "battleStartedTick", -1);
        Scribe_Values.Look(ref triggered, "triggered", false);
        Scribe_Values.Look(ref targetSettlementId, "targetSettlementId", -1);
        Scribe_Values.Look(ref reservePeople, "reservePeople", 0);
        Scribe_Values.Look(ref reserveElites, "reserveElites", 0);
        Scribe_Values.Look(ref reservePoints, "reservePoints", 0f);
        Scribe_Collections.Look(ref reserveKindDefs, "reserveKindDefs", LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            memberKeys ??= new List<string>();
            playerConquestKeys ??= new List<string>();
            removedMemberKeys ??= new List<string>();
            reserveKindDefs ??= new List<string>();
        }
        if (Scribe.mode == LoadSaveMode.PostLoadInit && settlementIds == null)
            settlementIds = new List<int>();
        if (Scribe.mode == LoadSaveMode.PostLoadInit && destroyedSettlementIds == null)
            destroyedSettlementIds = new List<int>();
    }
}
