using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[StaticConstructorOnStartup]
public static class EES_ScoutTabInjector
{
	static EES_ScoutTabInjector()
	{
		int num = 0;
		int num2 = 0;
		foreach (WorldObjectDef item in DefDatabase<WorldObjectDef>.AllDefsListForReading)
		{
			if (!(item.worldObjectClass == null) && typeof(Settlement).IsAssignableFrom(item.worldObjectClass))
			{
				if (item.inspectorTabsResolved == null)
				{
					num2++;
				}
				else if (!item.inspectorTabsResolved.Contains(WITab_Scouting.Instance))
				{
					item.inspectorTabsResolved.Add(WITab_Scouting.Instance);
					num++;
				}
			}
		}
		EESLog.Message($"Scout WITab injected into {num} settlement defs " + $"(null-list defs skipped={num2}, covered by GetInspectTabs postfix)");
	}
}
