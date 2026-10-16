using System;
using HarmonyLib;
using RimWorld;
using RimWorld.BaseGen;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(SymbolResolver_SingleThing), "Resolve")]
internal static class Patch_BaseGenPostSpawnGuard
{
	[ThreadStatic]
	private static int depth;

	private static int absorbed;

	private static int refueled;

	public static bool Active => depth > 0;

	public static void Begin()
	{
		depth++;
	}

	public static void End()
	{
		if (depth > 0)
		{
			depth--;
		}
	}

	private static void Prefix(ref ResolveParams rp)
	{
		if (!Active)
		{
			return;
		}
		Action<Thing> original = rp.postThingSpawn;
		if (original == null)
		{
			return;
		}
		rp.postThingSpawn = delegate(Thing t)
		{
			try
			{
				original(t);
			}
			catch (NullReferenceException ex) when (t?.def == ThingDefOf.Brazier && t.TryGetComp<CompRefuelable>() == null)
			{
				absorbed++;
				bool flag = RefuelBestEffort(t);
				if (flag)
				{
					refueled++;
				}
				if (absorbed <= 3)
				{
					EESLog.Message($"BaseGen postThingSpawn 예외 흡수({absorbed}건째, 누적 연료보정 {refueled}건): " + string.Format("thing={0} ({1} comps) ", t?.def?.defName ?? "null", t?.def?.comps?.Count ?? (-1)) + "연료보정=" + (flag ? "함" : "불요/불가") + " — 바닐라 무가드 콜백(타 모드가 comp 를 제거하면 터짐. 실측 사례=Tweaks Galore 가 Brazier 의 CompRefuelable 제거). 생성은 계속됨. 상세: " + ex.Message);
				}
			}
		};
	}

	private static bool RefuelBestEffort(Thing t)
	{
		try
		{
			CompRefuelable compRefuelable = t?.TryGetComp<CompRefuelable>();
			if (compRefuelable == null || compRefuelable.Props == null)
			{
				return false;
			}
			if (compRefuelable.Fuel >= compRefuelable.Props.fuelCapacity)
			{
				return false;
			}
			compRefuelable.Refuel(compRefuelable.Props.fuelCapacity);
			return true;
		}
		catch
		{
			return false;
		}
	}
}
