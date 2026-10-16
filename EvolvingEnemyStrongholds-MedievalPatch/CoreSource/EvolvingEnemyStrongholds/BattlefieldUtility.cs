using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class BattlefieldUtility
{
	public const string Role = "Battlefield";

	public const int MinTier = 5;

	public const int MaxTier = 9;

	public const float Chance = 0.04f;

	public const float ReinforceBudgetFactorB = 0.8f;

	public const float ReinforceBudgetFactorCD = 1f;

	private const int BattlefieldRollSeed = 567166;

	private const int FactionCSeed = 567169;

	private const int FactionDSeed = 567186;

	public static bool IsBattlefield(string role)
	{
		return role == "Battlefield";
	}

	public static bool ShouldBeBattlefield(Settlement settlement, int tier)
	{
		if (StrongholdMod.Settings != null && StrongholdMod.Settings.disableBattlefield)
		{
			return false;
		}
		if (settlement == null || tier < 5 || tier > 9)
		{
			return false;
		}
		if (!IsWarTech(settlement.Faction))
		{
			return false;
		}
		if (FactionTerritoriesCompat.HasActiveInvasion(settlement))
		{
			return false;
		}
		Rand.PushState(Gen.HashCombineInt(settlement.ID, 567166));
		bool result = Rand.Value < 0.04f;
		Rand.PopState();
		return result;
	}

	public static bool IsWarTech(Faction f)
	{
		if (f?.def == null || !f.def.humanlikeFaction)
		{
			return false;
		}
		int techLevel = (int)f.def.techLevel;
		if (techLevel >= 3)
		{
			return techLevel <= 5;
		}
		return false;
	}

	public static bool IsParticipantTech(Faction f)
	{
		if (f?.def == null || !f.def.humanlikeFaction)
		{
			return false;
		}
		int techLevel = (int)f.def.techLevel;
		if (techLevel >= 3)
		{
			return techLevel <= 5;
		}
		return false;
	}

	public static void SelectFactions(Faction b, int settlementId, out Faction c, out Faction d)
	{
		c = null;
		d = null;
		if (b == null)
		{
			return;
		}
		Faction player = Faction.OfPlayer;
		List<Faction> allFactionsListForReading = Find.FactionManager.AllFactionsListForReading;
		List<Faction> list = (from f in allFactionsListForReading
			where IsCandidate(f, b) && f.HostileTo(b)
			orderby f.loadID
			select f).ToList();
		if (list.Count != 0)
		{
			c = PickDeterministic(list, settlementId, 567169);
			Faction cc = c;
			List<Faction> list2 = (from f in allFactionsListForReading
				where IsCandidate(f, b) && f != cc && f.HostileTo(b) && f.HostileTo(cc) && f.HostileTo(player)
				orderby f.loadID
				select f).ToList();
			if (list2.Count > 0)
			{
				d = PickDeterministic(list2, settlementId, 567186);
			}
		}
	}

	private static bool IsCandidate(Faction f, Faction b)
	{
		if (f != null && f != b && !f.IsPlayer && !f.Hidden && !f.defeated)
		{
			return IsParticipantTech(f);
		}
		return false;
	}

	private static Faction PickDeterministic(List<Faction> pool, int settlementId, int seed)
	{
		Rand.PushState(Gen.HashCombineInt(settlementId, seed));
		Faction result = pool[Rand.Range(0, pool.Count)];
		Rand.PopState();
		return result;
	}
}
