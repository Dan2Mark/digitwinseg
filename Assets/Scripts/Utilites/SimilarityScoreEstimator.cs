using System;
using UnityEngine;

public class SimilarityScoreEstimator : MonoBehaviour
{
    private static readonly float[,] WeightMatrix = new float[7, 7]
    {
        // sky   ground  build   roof    window  stairs  other
        {  5f,    -5f,    -5f,   -5f,     -5f,    -5f,     0f }, // sky (0)
        { -5f,     5f,    -5f,   -5f,     -5f,     5f,     0f }, // ground (1)
        { -5f,    -5f,     5f,    3f,      3f,     2f,     0f }, // buildings (2)
        { -5f,    -5f,     3f,    5f,      3f,    -5f,     0f }, // roof (3)
        { -5f,    -5f,     3f,    3f,      5f,    -5f,     0f }, // window (4)
        { -5f,     5f,     2f,   -5f,     -5f,     5f,     0f }, // stairs (5)
        {  0f,     0f,     0f,    0f,      0f,     0f,     0f }  // other (6)
    };

    public static double GetSimilarityScore(byte[] img1, byte[] img2, bool normalize = false)
    {
        if (img1 == null || img2 == null)
        {
            Debugger.Log("img1 or img2 is null.", Debugger.MsgType.Error);
            return 0f;
        }
        if (img1.Length != img2.Length)
            Debugger.Log($"img1 length ({img1.Length}) isa not equal to img2 length ({img2.Length})", Debugger.MsgType.Error);

        long totalScore = 0;
        int length = img1.Length;

        for (int i = 0; i < length; i++)
        {
            byte class1 = img1[i];
            byte class2 = img2[i];
            if (class1 < 7 && class2 < 7)
            {
                totalScore += (int)WeightMatrix[class1, class2];
            }
        }

        double averageScore = (double)totalScore / length;

        return normalize ? averageScore / 5.0f : averageScore;
    }

}
