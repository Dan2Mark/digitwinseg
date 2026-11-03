using System;
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

    public void AcceptPosition(int place_id, GPSConnector.Cords realCords, GPSConnector.Cords GPSCords, GPSConnector.Cords DTACords, float realHeading, float compassHeading, float DTAheadingError, float GPSefficiency, float DTAefficiency, float GPS_FPS, float DTA_FPS, float GPS_similarity, float DTA_similarity, float obstaclesRatio, float skyRatio, float buldingRatio, float groundRatio, int viewsCount)
    {
        float GPS_square_err = GPSConnector.HaversineDistance(realCords, GPSCords) * 1000;
        float DTA_square_err = GPSConnector.HaversineDistance(realCords, DTACords) * 1000;
        float compass_err = Mathf.DeltaAngle(realHeading, compassHeading);
        float DTA_angle_err = DTAheadingError;
        SaveMetricsToCsv(place_id, GPS_square_err, compass_err, GPS_FPS, GPSefficiency, GPS_similarity, DTA_square_err, DTA_angle_err, DTA_FPS, DTAefficiency, DTA_similarity, obstaclesRatio, skyRatio, buldingRatio, groundRatio, viewsCount);
    }

    private void SaveMetricsToCsv(int place_id, float gpsSquareErr, float compassErr, float gpsAverageFps, float gpsEfficiency, float gpsSimilarity, float dtaSquareErr, float dtaAngleErr, float dtaAverageFps, float dtaEfficiency, float dtaSimilarity, float obstaclesRatio, float skyRatio, float buldingRatio, float groundRatio, int viewsCount)
    {
        Debugger.Log("Evaluation Manager -> SaveMetricsToCsv");
        string filePath = Path.Combine(Application.persistentDataPath, "alignment_metrics.csv");
        bool writeHeader = !File.Exists(filePath);
        var now = DateTime.Now;

        using (StreamWriter sw = new StreamWriter(filePath, true))
        {
            if (writeHeader)
                sw.WriteLine("place_id;GPS_square_err_meters;Compass_err;GPS_average_FPS;GPS_Efficiency;GPS_Similarity;DTA_square_err_meters;DTA_angle_err;DTA_average_FPS;DTA_Efficiency;DTA_Similarity;Time;Obstacles_Ratio;Sky_Ratio;Building_Ratio;Ground_Ratio;Views_Count");

            sw.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0};{1:F7};{2:F2};{3:F1};{4:F7};{5:F2};{6:F7};{7:F2};{8:F1};{9:F7};{10:F2};{11};{12:F4};{13:F4};{14:F4};{15:F4};{16}",
            place_id, gpsSquareErr, compassErr, gpsAverageFps, gpsEfficiency, gpsSimilarity, dtaSquareErr, dtaAngleErr, dtaAverageFps, dtaEfficiency, dtaSimilarity, now.ToString("HH:mm"), obstaclesRatio, skyRatio, buldingRatio, groundRatio, viewsCount));
        }

        Debugger.Log($"Data saved to CSV! GPS error: {gpsSquareErr} m; Compass error: {compassErr}; DTA position error: {dtaSquareErr} m; DTA angle error: {dtaAngleErr}");
        ResetMetrics();
    }
}
