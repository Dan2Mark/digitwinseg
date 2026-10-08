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
    [SerializeField] GameObject MultiviewScanUIGroup;
    [SerializeField] RawImage image;
    [SerializeField] Texture2D scan, turnUp, success, unsuccess;
    [SerializeField] TextMeshProUGUI text;

    [SerializeField] Texture mainImage;
    [SerializeField] Texture testimageSecond;
    [SerializeField] Quaternion secondImageOffsetRotation = Quaternion.EulerAngles(0,90,0);
    [SerializeField] Vector3 secondImageOffsetPosition = Vector3.zero;
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
        public static CameraPose operator +(CameraPose a, AdditionalPose b) => a + b.offset;
        public static void _Reset()
        {            
            _mainPoseId = 0;
            additionalPoses.Clear();
        }
    }
    private void Start()
    {
        //StartCoroutine(SetNewViews());
    }


    public void _Reset()
    {
        AdditionalPose._Reset();
    }
    public void StopScanning()
    {
        isScanning = false;
        AlignButton.SetActive(false);
        MultiviewScanUIGroup.SetActive(false);
        Cesium.SetActive(true);
    }

    public bool isScanning = false;

    public IEnumerator SetNewViews()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        yield return Scan();
        yield break;
#endif 
        AdditionalPose._Reset();
            AdditionalPose._mainPoseId = 0;
            var pose = new CameraPose(ARCamera.transform.position, ARCamera.transform.rotation);
            var photo = _CAM.segmentator.RunSegmentation(mainImage);
            AdditionalPose.additionalPoses.Add(new AdditionalPose(pose, photo));
            pose = pose + secondImageOffsetPosition + secondImageOffsetRotation;
            photo = _CAM.segmentator.RunSegmentation(testimageSecond);
            AdditionalPose.additionalPoses.Add(new AdditionalPose(pose, photo));
            yield return null;
    }
    private IEnumerator Scan (int photosMaxCnt = 3)
    {
        if (isScanning) yield break;
        isScanning = true;
        AlignButton.SetActive(false);
        MultiviewScanUIGroup.SetActive(true);
        Cesium.SetActive(false);

        string posesCntTxt = $"\n0/{photosMaxCnt}";
        for (int i = 0; AdditionalPose.additionalPoses.Count < photosMaxCnt; i++)
        {
            CameraPose prev = new CameraPose(ARCamera.transform.position, ARCamera.transform.rotation);
            while(Vector3.Dot(ARCamera.transform.forward, Vector3.down) > 0.5f && isScanning)
                {
                text.text = "Please turn the camera up" + posesCntTxt;
                image.texture = turnUp;
                yield return new WaitForSeconds(0.2f);
                yield return null;
            }
            image.texture = success;
            while (isScanning && AdditionalPose.additionalPoses.Count > 0)
            {
                while (Vector3.Dot(ARCamera.transform.forward, Vector3.down) > 0.5f && isScanning)
                {
                    text.text = "Please turn the camera up" + posesCntTxt;
                    image.texture = turnUp;
                    yield return new WaitForSeconds(0.2f);
                    yield return null;
                }

                CameraPose current = new CameraPose(ARCamera.transform.position, ARCamera.transform.rotation);

                float speed = Vector3.Distance(current.position, prev.position) / Time.deltaTime;
                Debugger.DisplayVar("CameraSpeed", () => $"{speed}");

                Debug.Log((speed < 0.1f) + (AdditionalPose.additionalPoses.Any(p => Mathf.DeltaAngle(p.pose.ry, current.py) < 45) + (AdditionalPose.additionalPoses.Count == 0).ToString()));
                yield return null;
                if (speed < 0.1f && (!AdditionalPose.additionalPoses.Any(p => Mathf.DeltaAngle(p.pose.ry, current.py) < 45) || AdditionalPose.additionalPoses.Count == 0))
                    break;
                image.texture = scan;
                text.text = "Please find another building" + posesCntTxt;

                prev = current;
                yield return null;
            }

            if (!isScanning) yield break;
            var pose = new CameraPose(ARCamera.transform.position, ARCamera.transform.rotation);
            var photo = _CAM.segmentPhoto();
            yield return _CAM.CalculateRatios(photo);
            _CAM.DisplayPhoto(photo);
            if (_CAM.obstaclesRatio <= 0.8f)
            {
                AdditionalPose.additionalPoses.Add(new AdditionalPose(pose, photo, 1f - (
    Mathf.Abs(_CAM.groundRatio - _CAM.skyRatio) +
    Mathf.Abs(_CAM.groundRatio - _CAM.buildingRatio) +
    Mathf.Abs(_CAM.skyRatio - _CAM.buildingRatio)
) / 2f));
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
        Cesium.SetActive(true);
        AdditionalPose._mainPoseId = AdditionalPose.additionalPoses.Select((p, i) => new { p, i }).OrderByDescending(x => x.p.valueability).First().i;
        yield return null;
    }
}
