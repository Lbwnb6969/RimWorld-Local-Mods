using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace EvolvingEnemyStrongholds;

public static class CounterScoutUtility
{
	public const int MinTier = 6;

	private const int SourceMaxDistTiles = 45;

	private const float LurkMinCenterFrac = 0.55f;

	private const int LurkEdgeMargin = 8;

	private const int LurkEntryCheckBudget = 30;

	private const float ScoutKindWeakestFraction = 0.3f;

	private const float ScoutKindPowerCapMul = 4f;

	private static readonly FloatRange RaidDelayDays = new FloatRange(2f, 7f);

	private static readonly FloatRange RaidPointsMul = new FloatRange(1f, 1f);

	private static readonly string[] OctantKeys = new string[8] { "EES.Dir.N", "EES.Dir.NE", "EES.Dir.E", "EES.Dir.SE", "EES.Dir.S", "EES.Dir.SW", "EES.Dir.W", "EES.Dir.NW" };

	private static readonly string[] OctantLogNames = new string[8] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

	public static bool Enabled
	{
		get
		{
			if (StrongholdMod.Settings != null)
			{
				return StrongholdMod.Settings.enableCounterScouts;
			}
			return true;
		}
	}

	public static HediffDef CloakDef => DefDatabase<HediffDef>.GetNamedSilentFail("EES_ScoutCloak");

	public static Faction FactionByLoadId(int loadId)
	{
		if (loadId < 0)
		{
			return null;
		}
		List<Faction> allFactionsListForReading = Find.FactionManager.AllFactionsListForReading;
		for (int i = 0; i < allFactionsListForReading.Count; i++)
		{
			if (allFactionsListForReading[i].loadID == loadId)
			{
				return allFactionsListForReading[i];
			}
		}
		return null;
	}

	public static bool TrySpawn()
	{
		return TrySpawn(ignoreActiveMission: false);
	}

	public static bool TrySpawn(bool ignoreActiveMission)
	{
		string tally;
		Map map = PickPlayerMap(ignoreActiveMission, out tally);
		if (map == null)
		{
			EESLog.Message("CScout: 대상 플레이어 맵 없음 → skip. " + tally);
			return false;
		}
		StrongholdData data;
		Settlement settlement = PickSourceStronghold(map, out data);
		if (settlement == null)
		{
			EESLog.Message($"CScout: 반경 {45}타일 내 T{6}+ 후보 거점 0 → skip.");
			return false;
		}
		return SpawnMission(map, settlement, data);
	}

	private static Map PickPlayerMap(bool ignoreActiveMission, out string tally)
	{
		List<Map> list = new List<Map>();
		List<Map> maps = Find.Maps;
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		string text = string.Empty;
		for (int i = 0; i < maps.Count; i++)
		{
			Map map = maps[i];
			if (!map.IsPlayerHome)
			{
				continue;
			}
			num++;
			if (VacuumMapUtility.IsSpaceMap(map))
			{
				num4++;
				text = text + ((text.Length > 0) ? "," : "") + (map.Parent?.LabelCap.ToString() ?? ("map" + map.uniqueID));
				continue;
			}
			if (map.mapPawns.FreeColonistsSpawnedCount == 0)
			{
				num2++;
				continue;
			}
			if (!ignoreActiveMission)
			{
				MapComponent_EESCounterScout component = map.GetComponent<MapComponent_EESCounterScout>();
				if (component != null && component.AnyActiveMission)
				{
					num3++;
					continue;
				}
			}
			list.Add(map);
		}
		tally = string.Format("본거지 {0}곳 [우주 맵 제외 {1}{2} / ", num, num4, (num4 > 0) ? ("@" + text) : "") + string.Format("정착민 0 {0} / 임무 진행중 {1}{2}] → 후보 {3}곳", num2, num3, ignoreActiveMission ? "(DEV 무시)" : "", list.Count);
		if (list.Count > 0 && num4 > 0)
		{
			EESLog.Message("CScout: 대상 맵 선정 — " + tally + ".");
		}
		if (list.Count <= 0)
		{
			return null;
		}
		return list.RandomElement();
	}

