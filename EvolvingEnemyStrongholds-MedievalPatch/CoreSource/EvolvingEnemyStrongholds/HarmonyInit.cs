using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

[StaticConstructorOnStartup]
public static class HarmonyInit
{
	public const string HarmonyId = "wowgag.EvolvingEnemyStrongholds";

	static HarmonyInit()
	{
		Harmony harmony = new Harmony("wowgag.EvolvingEnemyStrongholds");
		harmony.PatchAll(Assembly.GetExecutingAssembly());
		EESLog.Message("Harmony patches applied (wowgag.EvolvingEnemyStrongholds).");
		SafeApply(delegate
		{
			PatchFactionTerritoriesExpansionComplete(harmony);
		}, "Faction Territories 扩张完成钩子");
		LongEventHandler.ExecuteWhenFinished(delegate
		{
			SafeApply(delegate
			{
				PatchFactionTerritoriesExpansionComplete(harmony);
			}, "Faction Territories 延迟扩张完成钩子");
		});
		SafeApply(delegate
		{
			Patch_KCSG_CleanRect.TryApply(harmony);
		}, "KCSG CleanRect 가드");
		SafeApply(delegate
		{
			Patch_VEF_SpawnGuard.TryApply(harmony);
		}, "VEF 스폰 NRE 가드");
		SafeApply(delegate
		{
			Patch_IRBM_CounterScoutVision.TryApply(harmony);
		}, "IRBM 역정찰 시야 제외");
		SafeApply(ApplyCEShieldScaling, "CE 보호막 스케일링");
	}

	private static void SafeApply(Action apply, string label)
	{
		try
		{
			apply();
		}
		catch (Exception ex)
		{
			EESLog.Error(label + " 적용 중 예외(스킵, EES 본 기능에는 영향 없음): " + ex);
		}
	}

	private static void ApplyCEShieldScaling()
	{
		if (CECompat.Active)
		{
			CompProperties_ProjectileInterceptor compProperties_ProjectileInterceptor = DefDatabase<ThingDef>.GetNamedSilentFail("EES_CommanderShield")?.GetCompProperties<CompProperties_ProjectileInterceptor>();
			if (compProperties_ProjectileInterceptor != null)
			{
				compProperties_ProjectileInterceptor.hitPoints = 3500;
				EESLog.Message("CE active: commander shield dome hitPoints 2000 -> 3500.");
			}
		}
	}

	private static void PatchFactionTerritoriesExpansionComplete(Harmony harmony)
	{
		Patch_FactionTerritories_FinalStand.TryApply(harmony);
		Type utility = AccessTools.TypeByName("FactionTerritories.Expansion.Utility");
		MethodInfo target = utility == null ? null : AccessTools.Method(utility, "TryComplete");
		if (target == null)
		{
			return;
		}
		HarmonyLib.Patches patches = Harmony.GetPatchInfo(target);
		if (patches != null)
		{
			foreach (HarmonyLib.Patch patch in patches.Postfixes)
			{
				if (patch.owner == HarmonyId && patch.PatchMethod?.DeclaringType == typeof(Patch_FactionTerritories_ExpansionComplete))
				{
					return;
				}
			}
		}
		harmony.Patch(target, postfix: new HarmonyMethod(typeof(Patch_FactionTerritories_ExpansionComplete), nameof(Patch_FactionTerritories_ExpansionComplete.Postfix)));
		EESLog.Message("[FT兼容] 扩张完成 EES Postfix 已安装: " + target.DeclaringType.FullName + "." + target.Name + ".");
	}
}
