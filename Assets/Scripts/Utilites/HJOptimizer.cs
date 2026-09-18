using NUnit.Framework.Internal;
using System;
using UnityEngine;
using static CameraAlignmentManager;

public class HookeJeevesCameraPoseOptimizer : OptimizerBase
{
    enum Phase { TryPlus, TryMinus, Pattern }
    Phase phase;
    Vector4 step, baseQ, exploreQ, patternQ;
    double baseScore, exploreScore;
    int dimensionIndex;
    readonly float stepReduction;

    public HookeJeevesCameraPoseOptimizer(CameraPose initialPose, bool optimizeY = true, float positionStep = .25f, float yStep = .05f, float rotationStep = 5f, float scoreTolerance = .001f, float parameterTolerance = .02f, int maxIterations  = 100, float maxXOffset = 30f, float maxYOffset =10f, float maxZOffset = 30f, float maxYawOffset = 90f, float stepReduction = .5f)
        : base(initialPose, optimizeY, positionStep, yStep, rotationStep, scoreTolerance, parameterTolerance, maxIterations, maxXOffset, maxYOffset, maxZOffset, maxYawOffset)
    {
        if (stepReduction <= 0f || stepReduction >= 1f) throw new ArgumentOutOfRangeException(nameof(stepReduction));
        this.stepReduction = stepReduction;
        Reset(initialPose);
    }

    public override void Reset(CameraPose initialPose)
    {
        ResetBase(initialPose);
        step = new Vector4(1, optimizeY ? 1 : 0, 1, 1);
        baseQ = exploreQ = patternQ = Vector4.zero;
        dimensionIndex = 0;
        phase = Phase.TryPlus;
    }

    public override CameraPose Step(double similarityScore)
    {
        similarityScore = CleanScore(similarityScore);
        if (converged) return SetQ(bestQ);

        if (!initialized)
        {
            initialized = true;
            currentScore = bestScore = similarityScore;
            bestQ = currentQ;
            bestPose = currentPose;
            baseQ = exploreQ = currentQ;
            baseScore = exploreScore = similarityScore;
            dimensionIndex = 0;
            return StartPlus();
        }

        currentScore = similarityScore;
        UpdateBest(similarityScore);

        return phase switch
        {
            Phase.TryPlus => HandlePlus(similarityScore),
            Phase.TryMinus => HandleMinus(similarityScore),
            Phase.Pattern => HandlePattern(similarityScore),
            _ => currentPose
        };
    }

    Vector4 DimensionStep() => dimensionIndex switch
    {
        0 => new Vector4(step.x, 0, 0, 0),
        1 => new Vector4(0, step.y, 0, 0),
        2 => new Vector4(0, 0, step.z, 0),
        3 => new Vector4(0, 0, 0, step.w),
        _ => Vector4.zero
    };

    int DimensionCount => optimizeY ? 4 : 3;

    CameraPose StartPlus()
    {
        phase = Phase.TryPlus;
        return SetQ(exploreQ + DimensionStep());
    }

    CameraPose HandlePlus(double score)
    {
        Vector4 d = DimensionStep();
        if (score > exploreScore + scoreTolerance)
        {
            exploreQ = currentQ;
            exploreScore = score;
            return NextDimension();
        }
        phase = Phase.TryMinus;
        return SetQ(exploreQ - d);
    }

    CameraPose HandleMinus(double score)
    {
        if (score > exploreScore + scoreTolerance)
        {
            exploreQ = currentQ;
            exploreScore = score;
        }
        return NextDimension();
    }

    CameraPose NextDimension()
    {
        dimensionIndex++;
        return dimensionIndex < DimensionCount ? StartPlus() : FinishExploration();
    }

    CameraPose FinishExploration()
    {
        iteration++;

        if (exploreScore > baseScore + scoreTolerance)
        {
            Vector4 movement = exploreQ - baseQ;
            baseQ = exploreQ;
            baseScore = exploreScore;
            patternQ = ClampQ(baseQ + movement);
            phase = Phase.Pattern;
            return SetQ(patternQ);
        }

        step *= stepReduction;
        if (!optimizeY) step.y = 0f;

        float maxStep = Mathf.Max(Mathf.Abs(step.x), Mathf.Abs(step.z), Mathf.Abs(step.w));
        if (optimizeY) maxStep = Mathf.Max(maxStep, Mathf.Abs(step.y));

        if (iteration >= maxIterations || maxStep <= parameterTolerance)
        {
            converged = true;
            return SetQ(bestQ);
        }

        exploreQ = baseQ;
        exploreScore = baseScore;
        dimensionIndex = 0;
        return StartPlus();
    }

    CameraPose HandlePattern(double score)
    {
        if (score > baseScore + scoreTolerance)
        {
            exploreQ = currentQ;
            exploreScore = score;
            baseQ = exploreQ;
            baseScore = exploreScore;
        }
        else
        {
            exploreQ = baseQ;
            exploreScore = baseScore;
        }

        dimensionIndex = 0;
        return StartPlus();
    }
}