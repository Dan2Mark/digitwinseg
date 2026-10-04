using CesiumForUnity;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static GPSConnector;
using Unity.VisualScripting;


#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class LocationManager : MonoBehaviour
{


    [Header("Cesium Integration")]
    [SerializeField] private CesiumGeoreference _georeference;

    [Header("Managers")]
    [SerializeField] private GPSConnector _GPSconnector;
    [SerializeField] private CompassConnector _compassConnector;
    [SerializeField] public EvaluationManager _evaluationManager;


    public IEnumerator activateLocation(System.Action<bool> callback)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
        {
            Permission.RequestUserPermission(Permission.FineLocation);
            yield return new WaitForSeconds(2.0f);
        }
        Input.compass.enabled = true;
        Input.location.Start(1f, 1f);

        bool isGPSavailable = false;

        bool gpsConneptionCallbakRecived = false;
        bool compassConneptionCallbakRecived = false;

        StartCoroutine(_GPSconnector.activateGPS(isSuccess => {
            isGPSavailable = isSuccess;
            gpsConneptionCallbakRecived = true;
            }));
        StartCoroutine(_compassConnector.CheckCompassReady(isReady =>
        {
            compassConneptionCallbakRecived = true;
        }));


        yield return new WaitUntil(() => gpsConneptionCallbakRecived && compassConneptionCallbakRecived);

        bool isCompassAvailable = _compassConnector.ActivateCompass();
      
        callback(isCompassAvailable && isGPSavailable);  
#elif UNITY_EDITOR
        yield return null;
        callback(false);
#endif
    }
    public void deactivateLocation()
    {
        Input.compass.enabled = false;
        Input.location.Stop();
        _GPSconnector.deactivateGPS();
        _compassConnector.deactivateCompass();
    }


    public IEnumerator MultipleMeasuresGPSAccuracy(Cords realCords, int count, float realHeading, float compassHeading)
    {
        Debugger.Log($"Starting multiple GPS accuracy evaluation");
        float startTime = Time.time;
        bool _activateLocationCallbackRecieved = false;
        bool _isActivationSuccesful = false;
        float min_err = float.MaxValue;
        float max_err = float.MinValue;
        StartCoroutine(activateLocation((isActivated) =>
        {
            _isActivationSuccesful = isActivated;
            _activateLocationCallbackRecieved = true;
        }));

        yield return new WaitUntil(() => _activateLocationCallbackRecieved);
        if (!_isActivationSuccesful )
            yield break;
        Debugger.Log("Location activated successful. Trying getting coordinates...");

        float activationTime = Time.time - startTime;

        double lastTimestamp = Input.location.lastData.timestamp;

        for (int i = 0; i < count; i++)
        {
            Debugger.DisplayProgressBar("GPS Evaluation", i, count);
            float getCordsStartTime = Time.time;
            while (Input.location.lastData.timestamp == lastTimestamp)
            {
                if (Time.time - getCordsStartTime > 10)
                {
                    Debugger.Log("GPS coordinates are not changing. Stop measurements", Debugger.MsgType.Warn);
                    Debugger.HideProgressBar("GPS Evaluation");
                    yield break;
                }
                yield return null;
            }


            lastTimestamp = Input.location.lastData.timestamp;

            (Cords _deviceCords, bool isGPSfounded) = _GPSconnector.GetCords(); 

            if (!isGPSfounded)
            {
              Debugger.Log("GPS not found. Stop measurements", Debugger.MsgType.Error);
              Debugger.HideProgressBar("GPS Evaluation");
              break;
            }
            Debugger.Log($"GPS linked successfull: ({_deviceCords.lat}, {_deviceCords.lon})", Debugger.MsgType.Success);
            float square_err = HaversineDistance(realCords, _deviceCords);
            if (square_err < min_err) min_err = square_err;
            if (square_err > max_err) max_err = square_err;

            _evaluationManager.AcceptPosition(realCords, _deviceCords, realHeading, compassHeading, activationTime + Time.time - getCordsStartTime);
            yield return new WaitForSeconds(1f);
        }
        Debugger.Log($"Multiple GPS accuracy evaluation is ended. Max error: {max_err}, Min error: {min_err}");
        Debugger.HideProgressBar("GPS Evaluation");
        deactivateLocation();
    }


    public (Cords, bool, float, bool) GetLocation()
    {
        (Cords deviceCords, bool isGPSfounded) = _GPSconnector.GetCords();
        (float heading, bool isCompassAvailable) = _compassConnector.GetCompassHeading();
        return (deviceCords, isGPSfounded, heading, isCompassAvailable);
    }
}
