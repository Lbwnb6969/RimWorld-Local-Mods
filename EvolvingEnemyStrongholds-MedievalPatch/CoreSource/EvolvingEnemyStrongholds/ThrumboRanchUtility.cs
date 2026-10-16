using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class ThrumboRanchUtility
{
	public const string Ranch = "ThrumboRanch";

	public const string AlphaRanch = "AlphaThrumboRanch";

	public const int RanchTier = 3;

	public const float RanchChance = 0.25f;

	public const float AlphaChance = 0.33f;

	private const int RanchRollSeed = 464800;

	private const int AlphaRollSeed = 464817;

	public static bool IsRanch(string role)
	{
		if (!(role == "ThrumboRanch"))
		{
			return role == "AlphaThrumboRanch";
		}
		return true;
	}

	public static bool IsAlpha(string role)
	{
		return role == "AlphaThrumboRanch";
	}

	public static bool RanchConditionsMet(Settlement settlement, int tier)
	{
		if (settlement == null || tier != 3)
		{
			return false;
		}
		Faction faction = settlement.Faction;
		if (faction?.def == null)
		{
			return false;
		}
		if ((int)faction.def.techLevel > 2)
		{
			return false;
		}
		BiomeDef biome = settlement.Biome;
		if (biome == null || biome.defName != "Grasslands")
		{
			return false;
		}
		return true;
	}

	public static bool ShouldBeRanch(Settlement settlement, int tier)
	{
		if (!RanchConditionsMet(settlement, tier))
		{
			return false;
		}
		Rand.PushState(Gen.HashCombineInt(settlement.ID, 464800));
		bool result = Rand.Value < 0.25f;
		Rand.PopState();
		return result;
	}

	public static string PickVariant(Settlement settlement, int tier)
	{
		if (!ModsConfig.OdysseyActive)
		{
			return "ThrumboRanch";
		}
		Rand.PushState(Gen.HashCombineInt(settlement.ID, 464817));
		bool num = Rand.Value < 0.33f;
		Rand.PopState();
		if (!num)
		{
			return "ThrumboRanch";
		}
		return "AlphaThrumboRanch";
	}
}
