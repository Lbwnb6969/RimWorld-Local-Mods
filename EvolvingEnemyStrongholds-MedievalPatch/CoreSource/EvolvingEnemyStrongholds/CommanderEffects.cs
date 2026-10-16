using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public static class CommanderEffects
{
	private const float AuraRadius = 8f;

	private static HediffDef _marksman;

	private static HediffDef _assault;

	private static HediffDef _warProt;

	private static HediffDef _warExp;

	private static ThingDef _coreItem;

	private static bool _resolved;

	private const int TypeSeed = 377087;

	public const int DefenseMinTier = 9;

	private static void Resolve()
	{
		if (!_resolved)
		{
			_marksman = DefDatabase<HediffDef>.GetNamedSilentFail("EES_MarksmanAura");
			_assault = DefDatabase<HediffDef>.GetNamedSilentFail("EES_AssaultAura");
			_warProt = DefDatabase<HediffDef>.GetNamedSilentFail("EES_WarlordProtected");
			_warExp = DefDatabase<HediffDef>.GetNamedSilentFail("EES_WarlordExposed");
			_coreItem = DefDatabase<ThingDef>.GetNamedSilentFail("TechprofSubpersonaCore");
			_resolved = true;
		}
	}

	public static bool ReinforcementCommander(StrongholdData d)
	{
		if (d != null)
		{
			return d.commanderType == CommanderType.Comms;
		}
		return false;
	}

	public static bool TrapCommander(StrongholdData d)
	{
		if (d != null)
		{
			return d.commanderType == CommanderType.Engineer;
		}
		return false;
	}

	public static void AssignType(StrongholdData data, Settlement settlement)
	{
		if (data == null || !data.hasCommander)
		{
			if (data != null)
			{
				data.commanderType = CommanderType.None;
			}
			return;
		}
		List<CommanderType> list = new List<CommanderType>
		{
			CommanderType.Commander,
			CommanderType.Marksman,
			CommanderType.Comms,
			CommanderType.Fanatic,
			CommanderType.Assault,
			CommanderType.Engineer,
			CommanderType.Research,
			CommanderType.Warlord,
			CommanderType.Observer
		};
		if (data.tier >= 9)
		{
			list.Add(CommanderType.Defense);
		}
		if (data.tier >= 10)
		{
			list.Add(CommanderType.Security);
		}
		if (settlement != null && EESPuppetUtility.IsPuppetOutpost(settlement))
		{
			list.Remove(CommanderType.Research);
			list.Remove(CommanderType.Security);
		}
		if (settlement != null && EESCustomBaseDetector.IsDedicatedOutpostFaction(settlement.Faction))
		{
			list.Remove(CommanderType.Engineer);
			list.Remove(CommanderType.Security);
		}
		if (BattlefieldUtility.IsBattlefield(data.roleDefName))
		{
			list.Remove(CommanderType.Commander);
			list.Remove(CommanderType.Comms);
			list.Remove(CommanderType.Engineer);
			list.Remove(CommanderType.Observer);
		}
		list.RemoveAll((CommanderType t) => !CommanderTypeUtil.AllowedForFactionTech(t, settlement?.Faction));
		if (list.Count == 0)
		{
			list.Add(CommanderType.Marksman);
		}
		Rand.PushState(Gen.HashCombineInt(data.settlementId, 377087));
		CommanderType commanderType = list[Rand.Range(0, list.Count)];
		Rand.PopState();
		data.commanderType = commanderType;
		EESLog.Message($"Commander type {settlement.LabelCap} [{settlement.Faction?.Name}]: T{data.tier} -> {commanderType}.");
	}

	public static void ApplyGenerationEffects(Map map, StrongholdData data, Faction faction)
	{
		if (map == null || data == null || faction == null || !data.hasCommander)
		{
			EESLog.Message($"Commander gen SKIP: hasCommander={data?.hasCommander} (map/faction null? {map == null}/{faction == null}).");
			return;
		}
		Resolve();
		EESLog.Message($"Commander gen [{faction.Name}]: type={data.commanderType} (생성시 효과 적용).");
		switch (data.commanderType)
		{
		case CommanderType.Warlord:
		{
			int num = ApplyHediffToAll(map, faction, _warProt);
			EESLog.Message($"Commander gen [Warlord {faction.Name}]: protected {num} base pawns (dmg ×0.75).");
			break;
		}
		case CommanderType.Security:
			RegisterSecurityStructures(map, data, faction);
			break;
		}
	}

	private static void RegisterSecurityStructures(Map map, StrongholdData data, Faction faction)
	{
		StrongholdDefenseUtility.GetAncientCoreDefs(map, out var ancientWall, out var blastDoor);
		MapComponent_StrongholdSiege component = map.GetComponent<MapComponent_StrongholdSiege>();
		if (component == null)
		{
			return;
		}
		List<Thing> list = new List<Thing>();
		foreach (Thing allThing in map.listerThings.AllThings)
		{
			if (allThing != null && allThing.Spawned && allThing.Faction == faction && ((ancientWall != null && allThing.def == ancientWall) || (blastDoor != null && allThing.def == blastDoor)))
			{
				list.Add(allThing);
			}
		}
		component.RegisterSecurityStructures(list);
		int num = 0;
		foreach (Thing item in list)
		{
			if (item != null && item.def != null && item.def.useHitPoints && item.HitPoints < item.MaxHitPoints)
			{
				item.HitPoints = item.MaxHitPoints;
				num++;
			}
		}
		EESLog.Message($"Commander gen [Security {faction.Name}]: registered {list.Count} vault structures for HP×5 (current HP bumped {num}).");
	}

	public static void TickAuras(Map map, StrongholdData data, Faction faction, Pawn commander, bool functional)
	{
		if (map == null || data == null || faction == null)
		{
			return;
		}
		Resolve();
		if (!(data.commanderType == CommanderType.Warlord && functional))
		{
			return;
		}
		List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(faction);
		for (int i = 0; i < list.Count; i++)
		{
			Pawn pawn = list[i];
			if (pawn != null && !pawn.Dead && pawn.RaceProps != null && pawn.RaceProps.Humanlike)
			{
				if (HasHediff(pawn, _warExp))
				{
					SetHediff(pawn, _warExp, want: false);
				}
				SetHediff(pawn, _warProt, want: true);
			}
		}
	}

	public static void AttachCommanderShield(Pawn cmd)
	{
		if (cmd == null || cmd.GetComp<CompEESShieldCarrier>() != null)
		{
			return;
		}
		try
		{
			CompProperties_EESShieldCarrier props = new CompProperties_EESShieldCarrier();
			CompEESShieldCarrier compEESShieldCarrier = new CompEESShieldCarrier
			{
				parent = cmd
			};
			compEESShieldCarrier.Initialize(props);
			compEESShieldCarrier.PostPostMake();
			cmd.AllComps.Add(compEESShieldCarrier);
			compEESShieldCarrier.PostSpawnSetup(respawningAfterLoad: false);
			EESLog.Message("Commander shield carrier attached to " + cmd.LabelShortCap + " (def-based dome follows, intercepts).");
		}
		catch (Exception ex)
		{
			EESLog.Warning("Commander shield carrier attach: " + ex.Message);
		}
	}

	public static void AttachCommandAura(Pawn cmd, string hediffDefName)
	{
		if (cmd == null || hediffDefName.NullOrEmpty())
		{
			return;
		}
		try
		{
			CompProperties_EESCommandAura props = new CompProperties_EESCommandAura
			{
				hediffDefName = hediffDefName,
				range = 8f
			};
			CompEESCommandAura compEESCommandAura = new CompEESCommandAura
			{
				parent = cmd
			};
			compEESCommandAura.Initialize(props);
			compEESCommandAura.PostPostMake();
			cmd.AllComps.Add(compEESCommandAura);
			compEESCommandAura.PostSpawnSetup(respawningAfterLoad: false);
			EESLog.Message($"Command aura attached to {cmd.LabelShortCap} ({hediffDefName}, range {8f}).");
		}
		catch (Exception ex)
		{
			EESLog.Warning("Command aura attach: " + ex.Message);
		}
	}

	public static void RemoveCommanderShield(Pawn cmd)
	{
		if (cmd != null)
		{
			CompEESShieldCarrier comp = cmd.GetComp<CompEESShieldCarrier>();
			if (comp != null)
			{
				comp.ClearDome();
				cmd.AllComps.Remove(comp);
				EESLog.Message("Commander shield removed from " + cmd.LabelShortCap + " (down/dead).");
			}
		}
	}

	public static bool ResurrectWave(Map map, StrongholdData data, Faction faction)
	{
		if (map == null || data == null || faction == null || data.commanderType != CommanderType.Fanatic)
		{
			return false;
		}
		int num = 0;
		int num2 = 0;
		List<Pawn> list = new List<Pawn>();
		IReadOnlyList<Pawn> allPawnsSpawned = map.mapPawns.AllPawnsSpawned;
		for (int i = 0; i < allPawnsSpawned.Count; i++)
		{
			Pawn pawn = allPawnsSpawned[i];
			if (pawn != null && !pawn.Dead && pawn.Faction == faction)
			{
				if (pawn.Downed)
				{
					num2++;
					list.Add(pawn);
				}
				else
				{
					num++;
				}
			}
		}
		int num3 = 0;
		int num4 = 0;
		List<Pawn> list2 = new List<Pawn>();
		List<Thing> list3 = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse);
		for (int j = 0; j < list3.Count; j++)
		{
			if (list3[j] is Corpse { InnerPawn: { Dead: not false, Discarded: false } innerPawn })
			{
				num3++;
				if (innerPawn.Faction == faction)
				{
					num4++;
					list2.Add(innerPawn);
				}
			}
		}
		if (list.Count + list2.Count == 0)
		{
			int count = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).Count;
			EESLog.Message($"Fanatic rally [{faction.Name}]: 대상 없음 — 살아있는={num} 다운={num2} " + $"시체(그룹전체={count} / 부활가능={num3} / 관련={num4}) — skip.");
			return false;
		}
		int num5 = Rand.RangeInclusive(2, 3);
		int num6 = 0;
		int num7 = 0;
		List<Pawn> list4 = new List<Pawn>();
		for (int k = 0; k < list2.Count; k++)
		{
			if (list4.Count >= num5)
			{
				break;
			}
			Pawn pawn2 = list2[k];
			try
			{
				ResurrectionUtility.TryResurrect(pawn2);
				if (pawn2.Spawned && !pawn2.Dead)
				{
					list4.Add(pawn2);
					num6++;
				}
			}
			catch (Exception ex)
			{
				EESLog.Warning("Fanatic resurrect: " + ex.Message);
			}
		}
		for (int l = 0; l < list.Count; l++)
		{
			if (list4.Count >= num5)
			{
				break;
			}
			if (RouseDowned(list[l]))
			{
				list4.Add(list[l]);
				num7++;
			}
		}
		if (list4.Count > 0)
		{
			for (int m = 0; m < list4.Count; m++)
			{
				list4[m].GetLord()?.Notify_PawnLost(list4[m], PawnLostCondition.ForcedToJoinOtherLord);
			}
			LordJob lordJob = (BattlefieldUtility.IsBattlefield(data.roleDefName) ? ((LordJob)new LordJob_DefendBase_NoFlee(faction, map.Center, 2000000000)) : ((LordJob)new LordJob_AssaultColony_NoFlee(faction)));
			LordMaker.MakeNewLord(faction, lordJob, map, list4);
			Messages.Message(EESText.T((num6 > 0) ? "EES.Cmd.Resurrect" : "EES.Cmd.Rally", list4.Count), new TargetInfo(list4[0].Position, map), MessageTypeDefOf.ThreatSmall, historical: false);
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int n = 0; n < list4.Count; n++)
		{
			stringBuilder.Append(" " + list4[n].LabelShort + "<" + (list4[n].Faction?.Name ?? "null") + ">" + ((list4[n].Faction == faction) ? "" : "★적!"));
		}
		EESLog.Message($"Fanatic rally [{faction.Name}]: 부활(시체)={num6} + 재기립(다운)={num7} = {list4.Count}/{num5} " + $"(시체 후보={list2.Count}/부활가능{num3} / 다운 후보={list.Count} | 살아있는={num}) | 부활폰:{stringBuilder}");
		return list4.Count > 0;
	}

	private static bool RouseDowned(Pawn p)
	{
		if (p == null || p.Dead || !p.Spawned)
		{
			return false;
		}
		try
		{
			HealthUtility.HealNonPermanentInjuriesAndRestoreLegs(p);
			HediffSet hediffSet = p.health?.hediffSet;
			if (hediffSet != null)
			{
				List<Hediff> list = new List<Hediff>();
				List<Hediff> hediffs = hediffSet.hediffs;
				for (int i = 0; i < hediffs.Count; i++)
				{
					if (hediffs[i].def == HediffDefOf.BloodLoss || hediffs[i].def == HediffDefOf.Anesthetic)
					{
						list.Add(hediffs[i]);
					}
				}
				for (int j = 0; j < list.Count; j++)
				{
					p.health.RemoveHediff(list[j]);
				}
			}
			return !p.Dead && !p.Downed;
		}
		catch (Exception ex)
		{
			EESLog.Warning("RouseDowned: " + ex.Message);
			return false;
		}
	}

	public static void OnCommanderDeath(Map map, StrongholdData data, Faction faction, Pawn commander)
	{
		if (map != null && data != null && faction != null)
		{
			Resolve();
			switch (data.commanderType)
			{
			case CommanderType.Marksman:
				RemoveHediffFromAll(map, faction, _marksman);
				break;
			case CommanderType.Assault:
				RemoveHediffFromAll(map, faction, _assault);
				break;
			case CommanderType.Warlord:
			{
				RemoveHediffFromAll(map, faction, _warProt);
				int num = ApplyHediffToAll(map, faction, _warExp);
				EESLog.Message($"Commander death [Warlord {faction.Name}]: exposed {num} base pawns (dmg ×2.5).");
				break;
			}
			case CommanderType.Engineer:
				DeactivateTraps(map, faction);
				break;
			case CommanderType.Research:
				DropResearchCore(map, faction, commander);
				break;
			case CommanderType.Comms:
			case CommanderType.Defense:
			case CommanderType.Fanatic:
			case CommanderType.Security:
				break;
			}
		}
	}

	private static void DeactivateTraps(Map map, Faction faction)
	{
		int num = 0;
		foreach (Thing item in map.listerThings.AllThings.Where((Thing t) => t != null && t.Spawned && t.Faction == faction && t.def?.building != null && t.def.building.isTrap).ToList())
		{
			item.Destroy();
			num++;
		}
		EESLog.Message($"Commander death [Engineer {faction.Name}]: deactivated {num} traps.");
	}

	private static void DropResearchCore(Map map, Faction faction, Pawn commander)
	{
		if (_coreItem == null)
		{
			EESLog.Warning("Commander death [Research]: TechprofSubpersonaCore def not found — drop skipped.");
			return;
		}
		try
		{
			Thing thing = ThingMaker.MakeThing(_coreItem);
			IntVec3 intVec = ((commander != null && commander.PositionHeld.IsValid) ? commander.PositionHeld : ((commander != null && commander.Corpse != null && commander.Corpse.Spawned) ? commander.Corpse.Position : map.Center));
			GenPlace.TryPlaceThing(thing, intVec, map, ThingPlaceMode.Near);
			Messages.Message(EESText.T("EES.Cmd.ResearchCore"), new TargetInfo(intVec, map), MessageTypeDefOf.PositiveEvent, historical: false);
			EESLog.Message($"Commander death [Research {faction.Name}]: dropped {_coreItem.defName} @ {intVec}.");
		}
		catch (Exception ex)
		{
			EESLog.Error("Commander research-core drop: " + ex);
		}
	}

	private static int ApplyHediffToAll(Map map, Faction faction, HediffDef def)
	{
		if (def == null)
		{
			return 0;
		}
		int num = 0;
		List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(faction);
		for (int i = 0; i < list.Count; i++)
		{
			Pawn pawn = list[i];
			if (pawn != null && !pawn.Dead && pawn.RaceProps != null && pawn.RaceProps.Humanlike && SetHediff(pawn, def, want: true))
			{
				num++;
			}
		}
		return num;
	}

	private static void RemoveHediffFromAll(Map map, Faction faction, HediffDef def)
	{
		if (def != null)
		{
			List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(faction);
			for (int i = 0; i < list.Count; i++)
			{
				SetHediff(list[i], def, want: false);
			}
		}
	}

	private static bool HasHediff(Pawn p, HediffDef def)
	{
		if (def != null && p?.health?.hediffSet != null)
		{
			return p.health.hediffSet.HasHediff(def);
		}
		return false;
	}

	private static bool SetHediff(Pawn p, HediffDef def, bool want)
	{
		if (def == null || p?.health?.hediffSet == null)
		{
			return false;
		}
		Hediff firstHediffOfDef = p.health.hediffSet.GetFirstHediffOfDef(def);
		if (want && firstHediffOfDef == null)
		{
			p.health.AddHediff(def);
			return true;
		}
		if (!want && firstHediffOfDef != null)
		{
			p.health.RemoveHediff(firstHediffOfDef);
			return true;
		}
		return false;
	}
}
