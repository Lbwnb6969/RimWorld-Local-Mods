using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class MedievalOverhaulCompat
{
	private const string MoPackageId = "dankpyon.medieval.overhaul";

	private const string EmpireDefName = "Empire";

	private static bool _inited;

	private static ModContentPack _moMod;

	public static bool Active
	{
		get
		{
			if (!_inited)
			{
				Init();
			}
			return _moMod != null;
		}
	}

	private static void Init()
	{
		_inited = true;
		foreach (ModContentPack item in LoadedModManager.RunningModsListForReading)
		{
			if (item != null && item.PackageId == "dankpyon.medieval.overhaul")
			{
				_moMod = item;
				break;
			}
		}
		if (_moMod == null)
		{
			ThingDef thingDef = DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Turret_Scorpio") ?? DefDatabase<ThingDef>.GetNamedSilentFail("DankPyon_Turret_Ballista");
			if (thingDef?.modContentPack != null && !thingDef.modContentPack.IsOfficialMod)
			{
				_moMod = thingDef.modContentPack;
			}
		}
		if (_moMod != null)
		{
			EESLog.Message("Medieval Overhaul 감지 (" + _moMod.Name + ") — 제국(Empire) 포탑 풀에 MO 중세 포탑을 own으로 주입.");
		}
	}

	public static bool IsEmpireOwnTurret(Faction faction, ModContentPack turretMod)
	{
		if (!Active || turretMod == null)
		{
			return false;
		}
		if (faction?.def == null || faction.def.defName != "Empire")
		{
			return false;
		}
		return turretMod == _moMod;
	}
}
