using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class FakeOutpostUtility
{
	public const string Insect = "FakeOutpostInsect";

	public const string Mech = "FakeOutpostMech";

	public const string Entity = "FakeOutpostEntity";

	public const int MinTier = 8;

	public const float Rarity = 0.05f;

	public const bool NaturalSpawnEnabled = true;

	private const int FakeRollSeed = 500961;

	private const int FakeVariantSeed = 500978;

	public static bool IsFake(string role)
	{
		if (!role.NullOrEmpty())
		{
			return role.StartsWith("FakeOutpost");
		}
		return false;
	}

	public static FakeVariant VariantOf(string role)
	{
		return role switch
		{
			"FakeOutpostInsect" => FakeVariant.Insect,
			"FakeOutpostMech" => FakeVariant.Mech,
			"FakeOutpostEntity" => FakeVariant.Entity,
			_ => FakeVariant.None,
		};
	}

	public static List<string> AvailableVariants()
	{
		List<string> list = new List<string>();
		if (!AnySettlementFactionOfKind(FakeVariant.Mech))
		{
			list.Add("FakeOutpostMech");
		}
		if (!AnySettlementFactionOfKind(FakeVariant.Insect))
		{
			list.Add("FakeOutpostInsect");
		}
		if (ModsConfig.AnomalyActive && !AnySettlementFactionOfKind(FakeVariant.Entity))
		{
			list.Add("FakeOutpostEntity");
		}
		return list;
	}

	public static bool AnySettlementFactionOfKind(FakeVariant variant)
	{
		List<Faction> list = Find.FactionManager?.AllFactionsListForReading;
		if (list == null)
		{
			return false;
		}
		for (int i = 0; i < list.Count; i++)
		{
			Faction faction = list[i];
			if (faction?.def != null && !faction.IsPlayer && !faction.Hidden && !faction.def.settlementTexturePath.NullOrEmpty() && variant switch
			{
				FakeVariant.Insect => EESFactionKind.IsInsect(faction.def),
				FakeVariant.Mech => EESFactionKind.IsMech(faction.def),
				FakeVariant.Entity => EESFactionKind.IsEntity(faction.def),
				_ => false,
			})
			{
				EESLog.Message($"[FakeVariant진단] {variant} 변종 제외 → 세력 '{faction.Name}'({faction.def.defName}) " + $"humanlike={faction.def.humanlikeFaction} 가 해당 종류로 분류(정착지 보유). " + "basicMemberKind=" + faction.def.basicMemberKind?.defName + " race=" + faction.def.basicMemberKind?.race?.defName);
				return true;
			}
		}
		return false;
	}

	public static bool ShouldBeFake(Settlement settlement, int tier)
	{
		if (settlement == null || tier < 8)
		{
			return false;
		}
		Rand.PushState(Gen.HashCombineInt(settlement.ID, 500961));
		bool result = Rand.Value < 0.05f;
		Rand.PopState();
		return result;
	}

	public static string PickVariant(Settlement settlement, int tier)
	{
		List<string> list = AvailableVariants();
		if (list.Count == 0)
		{
			return null;
		}
		Rand.PushState(Gen.HashCombineInt(settlement.ID, 500978));
		string result = list[Rand.Range(0, list.Count)];
		Rand.PopState();
		return result;
	}
}
