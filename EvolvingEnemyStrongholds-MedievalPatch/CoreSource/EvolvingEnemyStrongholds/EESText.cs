using Verse;

namespace EvolvingEnemyStrongholds;

public static class EESText
{
	public static string T(string key)
	{
		return key.Translate().ToString();
	}

	public static string T(string key, params NamedArgument[] args)
	{
		return key.Translate(args).ToString();
	}
}
