using Verse;

namespace EvolvingEnemyStrongholds;

public static class EESLog
{
	private const string Prefix = "[EES] ";

	public static bool Verbose
	{
		get
		{
			if (StrongholdMod.Settings != null)
			{
				return StrongholdMod.Settings.debugLogging;
			}
			return false;
		}
	}

	public static void Message(string msg)
	{
		if (Verbose)
		{
			Log.Message("[EES] " + msg);
		}
	}

	public static void Warning(string msg)
	{
		if (Verbose)
		{
			Log.Warning("[EES] " + msg);
		}
	}

	public static void Error(string msg)
	{
		Log.Error("[EES] " + msg);
	}
}
