using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Local.CastleWallsCompatibility
{
    public sealed class StatPart_CastleWallStoneDurability : StatPart
    {
        private static readonly HashSet<string> CastleWallDefs = new HashSet<string>
        {
            "JDS_WallsTwo",
            "JDS_WallsTwo_Embrasure",
            "JDS_Walls",
            "JDS_Walls_Embrasure",
            "Castle_Walls_Corner_Embrasure",
            "JDS_Walls_End",
            "JDS_Walls_End_Embrasure",
            "JDS_Walls_L_connector",
            "JDS_Walls_T_connector",
            "JDS_Wall_X_connector",
            "JDS_Tower",
            "JDS_Tower_connector",
            "JDS_Tower_I_connector",
            "JDS_Tower_T_connector",
            "JDS_Tower_L_connector",
            "JDS_Walls_Dia_connector",
            "JDS_Walls_Dia_connector_Embrasure",
            "JDS_Walls_Dia_corner",
            "JDS_Walls_Dia_corner_Embrasure",
            "JDS_Walls_T_connector_Embrasure",
            "JDS_Wall_X_connector_Embrasure"
        };

        private static readonly Dictionary<string, float> CastleWallStoneFactors =
            new Dictionary<string, float>
            {
                { "BlocksSlate", 2500f / 1380f },
                { "BlocksLimestone", 2300f / 1380f },
                { "BlocksGranite", 2100f / 1380f },
                { "BlocksSandstone", 1800f / 1380f },
                { "BlocksMarble", 1500f / 1380f }
            };

        public override void TransformValue(StatRequest req, ref float val)
        {
            ThingDef buildableDef = req.BuildableDef as ThingDef;
            ThingDef stuffDef = req.StuffDef;
            if (buildableDef == null || stuffDef == null ||
                !CastleWallDefs.Contains(buildableDef.defName) ||
                !CastleWallStoneFactors.TryGetValue(stuffDef.defName, out float castleFactor))
            {
                return;
            }

            float existingStuffFactor = 1f;
            List<StatModifier> factors = stuffDef.stuffProps?.statFactors;
            if (factors != null)
            {
                for (int i = 0; i < factors.Count; i++)
                {
                    if (factors[i].stat?.defName == "MaxHitPoints")
                    {
                        existingStuffFactor = factors[i].value;
                        break;
                    }
                }
            }

            if (Math.Abs(existingStuffFactor) > 0.0001f)
            {
                val = val / existingStuffFactor * castleFactor;
            }
        }

        public override string ExplanationPart(StatRequest req)
        {
            return null;
        }
    }
}
