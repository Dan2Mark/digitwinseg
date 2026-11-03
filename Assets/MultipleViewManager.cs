using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine.UI;

using UnityEngine;
using static CameraAlignmentManager;
public class MultipleViewManager : MonoBehaviour
{
    [SerializeField] Camera ARCamera;
    [SerializeField] CameraAlignmentManager _CAM;
    [SerializeField] GameObject Cesium;
    [SerializeField] GameObject AlignButton;
    [SerializeField] GameObject ConfirmButton;
    [SerializeField] GameObject MultiviewScanUIGroup;
    [SerializeField] RawImage image;
    [SerializeField] Texture2D scan, turnUp, success, unsuccess;
    [SerializeField] TextMeshProUGUI text;

    [SerializeField] Texture mainImage;
    [SerializeField] Texture testimageSecond;
    [SerializeField] Quaternion secondImageOffsetRotation = Quaternion.Euler(0f,90f,0f);
    [SerializeField] Vector3 secondImageOffsetPosition = Vector3.zero;


    bool firstPositionAccepted = false;
    public struct AdditionalPose
    {

        static public List<AdditionalPose> additionalPoses = new List<AdditionalPose>();
        public static AdditionalPose mainAddPose { get { return additionalPoses[_mainPoseId]; } }
        public static CameraPose mainPose { get { return mainAddPose.pose; }}
        public static int _mainPoseId = 0;
        public CameraPose pose;
        public byte[] photo;
        public float valueability;

        public AdditionalPose(CameraPose pose, byte[] photo, float valueability = 0)
        {
            this.pose = pose;
            this.photo = photo;
            this.valueability = valueability;        
        }
        public CameraPose offset { get { return mainPose - this.pose; } }
        
         public CameraPose ApplyToAlignedMain(CameraPose a) => new(
            a.position + a.rotation * (Quaternion.Inverse(mainPose.rotation) * (pose.position - mainPose.position)),
            a.rotation * (Quaternion.Inverse(mainPose.rotation) * pose.rotation)
        );

        public static void SetNewPoseToMain(CameraPose p)
        {
            var r = Quaternion.Euler(0,(p.rotation * Quaternion.Inverse(mainPose.rotation)).eulerAngles.y,0);
            var t = p.position - r * mainPose.position;

            for (int i = 0; i < additionalPoses.Count; i++)
            {
                var a = additionalPoses[i];
                a.pose.position = r * a.pose.position + t;
                a.pose.rotation = r * a.pose.rotation;
                additionalPoses[i] = a;
            }
        }
        public static CameraPose operator +(CameraPose a, AdditionalPose b) => a + b.offset;
        public static void _Reset()
        {            
            _mainPoseId = 0;
            additionalPoses.Clear();
        }
    }
    private void Start()
    {
        // StartCoroutine(SetNewViews());
        cMask = ARCamera.cullingMask;
    }

    static public void AddOffset(float ry) => AddOffset(ry, Vector3.zero);
    static public void AddOffset(CameraPose pose) => AddOffset(pose.ry,pose.p);
    static public void AddOffset(float ry, Vector3 offset)
    {
        for (int i = 0; i < AdditionalPose.additionalPoses.Count; i++)
        {
            var ap = AdditionalPose.additionalPoses[i];
            Debugger.Log($"Prev Additional Pose [{i}]: {ap.pose.ToString()} + {offset.ToString()}", Debugger.MsgType.Info);
            var rot = Quaternion.Euler(ap.pose.rx, ap.pose.ry + ry, ap.pose.rz);
            ap.pose = new CameraPose(ap.pose.position + offset, rot);
            AdditionalPose.additionalPoses[i] = ap;
            Debug.Log($"New Additional Pose [{i}]: {ap.pose.ToString()}");
        }
    }
    int cMask = 0;
    public void SetCityVisualisation(bool enabled)
    {
        if (!enabled)
        {
            if (ARCamera.cullingMask != 0)
            {
                ARCamera.cullingMask = 0;
            }
        }
        else
        {
            ARCamera.cullingMask = cMask;
        }
    }

    public void _Reset()
    {
        AdditionalPose._Reset();
        firstPositionAccepted = false;
        AdditionalPose._mainPoseId = 0;
    }
    public void StopScanning()
    {
        isScanning = false;
        AlignButton.SetActive(false);
        MultiviewScanUIGroup.SetActive(false);
        Cesium.SetActive(true);
    }

    public void AcceptFirstPosition() => firstPositionAccepted = true;

    public bool isScanning = false;

