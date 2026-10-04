
using CesiumForUnity;
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
using static CameraAlignmentManager;
using static GlobalSearch;

public class CameraAlignmentManager : MonoBehaviour
{
    [SerializeField] public Segmentator segmentator;

    [SerializeField] private Camera ARCamera;
    [SerializeField] private Transform ARCameraOffset;
    [SerializeField] private ARCameraBackground _ARCameraBackground;
    [SerializeField] private ARCameraManager cameraManager;

    [SerializeField] private Cesium3DTileset _tileset;

    private CesiumCameraManager cesiumCameraManager;

    private bool previousUseMainCamera;
    private List<Camera> previousAdditionalCameras;

    [SerializeField] Shader buildingsAlignmentShader;
    [SerializeField] Shader groundAlignmentShader;

    [SerializeField] private int textureSize = 512;

    private Camera alignmentCamera;
    private Camera virtualSegmentationCamera;

    private Camera visualizationCamera;
    private Camera cesiumLoadingCamera;

    private RenderTexture alignmentTexture;
    private RenderTexture virtualPreSegmentationTexture;
    private RenderTexture alignmentRenderTexture;


    [SerializeField] public Texture testImage;
    private RenderTexture realCameraImage;

    private byte[] classMap;
    private byte[] groundMap;
    private byte[] buildingMap;
    private byte[] gbMap;

    private bool followAlignmentCamera = false;

    [SerializeField] private float positionSmoothTime = 0.2f;
    [SerializeField] private float rotationSpeed = 5f;

    [SerializeField] private int GROUND_LAYER = 6;
    [SerializeField] private int BUILDING_LAYER = 7;
    [SerializeField] private int RIVER_LAYER = 8;
    [SerializeField] private int ALIGNMENT_RENDERER_INDEX = 3;

    [SerializeField] private int visualizationRendererIndex;

    private float _efficiency;

    public RawImage displayRenderImage; public RawImage displayPhotoImage; public bool enableDisplay = true;

