using System.Xml;
using HarmonyLib;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(ScribeLoader), "InitLoading")]
public static class Patch_LegacySaveNodeScrub
{
	private const string LegacyGodModeWatchClass = "EvolvingEnemyStrongholds.GameComponent_EESDebugWatch";

	public static void Postfix(ScribeLoader __instance)
	{
		XmlNode xmlNode = __instance?.curXmlParent;
		if (xmlNode == null)
		{
			return;
		}
		XmlNode xmlNode2 = xmlNode.SelectSingleNode("game/components");
		if (xmlNode2 == null)
		{
			return;
		}
		int num = 0;
		for (int num2 = xmlNode2.ChildNodes.Count - 1; num2 >= 0; num2--)
		{
			XmlNode xmlNode3 = xmlNode2.ChildNodes[num2];
			XmlAttribute xmlAttribute = xmlNode3.Attributes?["Class"];
			if (xmlAttribute != null && xmlAttribute.Value == "EvolvingEnemyStrongholds.GameComponent_EESDebugWatch")
			{
				xmlNode2.RemoveChild(xmlNode3);
				num++;
			}
		}
		if (num > 0)
		{
			EESLog.Message("옛 세이브 잔존 노드 제거: EvolvingEnemyStrongholds.GameComponent_EESDebugWatch x" + num + " (구버전에서 저장된 세이브. 이 세이브를 한 번 저장하면 노드가 영구 소멸한다.)");
		}
	}
}
