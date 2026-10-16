using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class EESCustomBaseDetector
{
	private static HashSet<string> _dedicatedFactionDefNames;

	private static readonly HashSet<string> _loggedForeignDefs = new HashSet<string>();

	public static bool IsDedicatedOutpostFaction(Faction f)
	{
		if (f != null)
		{
			return IsDedicatedOutpostFaction(f.def);
		}
		return false;
	}

	public static bool IsDedicatedOutpostFaction(FactionDef def)
	{
		if (def == null)
		{
			return false;
		}
		EnsureScanned();
		return _dedicatedFactionDefNames.Contains(def.defName);
	}

	public static bool IsCustomGeneratorMap(Map map)
	{
		if (map != null && map.generatorDef != null)
		{
			return map.generatorDef != MapGeneratorDefOf.Base_Faction;
		}
		return false;
	}

	public static bool IsForeignSettlement(WorldObject wo)
	{
		if (wo?.def == null)
		{
			return false;
		}
		Type worldObjectClass = wo.def.worldObjectClass;
		if (worldObjectClass == null || worldObjectClass == typeof(Settlement))
		{
			return false;
		}
		if (_loggedForeignDefs.Add(wo.def.defName))
		{
			EESLog.Message("ForeignSettlement: " + wo.def.defName + " (worldObjectClass=" + worldObjectClass.FullName + ") → EES 비활성(RimCities류 커스텀 정착지 객체).");
		}
		return true;
	}

	public static string DetectReason(Faction f)
	{
		if (f?.def == null)
		{
			return "none";
		}
		if (!IsDedicatedOutpostFaction(f.def))
		{
			return "none";
		}
		return "custom outpost generator (framework hook)";
	}

	private static bool HasMapGeneratorHook(FactionDef def)
	{
		List<DefModExtension> modExtensions = def.modExtensions;
		if (modExtensions == null)
		{
			return false;
		}
		for (int i = 0; i < modExtensions.Count; i++)
		{
			DefModExtension defModExtension = modExtensions[i];
			if (defModExtension == null)
			{
				continue;
			}
			Type type = defModExtension.GetType();
			object obj = null;
			FieldInfo field = type.GetField("mapGeneratorDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null)
			{
				obj = field.GetValue(defModExtension);
			}
			if (obj == null)
			{
				PropertyInfo property = type.GetProperty("mapGeneratorDef", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (property != null)
				{
					obj = property.GetValue(defModExtension);
				}
			}
			if (obj is MapGeneratorDef mapGeneratorDef && mapGeneratorDef != null && mapGeneratorDef != MapGeneratorDefOf.Base_Faction)
			{
				return true;
			}
			if (type.Name.IndexOf("SettlementHook", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		return false;
	}

	private static void EnsureScanned()
	{
		if (_dedicatedFactionDefNames != null)
		{
			return;
		}
		_dedicatedFactionDefNames = new HashSet<string>();
		try
		{
			Type typeInAnyAssembly = GenTypes.GetTypeInAnyAssembly("EncounterFramework.LocationDef");
			if (typeInAnyAssembly != null)
			{
				Type type = typeof(DefDatabase<>).MakeGenericType(typeInAnyAssembly);
				PropertyInfo propertyInfo = type.GetProperty("AllDefsListForReading", BindingFlags.Static | BindingFlags.Public) ?? type.GetProperty("AllDefs", BindingFlags.Static | BindingFlags.Public);
				FieldInfo field = typeInAnyAssembly.GetField("factionBase", BindingFlags.Instance | BindingFlags.Public);
				if (propertyInfo != null && field != null && propertyInfo.GetValue(null) is IEnumerable enumerable)
				{
					foreach (object item in enumerable)
					{
						if (field.GetValue(item) is FactionDef factionDef && factionDef != null)
						{
							_dedicatedFactionDefNames.Add(factionDef.defName);
						}
					}
				}
			}
			foreach (FactionDef allDef in DefDatabase<FactionDef>.AllDefs)
			{
				if (allDef != null && !allDef.isPlayer && HasMapGeneratorHook(allDef))
				{
					_dedicatedFactionDefNames.Add(allDef.defName);
				}
			}
		}
		catch (Exception ex)
		{
			EESLog.Warning("CustomBaseDetector scan failed: " + ex.Message);
		}
		EESLog.Message(string.Format("CustomBaseDetector: dedicated-outpost factions = {0} [{1}]", _dedicatedFactionDefNames.Count, string.Join(", ", _dedicatedFactionDefNames)));
	}
}
