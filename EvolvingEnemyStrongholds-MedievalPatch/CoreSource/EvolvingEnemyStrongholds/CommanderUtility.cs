using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public static class CommanderUtility
{
	private static readonly Color CommanderColor = new Color(0.72f, 0.08f, 0.1f);

	private static bool Enabled
	{
		get
		{
			if (StrongholdMod.Settings != null)
			{
				return StrongholdMod.Settings.enableCommanders;
			}
			return true;
		}
	}

	public static void DecideCommander(StrongholdData data, Settlement settlement)
	{
		if (data == null || settlement == null)
		{
			return;
		}
		if (FakeOutpostUtility.IsFake(data.roleDefName) || EESFactionKind.IsInsect(settlement.Faction))
		{
			data.hasCommander = false;
			data.commanderKindDefName = null;
			data.commanderType = CommanderType.None;
			return;
		}
		if (data.commanderForced)
		{
			EESLog.Message("Commander decide SKIP " + settlement.LabelCap + ": commanderForced (gizmo/debug) -> " + (data.hasCommander ? data.commanderType.ToString() : "none") + " 유지.");
			return;
		}
		if (BattlefieldUtility.IsBattlefield(data.roleDefName) && Enabled)
		{
			data.hasCommander = true;
			data.commanderKindDefName = PickCommanderKind(settlement.Faction)?.defName;
			CommanderEffects.AssignType(data, settlement);
			EESLog.Message(string.Format("Commander decide {0} [{1}]: role=Battlefield -> hasCommander 강제 ON kind={2} type={3}.", settlement.LabelCap, settlement.Faction?.Name, data.commanderKindDefName ?? "?", data.commanderType));
			return;
		}
		bool flag = !data.roleDefName.NullOrEmpty() && data.roleDefName.IndexOf("command", StringComparison.OrdinalIgnoreCase) >= 0;
		if (!Enabled || (data.tier < 8 && !flag))
		{
			data.hasCommander = false;
			return;
		}
		float num = CommanderChance(data);
		Rand.PushState(Gen.HashCombineInt(data.settlementId, 12648430));
		float value = Rand.Value;
		Rand.PopState();
		bool flag2 = (data.hasCommander = value < num);
		if (flag2)
		{
			data.commanderKindDefName = PickCommanderKind(settlement.Faction)?.defName;
		}
		EESLog.Message("Commander decide " + settlement.LabelCap + " [" + settlement.Faction?.Name + "]: " + string.Format("T{0} chance={1:0.00} roll={2:0.00} -> {3}", data.tier, num, value, flag2 ? ("YES kind=" + (data.commanderKindDefName ?? "?")) : "no"));
		CommanderEffects.AssignType(data, settlement);
	}

	public static void ForceCommanderKind(StrongholdData data, Settlement settlement)
	{
		if (data != null && settlement != null)
		{
			data.commanderKindDefName = PickCommanderKind(settlement.Faction)?.defName;
			EESLog.Message("Commander FORCE-set " + settlement.LabelCap + ": kind=" + (data.commanderKindDefName ?? "?"));
		}
	}

	private static float CommanderChance(StrongholdData data)
	{
		if (!data.roleDefName.NullOrEmpty() && data.roleDefName.IndexOf("command", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return 1f;
		}
		return Mathf.Clamp(data.ContentTier, 4, 7) switch
		{
			4 => 0.4f,
			5 => 0.6f,
			6 => 0.8f,
			_ => 1f,
		};
	}

	public static Pawn SpawnCommander(Map map, StrongholdData data)
	{
		if (map == null || data == null)
		{
			EESLog.Message("Commander spawn SKIP: map/data null.");
			return null;
		}
		if (!Enabled)
		{
			EESLog.Message("Commander spawn SKIP " + map.Parent?.LabelCap + ": enableCommanders=OFF(설정).");
			return null;
		}
		if (!data.hasCommander)
		{
			EESLog.Message($"Commander spawn SKIP {map.Parent?.LabelCap}: data.hasCommander=false (type={data.commanderType}).");
			return null;
		}
		Faction parentFaction = map.ParentFaction;
		if (parentFaction == null || parentFaction.IsPlayer)
		{
			EESLog.Message("Commander spawn SKIP " + map.Parent?.LabelCap + ": faction null/player (" + parentFaction?.Name + ").");
			return null;
		}
		if (!StrongholdGarrisonUtility.TryConsumePopulationSlot(special: true))
		{
			EESLog.Warning("Commander spawn SKIP " + map.Parent?.LabelCap + ": EES population budget exhausted.");
			return null;
		}
		EESLog.Message(string.Format("Commander spawn BEGIN {0} [{1}]: type={2} kindDef={3}.", map.Parent?.LabelCap, parentFaction.Name, data.commanderType, data.commanderKindDefName ?? "(auto)"));
		Pawn pawn = null;
		bool spawned = false;
		try
		{
			PawnKindDef pawnKindDef = ((!data.commanderKindDefName.NullOrEmpty()) ? (DefDatabase<PawnKindDef>.GetNamedSilentFail(data.commanderKindDefName) ?? PickCommanderKind(parentFaction)) : PickCommanderKind(parentFaction));
			if (pawnKindDef == null)
			{
				StrongholdGarrisonUtility.ReleasePopulationSlot();
				EESLog.Error("Commander spawn " + map.Parent?.LabelCap + ": no usable kind.");
				return null;
			}
			pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(pawnKindDef, parentFaction, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: true, allowDead: false, allowDowned: false, canGeneratePawnRelations: false, mustBeCapableOfViolence: true, 0f));
			UpgradeCommanderGear(pawn);
			MarkCommanderVisually(pawn, data.commanderType);
			IntVec3 intVec = (data.commandRoomCenter.IsValid ? data.commandRoomCenter : map.Center);
			if (!CellFinder.TryFindRandomCellInsideWith(CellRect.CenteredOn(intVec, 4, 4).ClipInsideMap(map), (IntVec3 c) => c.InBounds(map) && c.Standable(map) && !c.Fogged(map), out var result) && !CellFinder.TryFindRandomCellInsideWith(CellRect.CenteredOn(intVec, 12, 12).ClipInsideMap(map), (IntVec3 c) => c.InBounds(map) && c.Standable(map) && !c.Fogged(map), out result))
			{
				result = ((intVec.IsValid && intVec.Standable(map)) ? intVec : map.Center);
			}
			GenSpawn.Spawn(pawn, result, map, Rot4.Random);
			spawned = true;
			if (data.commanderType == CommanderType.Defense)
			{
				ThingDef namedSilentFail = DefDatabase<ThingDef>.GetNamedSilentFail("EES_CommanderShieldBelt");
				if (namedSilentFail != null)
				{
					try
					{
						Apparel apparel = (Apparel)ThingMaker.MakeThing(namedSilentFail);
						CompEESCommanderBelt compEESCommanderBelt = apparel.TryGetComp<CompEESCommanderBelt>();
						if (compEESCommanderBelt != null)
						{
							compEESCommanderBelt.legit = true;
						}
						pawn.apparel.Wear(apparel, dropReplacedApparel: false);
						EESLog.Message("Commander shield belt equipped on " + pawn.LabelShortCap + " (Defense).");
					}
					catch (Exception ex)
					{
						EESLog.Warning("Commander shield belt equip: " + ex.Message);
					}
				}
				CommanderEffects.AttachCommanderShield(pawn);
			}
			else if (data.commanderType == CommanderType.Marksman)
			{
				CommanderEffects.AttachCommandAura(pawn, "EES_MarksmanAura");
			}
			else if (data.commanderType == CommanderType.Assault)
			{
				CommanderEffects.AttachCommandAura(pawn, "EES_AssaultAura");
			}
			EESLog.Message($"Commander effects setup {pawn.LabelShortCap}: type={data.commanderType}.");
			LordMaker.MakeNewLord(parentFaction, new LordJob_EESCommanderDefend(parentFaction, map.Center, 999999), map, Gen.YieldSingle(pawn));
			map.GetComponent<MapComponent_StrongholdSiege>()?.RegisterCommander(pawn);
			string arg = pawn.equipment?.Primary?.LabelCap.ToString() ?? "(무기없음)";
			EESLog.Message("Commander SPAWN " + map.Parent?.LabelCap + " [" + parentFaction.Name + "]: kind=" + pawnKindDef.defName + " " + $"@ {result} weapon={arg}.");
			return pawn;
		}
		catch (Exception arg2)
		{
			if (!spawned)
			{
				StrongholdGarrisonUtility.ReleasePopulationSlot();
			}
			EESLog.Error($"Commander SpawnCommander {map.Parent?.LabelCap}: {arg2}");
			return null;
		}
	}

	private static void UpgradeCommanderGear(Pawn cmd)
	{
		((cmd?.equipment?.Primary)?.TryGetComp<CompQuality>())?.SetQuality(QualityCategory.Masterwork, ArtGenerationContext.Outsider);
	}

	private static void MarkCommanderVisually(Pawn cmd, CommanderType type)
	{
		if (cmd == null)
		{
			return;
		}
		string text = type.Nickname();
		if (cmd.Name is NameTriple nameTriple)
		{
			cmd.Name = new NameTriple(nameTriple.First, text, nameTriple.Last);
		}
		else
		{
			cmd.Name = new NameSingle(text);
		}
		List<Apparel> list = cmd.apparel?.WornApparel;
		int num = 0;
		if (list != null)
		{
			for (int i = 0; i < list.Count; i++)
			{
				CompColorable compColorable = list[i]?.TryGetComp<CompColorable>();
				if (compColorable != null)
				{
					compColorable.SetColor(CommanderColor);
					num++;
				}
			}
		}
		cmd.Drawer?.renderer?.SetAllGraphicsDirty();
		EESLog.Message($"Commander mark: nick='{text}' dyedApparel={num}.");
	}

	private static PawnKindDef PickCommanderKind(Faction faction)
	{
		FactionDef factionDef = faction?.def;
		if (factionDef == null)
		{
			return null;
		}
		PawnKindDef pawnKindDef = null;
		float num = -1f;
		if (factionDef.pawnGroupMakers != null)
		{
			for (int i = 0; i < factionDef.pawnGroupMakers.Count; i++)
			{
				List<PawnGenOption> list = factionDef.pawnGroupMakers[i]?.options;
				if (list == null)
				{
					continue;
				}
				for (int j = 0; j < list.Count; j++)
				{
					PawnKindDef pawnKindDef2 = list[j]?.kind;
					if (pawnKindDef2 != null && pawnKindDef2.RaceProps != null && pawnKindDef2.RaceProps.Humanlike && pawnKindDef2.combatPower > num)
					{
						num = pawnKindDef2.combatPower;
						pawnKindDef = pawnKindDef2;
					}
				}
			}
		}
		return pawnKindDef ?? factionDef.basicMemberKind;
	}

	public static int ApplyCommanderDeathMorale(Map map, Faction faction)
	{
		if (map == null || faction == null)
		{
			return 0;
		}
		MentalStateDef panicFlee = MentalStateDefOf.PanicFlee;
		if (panicFlee == null)
		{
			return 0;
		}
		List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(faction);
		int num = 0;
		for (int i = 0; i < list.Count; i++)
		{
			Pawn pawn = list[i];
			if (pawn != null && !pawn.Dead && !pawn.Downed && pawn.RaceProps != null && pawn.RaceProps.Humanlike && !pawn.InMentalState && Rand.Value < 0.5f && pawn.mindState?.mentalStateHandler != null && pawn.mindState.mentalStateHandler.TryStartMentalState(panicFlee, "EES commander killed"))
			{
				num++;
			}
		}
		EESLog.Message($"Commander death morale [{faction.Name}]: {num}/{list.Count} pawns panicked.");
		return num;
	}
}
