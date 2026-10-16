using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class CECompat
{
    public static bool IsAmmo(ThingDef def) => def != null && Active && tAmmoDef != null && tAmmoDef.IsInstanceOfType(def);
	private static bool _inited;

	private static bool _active;

	private static Type tAmmoUser;

	private static Type tInventory;

	private static Type tAmmoDef;

	private static MethodInfo pUseAmmo;

	private static MethodInfo pHasMagazine;

	private static MethodInfo pCurMagCount;

	private static MethodInfo pMagSize;

	private static MethodInfo pCurrentAmmo;

	private static MethodInfo pProps;

	private static MethodInfo mResetAmmoCount;

	private static FieldInfo fAmmoSet;

	private static FieldInfo fAmmoTypes;

	private static FieldInfo fAmmoLink;

	private static MethodInfo pContainer;

	private static MethodInfo mUpdateInventory;

	private static MethodInfo mCanFit;

	private static readonly HashSet<string> NonRifleTags = new HashSet<string>
	{
		"CE_OneHandedWeapon", "CE_Sidearm", "CE_SpacerSidearm", "CE_SMG", "CE_Bow", "CE_XBow", "CE_Launchers", "CE_AdvancedLaunchers", "CE_FlareLauncher", "CE_GrenadeNeolithic",
		"CE_GrenadeFlashbang", "CE_Pila", "CE_AnimalBoom"
	};

	private static readonly Dictionary<Type, MethodInfo> _gunGetterCache = new Dictionary<Type, MethodInfo>();

	private static Type _tPropsAmmoUser;

	private static readonly Dictionary<int, int> _turretPrevMag = new Dictionary<int, int>();

	private static readonly Dictionary<int, int> _turretNextDiag = new Dictionary<int, int>();

	private const int TurretDiagInterval = 180;

	private static readonly string[] MgContains = new string[4] { "machinegun", "_lmg", "_hmg", "_gpmg" };

	private static FieldInfo _fAmmoClass;

	public static bool Active
	{
		get
		{
			if (StrongholdMod.Settings != null && !StrongholdMod.Settings.enableCEIntegration)
			{
				return false;
			}
			if (!_inited)
			{
				Init();
			}
			return _active;
		}
	}

	private static void Init()
	{
		_inited = true;
		try
		{
			tAmmoUser = AccessTools.TypeByName("CombatExtended.CompAmmoUser");
			tInventory = AccessTools.TypeByName("CombatExtended.CompInventory");
			tAmmoDef = AccessTools.TypeByName("CombatExtended.AmmoDef");
			if (tAmmoUser == null || tInventory == null || tAmmoDef == null)
			{
				_active = false;
				return;
			}
			pUseAmmo = AccessTools.PropertyGetter(tAmmoUser, "UseAmmo");
			pHasMagazine = AccessTools.PropertyGetter(tAmmoUser, "HasMagazine");
			pCurMagCount = AccessTools.PropertyGetter(tAmmoUser, "CurMagCount");
			pMagSize = AccessTools.PropertyGetter(tAmmoUser, "MagSize");
			pCurrentAmmo = AccessTools.PropertyGetter(tAmmoUser, "CurrentAmmo");
			pProps = AccessTools.PropertyGetter(tAmmoUser, "Props");
			mResetAmmoCount = AccessTools.Method(tAmmoUser, "ResetAmmoCount");
			Type type = AccessTools.TypeByName("CombatExtended.CompProperties_AmmoUser");
			Type type2 = AccessTools.TypeByName("CombatExtended.AmmoSetDef");
			Type type3 = AccessTools.TypeByName("CombatExtended.AmmoLink");
			fAmmoSet = ((type != null) ? AccessTools.Field(type, "ammoSet") : null);
			fAmmoTypes = ((type2 != null) ? AccessTools.Field(type2, "ammoTypes") : null);
			fAmmoLink = ((type3 != null) ? AccessTools.Field(type3, "ammo") : null);
			pContainer = AccessTools.PropertyGetter(tInventory, "container");
			mUpdateInventory = AccessTools.Method(tInventory, "UpdateInventory");
			mCanFit = AccessTools.Method(tInventory, "CanFitInInventory", new Type[4]
			{
				typeof(Thing),
				typeof(int).MakeByRefType(),
				typeof(bool),
				typeof(bool)
			});
			if (_active = pUseAmmo != null && pHasMagazine != null && pCurMagCount != null && pMagSize != null && pCurrentAmmo != null && pProps != null && mResetAmmoCount != null && fAmmoSet != null && fAmmoTypes != null && fAmmoLink != null && pContainer != null && mUpdateInventory != null && mCanFit != null)
			{
				EESLog.Message("CE detected (CombatExtended). CE-aware loadouts + turret ammo + tier MGs enabled.");
			}
			else
			{
				EESLog.Warning("CE detected but some reflection handles missing — CE integration disabled.");
			}
		}
		catch (Exception ex)
		{
			_active = false;
			EESLog.Warning("CECompat init failed (CE integration disabled): " + ex.Message);
		}
	}

	public static void ApplyCeGarrison(Map map, Faction faction, StrongholdData data, Pawn commander)
	{
		if (Active && map != null && faction != null)
		{
			AssignMachineGunners(map, faction, data?.tier ?? 1, commander);
			EnsureGarrisonAmmo(map, faction);
			RearmGarrisonByTier(map, faction, data?.tier ?? 1);
		}
	}

	private static bool IsRifleClass(ThingWithComps weapon)
	{
		List<string> list = weapon?.def?.weaponTags;
		if (list == null)
		{
			return true;
		}
		for (int i = 0; i < list.Count; i++)
		{
			if (NonRifleTags.Contains(list[i]))
			{
				return false;
			}
		}
		return true;
	}

	private static Dictionary<string, float> TierAmmoWeights(int tier)
	{
		if (tier <= 5)
		{
			return new Dictionary<string, float>
			{
				{ "FMJ", 1f },
				{ "HP", 1f }
			};
		}
		if (tier <= 8)
		{
			return new Dictionary<string, float>
			{
				{ "FMJ", 1f },
				{ "HP", 1f },
				{ "AP", 2f }
			};
		}
		if (tier <= 10)
		{
			return new Dictionary<string, float>
			{
				{ "FMJ", 1f },
				{ "AP", 1f },
				{ "Incendiary", 2f }
			};
		}
		return new Dictionary<string, float>
		{
			{ "FMJ", 1f },
			{ "Incendiary", 1f },
			{ "HE", 2f }
		};
	}

	private static string AmmoTypeSuffix(string defName)
	{
		if (string.IsNullOrEmpty(defName))
		{
			return null;
		}
		int num = defName.LastIndexOf('_');
		if (num < 0 || num >= defName.Length - 1)
		{
			return null;
		}
		return defName.Substring(num + 1);
	}

	private static ThingDef PickTierAmmoDef(object ammoUser, int tier)
	{
		try
		{
			object obj = pProps.Invoke(ammoUser, null);
			object obj2 = ((obj != null) ? fAmmoSet.GetValue(obj) : null);
			if (obj2 == null || !(fAmmoTypes.GetValue(obj2) is IEnumerable enumerable))
			{
				return null;
			}
			Dictionary<string, float> dictionary = TierAmmoWeights(tier);
			Dictionary<string, ThingDef> dictionary2 = new Dictionary<string, ThingDef>();
			foreach (object item in enumerable)
			{
				if (fAmmoLink.GetValue(item) is ThingDef thingDef)
				{
					string text = AmmoTypeSuffix(thingDef.defName);
					if (text != null && dictionary.ContainsKey(text) && !dictionary2.ContainsKey(text))
					{
						dictionary2[text] = thingDef;
					}
				}
			}
			if (dictionary2.Count == 0)
			{
				return null;
			}
			float num = 0f;
			foreach (KeyValuePair<string, ThingDef> item2 in dictionary2)
			{
				num += dictionary[item2.Key];
			}
			float num2 = Rand.Value * num;
			foreach (KeyValuePair<string, ThingDef> item3 in dictionary2)
			{
				num2 -= dictionary[item3.Key];
				if (num2 <= 0f)
				{
					return item3.Value;
				}
			}
			using Dictionary<string, ThingDef>.Enumerator enumerator2 = dictionary2.GetEnumerator();
			if (enumerator2.MoveNext())
			{
				return enumerator2.Current.Value;
			}
		}
		catch
		{
		}
		return null;
	}

	public static void RearmGarrisonByTier(Map map, Faction faction, int tier)
	{
		if (!Active || map == null || faction == null || (StrongholdMod.Settings != null && !StrongholdMod.Settings.enableTierAmmo))
		{
			return;
		}
		int num = 0;
		int num2 = 0;
		try
		{
			foreach (Pawn item in (from p in map.mapPawns.SpawnedPawnsInFaction(faction)
				where p?.equipment?.Primary != null
				select p).ToList())
			{
				ThingWithComps primary = item.equipment.Primary;
				if (!IsRifleClass(primary))
				{
					continue;
				}
				object comp = GetComp(primary, tAmmoUser);
				if (comp == null || !(bool)pUseAmmo.Invoke(comp, null))
				{
					continue;
				}
				num++;
				ThingDef thingDef = PickTierAmmoDef(comp, tier);
				if (thingDef == null || (pCurrentAmmo.Invoke(comp, null) is ThingDef thingDef2 && thingDef2 == thingDef))
				{
					continue;
				}
				try
				{
					mResetAmmoCount.Invoke(comp, new object[1] { thingDef });
				}
				catch
				{
				}
				object comp2 = GetComp(item, tInventory);
				if (comp2 != null)
				{
					int num3 = Math.Max(1, (int)pMagSize.Invoke(comp, null));
					AddAmmoToInventory(comp2, thingDef, Math.Min(Math.Max(num3 * 2, 20), 200));
					try
					{
						mUpdateInventory.Invoke(comp2, null);
					}
					catch
					{
					}
				}
				num2++;
			}
		}
		catch (Exception ex)
		{
			EESLog.Warning("CE RearmGarrisonByTier failed: " + ex.Message);
		}
		EESLog.Message($"CE rearm-by-tier [{faction.Name} T{tier}]: rifles={num} rearmed={num2}.");
	}

	public static void EnsureGarrisonAmmo(Map map, Faction faction)
	{
		if (!Active || map == null || faction == null)
		{
			return;
		}
		int num = 0;
		int rangedCe = 0;
		int alreadyOk = 0;
		int num2 = 0;
		int noAmmoWeapon = 0;
		try
		{
			foreach (Pawn item in (from p in map.mapPawns.SpawnedPawnsInFaction(faction)
				where p?.RaceProps != null && p.RaceProps.ToolUser && p.equipment?.Primary != null
				select p).ToList())
			{
				num++;
				int num3 = EnsurePawnAmmo(item, ref rangedCe, ref alreadyOk, ref noAmmoWeapon);
				num2 += num3;
			}
		}
		catch (Exception ex)
		{
			EESLog.Warning("CE EnsureGarrisonAmmo failed: " + ex.Message);
		}
		EESLog.Message($"CE: active=true {faction.Name} | pawns={num} ceAmmoWeapon={rangedCe} | " + $"ammo: alreadySupplied={alreadyOk} fallbackApplied={num2} noAmmoWeapon={noAmmoWeapon}.");
	}

	private static int EnsurePawnAmmo(Pawn p, ref int rangedCe, ref int alreadyOk, ref int noAmmoWeapon)
	{
		object comp = GetComp(p.equipment.Primary, tAmmoUser);
		if (comp == null)
		{
			return 0;
		}
		if (!(bool)pUseAmmo.Invoke(comp, null))
		{
			noAmmoWeapon++;
			return 0;
		}
		rangedCe++;
		object comp2 = GetComp(p, tInventory);
		if (comp2 == null)
		{
			return 0;
		}
		bool flag = (bool)pHasMagazine.Invoke(comp, null);
		bool num = flag && (int)pCurMagCount.Invoke(comp, null) > 0;
		HashSet<ThingDef> defs = AmmoSetDefs(comp);
		bool flag2 = InventoryHasAny(comp2, defs);
		if (num && flag2)
		{
			alreadyOk++;
			return 0;
		}
		object obj = pCurrentAmmo.Invoke(comp, null) ?? FirstValidAmmoObj(comp);
		if (!(obj is ThingDef ammoDef))
		{
			return 0;
		}
		if (flag && (int)pCurMagCount.Invoke(comp, null) <= 0)
		{
			try
			{
				mResetAmmoCount.Invoke(comp, new object[1] { obj });
			}
			catch
			{
			}
		}
		if (!flag2)
		{
			int desired = Math.Min(Math.Max(Math.Max(1, (int)pMagSize.Invoke(comp, null)) * 2, 20), 200);
			AddAmmoToInventory(comp2, ammoDef, desired);
		}
		try
		{
			mUpdateInventory.Invoke(comp2, null);
		}
		catch
		{
		}
		return 1;
	}

	public static List<ThingDef> PickLootAmmo(Faction faction, int maxKinds)
	{
		List<ThingDef> list = new List<ThingDef>();
		if (!Active)
		{
			return list;
		}
		try
		{
			TechLevel tech = faction?.def?.techLevel ?? TechLevel.Industrial;
			HashSet<string> hashSet = new HashSet<string>();
			List<ThingDef> list2 = (from d in DefDatabase<ThingDef>.AllDefs
				where tAmmoDef.IsInstanceOfType(d) && d.generateAllowChance > 0f && d.alwaysHaulable && StrongholdLootFilter.IsDroppableLoot(d) && (int)d.techLevel <= (int)(tech + 1)
				orderby d.BaseMarketValue
				select d).ToList();
			foreach (ThingDef item in list2)
			{
				string text = AmmoClassName(item);
				if (text == null || hashSet.Add(text))
				{
					list.Add(item);
					if (list.Count >= maxKinds)
					{
						break;
					}
				}
			}
			EESLog.Message(string.Format("CE loot ammo [{0}] tech={1} 요청 {2}종: ", faction?.Name ?? "null", tech, maxKinds) + $"사용가능 후보 {list2.Count}종 → 채택 {list.Count}종 " + "[" + string.Join(", ", list.Select((ThingDef d) => d.defName)) + "].");
		}
		catch (Exception ex)
		{
			EESLog.Warning("CE PickLootAmmo failed: " + ex.Message);
		}
		return list;
	}

	public static bool IsCeAmmoTurretDef(ThingDef turretDef)
	{
		if (!Active || turretDef?.building == null)
		{
			return false;
		}
		try
		{
			ThingDef turretGunDef = turretDef.building.turretGunDef;
			if (turretGunDef?.comps == null)
			{
				return false;
			}
			if (_tPropsAmmoUser == null)
			{
				_tPropsAmmoUser = AccessTools.TypeByName("CombatExtended.CompProperties_AmmoUser");
			}
			if (_tPropsAmmoUser == null)
			{
				return false;
			}
			for (int i = 0; i < turretGunDef.comps.Count; i++)
			{
				if (_tPropsAmmoUser.IsInstanceOfType(turretGunDef.comps[i]))
				{
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	public static bool LoadTurretAmmo(Thing turret, IntVec3 baseCenter, int tier = 1)
	{
		if (!Active || turret == null)
		{
			return false;
		}
		try
		{
			ThingWithComps turretGun = GetTurretGun(turret);
			if (turretGun == null)
			{
				return false;
			}
			object comp = GetComp(turretGun, tAmmoUser);
			if (comp == null)
			{
				return false;
			}
			if (!(bool)pUseAmmo.Invoke(comp, null))
			{
				return false;
			}
			object obj = ((StrongholdMod.Settings == null || StrongholdMod.Settings.enableTierAmmo) ? PickTierAmmoDef(comp, tier) : null) ?? pCurrentAmmo.Invoke(comp, null) ?? FirstValidAmmoObj(comp);
			if (!(obj is ThingDef thingDef))
			{
				return false;
			}
			try
			{
				mResetAmmoCount.Invoke(comp, new object[1] { obj });
			}
			catch
			{
			}
			bool flag = turret.TryGetComp<CompMannable>() != null;
			int num = Math.Max(1, (int)pMagSize.Invoke(comp, null));
			if (flag)
			{
				SpawnAmmoNear(turret, thingDef, Math.Min(num * 4, 400), baseCenter);
			}
			EESLog.Message($"CE turret ammo [{turret.def.defName}]: loaded {thingDef.defName} mag={num}" + (flag ? " + reserve(사수형)" : " (자동=예비탄 없음)") + ".");
			return true;
		}
		catch (Exception ex)
		{
			EESLog.Warning("CE LoadTurretAmmo failed: " + ex.Message);
			return false;
		}
	}

	private static ThingWithComps GetTurretGun(Thing turret)
	{
		if (turret is Building_TurretGun building_TurretGun)
		{
			return building_TurretGun.gun as ThingWithComps;
		}
		Type type = turret.GetType();
		if (!_gunGetterCache.TryGetValue(type, out var value))
		{
			value = AccessTools.PropertyGetter(type, "Gun");
			_gunGetterCache[type] = value;
		}
		if (!(value != null))
		{
			return null;
		}
		return value.Invoke(turret, null) as ThingWithComps;
	}

	public static void LogTurretRuntimeDiag(Thing turret, int tick)
	{
		if (Active && turret != null && (StrongholdMod.Settings == null || StrongholdMod.Settings.debugLogging) && (!_turretNextDiag.TryGetValue(turret.thingIDNumber, out var value) || tick >= value))
		{
			string text = BuildTurretDiagLine(turret, updatePrev: true);
			if (text != null)
			{
				_turretNextDiag[turret.thingIDNumber] = tick + 180;
				EESLog.Message(text);
			}
		}
	}

	public static int DumpAllTurretDiag(Map map)
	{
		if (!Active || map?.listerBuildings == null)
		{
			return 0;
		}
		int num = 0;
		List<Building> allBuildingsNonColonist = map.listerBuildings.allBuildingsNonColonist;
		for (int i = 0; i < allBuildingsNonColonist.Count; i++)
		{
			Building building = allBuildingsNonColonist[i];
			if (building != null && !building.Destroyed && building is Building_Turret && building.Faction != null && building.Faction != Faction.OfPlayer)
			{
				string text = BuildTurretDiagLine(building, updatePrev: false);
				if (text != null)
				{
					EESLog.Message(text);
					num++;
				}
			}
		}
		return num;
	}

	private static string BuildTurretDiagLine(Thing turret, bool updatePrev)
	{
		try
		{
			ThingWithComps turretGun = GetTurretGun(turret);
			if (turretGun == null)
			{
				return null;
			}
			object comp = GetComp(turretGun, tAmmoUser);
			if (comp == null)
			{
				return null;
			}
			bool flag = (bool)pUseAmmo.Invoke(comp, null);
			int num = (((bool)pHasMagazine.Invoke(comp, null)) ? ((int)pCurMagCount.Invoke(comp, null)) : (-1));
			int num2 = (int)pMagSize.Invoke(comp, null);
			ThingDef thingDef = pCurrentAmmo.Invoke(comp, null) as ThingDef;
			CompMannable compMannable = turret.TryGetComp<CompMannable>();
			bool flag2 = compMannable != null;
			bool flag3 = compMannable?.MannedNow ?? false;
			Pawn pawn = compMannable?.ManningPawn;
			int num3 = CountNearbyAmmo(turret, AmmoSetDefs(comp), 5f);
			string arg = "idle";
			if (_turretPrevMag.TryGetValue(turret.thingIDNumber, out var value) && num >= 0)
			{
				if (num < value)
				{
					arg = $"FIRED({value - num})";
				}
				else if (num > value)
				{
					arg = ((value <= 0) ? $"RELOADED(0->{num})" : $"RELOADED(+{num - value})");
				}
			}
			if (updatePrev && num >= 0)
			{
				_turretPrevMag[turret.thingIDNumber] = num;
			}
			return $"CE turret diag [{turret.def.defName} @{turret.Position} {turret.Faction?.Name}]: " + string.Format("mannable={0} manned={1} gunner={2} | ", flag2, flag3, (pawn != null) ? pawn.LabelShort : "none") + string.Format("useAmmo={0} mag={1}/{2} ammo={3} | ", flag, num, num2, (thingDef != null) ? thingDef.defName : "none") + $"reserveNearby={num3} | {arg}";
		}
		catch (Exception ex)
		{
			return "CE turret diag failed: " + ex.Message;
		}
	}

	private static int CountNearbyAmmo(Thing turret, HashSet<ThingDef> setDefs, float radius)
	{
		if (setDefs.Count == 0)
		{
			return 0;
		}
		Map map = turret.Map;
		if (map == null)
		{
			return 0;
		}
		int num = 0;
		foreach (IntVec3 item in GenRadial.RadialCellsAround(turret.Position, radius, useCenter: true))
		{
			if (!item.InBounds(map))
			{
				continue;
			}
			List<Thing> thingList = item.GetThingList(map);
			for (int i = 0; i < thingList.Count; i++)
			{
				Thing thing = thingList[i];
				if (thing != null && setDefs.Contains(thing.def))
				{
					num += thing.stackCount;
				}
			}
		}
		return num;
	}

	private static void SpawnAmmoNear(Thing turret, ThingDef ammoDef, int amount, IntVec3 baseCenter)
	{
		Map map = turret.Map;
		if (map == null)
		{
			return;
		}
		IntVec3 center = IntVec3.Invalid;
		float num = float.MaxValue;
		foreach (IntVec3 item in GenAdj.CellsAdjacent8Way(turret))
		{
			if (item.InBounds(map) && item.Standable(map) && item.GetFirstItem(map) == null)
			{
				float num2 = (item - baseCenter).LengthHorizontalSquared;
				if (num2 < num)
				{
					num = num2;
					center = item;
				}
			}
		}
		if (!center.IsValid)
		{
			Region region = turret.Position.GetRegion(map);
			foreach (IntVec3 item2 in GenRadial.RadialCellsAround(turret.Position, 3.5f, useCenter: false))
			{
				if (item2.InBounds(map) && item2.Standable(map) && item2.GetFirstItem(map) == null && (region == null || item2.GetRegion(map) == region))
				{
					float num3 = (item2 - turret.Position).LengthHorizontalSquared;
					if (num3 < num)
					{
						num = num3;
						center = item2;
					}
				}
			}
		}
		if (!center.IsValid)
		{
			return;
		}
		try
		{
			Thing thing = ThingMaker.MakeThing(ammoDef);
			int val = ((ammoDef.stackLimit > 0) ? ammoDef.stackLimit : amount);
			thing.stackCount = Math.Max(1, Math.Min(amount, val));
			thing.SetForbidden(value: false, warnOnFail: false);
			GenPlace.TryPlaceThing(thing, center, map, ThingPlaceMode.Near);
		}
		catch
		{
		}
	}

	private static void AssignMachineGunners(Map map, Faction faction, int tier, Pawn commander)
	{
		float num = ((tier < 3) ? 0f : Math.Min(0.12f + (float)(tier - 3) * 0.07f, 0.45f));
		if (num <= 0f)
		{
			return;
		}
		try
		{
			List<Pawn> list = (from p in map.mapPawns.SpawnedPawnsInFaction(faction)
				where p != commander && p?.RaceProps != null && p.RaceProps.Humanlike && p.equipment != null && p.equipment.Primary != null && p.equipment.Primary.def.IsRangedWeapon && !IsMachineGun(p.equipment.Primary.def)
				select p).ToList();
			if (list.Count == 0)
			{
				return;
			}
			List<ThingDef> list2 = MachineGunPool(faction);
			if (list2.Count == 0)
			{
				EESLog.Message($"CE MG: {faction.Name} T{tier} — no MG candidates (tech/mod filter). skip.");
				return;
			}
			int num2 = Math.Max(1, Mathf.RoundToInt((float)list.Count * num));
			list = list.OrderBy((Pawn p) => p.equipment.Primary.MarketValue).ToList();
			int num3 = Mathf.RoundToInt(Mathf.Clamp01((float)(tier - 3) / 4f) * (float)(list2.Count - 1));
			int num4 = 0;
			for (int num5 = 0; num5 < list.Count; num5++)
			{
				if (num4 >= num2)
				{
					break;
				}
				Pawn pawn = list[num5];
				int index = Math.Max(0, Math.Min(val2: num3 + (pawn.thingIDNumber % 3 - 1), val1: list2.Count - 1));
				if (TryEquipWeapon(pawn, list2[index]))
				{
					num4++;
				}
			}
			EESLog.Message($"CE MG: {faction.Name} T{tier} | candidates={list.Count} " + $"frac={num:0.00} assigned={num4} (poolKinds={list2.Count}, tierIdx={num3}).");
		}
		catch (Exception ex)
		{
			EESLog.Warning("CE AssignMachineGunners failed: " + ex.Message);
		}
	}

	private static bool IsMachineGun(ThingDef d)
	{
		if (d == null || !d.IsRangedWeapon || d.weaponTags == null)
		{
			return false;
		}
		for (int i = 0; i < d.weaponTags.Count; i++)
		{
			string text = d.weaponTags[i]?.ToLowerInvariant();
			switch (text)
			{
			case "ce_machinegun":
			case "ce_ai_lmg":
				return true;
			case null:
				continue;
			}
			string[] mgContains = MgContains;
			foreach (string value in mgContains)
			{
				if (text.Contains(value))
				{
					return true;
				}
			}
			if (text.EndsWith("mg") && !text.EndsWith("smg"))
			{
				return true;
			}
		}
		return false;
	}

	private static List<ThingDef> MachineGunPool(Faction faction)
	{
		List<ThingDef> list = new List<ThingDef>();
		try
		{
			TechLevel techLevel = faction?.def?.techLevel ?? TechLevel.Industrial;
			HashSet<ModContentPack> hashSet = FactionModSetSafe(faction);
			foreach (ThingDef item in DefDatabase<ThingDef>.AllDefsListForReading)
			{
				if (IsMachineGun(item) && (int)item.techLevel <= (int)techLevel)
				{
					ModContentPack modContentPack = item.modContentPack;
					if (modContentPack == null || modContentPack.IsOfficialMod || (hashSet != null && hashSet.Contains(modContentPack)))
					{
						list.Add(item);
					}
				}
			}
			list.Sort((ThingDef a, ThingDef b) => a.BaseMarketValue.CompareTo(b.BaseMarketValue));
		}
		catch
		{
		}
		return list;
	}

	private static bool TryEquipWeapon(Pawn p, ThingDef weaponDef)
	{
		try
		{
			if (!(ThingMaker.MakeThing(weaponDef) is ThingWithComps newEq))
			{
				return false;
			}
			p.equipment.DestroyAllEquipment();
			p.equipment.AddEquipment(newEq);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static HashSet<ModContentPack> FactionModSetSafe(Faction faction)
	{
		HashSet<ModContentPack> hashSet = new HashSet<ModContentPack>();
		try
		{
			if (faction?.def == null)
			{
				return hashSet;
			}
			if (faction.def.modContentPack != null)
			{
				hashSet.Add(faction.def.modContentPack);
			}
			ThingDef thingDef = faction.def.basicMemberKind?.race;
			if (thingDef?.modContentPack != null)
			{
				hashSet.Add(thingDef.modContentPack);
			}
		}
		catch
		{
		}
		return hashSet;
	}

	private static object GetComp(ThingWithComps thing, Type compType)
	{
		if (thing?.AllComps == null)
		{
			return null;
		}
		List<ThingComp> allComps = thing.AllComps;
		for (int i = 0; i < allComps.Count; i++)
		{
			if (compType.IsInstanceOfType(allComps[i]))
			{
				return allComps[i];
			}
		}
		return null;
	}

	private static HashSet<ThingDef> AmmoSetDefs(object ammoUser)
	{
		HashSet<ThingDef> hashSet = new HashSet<ThingDef>();
		try
		{
			object obj = pProps.Invoke(ammoUser, null);
			object obj2 = ((obj != null) ? fAmmoSet.GetValue(obj) : null);
			if (obj2 == null)
			{
				return hashSet;
			}
			if (fAmmoTypes.GetValue(obj2) is IEnumerable enumerable)
			{
				foreach (object item2 in enumerable)
				{
					if (fAmmoLink.GetValue(item2) is ThingDef item)
					{
						hashSet.Add(item);
					}
				}
			}
		}
		catch
		{
		}
		return hashSet;
	}

	private static object FirstValidAmmoObj(object ammoUser)
	{
		try
		{
			object obj = pProps.Invoke(ammoUser, null);
			object obj2 = ((obj != null) ? fAmmoSet.GetValue(obj) : null);
			if (obj2 == null)
			{
				return null;
			}
			if (fAmmoTypes.GetValue(obj2) is IEnumerable enumerable)
			{
				foreach (object item in enumerable)
				{
					object value = fAmmoLink.GetValue(item);
					if (value is ThingDef { generateAllowChance: >0f, alwaysHaulable: not false })
					{
						return value;
					}
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private static bool InventoryHasAny(object inv, HashSet<ThingDef> defs)
	{
		if (defs.Count == 0)
		{
			return false;
		}
		try
		{
			if (pContainer.Invoke(inv, null) is ThingOwner thingOwner)
			{
				foreach (Thing item in (IEnumerable<Thing>)thingOwner)
				{
					if (defs.Contains(item.def))
					{
						return true;
					}
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private static void AddAmmoToInventory(object inv, ThingDef ammoDef, int desired)
	{
		try
		{
			Thing thing = ThingMaker.MakeThing(ammoDef);
			thing.stackCount = Math.Max(1, desired);
			object[] array = new object[4] { thing, 0, false, false };
			bool num = (bool)mCanFit.Invoke(inv, array);
			int num2 = (int)array[1];
			if (!num && num2 <= 0)
			{
				thing.Destroy();
				return;
			}
			if (num2 < thing.stackCount)
			{
				thing.stackCount = num2;
			}
			if (thing.stackCount <= 0)
			{
				thing.Destroy();
			}
			else if (pContainer.Invoke(inv, null) is ThingOwner thingOwner)
			{
				thingOwner.TryAdd(thing);
			}
			else
			{
				thing.Destroy();
			}
		}
		catch
		{
		}
	}

	private static string AmmoClassName(ThingDef ammoDef)
	{
		try
		{
			if (_fAmmoClass == null)
			{
				_fAmmoClass = AccessTools.Field(tAmmoDef, "ammoClass");
			}
			return (_fAmmoClass?.GetValue(ammoDef) as Def)?.defName;
		}
		catch
		{
			return null;
		}
	}
}
