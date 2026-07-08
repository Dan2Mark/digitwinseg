using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CompassConnector : MonoBehaviour
{
    [Header("Mark for Debugger")]
    public const string mark = "Compass";

    public bool isCompassAvailable = false;
    public float _currentHeading = 0;
    private bool isCompassActive = false;
    public (float, bool) GetCompassHeading()
    {
        return(ReadCompass(), isCompassAvailable);
    }

    public bool checkAviliability()
    {
        isCompassAvailable = false;
        if (!Input.compass.enabled)
        {
            Debugger.Log("Compass disabled", Debugger.MsgType.Error);
        }
        else isCompassAvailable = true;
        return isCompassAvailable;
    }

    float ReadCompass()
    {
        if (!isCompassActive)
        {
            Debugger.Log("Cannot read compass. First activate compass", Debugger.MsgType.Error);
            return 0.0f;
        }
        if (!isCompassAvailable)
        {
            Debugger.Log("Cannot read compass. Compass is unavailable", Debugger.MsgType.Error);
            return 0.0f;
        }
        _currentHeading = Input.compass.trueHeading;
        Debugger.DisplayVar("Compass", () => _currentHeading.ToString());
        return _currentHeading;
    }
    public IEnumerator CheckCompassReady(System.Action<bool> callback)
    {
        int maxWait = 20;
        while (Input.compass.trueHeading == 0f && maxWait > 0)
        {
            yield return new WaitForSeconds(0.1f);
            maxWait--;
        }
        float prev = Input.compass.trueHeading;
        int stableCount = 0;

        maxWait = 30;
        while (stableCount < 5 && maxWait > 0)
        {
            yield return new WaitForSeconds(0.1f);
            float current = Input.compass.trueHeading;

            if (Mathf.Abs(Mathf.DeltaAngle(prev, current)) < 3f)
                stableCount++;
            else
                stableCount = 0;

            prev = current;
            maxWait--;
        }
        _currentHeading = prev;
        callback(true);
    }

    public bool ActivateCompass()
    {
        Debugger.HideVar("Compass");
        isCompassActive = checkAviliability();
        return isCompassActive;
    }
    public void deactivateCompass() {isCompassActive = false; isCompassAvailable = false;}

}
