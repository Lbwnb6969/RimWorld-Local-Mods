using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

[StaticConstructorOnStartup]
public static class EESTextures
{
	public static readonly Texture2D ScoutIcon = ContentFinder<Texture2D>.Get("UI/Scout", reportFailure: false) ?? ContentFinder<Texture2D>.Get("UI/Widgets/Search", reportFailure: false) ?? BaseContent.BadTex;

	public static readonly Texture2D InfiltrateIcon = ContentFinder<Texture2D>.Get("UI/Sabotage", reportFailure: false) ?? ScoutIcon;
}
