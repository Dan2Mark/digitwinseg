using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static CameraAlignmentManager;

public class GlobalSearch : MonoBehaviour
{
    public class GlobalSearchNode
    {
        public float rMin, rMax, angleMin, angleMax;
        public int depth;
        public double score = double.NegativeInfinity, searchPriority = double.NegativeInfinity;
        public CameraPose bestPose;
        public bool isCenter;

        public GlobalSearchNode(float rMin, float rMax, float angleMin, float angleMax, int depth = 0, bool isCenter = false)
        { this.rMin = rMin; this.rMax = rMax; this.angleMin = angleMin; this.angleMax = angleMax; this.depth = depth; this.isCenter = isCenter; }

        public float Radius => (rMin + rMax) * 0.5f;
        public float Angle => (angleMin + angleMax) * 0.5f;
        public float RadialSize => rMax - rMin;
        public float AngularSize => angleMax - angleMin;
        public float ArcLength => Radius * (AngularSize * Mathf.Deg2Rad);

        public List<GlobalSearchNode> Split()
        {
            float rMid = (rMin + rMax) * 0.5f, aMid = (angleMin + angleMax) * 0.5f;
            return new List<GlobalSearchNode>
        {
            new GlobalSearchNode(rMin,rMid,angleMin,aMid,depth+1),
            new GlobalSearchNode(rMin,rMid,aMid,angleMax,depth+1),
            new GlobalSearchNode(rMid,rMax,angleMin,aMid,depth+1),
            new GlobalSearchNode(rMid,rMax,aMid,angleMax,depth+1)
        };
        }
    }

    public class GlobalSearchContext
    {
        public int stagnationLevels;
        public int evaluations;
        public CameraPose initialPose;
        public byte[] photoSeg;

        public int similarityEvaluations;
        public int cheapEvaluations;
        public int accurateEvaluations;

        public int maxEvaluations = 500;

        public int beamWidth = 3;
        public int finalCandidateCount = 5;

        public int maxDepth = 2;

        public float gpsPeakRadius = 9f;
        public float gpsSigma = 3.5f;
        public float gpsPriorInfluence = 0.25f;

        public float minCellRadialSize = 3f;
        public float minCellArcLength = 3f;

        public double minLevelImprovement = 0.005;
        public int maxStagnationLevels = 1;

        public double bestScore = double.NegativeInfinity;
        public CameraPose bestPose;

        public int progressWork;
        public int progressTotalWork;

        // Один accurate evaluation примерно во столько раз
        // тяжелее cheap evaluation для ProgressBar.
        public int accurateWorkWeight = 8;

        public readonly List<PoseScore> topCandidates =
            new List<PoseScore>();

        public bool BudgetAvailable =>
            similarityEvaluations < maxEvaluations;


        public void RegisterEvaluation(bool accurate)
        {
            similarityEvaluations++;

            if (accurate)
            {
                accurateEvaluations++;
                progressWork += accurateWorkWeight;
            }
            else
            {
                cheapEvaluations++;
                progressWork++;
            }
        }


        public void UpdateProgress(string title)
        {
            int total = Mathf.Max(
                1,
                progressTotalWork
            );

            int current = Mathf.Min(
                progressWork,
                total
            );

            Debugger.DisplayProgressBar(
                title,
                current,
                total
            );
        }


        public void RegisterResult(
            CameraPose pose,
            double score,
            byte[] photoSeg,
            CameraAlignmentManager cam)
        {
            if (!double.IsFinite(score))
                return;

            if (score > bestScore)
            {
                bestScore = score;
                bestPose = pose;
            }

            topCandidates.Add(
                new PoseScore(
                    pose,
                    score,
                    photoSeg,
                    cam
                )
            );

            topCandidates.Sort(
                (a, b) =>
                    b.score.CompareTo(a.score)
            );

            int maxCandidates =
                finalCandidateCount * 3;

            if (topCandidates.Count > maxCandidates)
            {
                topCandidates.RemoveRange(
                    maxCandidates,
                    topCandidates.Count - maxCandidates
                );
            }
        }
    }
    public static double CalculateGpsWeight(float radius, float peakRadius, float sigma)
    {
        if (sigma <= 0f) return 1.0;
        float diff = radius - peakRadius;
        return Mathf.Exp(-0.5f * (diff * diff) / (sigma * sigma));
    }

    public static List<GlobalSearchNode> CreateGlobalSearchRootNodes()
    {
        const float INNER_MAX = 6f, MAIN_MIN = 6f, MAIN_MAX = 12f, OUTER_MIN = 12f, OUTER_MAX = 18f;
        const int SECTORS = 8; const float SECTOR_SIZE = 360f / SECTORS;
        var result = new List<GlobalSearchNode>();
        result.Add(new GlobalSearchNode(0f, 0f, 0f, 360f, 0, true));

        for (int i = 0; i < SECTORS; i++)
        {
            float a0 = i * SECTOR_SIZE, a1 = a0 + SECTOR_SIZE;
            result.Add(new GlobalSearchNode(0f, INNER_MAX, a0, a1));
            result.Add(new GlobalSearchNode(MAIN_MIN, MAIN_MAX, a0, a1));
            result.Add(new GlobalSearchNode(OUTER_MIN, OUTER_MAX, a0, a1));
        }
        return result;
    }

    public static bool ShouldRefineNode(GlobalSearchNode node, GlobalSearchContext context)
    {
        if (node.isCenter) return false;
        if (node.depth >= context.maxDepth) return false;
        if (node.RadialSize <= context.minCellRadialSize) return false;
        if (node.ArcLength <= context.minCellArcLength) return false;
        return true;
    }
}
