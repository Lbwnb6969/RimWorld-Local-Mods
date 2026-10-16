using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public class CompProperties_EESCommandAura : CompProperties
{
	public string hediffDefName;

	public float range = 8f;

	public int checkInterval = 10;

	public Color ringColor = new Color(0.85f, 0.75f, 0.2f);

	public CompProperties_EESCommandAura()
	{
		compClass = typeof(CompEESCommandAura);
	}
}
