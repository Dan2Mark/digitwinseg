using System;
using UnityEngine;

using UnityEngine;

public class SimilarityScoreEstimator : MonoBehaviour
{
    public static double maxScore = double.MinValue;
    private const int ClassCount = 13;

    // 0: OTHER
    // 1: GROUND
    // 2: SKY
    // 3: BUILDING
    // 4: WINDOW
    // 5: ROOF
    // 6: BUILDING_BEIDGE
    // 7: BUILDING_RED
    // 8: BUILDING_GREY
    // 9: BUILDING_GREEN
    // 10: BUILDING_BLUE
    // 11: BUILDING_DARK_GREY
    // 12: BUILDING_PINK

    private static readonly float[,] WeightMatrix = new float[ClassCount, ClassCount]
    {
    //               OTH    GND    SKY    BLD    WIN    ROF    BEI    RED    GRY    GRN    BLU    DGR    PNK
    /* 0: OTH */ {   10f,    0f,  0f,     0f,    0f,    0f,    0f,    0f,    0f,    0f,    0f,    0f,    0f },
    /* 1: GND */ {   0f,    0f,  -80f,   -80f,  -80f,  -80f,  -80f,  -80f,  -80f,  -80f,  -80f,  -80f,  -80f },
    /* 2: SKY */ {   0f,  -80f,   -1f,    -80f,  -80f,  -80f,  -80f,  -80f,  -80f,  -80f,  -80f,  -80f,  -80f },
    /* 3: BLD */ {   0f,  -80f,  -80f,    10f,   0f,    0f,    0f,    0f,    0f,    0f,    0f,    0f,    0f },
    /* 4: WIN */ {   0f,  -80f,  -80f,    0f,    10f,   0f,    0f,    0f,    0f,    0f,    0f,    0f,    0f },
    /* 5: ROF */ {   0f,  -80f,  -80f,    0f,    0f,    10f,   0f,    0f,    0f,    0f,    0f,    0f,    0f },
    /* 6: BEI */ {   0f,  -80f,  -80f,    0f,    0f,    0f,    10f,   1f,    1f,   -5f,   -5f,   -5f,    1f },
    /* 7: RED */ {   0f,  -80f,  -80f,    0f,    0f,    0f,    1f,   10f,    0f,   -5f,   -5f,    0f,    1f },
    /* 8: GRY */ {   0f,  -80f,  -80f,    0f,    0f,    0f,    1f,    0f,    10f,   0f,    0f,    0f,    1f },
    /* 9: GRN */ {   0f,  -80f,  -80f,    0f,    0f,    0f,   -5f,   -5f,    0f,    10f,  -5f,   -5f,   -5f },
    /* 10:BLU */ {   0f,  -80f,  -80f,    0f,    0f,    0f,   -5f,   -5f,    0f,   -5f,    10f,  -5f,   -5f },
    /* 11:DGR */ {   0f,  -80f,  -80f,    0f,    0f,    0f,   -5f,    1f,    0f,   -5f,   -5f,    10f,  -5f },
    /* 12:PNK */ {   0f,  -80f,  -80f,    0f,    0f,    0f,    1f,    0f,    1f,   -5f,   -5f,   -5f,   10f }
    };

    public static double GetSimilarityScore(byte[] img1, byte[] img2, bool normalize = false)
    {
        int pxSkyCntImg1 = 0, pxSkyCntImg2 = 0;

        int[] classes = new int[img1.Length];
        if (img1 == null || img2 == null)
        {
            Debugger.Log("img1 or img2 is null.", Debugger.MsgType.Error);
            return 0f;
        }
        if (img1.Length != img2.Length)
        {
            Debugger.Log($"img1 length ({img1.Length}) is not equal to img2 length ({img2.Length})", Debugger.MsgType.Error);
            return 0f;
        }

        long totalScore = 0;
        int length = img1.Length;

        for (int i = 0; i < length; i++)
        {
            byte class1 = img1[i];
            byte class2 = img2[i];

            if (class1 < ClassCount && class2 < ClassCount)
            {
                totalScore += (int)WeightMatrix[class1, class2];
            }
            if (class1 == 2 || (class1 == 0 && class2 == 2)) pxSkyCntImg1++;
            if (class2 == 2 || (class2 == 0 && class1 == 2)) pxSkyCntImg2++;
        }

        double averageScore = (double)totalScore / length;

        double skySimilarity = - Math.Abs(pxSkyCntImg1 - pxSkyCntImg2) / ((pxSkyCntImg1 + pxSkyCntImg2) / 2f) * 10f;
        
        var outScore = normalize ? ((averageScore + skySimilarity) + 90) / 100.0f : (averageScore + skySimilarity);
        Debugger.DisplayValueBar("Similarity", (float)outScore, normalize ? 0 : -90, normalize ? 1 : 10);
        
        if (outScore > maxScore)
        {
            maxScore = outScore;
            Debugger.DisplayValueBar("Max Similarity", (float)maxScore, normalize ? 0 : -90, normalize ? 1 : 10);
        }

        return outScore;
    }
    public static void _Reset()
    {
        maxScore = double.MinValue;
        Debugger.HideValueBar("Similarity");
        Debugger.HideValueBar("Max Similarity");
    }
}