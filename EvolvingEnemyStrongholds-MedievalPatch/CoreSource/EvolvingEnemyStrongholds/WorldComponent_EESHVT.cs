using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public class WorldComponent_EESHVT : WorldComponent
{
	private const int TickGate = 2500;

	private int nextOfferTick = -1;

	private int lastGateTick = -1;

	private Dictionary<int, int> truceUntil = new Dictionary<int, int>();

	private List<int> tmpTruceKeys;

	private List<int> tmpTruceVals;

	public static WorldComponent_EESHVT Instance => Find.World?.GetComponent<WorldComponent_EESHVT>();

	public WorldComponent_EESHVT(World world)
		: base(world)
	{
	}

	public void GrantTruce(Faction fac)
	{
		if (fac != null)
		{
			truceUntil[fac.loadID] = Find.TickManager.TicksGame + 180000;
			Messages.Message(EESText.T("EES.HVT.TruceStart", fac.Name, 3.ToString()), MessageTypeDefOf.PositiveEvent, historical: false);
			EESLog.Message($"HVT: 습격 중단 발효 fac={fac.Name} {3}일(갱신형).");
		}
	}

	public bool TruceActive(Faction fac)
	{
		if (fac == null || truceUntil == null || truceUntil.Count == 0)
		{
			return false;
		}
		if (truceUntil.TryGetValue(fac.loadID, out var value))
		{
			return Find.TickManager.TicksGame < value;
		}
		return false;
	}

	public override void WorldComponentTick()
	{
		base.WorldComponentTick();
		int ticksGame = Find.TickManager.TicksGame;
		if (lastGateTick >= 0 && ticksGame - lastGateTick < 2500)
		{
			return;
		}
		lastGateTick = ticksGame;
		HVTQuestUtility.ValidateLiveQuests();
		if (HVTQuestUtility.Enabled)
		{
			float num = StrongholdMod.Settings?.hvtOfferDays ?? 15f;
			if (nextOfferTick < 0)
			{
				nextOfferTick = ticksGame + Mathf.RoundToInt(num * 60000f * Rand.Range(0.5f, 1.15f));
			}
			if (ticksGame >= nextOfferTick)
			{
				nextOfferTick = ticksGame + Mathf.RoundToInt(num * 60000f * Rand.Range(0.75f, 1.25f));
				HVTQuestUtility.TryOffer();
			}
		}
	}

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look(ref nextOfferTick, "nextOfferTick", -1);
		Scribe_Collections.Look(ref truceUntil, "truceUntil", LookMode.Value, LookMode.Value, ref tmpTruceKeys, ref tmpTruceVals);
		if (Scribe.mode == LoadSaveMode.PostLoadInit && truceUntil == null)
		{
			truceUntil = new Dictionary<int, int>();
		}
	}
}