    private const int Size = 512, Pixels = Size * Size, Classes = 13;
    private Texture2D displayRenderTexture, displayPhotoTexture;
    private readonly Color32[] palette =
    {
        new(0, 0, 0, 255),
        new(255, 170, 50, 255),  // 1: GROUND
        new(150, 190, 255, 255),  // 2: SKY 
        new(255, 255, 255, 255), // 3: BUILDING 
        new(0, 0, 255, 255),     // 4: WINDOW 
        new(255, 50, 0, 255),    // 5: ROOF 
        new(240, 190, 150, 255), // 6: BUILDING_BEIGE
        new(255, 140, 140, 255),   // 7: BUILDING_RED 
        new(160, 160, 160, 255), // 8: BUILDING_GREY
        new(170, 255, 150, 255),   // 9: BUILDING_GREEN
        new(150, 150, 255, 255),  // 10: BUILDING_BLUE
        new(110,110,110, 255),    // 11: BUILDING_DARK_GREY 
        new(255, 160, 200, 255)  // 12: BUILDING_PINK 
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

    public struct PoseScore
    {
        public static List<PoseScore> bestPoses = new List<PoseScore>();

        public CameraPose pose;
        public double score;
        public double realScore;

        public PoseScore(CameraPose pose, double score)
        {
            this.pose = pose;
            this.score = score;
            this.realScore = score;
        }

        public static void ResetBestPose()
        {
            bestPoses.Clear();
        }
    }
    private PoseScore? _bestPose;

    public bool IsBestPose(PoseScore poseScore, PoseScore? bestPose, byte[] photo)
    {
        if (bestPose == null)
            bestPose = _bestPose;
        if (bestPose == null)
        {
            //Debug.Log("bestPose null");
            poseScore.realScore = SimilarityScoreEstimator.GetSimilarityScore(photo, segmentRender(false, true));
            if (_bestPose == null)
                _bestPose = poseScore;
            return true;
        }
        else if (poseScore.score > bestPose?.score)
        {
            //Debugger.Log("New best candidate: " + poseScore.pose.position.ToString() + "; " + poseScore.score);
            if (poseScore.score > (_bestPose?.score ?? double.MinValue))
            {
                poseScore.realScore = SimilarityScoreEstimator.GetSimilarityScore(photo, segmentRender(true, true));
                if (poseScore.realScore > bestPose?.realScore)
                {
                    //Debugger.Log("New best!: " + poseScore.pose.position.ToString() + "; " + poseScore.score, Debugger.MsgType.Success);
                    _bestPose = poseScore;
                    return true;
                }
            }
            else return true;
        }
        return false;
    }


    private CameraPose prevPose;
    bool hasPrevPose = false;

    public void _Reset()
    {
        hasPrevPose = false;
        prevPose = new CameraPose();
    }

    /// ### START ### ///
    void Start()
    {
        CesiumGeoreference cesiumGeoreference = FindFirstObjectByType<CesiumGeoreference>();

        if (cesiumGeoreference == null)
        {
            Debug.LogError("CesiumGeoreference not found in scene.");
            return;
        }

        cesiumCameraManager = CesiumCameraManager.GetOrCreate(cesiumGeoreference.gameObject);
        alignmentRenderTexture = CreateCameraTexture();

        alignmentTexture = CreateAlignmentTexture();

        displayPhotoTexture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        displayRenderTexture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };

        ClearDisplayImage(displayPhotoTexture);
        ClearDisplayImage(displayRenderTexture);


        buildingMap = new byte[textureSize * textureSize];
        groundMap = new byte[textureSize * textureSize];
        classMap = new byte[textureSize * textureSize];

        SpawnCameras();
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
    private RenderTexture CreateCameraTexture(int depth = 24)
    {
        float aspect = ARCamera.aspect;
        int w = 1024, h = Mathf.RoundToInt(w / aspect);
        var rt = new RenderTexture(w, h, depth, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
        rt.Create();
        return rt;
    }

    private RenderTexture CreateAlignmentTexture()
    {
        var alignmentTexture = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
        alignmentTexture.filterMode = FilterMode.Point;
        alignmentTexture.Create();
        return alignmentTexture;
    }

    
    void Update()
    {
        if (!followAlignmentCamera)
            return;

        CameraPose currentPose = _visualizationCameraPose;

        Vector3 smoothedPosition = Vector3.SmoothDamp(currentPose.position, _alignmentCameraPose.position, ref visualizationVelocity, positionSmoothTime);

        Quaternion smoothedRotation = Quaternion.Slerp(currentPose.rotation, _alignmentCameraPose.rotation, Time.deltaTime * rotationSpeed);
        visualsationCameraPose = new CameraPose(smoothedPosition, smoothedRotation);
    }

    bool _checkCesiumLoading = false;
    float _cesiumLoading = 0;
    private IEnumerator UpdateCesiumLoading()
    {
        _checkCesiumLoading = true;
        while (_checkCesiumLoading)
        {
            _cesiumLoading = _tileset.ComputeLoadProgress();
            yield return new WaitForSeconds(0.05f);
            if (_cesiumLoading == 100)
               Debugger.HideValueBar("Cesium Loading");
            else
                Debugger.DisplayValueBar("Cesium Loading", _cesiumLoading, 0, 100);
            yield return null;
        }
       // Debugger.HideValueBar("Cesium Loading");
    }
    
    private IEnumerator WaitUntilCesiumLoaded(float threahold = 60)
    {
        if (_cesiumLoading >= threahold) yield break;
        if (!_checkCesiumLoading)
        {
            StartCoroutine(UpdateCesiumLoading());
            yield return new WaitForSeconds(0.1f);
        }
        Debugger.Log("Wait until Cesium loaded...", Debugger.MsgType.Warn);
        while (_cesiumLoading < threahold) yield return new WaitForSeconds(0.05f);
    }

    // ### REFINEMENT ### //
    private IEnumerator OptimizeLocalPosition(CameraPose initialPose, byte[] photo, bool axis /* true x; false z */ , bool LookToPivotPoint = false, float max = 6f, float step = 2f)
    {

        alignmentCameraPose = initialPose;
        PoseScore bestPose = new PoseScore(initialPose, SimilarityScoreEstimator.GetSimilarityScore(photo, segmentRender(false, false)));

        var _pivot = LookToPivotPoint ? GetPivotPoint(initialPose) : null;
        if (LookToPivotPoint && _pivot == null)
            yield break;
        var pivot = _pivot ?? Vector3.zero;

        for (float x = -max; x <= max; x += step, globalIterCnt++)
        {
            yield return WaitUntilCesiumLoaded();
            var p = new CameraPose(initialPose.position + LocalToWorldOffset(initialPose, new Vector3(axis ? x : 0, 0, !axis ? x : 0)).position, initialPose.rotation);
            if (IsInsideBuilding(p.position))
                if (axis && TryGetOutFromBuilding(p, out CameraPose newP))
                    p = newP;
                else continue;

            if (LookToPivotPoint)
            {
                Vector3 dir = (pivot - p.position).normalized;
                float yRot = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                p.rotation = Quaternion.Euler(0f, yRot, 0f);
            }

            alignmentCameraPose = p;

            var currentPose = new PoseScore(p, SimilarityScoreEstimator.GetSimilarityScore(photo, segmentRender(true, false)));
            if (IsBestPose(currentPose, bestPose, photo))
                bestPose = currentPose;
            yield return null;
        }
        alignmentCameraPose = bestPose.pose;
    }
    private IEnumerator OptimizeRotation(CameraPose initialPose, byte[] photo, float maxAngle = 30f, float angleStep = 5f)
    {
        alignmentCameraPose = initialPose;
        PoseScore bestPose = new PoseScore(initialPose, SimilarityScoreEstimator.GetSimilarityScore(photo, segmentRender(false, false)));
        for (float a = -maxAngle; a <= maxAngle; a += angleStep, globalIterCnt++)
        {
            yield return WaitUntilCesiumLoaded();
            var p = new CameraPose(initialPose.position, Quaternion.Euler(0f, initialPose.ry + a, 0f));
            
            alignmentCameraPose = p;

            var currentPose = new PoseScore(p, SimilarityScoreEstimator.GetSimilarityScore(photo, segmentRender(true, false)));
            if (IsBestPose(currentPose, bestPose, photo))
                bestPose = currentPose;
            yield return null;
        }
        alignmentCameraPose = bestPose.pose;
    }

    private IEnumerator OptimizeRotationAroundPoint(CameraPose initialPose,byte[] photo, bool display = true, float maxAngle = 30f, float angleStep = 5f)
    {
        alignmentCameraPose = initialPose;
        PoseScore bestPose = new PoseScore(initialPose, SimilarityScoreEstimator.GetSimilarityScore(photo, segmentRender(false, false)));

        var _pivot = GetPivotPoint(initialPose);
        if (_pivot == null)
             yield break;
        var pivot = _pivot ?? Vector3.zero;

        for (float a = -maxAngle; a <= maxAngle; a += angleStep, globalIterCnt++)
        {
            yield return WaitUntilCesiumLoaded();
            Quaternion q = Quaternion.Euler(0f, a, 0f);
            Vector3 pos = pivot + q * (initialPose.position - pivot);
            Quaternion rot = q * initialPose.rotation;
            CameraPose p = new CameraPose(pos, rot);

            if (IsInsideBuilding(p.position))
                if (TryGetOutFromBuilding(p, out CameraPose newP))
                    p = newP;
                else continue;

            alignmentCameraPose = p;

            var currentPose = new PoseScore(p, SimilarityScoreEstimator.GetSimilarityScore(photo, segmentRender(display, false)));

            if (IsBestPose(currentPose,bestPose,photo))
               bestPose = currentPose;
            

            yield return null;
        }

        alignmentCameraPose = bestPose.pose;
    }

    private Vector3? GetPivotPoint(CameraPose pose)
    {
        Vector3 o = pose.position;
        Vector3 d = pose.rotation * Vector3.forward;

        if (!Physics.Raycast(o, d, out RaycastHit hit, 1000f, 1 << BUILDING_LAYER, QueryTriggerInteraction.Ignore))
            return null;
        return hit.point;

    }

    private IEnumerator OptimizePose(CameraPose initialPose, byte[] photo, int iterations = 50, int rec_i = 0)
    {
        if (rec_i > 3)
            yield break; // Stop infinite recursion
        IOptimizer optimizer = new HookeJeevesCameraPoseOptimizer(initialPose);

        byte[] render = new byte[512 * 512];

        int countNotChangedScore = 0, countWorseScore = 0;
        bool isDetail = false;

        for (int i = 0; globalIterCnt < 300 && !optimizer.IsConverged && i < iterations && localIterCnt < iterations && SimilarityScoreEstimator.maxScore < -2.8f; i++, localIterCnt++, globalIterCnt++)
        {
            
            isDetail = i > 50 || true;
            render = segmentRender(true, isDetail);
            double score = SimilarityScoreEstimator.GetSimilarityScore(photo, render);
            var currentPose = new PoseScore(alignmentCameraPose, score);

            if (!IsBestPose(currentPose, _bestPose, photo))
                countWorseScore++;
            else
            {
                countWorseScore = 0;
                _bestPose = currentPose;
            }

            if (score - (_bestPose?.score ?? score) < 0.01f)
            {
                countNotChangedScore++;
            }
            else
                countNotChangedScore = 0;
            if (countNotChangedScore > 15) 
                yield break;
                
            var nextPose = optimizer.Step(score);


            if (countWorseScore > 15 || IsInsideBuilding(nextPose.position))
            {
                if (_bestPose == null) yield break;
                yield return OptimizePose(_bestPose?.pose ?? initialPose, photo, 40, rec_i + 1);
                break;
            }
            else
            {
                new PoseScore(nextPose, score);
            }

            alignmentCameraPose = nextPose;
            yield return null;

        }
    }

    int localIterCnt = 0, globalIterCnt = 0;
    bool isAlignmentStarted = false;

    /// ### ALIGN ### ///
    public IEnumerator Align(Action<CameraPose> callback)
    {
        
        if (isAlignmentStarted) yield break;

        float _efficiencyStartTime = Time.time;

        UseAlignmentCameraForCesium();
        AlignmentCameraEnable();


        StartCoroutine(UpdateCesiumLoading());

        _bestPose = null;
        PoseScore.ResetBestPose();

        isAlignmentStarted = true;
        Debugger.Log("Starting alignment process...");
        SimilarityScoreEstimator._Reset();
        if (segmentator == null)
        {
            Debugger.Log("Segmentator is not assigned. Cannot run segmentation.", Debugger.MsgType.Error);
            RestoreCesiumCameras();
            AlignmentCameraDisable();
            yield break;
        }

        

        byte[] photo = segmentPhoto();

        DrawByte512x512(photo, displayPhotoTexture);

        byte[] render = new byte[512 * 512];

        Debugger.DisplayProgressBar("Localization 1/4", 0, 4);

        yield return globalPoseSearching(
                new CameraPose(basePose.position, basePose.rotation),
                photo
            );


        Debugger.Log($"Final verification completed. {PoseScore.bestPoses.Count} best poses found.", Debugger.MsgType.Info);

        globalIterCnt = 0;
        int j = -1;
        Debugger.DisplayProgressBar("Localization 2/4", () => 1 + ((j + 1) * 30 + localIterCnt) / (float)((PoseScore.bestPoses.Count + 1) * 30), 4);
        Debugger.DisplayProgressBar("Optimization", () => (j + 1) * 30 + localIterCnt, (PoseScore.bestPoses.Count + 1) * 30);

        for (; j < PoseScore.bestPoses.Count && SimilarityScoreEstimator.maxScore < -2.8f; j++)
        {
            if(j < 0)
                if (_bestPose != null)
                    alignmentCameraPose = _bestPose?.pose ?? alignmentCameraPose;
                else
                    continue;
            else
                alignmentCameraPose = PoseScore.bestPoses[j].pose;
            yield return OptimizeRotation(alignmentCameraPose, photo,85,8.5f);
            localIterCnt = 0;
            yield return OptimizePose(alignmentCameraPose, photo,30);

            if (j>=0) PoseScore.bestPoses[j] = new PoseScore(_bestPose?.pose ?? alignmentCameraPose, _bestPose?.score ?? double.MinValue);
        }

        Debugger.Log("Best poses optimized, choosed the best pose with score: " + Math.Round(SimilarityScoreEstimator.maxScore, 2) + $"; Took {Time.time - _efficiencyStartTime} seconds.", Debugger.MsgType.Info);

        Debugger.HideProgressBar("Localization 2/4");
        Debugger.HideProgressBar("Optimization");

        alignmentCameraPose = _bestPose?.pose ?? alignmentCameraPose;


        localIterCnt = 0;
        globalIterCnt = 0;

        const int refinIterCnt = 18*2/3*2 + 6*2/2 + 60*2/5 + 10*2 + 30*2/5 + 6*2/2;

        Debugger.DisplayProgressBar("Localization 3/4", () => 2 + localIterCnt/4f / 70f, 4 + refinIterCnt/4f);

        Debugger.DisplayProgressBar("Position Refinement", () => globalIterCnt, refinIterCnt);
        
        
        yield return OptimizeLocalPosition(alignmentCameraPose, photo, true, false, 18, 3);
        yield return OptimizeLocalPosition(alignmentCameraPose, photo, false);
        yield return OptimizeRotation(alignmentCameraPose, photo, 60);
        yield return OptimizeRotation(alignmentCameraPose, photo,10,1);
        yield return OptimizeRotationAroundPoint(alignmentCameraPose, photo, true, 60);
        yield return OptimizeLocalPosition(alignmentCameraPose, photo, true, true, 18, 3);
        yield return OptimizeLocalPosition(alignmentCameraPose, photo, false);


        alignmentCameraPose = _bestPose?.pose ?? alignmentCameraPose;
        localIterCnt = 0;
        globalIterCnt = 0;

        Debugger.HideProgressBar("Position Refinement");
        Debugger.HideProgressBar("Localization 3/4");
        Debugger.DisplayProgressBar("Localization 4/4", () => 3 + globalIterCnt / 70f, 4);
        Debugger.DisplayProgressBar("Final Optimization", () => globalIterCnt, 70);
        
        Debugger.Log("Pose refined. Score: " + Math.Round(SimilarityScoreEstimator.maxScore, 2) + $"; Took {Time.time - _efficiencyStartTime} seconds.", Debugger.MsgType.Info);


        yield return OptimizePose(alignmentCameraPose, photo, 70);
        alignmentCameraPose = _bestPose?.pose ?? alignmentCameraPose;
        yield return OptimizeLocalPosition(alignmentCameraPose, photo, true, true, 20, 2);

        Debugger.HideProgressBar("Localization 4/4");
        Debugger.HideProgressBar("Final Optimization");


        alignmentCameraPose = _bestPose?.pose ?? alignmentCameraPose;

        PoseScore.ResetBestPose();
        _bestPose = null;

        while (alignmentCameraPose.ComparePose(_visualizationCameraPose) > 0.01f)
        {
            yield return null;
        }


        RestoreCesiumCameras();
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


        _efficiency = Time.time - _efficiencyStartTime;
        Debugger.Log($"Alignment completed. Similarity Score: {Math.Round(SimilarityScoreEstimator.maxScore,2)}; Took {_efficiency} seconds.", Debugger.MsgType.Success);

        _checkCesiumLoading = false;

        SimilarityScoreEstimator._Reset();
        ClearDisplayImage(displayPhotoTexture);
        ClearDisplayImage(displayRenderTexture);
        isAlignmentStarted = false;  
        callback(new CameraPose(newOffset, Quaternion.Euler(0, rotationOffset, 0)));
        PoseScore.ResetBestPose();
    }


    /// ### GLOBAL POSE SEARCHING ### ///

    private IEnumerator EvaluateGlobalSearchNode(GlobalSearchNode node, GlobalSearchContext context, bool refined, byte[] photoSeg)
    {
        if (!context.BudgetAvailable) yield break;
        float[] rotations = refined ? new[] { -80f, -40f, 0f, 40f, 80f } : new[] { -45f, 0f, 45f };
        PoseScore? bestPose = null;

        foreach (float rotationOffset in rotations)
        {
            if (!context.BudgetAvailable && SimilarityScoreEstimator.maxScore > -4) yield break;
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

            double score = SimilarityScoreEstimator.GetSimilarityScore(context.photoSeg, segmentRender(true, false));

            var currentPoseScore = new PoseScore(candidatePose, score);
            context.RegisterEvaluation(false);

            if (IsBestPose(currentPoseScore, bestPose, photoSeg)) { bestPose = currentPoseScore; }

            yield return null;
        }

        if (bestPose == null) yield break;

        node.score = bestPose?.score ?? 0;
        node.bestPose = bestPose?.pose ?? alignmentCameraPose;

        float gpsWeight = (float)GlobalSearch.CalculateGpsWeight(node.Radius, context.gpsPeakRadius, context.gpsSigma);
        node.searchPriority = node.score * Mathf.Lerp(1f - context.gpsPriorInfluence, 1f, gpsWeight);
    }

    private IEnumerator RecursiveGlobalSearch(List<GlobalSearchNode> nodes, GlobalSearchContext context, int depth, byte[] photoSeg)
    {

        if (!context.BudgetAvailable || nodes == null || nodes.Count == 0 || depth > context.maxDepth || SimilarityScoreEstimator.maxScore > -3f) yield break;
        
        bool refined = depth == 0;
        double scoreBefore = context.bestScore;

        foreach (var node in nodes)
        {
            if (!context.BudgetAvailable && SimilarityScoreEstimator.maxScore > -4) yield break;
            yield return EvaluateGlobalSearchNode(node, context, refined, photoSeg);
        }

        var validNodes = nodes.Where(n => double.IsFinite(n.score)).OrderByDescending(n => n.searchPriority).ToList();


        if (validNodes.Count == 0) yield break;

        double improvement = double.IsNegativeInfinity(scoreBefore) ? double.PositiveInfinity : context.bestScore - scoreBefore;
        if (!double.IsNegativeInfinity(scoreBefore) && improvement < context.minLevelImprovement)
            context.stagnationLevels++;
        else
            context.stagnationLevels = 0;

        if (context.stagnationLevels >= context.maxStagnationLevels) yield break;
        
        var branches = validNodes.Take(context.beamWidth).ToList();
        var parents = new List<(GlobalSearchNode parent,List<GlobalSearchNode> children)>();

        foreach (var parent in branches)
            if (ShouldRefineNode(parent, context))
                parents.Add((parent, parent.Split()));

        if (parents.Count == 0) yield break;

        foreach (var parent in parents)
        {
            if (parent.children.Count > 0) {
                yield return RecursiveGlobalSearch(parent.children, context, depth + 1, photoSeg);
                var bestChild = parent.children.OrderByDescending(n => n.score).ToList()[0];
                if (bestChild.score > parent.parent.score)
                {
                    parent.parent.score = bestChild.score;
                    parent.parent.bestPose = bestChild.bestPose;
                }

            }
        }

    }

    IEnumerator FinalGlobalPosesVerification (GlobalSearchContext context, List<GlobalSearchNode> bestNodes)
    {
        bestNodes.Sort((a, b) => b.score.CompareTo(a.score));
        double max_score = bestNodes[0].score;

        for (int i = 0; i < bestNodes.Count && bestNodes.Count >= context.finalCandidateCount; i++)
        {
            var p = new PoseScore(bestNodes[i].bestPose, bestNodes[i].score);

            if (bestNodes[i].score <= max_score - Math.Abs(max_score) || PoseScore.bestPoses.Any(x => Vector3.Distance(x.pose.position, p.pose.position) < 10f || Quaternion.Angle(x.pose.rotation, p.pose.rotation) < 29f))
                continue;

            PoseScore.bestPoses.Add(p);
            yield return null;
        }
    }

    IEnumerator globalPoseSearching(CameraPose initialPose, byte[] photoSeg, float max_pos_offset = 18f, float max_rot_offset = 60f, float rot_step = 15f)
    {
        PoseScore.ResetBestPose();
        var context = new GlobalSearchContext
        {
            initialPose = initialPose,
            photoSeg = photoSeg,
            maxEvaluations = 150,
            beamWidth = 3,
            finalCandidateCount = 3,
            gpsPeakRadius = 9f,
            gpsSigma = 3.5f,
            gpsPriorInfluence = 0.25f,
            minCellRadialSize = 3f,
            minCellArcLength = 3f,
            maxDepth = 2,
            minLevelImprovement = 0.005,
            maxStagnationLevels = 1
        };


        Debugger.DisplayProgressBar("Localization 1/4", () => (float)context.similarityEvaluations / (float)context.maxEvaluations, 4);

        Debugger.DisplayProgressBar("Global Search", () => context.similarityEvaluations, context.maxEvaluations);


        List<GlobalSearchNode> rootNodes = CreateGlobalSearchRootNodes();

        yield return RecursiveGlobalSearch(rootNodes, context, 0, photoSeg);


        Debugger.HideProgressBar("Global Search");
        Debugger.HideProgressBar("Localization 1/4");

        yield return FinalGlobalPosesVerification(context, rootNodes.OrderByDescending(n => n.score).ToList());

        yield return null;

    }

    public bool IsInsideBuilding(Vector3 position, float checkHeight = 200f)
    {
        Vector3 rayOrigin = position + Vector3.up * checkHeight;
        
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, checkHeight * 1.5f, (1 << BUILDING_LAYER), QueryTriggerInteraction.Ignore))
        {
            return hit.point.y > position.y;
        } 
        else if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hitRiver, checkHeight * 1.5f, (1 << RIVER_LAYER), QueryTriggerInteraction.Ignore))
        {
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hitGround, checkHeight * 1.5f, (1 << GROUND_LAYER), QueryTriggerInteraction.Ignore))
                return hitRiver.point.y > hitGround.point.y;
        }

        return false;
    }
    public bool TryGetOutFromBuilding(CameraPose cameraPosition, out CameraPose newPosition)
    {
        newPosition = cameraPosition;

        Vector3 cameraForward = cameraPosition.rotation * Vector3.forward;

        Vector3 rayOrigin = cameraPosition.position + cameraForward.normalized * 20f;
        Vector3 directionToCamera = -cameraForward.normalized;


        if (Physics.Raycast(rayOrigin, directionToCamera, out RaycastHit hit, 20f, 1 << BUILDING_LAYER, QueryTriggerInteraction.Ignore))
        {
            newPosition = new CameraPose(hit.point - directionToCamera * 4f, cameraPosition.rotation);
            return true; //IsInsideBuilding(newPosition.position);
        }

        return false;
    }

    /// ### CAMERAS ### ///
    void SetVisualisationCamerasPosition()
    {
        visualizationCamera.transform.SetPositionAndRotation(visualsationCameraPose.position, visualsationCameraPose.rotation);
        cesiumLoadingCamera.transform.SetPositionAndRotation(visualsationCameraPose.position, visualsationCameraPose.rotation);
    }
    void SetAlignmentCamerasPosition()
    {
        virtualSegmentationCamera.transform.SetPositionAndRotation(alignmentCameraPose.position, alignmentCameraPose.rotation);
        alignmentCamera.transform.SetPositionAndRotation(alignmentCameraPose.position, alignmentCameraPose.rotation);
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


    private byte[] ReadTextureToMap(RenderTexture texture)
    {
        RenderTexture.active = texture;
        Texture2D tex = new Texture2D(textureSize, textureSize, TextureFormat.R8, false, true);
        tex.ReadPixels(new Rect(0, 0, textureSize, textureSize), 0, 0);
        tex.Apply();
        byte[] result = tex.GetRawTextureData<byte>().ToArray();
        RenderTexture.active = null;
        Destroy(tex);
        return result;
    }

    public double getSimilarity()
    {

        AlignmentCameraEnable();

        byte[] photo = segmentPhoto();
        byte[] render = segmentRender(true, true);

        AlignmentCameraDisable();
        return SimilarityScoreEstimator.GetSimilarityScore(photo, render);
    }


    public void ClearDisplayImage(Texture2D texture)
    {
        if (!enableDisplay || texture == null) return;

        var pixels = new Color32[Pixels];
        Array.Fill(pixels, new Color32(0, 0, 0, 0));

        texture.SetPixels32(pixels);
        texture.Apply();
        SimilarityScoreEstimator._Reset();
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
#if UNITY_ANDROID && !UNITY_EDITOR
        int mask = ARCamera.cullingMask;
        ARCamera.cullingMask = 0;
        var result = segmentImage(ARCamera);
        ARCamera.cullingMask = mask;
        return result;
#endif
        if (testImage == null)
        {
            Debugger.LogError("Test Image in the CameraAlignmentManager is null");
        }
        return segmentator.RunSegmentation(testImage);
    }
    private byte[] segmentRender(bool display = false, bool useSegmentation = false)
    {
        alignmentCamera.Render();
        Graphics.Blit(alignmentRenderTexture, alignmentTexture);

        byte[] map = ReadTextureToMap(alignmentTexture);

        if (useSegmentation)
            classMap = segmentImage(virtualSegmentationCamera);
        for (int i = 0; i < classMap.Length; i++)
        {
            if (map[i] == 128)
                classMap[i] = 1;

            else if (map[i] == 2)
                if (useSegmentation)
                    if (classMap[i] != 2)
                        continue;
                    else
                    {
                        classMap[i] = 3;
                    }
                else
                    classMap[i] = 3;
            else
                classMap[i] = 2;
        }
        DrawByte512x512(classMap, displayRenderTexture);
        return classMap;
    }

    private void SpawnCameras()
    {
        CreateCesiumLoadingCamera();
        visualizationCamera = SpawnCamera("VisualizationCamera");
        virtualSegmentationCamera = SpawnCamera("VirtualSegmentationCamera");

        alignmentCamera = SpawnCamera("AlignmentCamera");


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

        var rendererData = alignmentCamera.GetUniversalAdditionalCameraData();

        rendererData.SetRenderer(ALIGNMENT_RENDERER_INDEX);

        alignmentCamera.cullingMask = (1 << GROUND_LAYER) | (1 << BUILDING_LAYER);
        alignmentCamera.clearFlags = CameraClearFlags.SolidColor;
        alignmentCamera.backgroundColor = UnityEngine.Color.black;
        alignmentCamera.targetTexture = alignmentRenderTexture;
        alignmentCamera.aspect = ARCamera.aspect;
        alignmentCamera.enabled = false;



        virtualSegmentationCamera.targetTexture = virtualPreSegmentationTexture;

        virtualSegmentationCamera.aspect = ARCamera.aspect;

    }


    private Camera SpawnCamera(string name)
    {
        GameObject obj = new GameObject(name);
        Camera cam = obj.AddComponent<Camera>();
        cam.CopyFrom(ARCamera);
        cam.enabled = false;
        cam.nearClipPlane = 0.03f;
        cam.farClipPlane = 150f;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.depthTextureMode = DepthTextureMode.None;
        return cam;
    }

    private void CreateCesiumLoadingCamera()
    {
        GameObject obj = new GameObject("CesiumLoadingCamera");
        cesiumLoadingCamera = obj.AddComponent<Camera>();
        cesiumLoadingCamera.fieldOfView = 130f;
        cesiumLoadingCamera.nearClipPlane = 0.03f;
        cesiumLoadingCamera.farClipPlane = 300f;
        cesiumLoadingCamera.enabled = false;
        cesiumLoadingCamera.cullingMask = 0;
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

    private void UseAlignmentCameraForCesium()
    {
        previousUseMainCamera = cesiumCameraManager.useMainCamera;
        previousAdditionalCameras = new List<Camera>(cesiumCameraManager.additionalCameras);
        cesiumCameraManager.useMainCamera = false;
        cesiumCameraManager.additionalCameras.Clear();
        cesiumCameraManager.additionalCameras.Add(cesiumLoadingCamera);
    }

    private void RestoreCesiumCameras()
    {
        cesiumCameraManager.useMainCamera = previousUseMainCamera;

        cesiumCameraManager.additionalCameras.Clear();
        cesiumCameraManager.additionalCameras.AddRange(previousAdditionalCameras);
    }

    public static CameraPose LocalToWorldOffset(CameraPose referencePose, Vector3 localOffset)
    {
        Vector3 forward = referencePose.rotation * Vector3.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 right = referencePose.rotation * Vector3.right;
        right.y = 0f;
        right.Normalize();
        return new CameraPose(right * localOffset.x + Vector3.up * localOffset.y + forward * localOffset.z, referencePose.rotation);
    }
    public Texture2D AcquireCameraTexture(XRCpuImage image)
    {
        try
        {
            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = new Vector2Int(image.width, image.height),
                outputFormat = TextureFormat.RGB24,
                transformation = XRCpuImage.Transformation.None
            };

            int dataSize = image.GetConvertedDataSize(conversionParams);

            NativeArray<byte> buffer = new NativeArray<byte>(dataSize, Allocator.Temp);

            image.Convert(conversionParams, buffer);

            Texture2D sourceTexture = new Texture2D(image.width, image.height, TextureFormat.RGB24, false);

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
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        Graphics.Blit(source, rt);
        RenderTexture.active = rt;
        
        Texture2D result = new Texture2D(width, height, TextureFormat.RGB24, false);
        result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        result.Apply(false, false);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        return result;
    }
}
