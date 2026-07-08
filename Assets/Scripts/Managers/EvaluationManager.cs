using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class EvaluationManager : MonoBehaviour
{

    float FPS = 30.0f;
    float LastFPS = 0;
    // Start is called before the first frame update
    void Update()
    {
        LastFPS = (1.0f / Time.unscaledDeltaTime);
        FPS = FPS * 0.01f + LastFPS * 0.99f;
    }

    public void ResetMetrics()
    {
        FPS = LastFPS;
    }

    public void AcceptPosition(GPSConnector.Cords realCords, GPSConnector.Cords alignmentCords, float realHeading, float alignmentHeading, float efficiency)
    {
        float square_err = GPSConnector.HaversineDistance(realCords, alignmentCords); //Mathf.Sqrt(_pureJoystickOffsetMeters.x * _pureJoystickOffsetMeters.x + _pureJoystickOffsetMeters.z * _pureJoystickOffsetMeters.z);
        float compass_err = Mathf.DeltaAngle(realHeading, alignmentHeading);
        Debugger.Log("Compass error: " + compass_err);        
        SaveMetricsToCsv(alignmentCords.lon, alignmentCords.lat, efficiency, Mathf.Abs(realCords.lat - alignmentCords.lat), Mathf.Abs(realCords.lon - alignmentCords.lon), square_err, alignmentHeading, compass_err, FPS);
    }

    private void SaveMetricsToCsv(double gpsLon, double gpsLat, float efficiency, double latErr, double lonErr, float squareErr, float compassHeading, float compassErr, float avgFps)
    {
        Debugger.Log("Evaluation Manager -> SaveMetricsToCsv");
        string filePath = Path.Combine(Application.persistentDataPath, "alignment_metrics.csv");
        bool writeHeader = !File.Exists(filePath);

        using (StreamWriter sw = new StreamWriter(filePath, true))
        {
            if (writeHeader)
            {
                sw.WriteLine("gps_lon;gps_lat;Efficiency;lat_err;lon_err;square_err_meters;CompassHeading;compass_err;average_FPS");
            }

            string line = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0:F7};{1:F7};{2:F2};{3:F7};{4:F7};{5:F2};{6:F1};{7:F1};{8:F1}",
                gpsLon, gpsLat, efficiency, latErr, lonErr, squareErr, compassHeading, compassErr, avgFps);
            sw.WriteLine(line);
        }
        Debugger.Log($"Data saved to CSV! Compass error: {compassErr}; GPS Error: {squareErr}");
        ResetMetrics();
    }
}