	private static Settlement PickSourceStronghold(Map map, out StrongholdData data)
	{
		data = null;
		WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
		if (instance == null)
		{
			return null;
		}
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		List<Settlement> list = new List<Settlement>();
		int num = 0;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			if (!EligibleSource(settlement) || Find.WorldGrid.ApproxDistanceInTiles(settlement.Tile, map.Tile) > 45f)
			{
				continue;
			}
			StrongholdData orCreate = instance.GetOrCreate(settlement);
			if (orCreate != null && orCreate.tier >= 6 && !FakeOutpostUtility.IsFake(orCreate.roleDefName) && !(orCreate.roleDefName == "Battlefield"))
			{
				if (!IgnoranceCompat.FactionAllowed(settlement.Faction))
				{
					num++;
				}
				else
				{
					list.Add(settlement);
				}
			}
		}
		if (num > 0)
		{
			EESLog.Message($"CScout: 후보 거점 {num}곳 제외 — Ignorance Is Bliss 테크 제한(잔여 후보 {list.Count}곳).");
		}
		if (list.Count == 0)
		{
			return null;
		}
		Settlement settlement2 = list.RandomElement();
		data = instance.GetOrCreate(settlement2);
		return settlement2;
	}

	private static bool EligibleSource(Settlement s)
	{
		Faction faction = s?.Faction;
		if (faction == null || faction.IsPlayer || !faction.HostileTo(Faction.OfPlayer))
		{
			return false;
		}
		if (!s.Tile.Valid || !s.Tile.Layer.IsRootSurface)
		{
			return false;
		}
		if (faction.def == null || !faction.def.humanlikeFaction || EESFactionKind.IsInsect(faction))
		{
			return false;
		}
		if (EESFactionBlacklist.IsBlocked(faction) || EESCustomBaseDetector.IsForeignSettlement(s))
		{
			return false;
		}
		return !s.HasMap;
	}

	private static PawnKindDef PickScoutKind(Faction faction)
	{
		int rejElite = 0;
		int rejOther = 0;
		List<PawnKindDef> list = CollectScoutKinds(faction, combatOnly: true, ref rejElite, ref rejOther);
		string text = "Combat";
		if (list.Count == 0)
		{
			rejElite = 0;
			rejOther = 0;
			list = CollectScoutKinds(faction, combatOnly: false, ref rejElite, ref rejOther);
			text = "전체";
		}
		if (list.Count == 0)
		{
			PawnKindDef pawnKindDef = faction?.def?.basicMemberKind ?? faction?.RandomPawnKind();
			EESLog.Message($"CScout kind [{faction?.Name}]: 후보 0종 (제외 지휘관·보스={rejElite} 기타={rejOther}) " + "→ 폴백 " + (pawnKindDef?.defName ?? "없음") + ".");
			return pawnKindDef;
		}
		bool byPower = false;
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i].combatPower >= 0f)
			{
				byPower = true;
				break;
			}
		}
		if (byPower)
		{
			list.RemoveAll((PawnKindDef k) => k.combatPower < 0f);
		}
		list.Sort(delegate(PawnKindDef a, PawnKindDef b)
		{
			if (byPower)
			{
				int num7 = a.combatPower.CompareTo(b.combatPower);
				if (num7 != 0)
				{
					return num7;
				}
			}
			int num8 = GearMoney(a).CompareTo(GearMoney(b));
			return (num8 == 0) ? string.CompareOrdinal(a.defName, b.defName) : num8;
		});
		int count = list.Count;
		float num = 4f;
		float num2 = (byPower ? list[0].combatPower : GearMoney(list[0]));
		float num3 = num2 * num;
		if (num > 0f && num2 > 0f)
		{
			for (int num4 = list.Count - 1; num4 >= 1; num4--)
			{
				if ((byPower ? list[num4].combatPower : GearMoney(list[num4])) > num3)
				{
					list.RemoveAt(num4);
				}
			}
		}
		int num5 = count - list.Count;
		int num6 = Mathf.Max(1, Mathf.CeilToInt((float)list.Count * 0.3f));
		PawnKindDef pawnKindDef2 = list[Rand.Range(0, num6)] ?? faction?.RandomPawnKind();
		PawnKindDef pawnKindDef3 = list[list.Count - 1];
		EESLog.Message(string.Format("CScout kind [{0}]: 풀={1} {2}종 기준={3} ", faction?.Name, text, count, byPower ? "combatPower" : "장비예산") + $"| 상한 최약{num2:F0}×{num:F1}={num3:F0} → 잘림 {num5}종, 남음 {list.Count}종 " + string.Format("→ 하위 {0}종 후보 [{1}] → 선택 {2} ", num6, KindList(list, num6), pawnKindDef2?.defName ?? "없음") + $"(cp={pawnKindDef2?.combatPower ?? (-1f):F0} gear={GearMoney(pawnKindDef2):F0}) | " + $"풀 최강={pawnKindDef3.defName}(cp={pawnKindDef3.combatPower:F0} gear={GearMoney(pawnKindDef3):F0}) | " + $"제외 지휘관·보스={rejElite} 기타(비인간형·비전투)={rejOther}");
		return pawnKindDef2;
	}

	private static List<PawnKindDef> CollectScoutKinds(Faction faction, bool combatOnly, ref int rejElite, ref int rejOther)
	{
		List<PawnKindDef> list = new List<PawnKindDef>();
		List<PawnGroupMaker> list2 = faction?.def?.pawnGroupMakers;
		if (list2 == null)
		{
			return list;
		}
		for (int i = 0; i < list2.Count; i++)
		{
			if (list2[i] == null || (combatOnly && list2[i].kindDef != PawnGroupKindDefOf.Combat))
			{
				continue;
			}
			List<PawnGenOption> options = list2[i].options;
			if (options == null)
			{
				continue;
			}
			for (int j = 0; j < options.Count; j++)
			{
				PawnKindDef pawnKindDef = options[j]?.kind;
				if (pawnKindDef != null && !list.Contains(pawnKindDef))
				{
					if (pawnKindDef.RaceProps == null || !pawnKindDef.RaceProps.Humanlike || !pawnKindDef.isFighter)
					{
						rejOther++;
					}
					else if (pawnKindDef.factionLeader || pawnKindDef.isBoss || pawnKindDef.maxPerGroup == 1)
					{
						rejElite++;
					}
					else
					{
						list.Add(pawnKindDef);
					}
				}
			}
		}
		return list;
	}

	private static float GearMoney(PawnKindDef k)
	{
		if (k != null)
		{
			return k.weaponMoney.Average + k.apparelMoney.Average;
		}
		return 0f;
	}

	private static string KindList(List<PawnKindDef> sorted, int take)
	{
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < take && i < sorted.Count; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append(", ");
			}
			stringBuilder.Append(sorted[i].defName).Append('(').Append(sorted[i].combatPower.ToString("F0"))
				.Append(')');
		}
		return stringBuilder.ToString();
	}

	private static bool SpawnMission(Map map, Settlement source, StrongholdData data)
	{
		Faction faction = source.Faction;
		int num = ((data.tier >= 10) ? 2 : Rand.RangeInclusive(1, 2));
		if (!TryPickLurkSpot(map, out var spot, out var octant, out var entry))
		{
			return false;
		}
		List<Pawn> list = new List<Pawn>();
		for (int i = 0; i < num; i++)
		{
			try
			{
				Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PickScoutKind(faction), faction, PawnGenerationContext.NonPlayer, map.Tile, forceGenerateNewPawn: false, allowDead: false, allowDowned: false, canGeneratePawnRelations: true, mustBeCapableOfViolence: true));
				IntVec3 loc = ((i == 0) ? entry : CellFinder.RandomClosewalkCellNear(entry, map, 4));
				GenSpawn.Spawn(pawn, loc, map);
				if (CloakDef != null)
				{
					pawn.health.AddHediff(CloakDef);
				}
				list.Add(pawn);
			}
			catch (Exception ex)
			{
				EESLog.Error("CScout: 정찰병 생성 실패: " + ex);
			}
		}
		if (list.Count == 0)
		{
			return false;
		}
		LordMaker.MakeNewLord(faction, new LordJob_EESCounterScout(spot), map, list);
		map.GetComponent<MapComponent_EESCounterScout>()?.RegisterMission(list, source.ID, faction);
		if (StrongholdMod.Settings == null || StrongholdMod.Settings.scoutLetterNotify)
		{
			Find.LetterStack.ReceiveLetter(EESText.T("EES_Letter_CounterScoutArrived"), EESText.T("EES.CScout.Arrived.Text", faction.Name, OctantLabel(octant)), EESLetterDefOf.EES_CounterScoutArrived);
		}
		else
		{
			Messages.Message(EESText.T("EES.CScout.Arrived.Text", faction.Name, OctantLabel(octant)), MessageTypeDefOf.ThreatSmall, historical: false);
		}
		EESLog.Message($"CScout: 파견 src={source.LabelCap}(T{data.tier}) fac={faction.Name} " + $"scouts={list.Count} 진입@{entry} 잠복@{spot}(팔분면 {octant}) " + "은신=" + ((CloakDef != null) ? "ON" : "OFF(def 미로드)") + ".");
		return true;
	}

	private static bool TryPickLurkSpot(Map map, out IntVec3 spot, out int octant, out IntVec3 entry)
	{
		spot = IntVec3.Invalid;
		octant = -1;
		entry = IntVec3.Invalid;
		IntVec3 center = map.Center;
		float num = (float)Mathf.Min(map.Size.x, map.Size.z) * 0.5f * 0.55f;
		int num2 = Rand.Range(0, 8);
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int num6 = 0;
		int num7 = 0;
		int num8 = 0;
		int[] array = new int[8];
		int num9 = 0;
		int num10 = 0;
		int num11 = 0;
		int num12 = 0;
		int num13 = 0;
		int num14 = 0;
		for (int i = 0; i < 8; i++)
		{
			int num15 = (num2 + i) % 8;
			for (int j = 0; j < 150; j++)
			{
				num9++;
				IntVec3 intVec = CellFinder.RandomCell(map);
				if (intVec.x < 8 || intVec.z < 8 || intVec.x >= map.Size.x - 8 || intVec.z >= map.Size.z - 8)
				{
					num10++;
					continue;
				}
				if ((intVec - center).LengthHorizontal < num)
				{
					num11++;
					continue;
				}
				if (OctantOf(center, intVec) != num15)
				{
					num12++;
					continue;
				}
				if (!intVec.Standable(map))
				{
					num13++;
					continue;
				}
				if (intVec.Fogged(map))
				{
					num14++;
					continue;
				}
				if (map.areaManager.Home[intVec])
				{
					num3++;
					continue;
				}
				if (intVec.Roofed(map))
				{
					num4++;
					continue;
				}
				Room room = intVec.GetRoom(map);
				if (room == null || !room.TouchesMapEdge)
				{
					num5++;
					continue;
				}
				if (!map.reachability.CanReachColony(intVec))
				{
					num6++;
					continue;
				}
				if (num8 >= 30)
				{
					break;
				}
				num8++;
				IntVec3 intVec2 = FindEntryEdge(map, intVec, num15);
				if (!intVec2.IsValid)
				{
					num7++;
					array[num15]++;
					continue;
				}
				spot = intVec;
				octant = num15;
				entry = intVec2;
				return true;
			}
			if (num8 >= 30)
			{
				break;
			}
		}
		EESLog.Warning($"CScout: 잠복 지점 못 찾음 → 파견 skip. 추첨 {num9}회 " + $"[앞단] 가장자리여백 {num10} / 중심 {0.55f:P0} 안쪽 {num11} / " + $"팔분면 불일치 {num12}(정상: 1/8 표집) / 못 서는 칸 {num13} / 미개척(안개) {num14} " + $"[본검사] 홈 구역 {num3} / 실내(지붕) {num4} / 막힌 공간(맵 가장자리 미접) {num5} / " + $"콜로니 도달불가 {num6} / 도보 진입 불가 {num7} [팔분면별 {OctantTally(array)}] " + $"(진입 검사 {num8}/{30}회). " + $"맵 {map.Size.x}x{map.Size.z} @{map.Parent?.LabelCap}");
		return false;
	}

	private static IntVec3 FindEntryEdge(Map map, IntVec3 spot, int octant)
	{
		IntVec3 center = map.Center;
		if (CellFinder.TryFindRandomEdgeCellWith((IntVec3 c) => Reach(c) && OctantOf(center, c) == octant, map, CellFinder.EdgeRoadChance_Ignore, out var result))
		{
			return result;
		}
		return IntVec3.Invalid;
		bool Reach(IntVec3 c)
		{
			if (c.Standable(map) && !c.Fogged(map))
			{
				return map.reachability.CanReach(c, spot, PathEndMode.OnCell, TraverseParms.For(TraverseMode.NoPassClosedDoors, Danger.Some));
			}
			return false;
		}
	}

	private static int OctantOf(IntVec3 center, IntVec3 c)
	{
		float num = Mathf.Atan2(c.x - center.x, c.z - center.z) * 57.29578f;
		if (num < 0f)
		{
			num += 360f;
		}
		return Mathf.FloorToInt((num + 22.5f) % 360f / 45f);
	}

	private static string OctantLabel(int octant)
	{
		return EESText.T(OctantKeys[Mathf.Clamp(octant, 0, 7)]);
	}

	private static string OctantTally(int[] byOct)
	{
		string text = string.Empty;
		for (int i = 0; i < 8; i++)
		{
			if (i > 0)
			{
				text += " ";
			}
			text = text + OctantLogNames[i] + byOct[i];
		}
		return text;
	}
}
