using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

[DefOf]
public static class EESLetterDefOf
{
	public static LetterDef EES_CounterScoutArrived;

	public static LetterDef EES_CounterScoutEscaped;

	public static LetterDef EES_HVTTargetSpawned;

	public static LetterDef EES_StrikeFailed;

	public static LetterDef EES_SupplyCaravan;

	public static LetterDef EES_SupplyInvaded;

	public static LetterDef EES_SupplyEngaged;

	public static LetterDef EES_ScoutSuccess;

	public static LetterDef EES_ScoutFail;

	public static LetterDef EES_InfilSuccess;

	public static LetterDef EES_StrikeSuccess;

	public static LetterDef EES_InfilFail;

	static EESLetterDefOf()
	{
		DefOfHelper.EnsureInitializedInCtor(typeof(EESLetterDefOf));
	}
}
