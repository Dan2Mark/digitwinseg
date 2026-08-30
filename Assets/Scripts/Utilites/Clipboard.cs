using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Clipboard : MonoBehaviour
{
    // Start is called before the first frame update
    public static void Copy(string text)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

            AndroidJavaObject clipboardManager = activity.Call<AndroidJavaObject>("getSystemService", "clipboard");

            AndroidJavaClass clipDataClass = new AndroidJavaClass("android.content.ClipData");
            AndroidJavaObject clipData = clipDataClass.CallStatic<AndroidJavaObject>(
                "newPlainText", "label", text);

            clipboardManager.Call("setPrimaryClip", clipData);
        }
        catch (System.Exception e)
        {
            Debug.LogError("Clipboard copy failed: " + e);
        }
#else
        GUIUtility.systemCopyBuffer = text;
#endif
    }
}
