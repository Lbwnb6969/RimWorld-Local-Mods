using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Settlement), "GetGizmos")]
public static class Patch_Settlement_GetGizmos
{
	public static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> values, Settlement __instance)
	{
		foreach (Gizmo value in values)
		{
			yield return value;
		}
		if (__instance.Faction == null || __instance.Faction == Faction.OfPlayer || EESFactionBlacklist.IsBlocked(__instance.Faction) || EESCustomBaseDetector.IsForeignSettlement(__instance))
		{
			yield break;
		}
		WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
		if (instance == null)
		{
			EESLog.Warning("GetGizmos: WorldComponent_Strongholds is null.");
			yield break;
		}
		StrongholdData data = instance.GetOrCreate(__instance);
		if (ScoutUtility.Enabled && !ScoutUtility.IsScouted(data))
		{
			ThingDef thingDef = IntelItemUtility.ChooseForTarget(data.tier);
			Command_Action command_Action = new Command_Action
			{
				defaultLabel = EESText.T("EES.BuyIntel.Label"),
				defaultDesc = EESText.T("EES.BuyIntel.Desc"),
				icon = IntelItemUtility.GizmoIcon,
				defaultIconColor = IntelItemUtility.GizmoIconColor,
				action = delegate
				{
					if (IntelItemUtility.ConsumeForTarget(data.tier, out var usedLabel))
					{
						data.intelLevel = 1;
						data.lastScoutedTick = Find.TickManager.TicksGame;
						Messages.Message(EESText.T("EES.BuyIntel.Done", __instance.LabelCap, usedLabel), new GlobalTargetInfo(__instance), MessageTypeDefOf.PositiveEvent, historical: false);
						EESLog.Message($"Buy intel {__instance.LabelCap} (T{data.tier}) consumed [{usedLabel}].");
					}
				}
			};
			if (thingDef == null)
			{
				command_Action.Disable(IntelItemUtility.TierTooHigh(data.tier) ? EESText.T("EES.BuyIntel.TooHigh") : EESText.T("EES.BuyIntel.NoItem", data.tier));
			}
			yield return command_Action;
		}
		if (!EESPuppetUtility.IsPuppetOutpost(__instance))
		{
			yield break;
		}
		int[] outpostState = EESPuppetApi.GetOutpostState(__instance);
		if (outpostState == null)
		{
			yield break;
		}
		int num = outpostState[0];
		int num2 = outpostState[1];
		int num3 = outpostState[3];
		int numTicks = outpostState[6];
		Command_Action command_Action2 = new Command_Action();
		command_Action2.defaultLabel = EESText.T("EES.Puppet.Gizmo.UpgradeLabel", num, num2);
		command_Action2.defaultDesc = ((num3 < 0) ? EESText.T("EES.Puppet.Gizmo.UpgradeDescCap", num2) : EESText.T("EES.Puppet.Gizmo.UpgradeDesc", num + 1, num3, numTicks.ToStringTicksToPeriod()));
		command_Action2.icon = ContentFinder<Texture2D>.Get("UI/Commands/DesirePower", reportFailure: false) ?? BaseContent.BadTex;
		command_Action2.action = delegate
		{
			string text = EESPuppetApi.TryUpgradeTier(__instance);
			if (text != null)
			{
				Messages.Message(text, new GlobalTargetInfo(__instance), MessageTypeDefOf.RejectInput, historical: false);
			}
			else
			{
				Messages.Message(EESText.T("EES.Puppet.Msg.Upgraded", __instance.LabelCap, EESPuppetApi.GetOutpostState(__instance)[0]), new GlobalTargetInfo(__instance), MessageTypeDefOf.PositiveEvent, historical: false);
			}
		};
		Command_Action command_Action3 = command_Action2;
		if (num3 < 0)
		{
			command_Action3.Disable(EESText.T("EES.Puppet.AtCap", num2));
		}
		else if (EESPuppetApi.PaymentMap(num3) == null)
		{
			command_Action3.Disable(EESText.T("EES.Puppet.NeedSilver", num3));
		}
		yield return command_Action3;
		string[] roles = EESPuppetApi.SelectableRoles(__instance);
		command_Action2 = new Command_Action();
		command_Action2.defaultLabel = EESText.T("EES.Puppet.Gizmo.RoleLabel", RoleLabel(data.roleDefName));
		command_Action2.defaultDesc = EESText.T("EES.Puppet.Gizmo.RoleDesc");
		command_Action2.icon = ContentFinder<Texture2D>.Get("WorldObjects/RptPuppetSettlement", reportFailure: false) ?? BaseContent.BadTex;
		command_Action2.action = delegate
		{
			List<FloatMenuOption> list = new List<FloatMenuOption>();
			foreach (string picked in roles)
			{
				list.Add(new FloatMenuOption(RoleLabel(picked), delegate
				{
					string text = EESPuppetApi.TrySetRole(__instance, picked);
					Messages.Message(text ?? EESText.T("EES.Puppet.Msg.RoleSet", __instance.LabelCap, RoleLabel(picked)), new GlobalTargetInfo(__instance), (text != null) ? MessageTypeDefOf.RejectInput : MessageTypeDefOf.TaskCompletion, historical: false);
				}));
			}
			Find.WindowStack.Add(new FloatMenu(list));
		};
		Command_Action command_Action4 = command_Action2;
		if (roles == null || roles.Length == 0)
		{
			command_Action4.Disable(EESText.T("EES.Puppet.RoleUnavailable"));
		}
		yield return command_Action4;
		yield return new Command_Action
		{
			defaultLabel = EESText.T("EES.Puppet.Gizmo.CommanderLabel", data.commanderType.Nickname()),
			defaultDesc = EESText.T("EES.Puppet.Gizmo.CommanderDesc"),
			icon = (ContentFinder<Texture2D>.Get("UI/Commands/Draft", reportFailure: false) ?? BaseContent.BadTex),
			action = delegate
			{
				List<FloatMenuOption> list = new List<FloatMenuOption>();
				string[] array = EESPuppetApi.SelectableCommanderTypes(__instance) ?? new string[0];
				foreach (string text in array)
				{
					string picked = text;
					string label = ((CommanderType)Enum.Parse(typeof(CommanderType), text)).Nickname();
					list.Add(new FloatMenuOption(label, delegate
					{
						string text2 = EESPuppetApi.TrySetCommander(__instance, picked);
						Messages.Message(text2 ?? EESText.T("EES.Puppet.Msg.CommanderSet", __instance.LabelCap, label), new GlobalTargetInfo(__instance), (text2 != null) ? MessageTypeDefOf.RejectInput : MessageTypeDefOf.TaskCompletion, historical: false);
					}));
				}
				Find.WindowStack.Add(new FloatMenu(list));
			}
		};
	}

	public static string RoleLabel(string roleDefName)
	{
		return EESText.T("EES.Role." + (string.IsNullOrEmpty(roleDefName) ? "Generic" : roleDefName));
	}
}
