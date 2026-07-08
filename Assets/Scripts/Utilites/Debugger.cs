using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class Debugger : MonoBehaviour
{
    public static Debugger Instance;

    [Header("UI")]
    public TextMeshProUGUI textTMP;
    private string _stateString = "";
    private string _progressBar = "";


    private static string buffer = "";
    static public int maxLines = 10;

    static Dictionary<string, (object value, bool show)> vars = new();

    static int barLength = 10;
    static Dictionary<string, float> progressBars = new();

    void Update()
    {
        _stateString = string.Join("  ",
            vars.Where(v => v.Value.show)
                .Select(v => $"<color=#FFAA55>{v.Key}:</color> {Format(v.Value.value)}"));
            _progressBar = "";
            foreach (string name in progressBars.Keys)
            {
                float progress = progressBars[name];
                int filled = Mathf.RoundToInt(progress * barLength);
                int empty = barLength - filled;

                string bar = $"<color=#FFAA55>{name}:</color> <color=#55FFAA>" + new string('█', filled) + "</color>" + new string('░', empty) + $" {(int)(progress * 100)}%";
                _progressBar += bar + '\n';
            }
     

        if (Instance != null)
        {
            if (Instance.textTMP != null)
                Instance.textTMP.text = $"{_stateString}\n<color=#000>---------------------------------------------</color>\n" + ((progressBars.Count > 0) ? (_progressBar + "<color=#000>-------------------------------------------------------------</color>\n") : "") + buffer;
        }
        
    }

    static string Format(object v)
        => v is Func<string> f ? f() : v.ToString();

    public static void DisplayVar(string name, string text)
        => vars[name] = (text, true);

    public static void DisplayVar(string name, Func<string> getter)
        => vars[name] = (getter, true);

    public static void DisplayVars(string name, params object[] values)
    {
        DisplayVar(name, () =>
        {
            if (values == null || values.Length == 0) return "()";
            return "(" + string.Join(", ", values.Select(v => v?.ToString() ?? "null")) + ")";
        });
    }

    public static void DisplayVector(string name, Func<Vector3> vectorGetter)
    {
        DisplayVar(name, () => {
            Vector3 v = vectorGetter();
            return $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
        });
    }

    public static void HideVar(string name)
    {
        if (vars.TryGetValue(name, out var v))
            vars[name] = (v.value, false);
    }

    public static void RemoveVar(string name)
        => vars.Remove(name);


    void Awake()
    {
        Instance = this;
        Clear();
    }

    public enum MsgType
    {
        Info = 0,
        Warn = 1,
        Error = 2,
        Success = 3
    }

    //public static void Log(string msg, MsgType type = MsgType.Info) => Log(msg,"", type);
    public static void Log(string msg, MsgType type = MsgType.Info)
    {
        switch (type)
        {
            case MsgType.Warn: msg = $"<color=#FFCB55>{msg}</color>"; break;
            case MsgType.Error: msg = $"<color=#FF0000>{msg}</color>"; break;
            case MsgType.Success: msg = $"<color=#55FFAA>{msg}</color>"; break;
        }
        /*
        var markField = new StackTrace().GetFrame(1).GetMethod().DeclaringType.GetField("mark", BindingFlags.Public | BindingFlags.Static);

        string mark = markField?.GetValue(null) as string;

        msg = (mark != null ? $"<mark=#33333355><color=#FFDD55>{mark}</color></mark>  " : "") + msg;
        */
        buffer +=  msg + "\n";

        var lines = buffer.Split('\n');
        if (lines.Length > maxLines)
            buffer = string.Join("\n", lines, lines.Length - maxLines, maxLines);

    }

    public static void Clear()
    {
        buffer = "";
        if (Instance != null)
        {
            if (Instance.textTMP != null)
                Instance.textTMP.text = "";
        }
    }

    public static void DisplayProgressBar(string name, int currentProgress, int maxProgress)
    {
        

        if (currentProgress == maxProgress)
        {
            HideProgressBar(name);
            return;
        }
        float progress = Mathf.Clamp01((float)currentProgress / maxProgress);
        progressBars[name] = progress;       
    }

    public static void HideProgressBar(string name)
    {
        progressBars.Remove(name);
    }
}
