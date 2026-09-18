using NUnit.Framework.Internal;
using System;
using System.Collections.Generic;
using UnityEngine;
using static CameraAlignmentManager;

public class PowellCameraPoseOptimizer : OptimizerBase
{
    enum Phase { ProbePlus, ProbeMinus, Expand, Golden1, Golden2 }
    readonly List<Vector4> directions = new();
    int directionIndex;
    Vector4 cycleStartQ, lineBaseQ, lineDirection;
    double cycleStartScore, lineBaseScore, lineBestScore;
    float lineBestT, probeT;
    float a, b, x1, x2;
    double f1, f2;
    Phase phase;

    public PowellCameraPoseOptimizer(CameraPose initialPose, bool optimizeY = true, float positionStep = 0.5f, float yStep = .05f, float rotationStep = 3f, float scoreTolerance = .01f, float parameterTolerance = .2f, int maxIterations = 50, float maxXOffset = 36f, float maxYOffset = 5f, float maxZOffset = 36f, float maxYawOffset = 30f)
        : base(initialPose, optimizeY, positionStep, yStep, rotationStep, scoreTolerance, parameterTolerance, maxIterations, maxXOffset, maxYOffset, maxZOffset, maxYawOffset) { Reset(initialPose); }

    public override void Reset(CameraPose initialPose)
    {
        ResetBase(initialPose);
        directions.Clear();
        directions.Add(new Vector4(1, 0, 0, 0));
        if (optimizeY) directions.Add(new Vector4(0, 1, 0, 0));
        directions.Add(new Vector4(0, 0, 1, 0));
        directions.Add(new Vector4(0, 0, 0, 1));
        directionIndex = 0;
        phase = Phase.ProbePlus;
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
            cycleStartQ = currentQ;
            cycleStartScore = similarityScore;
            return StartLine(0);
        }

        currentScore = similarityScore;
        UpdateBest(similarityScore);

        return phase switch
        {
            Phase.ProbePlus => ProbePlus(similarityScore),
            Phase.ProbeMinus => ProbeMinus(similarityScore),
            Phase.Expand => Expand(similarityScore),
            Phase.Golden1 => Golden1(similarityScore),
            Phase.Golden2 => Golden2(similarityScore),
            _ => currentPose
        };
    }

    CameraPose StartLine(int index)
    {
        directionIndex = index;
        lineBaseQ = currentQ;
        lineBaseScore = currentScore;
        lineDirection = directions[index];
        lineBestScore = lineBaseScore;
        lineBestT = probeT = 1f;
        phase = Phase.ProbePlus;
        return SetQ(lineBaseQ + lineDirection);
    }

    CameraPose ProbePlus(double score)
    {
        if (score > lineBestScore + scoreTolerance)
        {
            lineBestScore = score;
            lineBestT = probeT;
            phase = Phase.Expand;
            probeT *= 2f;
            return SetQ(lineBaseQ + lineDirection * probeT);
        }
        phase = Phase.ProbeMinus;
        probeT = 1f;
        return SetQ(lineBaseQ - lineDirection * probeT);
    }

    CameraPose ProbeMinus(double score)
    {
        if (score > lineBestScore + scoreTolerance)
        {
            lineBestScore = score;
            lineBestT = -probeT;
            phase = Phase.Expand;
            probeT = -probeT * 2f;
            return SetQ(lineBaseQ + lineDirection * probeT);
        }
        return StartGolden(-1f, 1f);
    }

    CameraPose Expand(double score)
    {
        if (score > lineBestScore + scoreTolerance)
        {
            lineBestScore = score;
            lineBestT = probeT;
            probeT *= 2f;
            return SetQ(lineBaseQ + lineDirection * probeT);
        }
        return StartGolden(Mathf.Min(0f, probeT / 2f), Mathf.Max(0f, probeT / 2f));
    }

    CameraPose StartGolden(float left, float right)
    {
        a = left;
        b = right;
        x1 = a + .381966f * (b - a);
        x2 = b - .381966f * (b - a);
        f1 = f2 = double.NegativeInfinity;
        phase = Phase.Golden1;
        return SetQ(lineBaseQ + lineDirection * x1);
    }

    CameraPose Golden1(double score)
    {
        f1 = score;
        phase = Phase.Golden2;
        return SetQ(lineBaseQ + lineDirection * x2);
    }

    CameraPose Golden2(double score)
    {
        f2 = score;
        if (f1 > lineBestScore) { lineBestScore = f1; lineBestT = x1; }
        if (f2 > lineBestScore) { lineBestScore = f2; lineBestT = x2; }

        if (Mathf.Abs(b - a) <= parameterTolerance) return FinishLine();

        if (f1 > f2)
        {
            b = x2;
            x2 = x1;
            f2 = f1;
            x1 = a + .381966f * (b - a);
            phase = Phase.Golden1;
            return SetQ(lineBaseQ + lineDirection * x1);
        }

        a = x1;
        x1 = x2;
        f1 = f2;
        x2 = b - .381966f * (b - a);
        phase = Phase.Golden2;
        return SetQ(lineBaseQ + lineDirection * x2);
    }

    CameraPose FinishLine()
    {
        SetQ(lineBaseQ + lineDirection * lineBestT);
        currentScore = lineBestScore;
        directionIndex++;
        if (directionIndex < directions.Count) return StartLine(directionIndex);
        return FinishCycle();
    }

    CameraPose FinishCycle()
    {
        Vector4 displacement = currentQ - cycleStartQ;
        double improvement = currentScore - cycleStartScore;
        iteration++;

        if (iteration >= maxIterations || (improvement <= scoreTolerance && ParameterDelta(cycleStartQ, currentQ) <= parameterTolerance))
        {
            converged = true;
            return SetQ(bestQ);
        }

        if (displacement.sqrMagnitude > parameterTolerance * parameterTolerance)
        {
            int worst = 0;
            double maxGain = double.NegativeInfinity;
            for (int i = 0; i < directions.Count; i++)
            {
                double gain = Mathf.Abs(Vector4.Dot(displacement, directions[i]));
                if (gain > maxGain) { maxGain = gain; worst = i; }
            }
            directions[worst] = Normalize(displacement);
        }

        cycleStartQ = currentQ;
        cycleStartScore = currentScore;
        return StartLine(0);
    }
}