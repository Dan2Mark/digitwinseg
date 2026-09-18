
using NUnit.Framework.Internal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.VisualScripting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SocialPlatforms.Impl;
using UnityEngine.UI;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

using static GlobalSearch;

public class CameraAlignmentManager : MonoBehaviour
{
    [SerializeField] public Segmentator segmentator;

    [SerializeField] private Camera ARCamera;
    [SerializeField] private Transform ARCameraOffset;
    [SerializeField] private ARCameraBackground _ARCameraBackground;
    [SerializeField] private ARCameraManager cameraManager;

    [SerializeField] Shader buildingsAlignmentShader;
    [SerializeField] Shader groundAlignmentShader;

    [SerializeField] private int textureSize = 512;

    private Camera groundAlignmentCamera;
    private Camera buildingAlignmentCamera;
    private Camera virtualSegmentationCamera;

    private Camera visualizationCamera;

    private RenderTexture groundAlignmentTexture;
    private RenderTexture buildingAlignmentTexture;
    private RenderTexture virtualPreSegmentationTexture;
    private RenderTexture groundRenderTexture, buildingRenderTexture;



    private RenderTexture realCameraImage;

    private byte[] classMap;
    private byte[] groundMap;
    private byte[] buildingMap;

    private bool followAlignmentCamera = false;

    [Header("Visualization Smooth")]
    [SerializeField] private float positionSmoothTime = 0.2f;
    [SerializeField] private float rotationSpeed = 5f;

    [SerializeField] private int GROUND_LAYER = 6;
    [SerializeField] private int BUILDING_LAYER = 7;

    [Header("URP Settings")]
    [SerializeField] private int visualizationRendererIndex;

    [Header("Display")] public RawImage displayRenderImage; public RawImage displayPhotoImage; public bool enableDisplay = true;

    private const int Size = 512, Pixels = Size * Size, Classes = 7;
    private Texture2D displayRenderTexture, displayPhotoTexture;
    private readonly Color32[] palette =
    {
        new(70,190,255,255), new(255,170,50,255), new(255,255,255,255),
        new(255,0,0,255), new(0,50,255,255), new(255,0,255,255),
        new(0,0,0,255)
    };

    private Vector3 visualizationVelocity;

    public struct CameraPose
    {
        public Vector3 position;
        public Quaternion rotation;
        public CameraPose(Vector3 position, Quaternion rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }
        public Vector3 rotationToVector()
        {
            return new Vector3(rx, ry, rz);
        }
        public void setPosition(Vector3 position) => this.position = position;
        public void setRotation(float yRotation) => this.rotation = Quaternion.Euler(rx, yRotation, rz);

        public float ComparePose(CameraPose pose) => Mathf.Sqrt(Mathf.Pow(px - pose.px, 2) + Mathf.Pow(pz - pose.pz, 2)) + Mathf.Abs(pose.ry - ry) / 10;
        public float rx { get { return rotation.eulerAngles.x; } }
        public float ry { get { return rotation.eulerAngles.y; } }
        public float rz { get { return rotation.eulerAngles.z; } }
        public float px { get { return position.x; } }
        public float py { get { return position.y; } }

        public float pz { get { return position.z; } }

        public double[] ToVector4D()
        {
            return new double[] { px, py, pz, ry };
        }

        public static CameraPose FromVector4D(double[] v, float rxFixed = 0f, float rzFixed = 0f)
        {
            if (v.Length != 4)
            {
                Debugger.Log($"FromVector4D: Input array has {v.Length} instead of 4 elements.", Debugger.MsgType.Error);
                return new CameraPose(Vector3.zero, Quaternion.identity);
            }
            Vector3 pos = new Vector3((float)v[0], (float)v[1], (float)v[2]);
            Quaternion rot = Quaternion.Euler(rxFixed, (float)v[3], rzFixed);
            return new CameraPose(pos, rot);
        }
    }
    private CameraPose visualsationCameraPose { get { return _visualizationCameraPose; } set { _visualizationCameraPose = value; SetVisualisationCamerasPosition(); } }
    private CameraPose alignmentCameraPose { get { return _alignmentCameraPose; } set { _alignmentCameraPose = value; SetAlignmentCamerasPosition(); } }
    private CameraPose _alignmentCameraPose = new();
    private CameraPose _visualizationCameraPose = new();
    private CameraPose basePose = new();

