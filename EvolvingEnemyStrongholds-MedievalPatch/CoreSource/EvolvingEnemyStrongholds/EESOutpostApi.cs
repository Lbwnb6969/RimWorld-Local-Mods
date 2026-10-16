using System.Collections.Generic;
using System.Diagnostics;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class EESOutpostApi
{
	public static bool Available()
	{
		return WorldComponent_Strongholds.Instance != null;
	}

	public static bool IsGeneralOutpost(Settlement s)
	{
		return RejectReasonKey(s) == null;
	}

	public static string TryDowngradeTier(Settlement s, int steps)
	{
		string text = RejectReasonKey(s);
		if (text != null)
		{
			return EESText.T(text);
		}
		WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
		StrongholdData orCreate = instance.GetOrCreate(s);
		if (orCreate.tier <= 1)
		{
			return EESText.T("EES.Outpost.AtFloor", 1);
		}
		int tier = orCreate.tier;
		int num = instance.LowerGeneralTier(orCreate, s, steps);
		if (num <= 0)
		{
			EESLog.Warning($"[전초 전쟁페널티] {s.LabelCap}: 티어 하향 요청 {steps}단계가 0단계로 끝났다(현재 T{orCreate.tier}).");
			return EESText.T("EES.Outpost.AtFloor", 1);
		}
		EESLog.Message($"[전초 전쟁페널티] {s.LabelCap} [{s.Faction?.Name}]: T{tier}→T{orCreate.tier} " + $"(요청 {steps}단계 · 실제 {num}단계 · 하한 T{1}) | " + $"누적 페널티 -{orCreate.warTierPenalty} | 가속 x{0.4f} · " + $"다음 회복 {WorldComponent_Strongholds.GeneralRecoverTicks(orCreate.tier) / 60000}일 · " + $"전량 복구 {TotalRecoverDays(orCreate.tier, orCreate.warTierPenalty)}일");
		return null;
	}

	public static string TryDowngradeTierWorld(int steps)
	{
		if (!Available())
		{
			return EESText.T("EES.Outpost.Unavailable");
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		WorldComponent_Strongholds instance = WorldComponent_Strongholds.Instance;
		List<Settlement> settlements = Find.WorldObjects.Settlements;
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		int num5 = 0;
		int num6 = 0;
		int num7 = 0;
		for (int i = 0; i < settlements.Count; i++)
		{
			Settlement settlement = settlements[i];
			switch (RejectReasonKey(settlement))
			{
			case "EES.Outpost.IsPuppet":
				num4++;
				break;
			case "EES.Outpost.TierFixed":
				num6++;
				break;
			case "EES.Outpost.NotTarget":
				num5++;
				break;
			default:
				num3++;
				break;
			case null:
			{
				StrongholdData orCreate = instance.GetOrCreate(settlement);
				if (orCreate.tier <= 1)
				{
					num7++;
					break;
				}
				int num8 = instance.LowerGeneralTier(orCreate, settlement, steps);
				if (num8 <= 0)
				{
					num7++;
					break;
				}
				num++;
				num2 += num8;
				break;
			}
			}
		}
		stopwatch.Stop();
		EESLog.Message($"[전초 일괄 하향] 요청 {steps}단계 | 정착지 {settlements.Count}곳 → 적용 {num}곳(합계 {num2}단계) | " + $"건너뜀: 플레이어·비대상 {num3} · 괴뢰국 {num4} · 제외세력/외래 {num5} · " + $"고정티어 {num6} · 이미 하한 {num7} | 하한 T{1} · " + $"가속 x{0.4f} | 소요 {stopwatch.ElapsedMilliseconds}ms");
		if (num <= 0)
		{
			return EESText.T("EES.Outpost.NoTargets");
		}
		return null;
	}

	private static string RejectReasonKey(Settlement s)
	{
		if (!Available())
		{
			return "EES.Outpost.Unavailable";
		}
		if (s == null || !s.Spawned || s.Faction == null || s.Faction.IsPlayer)
		{
			return "EES.Outpost.NotTargetPlayer";
		}
		if (EESPuppetUtility.IsPuppetOutpost(s))
		{
			return "EES.Outpost.IsPuppet";
		}
		StrongholdData orCreate = WorldComponent_Strongholds.Instance.GetOrCreate(s);
		if (orCreate == null)
		{
			return "EES.Outpost.NotTarget";
		}
		if (orCreate.tierSource != "auto")
		{
			return "EES.Outpost.TierFixed";
		}
		return null;
	}

	private static int TotalRecoverDays(int tier, int penalty)
	{
		int num = 0;
		for (int i = 0; i < penalty; i++)
		{
			num += WorldComponent_Strongholds.GeneralRecoverTicks(tier + i) / 60000;
		}
		return num;
	}
}