    public IEnumerator SetNewViews()
    {
        _Reset();
#if UNITY_ANDROID && !UNITY_EDITOR
        yield return Scan();
        yield break;
#endif

        SetCityVisualisation(false);
        CameraPose pose = new CameraPose(ARCamera.transform.position, ARCamera.transform.rotation) + AlignmentManager.OffsetWithoutGPS;
        var photo = _CAM.segmentator.RunSegmentation(mainImage);
        AdditionalPose.additionalPoses.Add(new AdditionalPose(pose, photo));
        
        pose = pose + secondImageOffsetPosition;
        
        pose = pose + secondImageOffsetRotation;
        photo = _CAM.segmentator.RunSegmentation(testimageSecond);
        AdditionalPose.additionalPoses.Add(new AdditionalPose(pose, photo));
        AdditionalPose._mainPoseId = 0;

        SetCityVisualisation(true);
        //Debugger.DisplayVar("MainPose", () => $"({AdditionalPose.mainPose.px};{AdditionalPose.mainPose.pz});{AdditionalPose.mainPose.ry}");
        yield return null;
    }
    string posesCntTxt = "";
    private IEnumerator Scan(int photosMaxCnt = 3)
    {
        if (isScanning) yield break;
        isScanning = true;
        AlignButton.SetActive(false);
        ConfirmButton.SetActive(false);
        MultiviewScanUIGroup.SetActive(true);
        SetCityVisualisation(false);

        posesCntTxt = $"\n0/{photosMaxCnt}";
        for (int i = 0; AdditionalPose.additionalPoses.Count < photosMaxCnt; i++)
        {
            CameraPose prev = new CameraPose(ARCamera.transform.position, ARCamera.transform.rotation);
            yield return IsCameraDownCheck();
            image.texture = success;
            if (AdditionalPose.additionalPoses.Count > 0)
                while (isScanning)
                {
                    yield return IsCameraDownCheck();

                    CameraPose current = new CameraPose(ARCamera.transform.position, ARCamera.transform.rotation);

                    float speed = Quaternion.Angle(current.rotation, prev.rotation) / Time.deltaTime;
                    Debugger.DisplayVar("CameraSpeed", () => $"{speed}");

                    yield return null;
                    if (speed < 12f && AdditionalPose.additionalPoses.All(p => Mathf.Abs(Mathf.DeltaAngle(p.pose.ry, current.ry)) >= 30f))
                    {
           
                        break;

                    }
                    image.texture = scan;
                    text.text = "Please find another building" + posesCntTxt;

                    prev = current;
                    yield return null;
                }
            else
            {
               // Debugger.Log("First Pose");
                ConfirmButton.SetActive(true);
                firstPositionAccepted = false;
                while (!firstPositionAccepted && isScanning)
                {
                 //   Debugger.Log("Waiting confirmation");
                    yield return IsCameraDownCheck();
                    yield return new WaitForSeconds(0.2f);
                }
                ConfirmButton.SetActive(false);
            }
            if (!isScanning)
            {
                SetCityVisualisation(true);
                yield break;
            }
            
            var pose = new CameraPose(ARCamera.transform.position, ARCamera.transform.rotation) + AlignmentManager.OffsetWithoutGPS;
            var photo = _CAM.segmentPhoto();
            _CAM.DisplayPhoto(photo);
         //   Debugger.Log(_CAM.obstaclesRatio.ToString());
            if (_CAM.obstaclesRatio <= 0.5f && isScanning)
            {
                AdditionalPose.additionalPoses.Add(new AdditionalPose(pose, photo, 1f - (Mathf.Abs(_CAM.groundRatio - _CAM.skyRatio) + Mathf.Abs(_CAM.groundRatio - _CAM.buildingRatio) + Mathf.Abs(_CAM.skyRatio - _CAM.buildingRatio)) / 2f));
                posesCntTxt = $"\n{AdditionalPose.additionalPoses.Count}/{photosMaxCnt}";
                image.texture = success;
                AlignButton.SetActive(true);
                yield return new WaitForSeconds(1f);
                _CAM.ClearDisplayImages();
            }
            else
            {
                image.texture = unsuccess;
                text.text = "Too many obstacles!" + posesCntTxt;
                yield return new WaitForSeconds(3f);
                _CAM.ClearDisplayImages();
            }

        }

        Debugger.HideVar("CameraSpeed");
        isScanning = false;
        AlignButton.SetActive(false);
        MultiviewScanUIGroup.SetActive(false);
        SetCityVisualisation(true);
        AdditionalPose._mainPoseId = AdditionalPose.additionalPoses.Select((p, i) => new { p, i }).OrderByDescending(x => x.p.valueability).First().i;
        yield return null;
    }

    IEnumerator IsCameraDownCheck()
    {
        var imagePrevTexture = image.texture;
        bool confButACtive = ConfirmButton.active;
        while (Vector3.Dot(ARCamera.transform.forward, Vector3.down) > 0.5f && isScanning)
        {

           // Debugger.Log("Turn Camera Up");
            ConfirmButton.SetActive(false);
            text.text = "Please turn the camera up" + posesCntTxt;
            image.texture = turnUp;
            yield return new WaitForSeconds(0.2f);
            yield return null;
        }
        ConfirmButton.SetActive(confButACtive);
        image.texture = imagePrevTexture;

    }

}