    void Start()
    {
        Directory.CreateDirectory(Path.Combine(Application.persistentDataPath, $"ModelPresegmentation"));
        groundAlignmentTexture = CreateAlignmentTexture();
        buildingAlignmentTexture = CreateAlignmentTexture();
        groundRenderTexture = CreateCameraTexture();
        buildingRenderTexture = CreateCameraTexture();
        groundAlignmentTexture = CreateAlignmentTexture();
        buildingAlignmentTexture = CreateAlignmentTexture();

        displayPhotoTexture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        displayRenderTexture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

        ClearDisplayImage(displayPhotoTexture);
        ClearDisplayImage(displayRenderTexture);

        var alignmentTexture = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32);
        alignmentTexture.Create();


        buildingMap = new byte[textureSize * textureSize];
        groundMap = new byte[textureSize * textureSize];
        classMap = new byte[textureSize * textureSize];

        SpawnCameras();
        //cameraManager.frameReceived += OnCameraFrameReceived;
        realCameraImage = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        realCameraImage.Create();


        displayPhotoImage.texture = displayPhotoTexture;
        displayPhotoImage.enabled = true;
        displayRenderImage.texture = displayRenderTexture;
        displayRenderImage.enabled = true;

    }
    private void OnDestroy()
    {
        if (displayPhotoTexture != null) Destroy(displayPhotoTexture);
        if (displayRenderTexture != null) Destroy(displayRenderTexture);
    }
    private RenderTexture CreateCameraTexture()
    {
        float aspect = ARCamera.aspect;
        int w = 1024, h = Mathf.RoundToInt(w / aspect);
        var rt = new RenderTexture(w, h, 0, RenderTextureFormat.R8);
        rt.Create();
        return rt;
    }
    private RenderTexture CreateAlignmentTexture()
    {
        var alignmentTexture = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.R8);
        alignmentTexture.filterMode = FilterMode.Point;
        alignmentTexture.Create();
        return alignmentTexture;
    }

    void Update()
    {
        if (!followAlignmentCamera)
            return;

        CameraPose currentPose = _visualizationCameraPose;

        Vector3 smoothedPosition = Vector3.SmoothDamp(
            currentPose.position,
            _alignmentCameraPose.position,
            ref visualizationVelocity,
            positionSmoothTime
        );

        Quaternion smoothedRotation = Quaternion.Slerp(
            currentPose.rotation,
            _alignmentCameraPose.rotation,
            Time.deltaTime * rotationSpeed
        );
        visualsationCameraPose = new CameraPose(smoothedPosition, smoothedRotation);
    }
    void SetVisualisationCamerasPosition()
    {
        visualizationCamera.transform.SetPositionAndRotation(visualsationCameraPose.position, visualsationCameraPose.rotation);
    }
    void SetAlignmentCamerasPosition()
    {
        groundAlignmentCamera.transform.SetPositionAndRotation(alignmentCameraPose.position, alignmentCameraPose.rotation);
        buildingAlignmentCamera.transform.SetPositionAndRotation(alignmentCameraPose.position, alignmentCameraPose.rotation);
        virtualSegmentationCamera.transform.SetPositionAndRotation(alignmentCameraPose.position, alignmentCameraPose.rotation);

    }



    private byte[] ReadTextureToMap(RenderTexture texture)
    {
        RenderTexture.active = texture;
        Texture2D tex = new Texture2D(textureSize, textureSize, TextureFormat.R8, false);
        tex.ReadPixels(new Rect(0, 0, textureSize, textureSize), 0, 0);
        tex.Apply();
        byte[] result = tex.GetRawTextureData<byte>().ToArray();
        RenderTexture.active = null;
        Destroy(tex);
        return result;
    }


    private CameraPose prevPose;
    bool hasPrevPose = false;

    public IEnumerator Align(Action<CameraPose> callback)
    {
        Debugger.Log("Starting alignment process...");

        if (segmentator == null)
        {
            Debugger.Log("Segmentator is not assigned. Cannot run segmentation.", Debugger.MsgType.Error);
            yield break;
        }

        AlignmentCameraEnable();


        byte[] photo = segmentPhoto();

        DrawByte512x512(photo, displayPhotoTexture);

        byte[] render = new byte[512 * 512];

        yield return globalPoseSearching(
                new CameraPose(basePose.position, basePose.rotation),
                photo
            );


        int countNotChangedScore = 0;
        int countWorseScore = 0;

        var prevPoseScore = PoseScore.bestPose;

        IOptimizer optimizer = new HookeJeevesCameraPoseOptimizer(alignmentCameraPose);
        for (int i = 0; !optimizer.IsConverged && i < 70; i++)
        {

            render = segmentRender(false, i > 40);
            double score = SimilarityScoreEstimator.GetSimilarityScore(photo, render);
            if (score - PoseScore.bestPose?.score < 0.01f)
            {
                countNotChangedScore++;
            }
            else
                countNotChangedScore = 0;
            if (countNotChangedScore > 20)
                break;
            if (score < PoseScore.bestPose?.score) 
                countWorseScore++;
            else
                countWorseScore = 0;

            var nextPose = optimizer.Step(score);

            if (countWorseScore > 15 && IsInsideBuilding(nextPose.position))
            {
                nextPose = PoseScore.bestPose?.pose ?? nextPose;
                optimizer.Reset(nextPose);
            }
            else
            {
                new PoseScore(nextPose, score, photo, this);
            }

            alignmentCameraPose = nextPose;
            
            Debugger.DisplayVar("Similarity", score.ToString() + $" {i}");
            yield return null;

        }
        alignmentCameraPose = PoseScore.bestPose?.pose ?? alignmentCameraPose;
        PoseScore.bestPoses.Clear();
        

        while (alignmentCameraPose.ComparePose(_visualizationCameraPose) > 0.01f)
        {
            yield return null;
        }


        AlignmentCameraDisable();

        Vector3 newOffset = _alignmentCameraPose.position - basePose.position;
        float rotationOffset = _alignmentCameraPose.ry - basePose.ry;

        if (hasPrevPose)
        {
            rotationOffset += prevPose.ry;
            newOffset = newOffset + prevPose.position;
        }

        hasPrevPose = true;
        prevPose = new CameraPose(newOffset, Quaternion.Euler(0, rotationOffset, 0));
        ClearDisplayImage(displayPhotoTexture);
        ClearDisplayImage(displayRenderTexture);
        callback(new CameraPose(
            newOffset,
            Quaternion.Euler(0, rotationOffset, 0)
        ));
        PoseScore.ResetBestPose();
    }

    void DrawByte512x512(byte[] pixels, Texture2D texture)
    {
        if (!enableDisplay)
            return;
        Color32[] displayPixels = enableDisplay ? new Color32[Pixels] : null;
        for (int y = 0; y < Size; y++)
        {
            int srcY = Size - 1 - y;

            for (int x = 0; x < Size; x++)
            {
                int srcIndex = srcY * Size + x;
                int dstIndex = y * Size + x;
                if (enableDisplay)
                    displayPixels[dstIndex] = palette[pixels[dstIndex]];
            }
        }

        texture.SetPixels32(displayPixels);
        texture.Apply();
    }


    public struct PoseScore
    {
        public static PoseScore? bestPose;
        public static List<PoseScore> bestPoses = new List<PoseScore>();

        public CameraPose pose;
        public double score;
        public double realScore;
        public bool isBest;

        public PoseScore(CameraPose pose, double score, byte[] photo, CameraAlignmentManager cam)
        {
            this.pose = pose;
            this.score = score;
            this.realScore = score;
            this.isBest = false;
            if (bestPose == null)
            {
                this.realScore = SimilarityScoreEstimator.GetSimilarityScore(photo, cam.segmentRender(false, true));
                bestPose = this;
            }
            else if (this.score > bestPose?.score)
            {
                this.realScore = SimilarityScoreEstimator.GetSimilarityScore(photo, cam.segmentRender(false, true));
                if (this.realScore > bestPose?.realScore)
                {
                    bestPose = this;
                    isBest = true;
                }
            }
        }

        public static void ResetBestPose()
        {
            bestPose = null;
            bestPoses.Clear();
        }
    }
    private IEnumerator EvaluateGlobalSearchNodeCheap(GlobalSearchNode node, GlobalSearchContext context, bool refined, byte[] photoSeg)
    {
        if (!context.BudgetAvailable) yield break;
        float[] rotations = refined ? new[] { -30f, -15f, 0f, 15f, 30f } : new[] { -30f, 0f, 30f };
        double bestScore = double.NegativeInfinity;
        CameraPose bestPose = default;

        foreach (float rotationOffset in rotations)
        {
            if (!context.BudgetAvailable) yield break;
            float radius = node.isCenter ? 0f : node.Radius;
            float angle = node.isCenter ? 0f : node.Angle;
            float angleRad = angle * Mathf.Deg2Rad;
            float dx = Mathf.Cos(angleRad) * radius;
            float dz = Mathf.Sin(angleRad) * radius;

            var candidatePose = new CameraPose(
                new Vector3(context.initialPose.px + dx, context.initialPose.py, context.initialPose.pz + dz),
                Quaternion.Euler(context.initialPose.rx, context.initialPose.ry + rotationOffset, context.initialPose.rz)
            );

            if (IsInsideBuilding(candidatePose.position)) continue;

            alignmentCameraPose = candidatePose;

            double score = SimilarityScoreEstimator.GetSimilarityScore(
                context.photoSeg,
                segmentRender(false, false),
                true
            );

            context.RegisterEvaluation(false);
            context.UpdateProgress("Global Searching");

            if (score > bestScore) { bestScore = score; bestPose = candidatePose; }
            context.RegisterResult(candidatePose, score, photoSeg, this);

            yield return null;
        }

        if (double.IsNegativeInfinity(bestScore)) yield break;

        node.score = bestScore;
        node.bestPose = bestPose;

        float gpsWeight = (float)GlobalSearch.CalculateGpsWeight(node.Radius, context.gpsPeakRadius, context.gpsSigma);
        node.searchPriority = node.score * Mathf.Lerp(1f - context.gpsPriorInfluence, 1f, gpsWeight);
    }

    private IEnumerator EvaluateGlobalSearchNodeAccurate(GlobalSearchNode node, GlobalSearchContext context, byte[] photoSeg)
    {
        if (!context.BudgetAvailable) yield break;
        if (double.IsNegativeInfinity(node.score)) yield break;

        CameraPose pose = node.bestPose;
        if (IsInsideBuilding(pose.position)) yield break;

        alignmentCameraPose = pose;

        double accurateScore = SimilarityScoreEstimator.GetSimilarityScore(
            context.photoSeg,
            segmentRender(true, true),
            true
        );

        context.RegisterEvaluation(true);
        context.UpdateProgress("Global Searching");

        if (accurateScore > node.score) { node.score = accurateScore; node.bestPose = pose; }

        float gpsWeight = (float)CalculateGpsWeight(node.Radius, context.gpsPeakRadius, context.gpsSigma);
        node.searchPriority = node.score * Mathf.Lerp(1f - context.gpsPriorInfluence, 1f, gpsWeight);

        context.RegisterResult(node.bestPose, node.score, photoSeg, this);
        yield return null;
    }

    private IEnumerator RecursiveGlobalSearch(List<GlobalSearchNode> nodes, GlobalSearchContext context, int depth, byte[] photoSeg)
    {
        if (!context.BudgetAvailable || nodes == null || nodes.Count == 0 || depth > context.maxDepth) yield break;

        bool refined = depth > 0;
        double scoreBefore = context.bestScore;

        foreach (var node in nodes)
        {
            if (!context.BudgetAvailable) yield break;
            yield return EvaluateGlobalSearchNodeCheap(node, context, refined, photoSeg);
        }

        var validNodes = nodes.Where(n => double.IsFinite(n.score)).OrderByDescending(n => n.searchPriority).ToList();
        if (validNodes.Count == 0) yield break;

        int accurateCount = Mathf.Min(context.beamWidth, validNodes.Count);
        for (int i = 0; i < accurateCount; i++)
        {
            if (!context.BudgetAvailable) yield break;
            yield return EvaluateGlobalSearchNodeAccurate(validNodes[i], context, photoSeg);
        }

        validNodes.Sort((a, b) => b.searchPriority.CompareTo(a.searchPriority));

        double improvement = double.IsNegativeInfinity(scoreBefore) ? double.PositiveInfinity : context.bestScore - scoreBefore;
        if (!double.IsNegativeInfinity(scoreBefore) && improvement < context.minLevelImprovement)
            context.stagnationLevels++;
        else
            context.stagnationLevels = 0;

        if (context.stagnationLevels >= context.maxStagnationLevels) yield break;

        var branches = validNodes.Take(context.beamWidth).ToList();
        var children = new List<GlobalSearchNode>();

        foreach (var parent in branches)
            if (ShouldRefineNode(parent, context))
                children.AddRange(parent.Split());

        if (children.Count == 0) yield break;

        yield return RecursiveGlobalSearch(children, context, depth + 1, photoSeg);
    }

    private IEnumerator FinalGlobalPoseVerification(GlobalSearchContext context)
    {
        if (context.topCandidates.Count == 0) yield break;
        var candidates = context.topCandidates.OrderByDescending(p => p.score).Take(context.finalCandidateCount).ToList();
        double bestFinalScore = double.NegativeInfinity;
        CameraPose bestFinalPose = context.bestPose;

        foreach (var candidate in candidates)
        {
            if (!context.BudgetAvailable) break;
            alignmentCameraPose = candidate.pose;

            double score = SimilarityScoreEstimator.GetSimilarityScore(context.photoSeg, segmentRender(true, true), true);
            context.evaluations++;
            Debugger.DisplayProgressBar("Global Verification", context.evaluations, context.maxEvaluations);

            if (score > bestFinalScore) { bestFinalScore = score; bestFinalPose = candidate.pose; }
            yield return null;
        }
        Debugger.HideProgressBar("Global Verification");
        if (!double.IsNegativeInfinity(bestFinalScore))
        { context.bestScore = bestFinalScore; context.bestPose = bestFinalPose; }
    }

    IEnumerator globalPoseSearching(CameraPose initialPose, byte[] photoSeg, float max_pos_offset = 18f, float max_rot_offset = 30f, float rot_step = 15f)
    {
        PoseScore.ResetBestPose();
        var context = new GlobalSearchContext
        {
            initialPose = initialPose,
            photoSeg = photoSeg,
            maxEvaluations = 500,
            beamWidth = 3,
            finalCandidateCount = 5,
            gpsPeakRadius = 9f,
            gpsSigma = 3.5f,
            gpsPriorInfluence = 0.25f,
            minCellRadialSize = 3f,
            minCellArcLength = 3f,
            maxDepth = 2,
            minLevelImprovement = 0.005,
            maxStagnationLevels = 1
        };

        List<GlobalSearchNode> rootNodes = CreateGlobalSearchRootNodes();
        Debugger.Log($"GLOBAL SEARCH: root nodes = {rootNodes.Count}");

        yield return RecursiveGlobalSearch(rootNodes, context, 0, photoSeg);
        yield return FinalGlobalPoseVerification(context);

        PoseScore.bestPose = new PoseScore(context.bestPose, context.bestScore, photoSeg, this);
        PoseScore.bestPoses = context.topCandidates.OrderByDescending(p => p.score).Take(5).ToList();
        alignmentCameraPose = context.bestPose;

        Debugger.Log($"GLOBAL SEARCH DONE: score={context.bestScore:F4}, evaluations={context.evaluations}");
        yield return null;
    }

    /*
    IEnumerator globalPoseSearching(CameraPose initialPose, byte[] photoSeg, float max_pos_offset = 9, float pos_step = 3, float max_rot_offset = 26, float rot_step = 13)
    {
        PoseScore.bestPoses.Clear();
        PoseScore bestPoseScore = new PoseScore(initialPose, double.NegativeInfinity);

        int xCount = Mathf.CeilToInt(2 * max_pos_offset / pos_step);
        int total = xCount;
        int current = 0;

        for (float x = -max_pos_offset; x < max_pos_offset; x += pos_step)
        {
            current++;
            Debugger.DisplayProgressBar("Global Searching", current, total);

            for (float z = -max_pos_offset; z < max_pos_offset; z += pos_step)
            {
                for (float r = -max_rot_offset; r < max_rot_offset; r += rot_step)
                {
                    var newCameraPose = new CameraPose(
                        new Vector3(initialPose.px + x, initialPose.py, initialPose.pz + z),
                        Quaternion.Euler(initialPose.rx, initialPose.ry + r, initialPose.rz)
                    );

                    if (IsInsideBuilding(newCameraPose.position)) continue;

                    alignmentCameraPose = newCameraPose;

                    double score = SimilarityScoreEstimator.GetSimilarityScore(
                        photoSeg,
                        segmentRender(r == 0),
                        true
                    );

                    bestPoseScore = bestPoseScore.getBest(
                        new PoseScore(alignmentCameraPose, score)
                    );

                    yield return null;
                }
            }
        }

        bestPoseScore.NormalizeBestPoses();

        double bestScore = double.NegativeInfinity;

        foreach (var pose in PoseScore.bestPoses)
        {
            alignmentCameraPose = pose.pose;
            double score = SimilarityScoreEstimator.GetSimilarityScore(
                photoSeg,
                segmentRender(true, true),
                true
            );

            if (score > bestScore)
            {
                bestScore = score;
                bestPoseScore = new PoseScore(pose.pose, score);
            }

            yield return null;
        }

        PoseScore.bestPoses.Clear();
        alignmentCameraPose = bestPoseScore.pose;
    }
    */
    bool IsInsideBuilding(Vector3 position)
    {
        if (!Physics.Raycast(position, Vector3.up, out RaycastHit hit, 100f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return false;

        Debugger.Log($"RAY HIT: {hit.collider.name} layer={hit.collider.gameObject.layer}");
        return true;
    }
    public void ClearDisplayImage(Texture2D texture)
    {
        if (!enableDisplay || texture == null) return;

        var pixels = new Color32[Pixels];
        Array.Fill(pixels, new Color32(0, 0, 0, 0));

        texture.SetPixels32(pixels);
        texture.Apply();
    }

    public void testSegmentation()
    {
        DrawByte512x512(segmentPhoto(), displayPhotoTexture);
    }
    public void ClearDisplayImages()
    {
        ClearDisplayImage(displayPhotoTexture);
        ClearDisplayImage(displayRenderTexture);
    }

    int cMask = 0;
    bool disabled = false;
    public void ToggleCityVisualisation()
    {
        if (disabled)
            ARCamera.cullingMask = cMask;
        else
        {
            cMask = ARCamera.cullingMask;
            ARCamera.cullingMask = 0;
        }
        disabled = !disabled;
    }

    private byte[] segmentImage(Camera camera)
    {
        if (ARCamera != null)
        {
            try
            {
                var prevTexture = camera.targetTexture;
                camera.targetTexture = realCameraImage;
                camera.Render();
                camera.targetTexture = prevTexture;
                return segmentator.RunSegmentation(realCameraImage);
            }
            catch (System.Exception e)
            {
                Debugger.Log($"CPU IMAGE: error getting format - {e.Message}", Debugger.MsgType.Error);
                return null;
            }

        }
        else
        {
            Debugger.Log("AR Camera is null.", Debugger.MsgType.Error);
            return null;
        }
    }

    private byte[] segmentPhoto()
    {
        int mask = ARCamera.cullingMask;
        ARCamera.cullingMask = 0;
        var result = segmentImage(ARCamera);
        ARCamera.cullingMask = mask;
        return result;
    }
    private byte[] segmentRender(bool display = false, bool useSegmentation = false)
    {
        buildingAlignmentCamera.Render();
        groundAlignmentCamera.Render();
        Graphics.Blit(buildingRenderTexture, buildingAlignmentTexture);
        Graphics.Blit(groundRenderTexture, groundAlignmentTexture);

        buildingMap = ReadTextureToMap(buildingAlignmentTexture);
        groundMap = ReadTextureToMap(groundAlignmentTexture);
        if (useSegmentation)
            classMap = segmentImage(virtualSegmentationCamera);
        for (int i = 0; i < classMap.Length; i++)
        {
            if (buildingMap[i] > 0)
                if (useSegmentation)
                    switch (classMap[i])
                    {
                        case 2: case 3: case 4: continue;
                        default: classMap[i] = 2; break;
                    }
                else
                    classMap[i] = 2;
            else if (groundMap[i] > 0)
                classMap[i] = 1;

            else
                classMap[i] = 0;
        }
        DrawByte512x512(classMap, displayRenderTexture);
        return classMap;
    }

    private void SpawnCameras()
    {
        buildingAlignmentCamera = SpawnCamera("BuildingAlignmentCamera");
        groundAlignmentCamera = SpawnCamera("GroundAlignmentCamera");
        visualizationCamera = SpawnCamera("VisualizationCamera");
        virtualSegmentationCamera = SpawnCamera("VirtualSegmentationCamera");

        groundAlignmentCamera.SetReplacementShader(groundAlignmentShader, "");
        buildingAlignmentCamera.SetReplacementShader(buildingsAlignmentShader, "");

        visualizationCamera.GetUniversalAdditionalCameraData().SetRenderer(1);
        visualizationCamera.backgroundColor = new UnityEngine.Color(0.53f, 0.81f, 0.92f);
        visualizationCamera.clearFlags = CameraClearFlags.SolidColor;
        visualizationCamera.depth = 1;
        visualizationCamera.enabled = false;

        virtualSegmentationCamera.GetUniversalAdditionalCameraData().SetRenderer(2);
        virtualSegmentationCamera.backgroundColor = new UnityEngine.Color(0.5f, 0.5f, 0.5f);
        virtualSegmentationCamera.clearFlags = CameraClearFlags.SolidColor;
        virtualSegmentationCamera.depth = 1;
        virtualSegmentationCamera.enabled = false;


        groundAlignmentCamera.backgroundColor = UnityEngine.Color.black;
        groundAlignmentCamera.clearFlags = CameraClearFlags.SolidColor;
        buildingAlignmentCamera.backgroundColor = UnityEngine.Color.black;
        buildingAlignmentCamera.clearFlags = CameraClearFlags.SolidColor;

        virtualSegmentationCamera.targetTexture = virtualPreSegmentationTexture;

        groundAlignmentCamera.cullingMask = 1 << GROUND_LAYER;
        buildingAlignmentCamera.cullingMask = 1 << BUILDING_LAYER;

        groundAlignmentCamera.targetTexture = groundRenderTexture;
        buildingAlignmentCamera.targetTexture = buildingRenderTexture;
        groundAlignmentCamera.aspect = ARCamera.aspect;
        buildingAlignmentCamera.aspect = ARCamera.aspect;
        virtualSegmentationCamera.aspect = ARCamera.aspect;

    }


    private Camera SpawnCamera(string name)
    {
        GameObject obj = new GameObject(name);
        Camera cam = obj.AddComponent<Camera>();
        cam.CopyFrom(ARCamera);
        cam.enabled = false;
        cam.nearClipPlane = 0.03f;
        cam.farClipPlane = 20000f;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.depthTextureMode = DepthTextureMode.None;
        return cam;
    }


    private CameraPose GetNewCameraPosition() =>
        new CameraPose(
           ARCamera.transform.position,
            Quaternion.Euler(ARCamera.transform.rotation.eulerAngles.x, ARCamera.transform.rotation.eulerAngles.y, ARCamera.transform.rotation.eulerAngles.z)
        );


    private void AlignmentCameraEnable()
    {
        basePose = GetNewCameraPosition();
        alignmentCameraPose = basePose;
        visualsationCameraPose = basePose;
        /*
                Debugger.DisplayVector("ACamPos", () => alignmentCameraPose.position);
                Debugger.DisplayVector("ACamRot", () => alignmentCameraPose.rotationToVector());
                Debugger.DisplayVector("VCamPos", () => visualsationCameraPose.position);
                Debugger.DisplayVector("VCamRot", () => alignmentCameraPose.rotationToVector());
        */

        followAlignmentCamera = true;
        visualizationCamera.enabled = true;
        ARCamera.enabled = false;
        ARCamera.depth = 0;
    }
    private void AlignmentCameraDisable()
    {
        followAlignmentCamera = false;
        visualizationCamera.enabled = false;
        ARCamera.enabled = true;

        ARCamera.depth = 1;
    }

    public void SavePreSegmentation()
    {

        alignmentCameraPose = GetNewCameraPosition();
        virtualSegmentationCamera.Render();

        RenderTexture prevActive = RenderTexture.active;
        RenderTexture.active = virtualPreSegmentationTexture;

        Texture2D result = new Texture2D(textureSize, textureSize, TextureFormat.RGB24, false);
        result.ReadPixels(new Rect(0, 0, textureSize, textureSize), 0, 0);
        result.Apply();

        RenderTexture.active = prevActive;
        string path = Path.Combine(Application.persistentDataPath, $"ModelPresegmentation/SegFrame_{System.DateTime.Now:yyyyMMdd_HHmmss}.png");

        File.WriteAllBytes(path, result.EncodeToPNG());

        Destroy(result);
        Debugger.Log($"Pre-segmentation reneder saved to {path}");
    }
    public Texture2D AcquireCameraTexture(XRCpuImage image)
    {
        try
        {
            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(
                    0,
                    0,
                    image.width,
                    image.height
                ),

                outputDimensions = new Vector2Int(
                    image.width,
                    image.height
                ),

                outputFormat = TextureFormat.RGB24,

                transformation = XRCpuImage.Transformation.None
            };

            int dataSize = image.GetConvertedDataSize(conversionParams);

            NativeArray<byte> buffer =
                new NativeArray<byte>(dataSize, Allocator.Temp);

            image.Convert(conversionParams, buffer);

            Texture2D sourceTexture = new Texture2D(
                image.width,
                image.height,
                TextureFormat.RGB24,
                false
            );

            sourceTexture.LoadRawTextureData(buffer);
            sourceTexture.Apply(false, false);

            buffer.Dispose();

            Texture2D result = ResizeTexture(sourceTexture, 512, 512);

            Destroy(sourceTexture);

            return result;
        }
        finally
        {
            image.Dispose();
        }
    }
    private Texture2D ResizeTexture(Texture source, int width, int height)
    {
        RenderTexture rt = RenderTexture.GetTemporary(
            width,
            height,
            0,
            RenderTextureFormat.ARGB32
        );

        RenderTexture previous = RenderTexture.active;

        Graphics.Blit(source, rt);

        RenderTexture.active = rt;

        Texture2D result = new Texture2D(
            width,
            height,
            TextureFormat.RGB24,
            false
        );

        result.ReadPixels(
            new Rect(0, 0, width, height),
            0,
            0
        );

        result.Apply(false, false);

        RenderTexture.active = previous;

        RenderTexture.ReleaseTemporary(rt);

        return result;
    }
}
