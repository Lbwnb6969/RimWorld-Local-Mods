using RimWorld;

namespace EvolvingEnemyStrongholds;

public static class CommanderTypeUtil
{
	public static bool AllowedForFactionTech(CommanderType t, Faction f)
	{
		TechLevel techLevel = ((f != null && f.def != null) ? f.def.techLevel : TechLevel.Undefined);
		return t switch
		{
			CommanderType.Comms => (int)techLevel >= 4,
			CommanderType.Defense => (int)techLevel >= 5,
			_ => true,
		};
	}

	public static TechLevel RequiredTech(CommanderType t)
	{
		return t switch
		{
			CommanderType.Comms => TechLevel.Industrial,
			CommanderType.Defense => TechLevel.Spacer,
			_ => TechLevel.Undefined,
		};
	}

	public static string LabelKo(this CommanderType t)
	{
		return t switch
		{
			CommanderType.None => "없음",
			CommanderType.Commander => "지휘관 (능력 없음)",
			CommanderType.Marksman => "사격 장교 (사격 오라)",
			CommanderType.Comms => "통신 장교 (증원)",
			CommanderType.Defense => "방어 장교 (이동 보호막)",
			CommanderType.Fanatic => "광신 선동가 (사망자 부활)",
			CommanderType.Security => "보안 책임자 (금고 HP×5)",
			CommanderType.Assault => "돌격대장 (이동 오라)",
			CommanderType.Engineer => "공병 지휘관 (맵 전역 덫)",
			CommanderType.Research => "연구 책임자 (사망 시 핵 드랍)",
			CommanderType.Warlord => "전쟁군주 (받는 피해 ↓)",
			CommanderType.Observer => "관측 장교 (표적 집중사격)",
			_ => t.ToString(),
		};
	}

	public static string Nickname(this CommanderType t)
	{
		return EESText.T("EES.Cmd.Type." + ((t == CommanderType.None) ? CommanderType.Commander : t));
	}
}
