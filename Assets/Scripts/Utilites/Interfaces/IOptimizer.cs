using System;
using System.Collections.Generic;
using UnityEngine;
using static CameraAlignmentManager;

public interface IOptimizer
{
    CameraPose CurrentPose { get; }
    CameraPose BestPose { get; }
    double BestScore { get; }
    bool IsConverged { get; }
    int Iteration { get; }
    CameraPose Step(double similarityScore);
    void Reset(CameraPose initialPose);
}

public abstract class OptimizerBase : IOptimizer
{
    protected CameraPose initialPose;
    protected readonly bool optimizeY;
    protected readonly float positionStep, yStep, rotationStep, scoreTolerance, parameterTolerance;
    protected readonly int maxIterations;
    protected readonly float maxX, maxY, maxZ, maxYaw;
    protected Vector4 currentQ, bestQ;
    protected CameraPose currentPose, bestPose;
    protected double currentScore, bestScore = double.NegativeInfinity;
    protected bool initialized, converged;
    protected int iteration;
    protected Vector3 localRight;
    protected Vector3 localForward;

    public CameraPose CurrentPose => currentPose;
    public CameraPose BestPose => bestPose;
    public double BestScore => bestScore;
    public bool IsConverged => converged;
    public int Iteration => iteration;

    protected OptimizerBase(CameraPose initialPose, bool optimizeY = false, float positionStep = .25f, float yStep = .05f, float rotationStep = 5f, float scoreTolerance = .001f, float parameterTolerance = .02f, int maxIterations = 50, float maxXOffset = 30f, float maxYOffset = 1f, float maxZOffset = 30f, float maxYawOffset = 30f)
    {
        this.optimizeY = optimizeY;
        this.positionStep = positionStep;
        this.yStep = yStep;
        this.rotationStep = rotationStep;
        this.scoreTolerance = scoreTolerance;
        this.parameterTolerance = parameterTolerance;
        this.maxIterations = maxIterations;
        maxX = maxXOffset / positionStep;
        maxY = maxYOffset / yStep;
        maxZ = maxZOffset / positionStep;
        maxYaw = maxYawOffset / rotationStep;
        ResetBase(initialPose);
    }

    protected void ResetBase(CameraPose initialPose)
    {
        this.initialPose = initialPose;

        // Фиксируем локальную систему координат
        localForward = initialPose.rotation * Vector3.forward;
        localForward.y = 0f;
        localForward.Normalize();

        localRight = initialPose.rotation * Vector3.right;
        localRight.y = 0f;
        localRight.Normalize();

        currentQ = bestQ = Vector4.zero;
        currentPose = bestPose = initialPose;
        currentScore = 0f;
        bestScore = double.NegativeInfinity;
        initialized = converged = false;
        iteration = 0;
    }

    protected double CleanScore(double score) => double.IsNaN(score) ? double.NegativeInfinity : score;

    protected void UpdateBest(double score)
    {
        if (score <= bestScore) return;
        bestScore = score;
        bestQ = currentQ;
        bestPose = currentPose;
    }

    protected Vector4 ClampQ(Vector4 q)
    {
        q.x = Mathf.Clamp(q.x, -maxX, maxX);
        q.z = Mathf.Clamp(q.z, -maxZ, maxZ);
        q.w = Mathf.Clamp(q.w, -maxYaw, maxYaw);
        q.y = optimizeY ? Mathf.Clamp(q.y, -maxY, maxY) : 0f;
        return q;
    }

    protected CameraPose SetQ(Vector4 q)
    {
        currentQ = ClampQ(q);

        Vector3 worldOffset =
            localRight * (currentQ.x * positionStep) +
            Vector3.up * (currentQ.y * yStep) +
            localForward * (currentQ.z * positionStep);

        Vector3 position = initialPose.position + worldOffset;

        Quaternion rotation = Quaternion.Euler(
            initialPose.rx,
            initialPose.ry + currentQ.w * rotationStep,
            initialPose.rz
        );

        currentPose = new CameraPose(position, rotation);

        return currentPose;
    }

    protected float ParameterDelta(Vector4 a, Vector4 b)
    {
        Vector4 d = a - b;
        float result = Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.z), Mathf.Abs(d.w));
        return optimizeY ? Mathf.Max(result, Mathf.Abs(d.y)) : result;
    }

    protected Vector4 Normalize(Vector4 v) => v.sqrMagnitude < 1e-8f ? Vector4.zero : v.normalized;

    public abstract CameraPose Step(double similarityScore);
    public abstract void Reset(CameraPose initialPose);
}