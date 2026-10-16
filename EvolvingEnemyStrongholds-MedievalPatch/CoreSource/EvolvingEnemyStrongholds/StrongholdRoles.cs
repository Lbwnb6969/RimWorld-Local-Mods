using System.Collections.Generic;

namespace EvolvingEnemyStrongholds;

public static class StrongholdRoles
{
	public const string SupplyDepot = "SupplyDepot";

	public const string AmmoDepot = "AmmoDepot";

	public const string PrisonCamp = "PrisonCamp";

	public const string ResearchOutpost = "ResearchOutpost";

	public const string TrainingCamp = "TrainingCamp";

	public const string MortarBattery = "MortarBattery";

	public const string BlackMarketHub = "BlackMarketHub";

	public const string NobleEstate = "NobleEstate";

	public const string CommandFortress = "CommandFortress";

	public const string MechanoidRelay = "MechanoidRelay";

	public const string InsectHive = "InsectHive";

	public const string Mine = "Mine";

	public const string PollutedOutpost = "PollutedOutpost";

	public const string Battlefield = "Battlefield";

	public const string CryoBarracks = "CryoBarracks";

	public const string ArtilleryBase = "ArtilleryBase";

	public const string PsychicShrine = "PsychicShrine";

	public const string JammingStation = "JammingStation";

	public static readonly List<string> All = new List<string>
	{
		"SupplyDepot", "PrisonCamp", "ResearchOutpost", "BlackMarketHub", "NobleEstate", "CommandFortress", "MechanoidRelay", "Mine", "CryoBarracks", "ArtilleryBase",
		"PsychicShrine", "JammingStation"
	};

	public static readonly Dictionary<string, int> MinTier = new Dictionary<string, int>
	{
		{ "SupplyDepot", 1 },
		{ "PrisonCamp", 1 },
		{ "AmmoDepot", 2 },
		{ "ResearchOutpost", 2 },
		{ "BlackMarketHub", 2 },
		{ "MortarBattery", 3 },
		{ "NobleEstate", 3 },
		{ "CommandFortress", 4 },
		{ "MechanoidRelay", 1 },
		{ "InsectHive", 1 },
		{ "Mine", 2 },
		{ "PollutedOutpost", 2 },
		{ "Battlefield", 5 },
		{ "CryoBarracks", 4 },
		{ "ArtilleryBase", 3 },
		{ "PsychicShrine", 1 },
		{ "JammingStation", 2 }
	};
}
