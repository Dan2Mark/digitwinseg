
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.VisualScripting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

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

    RenderTexture groundAlignmentTexture;
    RenderTexture buildingAlignmentTexture;
    RenderTexture virtualPreSegmentationTexture;


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
        public void setRotation(float yRotation) => this.rotation = Quaternion.Euler(rx,yRotation, rz);

        public float ComparePose(CameraPose pose) => Mathf.Sqrt(Mathf.Pow(px - pose.px,2) + Mathf.Pow(pz - pose.pz, 2)) + Mathf.Abs(pose.ry - ry)/10;
        public float rx { get { return rotation.eulerAngles.x; } }
        public float ry { get { return rotation.eulerAngles.y; } }
        public float rz { get { return rotation.eulerAngles.z; } }  
        public float px { get { return position.x; }}
        public float py { get { return position.y; }}

        public float pz { get { return position.z; } }

        public double[] ToVector4D()
        {
            return new double[] { px, py, pz, ry };
        }

        public static CameraPose FromVector4D(double[] v, float rxFixed = 0f, float rzFixed = 0f)
        {
            if (v.Length != 4){
                Debugger.Log($"FromVector4D: Input array has {v.Length} instead of 4 elements.",Debugger.MsgType.Error);
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

        var alignmentTexture = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32);
        alignmentTexture.Create();



        buildingMap = new byte[textureSize * textureSize];
        groundMap = new byte[textureSize * textureSize];
        classMap = new byte[textureSize * textureSize];

        SpawnCameras();
        //cameraManager.frameReceived += OnCameraFrameReceived;
        realCameraImage = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        realCameraImage.Create();
    }
    /*
    private Matrix4x4 cameraDisplayMatrix;

    private void OnCameraFrameReceived(
        ARCameraFrameEventArgs args)
    {
        if (args.displayMatrix.HasValue && args.displayMatrix != cameraDisplayMatrix)
        {
            Debugger.Log(Screen.orientation.ToString());
            cameraDisplayMatrix = args.displayMatrix.Value;

            Debugger.Log(
                $"DISPLAY MATRIX: {cameraDisplayMatrix}"
            );
        }
    }
    */
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

    private void ReadClassMap()
    {
        for (int i = 0; i < classMap.Length; i++)
        {
            if (buildingMap[i] > 0)
                classMap[i] = 2;

            else if (groundMap[i] > 0)
                classMap[i] = 1;

            else
                classMap[i] = 0;
        }
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

    public byte[] GetClassMap()
    {
        Debugger.Log($"Getting class map");
        buildingAlignmentCamera.Render();
        groundAlignmentCamera.Render();
        buildingMap = ReadTextureToMap(buildingAlignmentTexture);
        groundMap = ReadTextureToMap(groundAlignmentTexture);
        ReadClassMap();
        //ClassMapToString(classMap);
       // Debugger.Log(ClassMapToString(classMap));
        return classMap;
    }

    private CameraPose prevPose;
    bool hasPrevPose = false;

    public IEnumerator Align(System.Action<CameraPose> callback)
    {
        Debugger.Log($"CPU IMAGE API: {cameraManager.subsystem.subsystemDescriptor.supportsCameraImage}");
        if (segmentator == null)
        {
            Debugger.Log("Segmentator is not assigned. Cannot run segmentation.", Debugger.MsgType.Error);
            yield break;
        }
        if (_ARCameraBackground == null || _ARCameraBackground.material == null)
        {
            Debugger.Log("AR Camera background or its material is not assigned. Cannot run segmentation.", Debugger.MsgType.Error);
            yield break;
        }

        Debugger.Log("Starting alignment process...");
        if (_ARCameraBackground != null && _ARCameraBackground.material != null)
        {
            Debugger.Log("Try to get texture from camera");
            try 
            {
                ARCamera.targetTexture = realCameraImage;
                ARCamera.Render();
                Debugger.Log("Try to run segmentation");
                segmentator.RunSegmentation(realCameraImage);
                ARCamera.targetTexture = null;
            }
            catch (System.Exception e)
            {
                Debugger.Log($"CPU IMAGE: error getting format - {e.Message}", Debugger.MsgType.Error);
            }
           
        }
        else
        {
            Debugger.Log("AR Camera background or its material is null.", Debugger.MsgType.Error);
        }
        yield break; 
        /*
        AlignmentCameraEnable();

        Debugger.Log($"basePose + ({basePose.px};{basePose.py};{basePose.pz}), base rotation {basePose.ry}");
        var map = GetClassMap();
        Debugger.Log($"Count: G: {map.Where(a => a == 1).Count()} B: {map.Where(a => a == 2).Count()} S: {map.Where(a => a == 0).Count()}");

        Vector3 randomOffset = new Vector3(Random.Range(-10f, 10f), 0, Random.Range(-10f, 10f));
        Quaternion randomRotation = Quaternion.Euler(basePose.rx, basePose.ry + Random.Range(-50f, 50f), basePose.rz);

        alignmentCameraPose = new CameraPose(basePose.position + randomOffset, randomRotation);

        Debugger.Log($"Alignment Camera Pose = ({alignmentCameraPose.px};{alignmentCameraPose.py};{alignmentCameraPose.pz}) = ({basePose.px};{basePose.py};{basePose.pz}) + ({randomOffset.x};{randomOffset.y};{randomOffset.z})");

        while (_alignmentCameraPose.ComparePose(_visualizationCameraPose) > 0.01f)
        {
            yield return null; 
        }

        AlignmentCameraDisable();

        Debugger.Log($"basePose + ({basePose.px};{basePose.py};{basePose.pz}), base rotation {basePose.ry}");
        Vector3 newOffset = _alignmentCameraPose.position - basePose.position;
        float rotationOffset = _alignmentCameraPose.ry - basePose.ry;

        if (hasPrevPose)
        {
            rotationOffset += prevPose.ry;
            newOffset = newOffset + prevPose.position;
        }
        hasPrevPose = true;
        prevPose = new CameraPose(newOffset, Quaternion.Euler(0,rotationOffset, 0));
        Debugger.Log($"New Offset + ({newOffset.x};{newOffset.y};{newOffset.z}), New rotation offset {rotationOffset}");

        callback(new CameraPose(
            newOffset,
            Quaternion.Euler(0, rotationOffset, 0)
        ));*/
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
        visualizationCamera.backgroundColor = new Color(0.53f, 0.81f, 0.92f);
        visualizationCamera.clearFlags = CameraClearFlags.SolidColor;
        visualizationCamera.depth = 1;
        visualizationCamera.enabled = false;

        virtualSegmentationCamera.GetUniversalAdditionalCameraData().SetRenderer(2);
        virtualSegmentationCamera.backgroundColor = new Color(0.5f, 0.5f, 0.5f);
        virtualSegmentationCamera.clearFlags = CameraClearFlags.SolidColor;
        virtualSegmentationCamera.depth = 1;
        virtualSegmentationCamera.enabled = false;


        groundAlignmentCamera.backgroundColor = Color.black;
        groundAlignmentCamera.clearFlags = CameraClearFlags.SolidColor;
        buildingAlignmentCamera.backgroundColor = Color.black;
        buildingAlignmentCamera.clearFlags = CameraClearFlags.SolidColor;

        groundAlignmentCamera.targetTexture = groundAlignmentTexture;
        buildingAlignmentCamera.targetTexture = buildingAlignmentTexture;

        virtualSegmentationCamera.targetTexture = virtualPreSegmentationTexture;

        groundAlignmentCamera.cullingMask = 1 << GROUND_LAYER;
        buildingAlignmentCamera.cullingMask = 1 << BUILDING_LAYER;
        Debugger.Log(
    $"PIPELINE: {UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline}"
);

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

        Debugger.DisplayVector("ACamPos", () => alignmentCameraPose.position);
        Debugger.DisplayVector("ACamRot", () => alignmentCameraPose.rotationToVector());
        Debugger.DisplayVector("VCamPos", () => visualsationCameraPose.position);
        Debugger.DisplayVector("VCamRot", () => alignmentCameraPose.rotationToVector());


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
        result.ReadPixels(new Rect(0,0,textureSize, textureSize), 0, 0);
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
    private Texture GetARCameraTexture()
    {
        if (_ARCameraBackground == null)
        {
            Debugger.Log("AR CAMERA BACKGROUND NULL");
            return null;
        }

        Texture texture = _ARCameraBackground.material.mainTexture;

        if (texture == null)
        {
            Debugger.Log("AR CAMERA BACKGROUND TEXTURE NULL");
            return null;
        }

        Debugger.Log(
            $"AR CAMERA TEXTURE: {texture.width}x{texture.height} " +
            $"type={texture.GetType().Name}"
        );
        

        return texture;
    }
}
