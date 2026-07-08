using CesiumForUnity;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GPSConnector : MonoBehaviour
{
    [Header("Mark for Debugger")]
    public const string mark = "GPS";
    [SerializeField] CesiumGeoreference _georeference;
    static public Cords _currentCords = new Cords(0,0,0);
    static private Cords _baseCords;
    public bool isGPSAvailible = false;

    public float distanceToCords(Cords cords) => HaversineDistance(_currentCords.lat, _currentCords.lon, cords.lat, cords.lon);
    public float distanceToBase() => distanceToCords(_baseCords);

    void Start()
    {
        if (_georeference == null) _georeference = GetComponent<CesiumGeoreference>();
        if (_georeference != null)
        {
            _baseCords = new Cords((float)_georeference.latitude,(float)_georeference.longitude,(float)_georeference.height);
        }
    }

    public (Cords, bool) GetCords()
    {
       return(ReadGPS(), isGPSAvailible);
    }

    public class Cords
    {
        public float lat;
        public float lon;
        public float alt;

        public Cords() { }

        public Cords(float lat, float lon, float alt)
        {
            this.lat = lat;
            this.lon = lon;
            this.alt = alt;
        }
        public string ToString()
        {
            return $"({lat}, {lon}, {alt})";
        }
    }


    public Vector3 ToUnityCords(Cords point)
    {
        var pointECEF = _georeference.TransformEarthCenteredEarthFixedPositionToUnity(CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(new Unity.Mathematics.double3(point.lon, point.lat, point.alt)));
        var baseECEF = _georeference.TransformEarthCenteredEarthFixedPositionToUnity(CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(new Unity.Mathematics.double3(_baseCords.lon, _baseCords.lat, _baseCords.alt)));


        Vector3 pointUnity = new Vector3((float)pointECEF.x, (float)pointECEF.y, (float)pointECEF.z);
        Vector3 baseUnity = new Vector3((float)baseECEF.x, (float)baseECEF.y, (float)baseECEF.z);

        return pointUnity - baseUnity;
    }

    public Cords FromUnityCords(Vector3 unityDelta)
    {
        try
        {
            var baseECEF = _georeference.TransformEarthCenteredEarthFixedPositionToUnity(CesiumWgs84Ellipsoid.LongitudeLatitudeHeightToEarthCenteredEarthFixed(new Unity.Mathematics.double3(_baseCords.lon, _baseCords.lat, _baseCords.alt)));

            var pointECEF = _georeference.TransformUnityPositionToEarthCenteredEarthFixed(baseECEF + new Unity.Mathematics.double3(unityDelta.x, unityDelta.y, unityDelta.z));
            var wgs84 = CesiumWgs84Ellipsoid.EarthCenteredEarthFixedToLongitudeLatitudeHeight(pointECEF);
            return new Cords((float)wgs84.y, (float)wgs84.x, (float)wgs84.z); // y = lat, x = lon, z = alt
        }
        catch (Exception ex)
        {
            Debugger.Log(ex.Message, Debugger.MsgType.Error);
            return null;
        }
    }

    Cords ReadGPS()
    {
        if (!isGPSAvailible)
        {
            Debugger.Log("Cannot read GPS. GPS is unavailible", Debugger.MsgType.Error);
            return null;
        }
        _currentCords = new Cords(
            Input.location.lastData.latitude,
            Input.location.lastData.longitude,
            Input.location.lastData.altitude
        );

        Debugger.DisplayVar("GPS", () => $"{_currentCords.lat}<color=#FFAA55>;</color>{_currentCords.lon}");
        return _currentCords;
    }

    public IEnumerator activateGPS(System.Action<bool> callback)
    {
        Debugger.HideVar("GPS");
        bool isCheckAviliabilityCallbackRecieved = false;
        StartCoroutine(checkAviliability(callback => isCheckAviliabilityCallbackRecieved = true));

        yield return new WaitUntil(() => isCheckAviliabilityCallbackRecieved);
        callback(isGPSAvailible);
    }

    public void deactivateGPS() => isGPSAvailible = false;

    IEnumerator checkAviliability (System.Action<bool> callback)
    {
        isGPSAvailible = false;
        if (!Input.location.isEnabledByUser)
        {
            Debugger.Log("GPS disabled", Debugger.MsgType.Warn);
            yield break;
        }


        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(0.5f);
            maxWait--;
        }

        if (Input.location.status == LocationServiceStatus.Failed)
        {
            Debugger.Log("GPS failed", Debugger.MsgType.Error);
            yield break;
        }
        isGPSAvailible = true;
        callback(isGPSAvailible);
    }

    static public float HaversineDistance(Cords point1, Cords point2)
    {
        if (point1 == null)
            Debugger.Log("GPSConnector.HaversineDistance: point 1 is null", Debugger.MsgType.Error);
        if (point2 == null)
            Debugger.Log("GPSConnector.HaversineDistance: point 2 is null", Debugger.MsgType.Error);
        return HaversineDistance(point1.lat, point1.lon, point2.lat, point2.lon);
    }
    static private float HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        double R = 6371.0;
        double dLat = Mathf.Deg2Rad * (lat2 - lat1);
        double dLon = Mathf.Deg2Rad * (lon2 - lon1);
        double a = System.Math.Sin(dLat / 2) * System.Math.Sin(dLat / 2) +
                   System.Math.Cos(Mathf.Deg2Rad * (float)lat1) * System.Math.Cos(Mathf.Deg2Rad * (float)lat2) *
                   System.Math.Sin(dLon / 2) * System.Math.Sin(dLon / 2);
        double c = 2 * System.Math.Atan2(System.Math.Sqrt(a), System.Math.Sqrt(1 - a));
        return (float)(R * c);
    }

}
