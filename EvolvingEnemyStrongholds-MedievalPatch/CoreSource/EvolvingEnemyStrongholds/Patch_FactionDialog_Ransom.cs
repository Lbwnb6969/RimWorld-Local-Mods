using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(FactionDialogMaker), "FactionDialogFor")]
public static class Patch_FactionDialog_Ransom
{
	private class RansomEntry
	{
		public Pawn pawn;

		public StrongholdData data;

		public Faction faction;

		public string settlementLabel;

		public int fee;
	}

	private const float FeeFactor = 1.7f;

	private const float FeeTierStep = 0.05f;

	public static void Postfix(DiaNode __result, Pawn negotiator, Faction faction)
	{
		try
		{
			Map map = negotiator?.Map;
			if (__result == null || map == null || !map.IsPlayerHome || faction == null || faction.IsPlayer)
			{
				return;
			}
			List<RansomEntry> list = CollectRansomable(faction);
			if (list.Count != 0)
			{
				DiaOption diaOption = new DiaOption(EESText.T("EES.Ransom.Option", list.Count));
				diaOption.link = BuildRansomNode(__result, map, list);
				if (negotiator.skills != null && negotiator.skills.GetSkill(SkillDefOf.Social).TotallyDisabled)
				{
					diaOption.Disable("WorkTypeDisablesOption".Translate(SkillDefOf.Social.label));
				}
				int index = ((__result.options.Count > 0) ? (__result.options.Count - 1) : 0);
				__result.options.Insert(index, diaOption);
			}
		}
		catch (Exception ex)
		{
			EESLog.Error("Ransom dialog inject: " + ex);
		}
	}

	private static List<RansomEntry> CollectRansomable(Faction faction)
	{
		List<RansomEntry> list = new List<RansomEntry>();
		WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
		if (instance == null)
		{
			return list;
		}
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (settlement.Faction != faction)
			{
				continue;
			}
			StrongholdData existing = instance.GetExisting(settlement.ID);
			if (existing?.capturedPrisoners == null)
			{
				continue;
			}
			for (int j = 0; j < existing.capturedPrisoners.Count; j++)
			{
				Pawn pawn = existing.capturedPrisoners[j];
				if (pawn != null && !pawn.Dead && !pawn.Destroyed && !pawn.Spawned && pawn.Faction != Faction.OfPlayer)
				{
					float num = pawn.MarketValue * 1.7f * (1f + 0.05f * (float)existing.tier);
					int fee = Mathf.Max(50, GenMath.RoundTo((int)num, 10));
					list.Add(new RansomEntry
					{
						pawn = pawn,
						data = existing,
						faction = faction,
						settlementLabel = settlement.LabelCap,
						fee = fee
					});
				}
			}
		}
		return list;
	}

	private static DiaNode BuildRansomNode(DiaNode root, Map map, List<RansomEntry> entries)
	{
		DiaNode diaNode = new DiaNode(EESText.T("EES.Ransom.Intro"));
		for (int i = 0; i < entries.Count; i++)
		{
			RansomEntry e = entries[i];
			DiaOption diaOption = new DiaOption(EESText.T("EES.Ransom.Entry", e.pawn.LabelShortCap, e.settlementLabel, e.fee));
			diaOption.action = delegate
			{
				PayAndReturn(map, e);
			};
			diaOption.resolveTree = true;
			if (!TradeUtility.ColonyHasEnoughSilver(map, e.fee))
			{
				diaOption.Disable("NeedSilverLaunchable".Translate(e.fee.ToString()));
			}
			diaNode.options.Add(diaOption);
		}
		DiaOption diaOption2 = new DiaOption("GoBack".Translate());
		diaOption2.link = root;
		diaNode.options.Add(diaOption2);
		return diaNode;
	}

	private static void PayAndReturn(Map map, RansomEntry e)
	{
		try
		{
			Pawn pawn = e.pawn;
			if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.Spawned || map == null)
			{
				return;
			}
			TradeUtility.LaunchSilver(map, e.fee);
			e.data.capturedPrisoners?.Remove(pawn);
			if (pawn.IsWorldPawn())
			{
				Find.WorldPawns.RemovePawn(pawn);
			}
			if (pawn.Faction != Faction.OfPlayer)
			{
				pawn.SetFaction(Faction.OfPlayer);
			}
			IntVec3 result;
			if ((int)e.faction.def.techLevel < 4)
			{
				if (!CellFinder.TryFindRandomEdgeCellWith((IntVec3 c) => c.Standable(map) && map.reachability.CanReachColony(c), map, CellFinder.EdgeRoadChance_Friendly, out result) && !CellFinder.TryFindRandomEdgeCellWith((IntVec3 c) => c.Standable(map), map, CellFinder.EdgeRoadChance_Friendly, out result))
				{
					result = DropCellFinder.TradeDropSpot(map);
				}
				GenSpawn.Spawn(pawn, result, map);
			}
			else
			{
				result = DropCellFinder.TradeDropSpot(map);
				TradeUtility.SpawnDropPod(result, map, pawn);
			}
			CameraJumper.TryJump(result, map);
			Messages.Message(EESText.T("EES.Ransom.Paid", pawn.LabelShortCap, e.fee), new GlobalTargetInfo(result, map), MessageTypeDefOf.PositiveEvent);
			EESLog.Message($"Ransom: {pawn.LabelShortCap} returned for {e.fee} silver (settlement id={e.data.settlementId}).");
		}
		catch (Exception ex)
		{
			EESLog.Error("Ransom pay: " + ex);
		}
	}
}
