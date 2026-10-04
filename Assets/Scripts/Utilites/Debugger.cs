using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

public class Debugger : MonoBehaviour
{
    public static Debugger Instance;
    public TextMeshProUGUI textTMP;

    [SerializeField] float minFontSize = 12f;
    [SerializeField] float maxFontSize = 40f;
    [SerializeField] float initialFontSize = 20f;
    [SerializeField] float gestureThreshold = 20f;
    [SerializeField] float scrollSensitivity = .08f;
    [SerializeField] float textSizeSensitivity = .03f;
    [SerializeField] float pinchSensitivity = .5f;
    [SerializeField] RectTransform debuggerCanvas;

    [SerializeField] bool collapsed = false;
    float textSize, scrollOffset, openedY;
    Vector2 gestureStart, lastPosition;
    bool gestureStarted, horizontalGesture, verticalGesture;
    float previousPinchDistance;

    const int MaxHistoryLines = 500;
    public static int maxLines = MaxHistoryLines;
    static readonly List<string> logHistory = new();
    static readonly Dictionary<string, (object value, bool show)> vars = new();
    static readonly Dictionary<string, object> progressBars = new();
    static readonly Dictionary<string, object> valueBars = new();

    float headerHeight = 0;
    const int barLength = 10;
    float collapsedHeight;

    string _stateString = "", _progressBar = "", _valueBar = "";
    private string _devider = "";
    string devider { get { return _devider; } }

    readonly Queue<(string message, string stackTrace, LogType type)> logQueue = new();
    readonly object logLock = new();

    public enum MsgType { Debug, Info, Warn, Error, Success }

    void Awake()
    {
        Instance = this;
        textSize = initialFontSize;

        if (debuggerCanvas == null)
            debuggerCanvas = GetComponent<RectTransform>();

        if (textTMP != null)
            textTMP.fontSize = textSize;

        if (debuggerCanvas != null)
            openedY = debuggerCanvas.anchoredPosition.y;

    }

    bool isFirstLoop = true;
    void Update()
    {
        if (isFirstLoop)
        {
            if (collapsed)
                ToggleDebugger(true);
            isFirstLoop = false;
        }
        CalculateDevider();
        ProcessQueuedLogs();
        UpdateGestures();
        UpdateStateString();
        UpdateProgressBars();
        UpdateValueBars();
        UpdateVisibleLogs();
    }
    void ProcessQueuedLogs()
    {
        lock (logLock)
            while (logQueue.Count > 0)
            {
                var e = logQueue.Dequeue();
                AddLog(e.message + (e.type == LogType.Exception ? e.stackTrace : ""), Log2MsgType(e.type));
            }
    }

    void AddLog(string msg, MsgType type)
    {
        if (type == MsgType.Warn) msg = $"<color=#FFCB55>{msg}</color>";
        else if (type == MsgType.Error) msg = $"<color=#FF0000>{msg}</color>";
        else if (type == MsgType.Success) msg = $"<color=#55FFAA>{msg}</color>";
        else if (type == MsgType.Info) msg = $"<color=#25D3FF>{msg}</color>";

        logHistory.AddRange(msg.Split('\n'));
        while (logHistory.Count > maxLines) logHistory.RemoveAt(0);
        scrollOffset = 0;
    }

    void UpdateStateString() =>
        _stateString = string.Join("  ", vars.Where(v => v.Value.show).Select(v => $"<color=#FFAA55>{v.Key}:</color> {Format(v.Value.value)}"));

    void UpdateProgressBars()
    {
        _progressBar = "";
        foreach (var p in progressBars)
        {
            float value = (float)(p.Value is float ? p.Value : ((Func<float>)p.Value)());
            int filled = Mathf.Clamp(Mathf.RoundToInt(value * barLength), 0, barLength);
            _progressBar += $"<color=#FFAA55>{p.Key}:</color> <color=#55FFAA>{new string('█', filled)}</color><color=#555>{new string('░', barLength - filled)}</color> {(int)(value * 100)}%\t";
        }
    }
    void UpdateValueBars()
    {

        _valueBar = "";
        foreach (var p in valueBars)
        {
            ValueBar value = (ValueBar)(p.Value is ValueBar ? p.Value : ((Func<ValueBar>)p.Value)());
            int filled = (int)Map(value.value,value.min,value.max,0,barLength);
            /*var strV = Math.Round(value.value,2).ToString();
            string bar = strV + "</color><color=#FFCB55>" + new string('_', Math.Max(0,barLength - strV.Length - filled)) + new string('█', Math.Max(0, Math.Max(barLength - strV.Length, barLength - filled)));
            bar.Insert(filled, "</mark></color><color=#000><mark=#000>");
            _valueBar += $"<color=#FFAA55>{p.Key}:</color><mark=#FFCB5588><color=#FFF> {bar}</mark></color>\t";
            */
            _valueBar += $"<color=#FFAA55>{p.Key}:</color> <color=#FFCB55>{new string('█', filled)}</color><color=#555>{new string('░', barLength - filled)}</color> {Math.Round(value.value,2)}\t";
        }
    }

    void UpdateVisibleLogs()
    {
        if (textTMP == null || collapsed)
        {
            textTMP.text = "<align=center><color=#555>Debug</color>    <b>▲</b>    <color=#555> Open</color></align>\n" +  (progressBars.Count > 0 ? devider : "") + _progressBar + ((valueBars.Count > 0 && progressBars.Count > 0) ? "\n" + devider : "" ) + _valueBar;
            return;
        }
            

        float width = textTMP.rectTransform.rect.width;
        float height = textTMP.rectTransform.rect.height;

        string header =
            "<align=center><color=#DDD>Debug</color>    <b>▼</b>    <color=#DDD>Close</color></align>\n" + devider + 

            $"{_stateString}\n" + devider + 
            (progressBars.Count > 0 ? _progressBar + "\n" + devider : "") +
            (valueBars.Count > 0 ? _valueBar + "\n" + devider : "");

        headerHeight = textTMP.GetPreferredValues(header, width, 0).y;
        float availableHeight = Mathf.Max(0, height - headerHeight);

        int end = Mathf.Clamp(logHistory.Count - Mathf.RoundToInt(scrollOffset), 0, logHistory.Count);
        float usedHeight = 0;
        int start = end;

        for (int i = end - 1; i >= 0; i--)
        {
            float h = textTMP.GetPreferredValues(logHistory[i], width, 0).y;
            if (usedHeight + h > availableHeight) break;
            usedHeight += h;
            start = i;
        }

        string logs = start < end ? string.Join("\n", logHistory.GetRange(start, end - start)) : "";
        textTMP.text = header + logs;
    }

    int GetVisibleLogCount()
    {
        if (textTMP == null) return 0;

        float width = textTMP.rectTransform.rect.width;
        float height = textTMP.rectTransform.rect.height;

        string header =
            $"{_stateString}\n<color=#000>---------------------------------------------</color>\n" +
            (progressBars.Count > 0 ? _progressBar + "<color=#000>-------------------------------------------------------------</color>\n" : "");

        float availableHeight = Mathf.Max(0, height - textTMP.GetPreferredValues(header, width, 0).y);
        float used = 0;
        int count = 0;

        for (int i = logHistory.Count - 1; i >= 0; i--)
        {
            float h = textTMP.GetPreferredValues(logHistory[i], width, 0).y;
            if (used + h > availableHeight) break;
            used += h;
            count++;
        }

        return count;
    }

    void Scroll(float delta)
    {
        if (collapsed) return;

        int visible = GetVisibleLogCount();
        int maxOffset = Mathf.Max(0, logHistory.Count - visible);

        scrollOffset = Mathf.Clamp(
            scrollOffset + delta * scrollSensitivity,
            0,
            maxOffset
        );
    }

    void ChangeTextSize(float delta)
    {
        textSize = Mathf.Clamp(textSize + delta * textSizeSensitivity, minFontSize, maxFontSize);
        if (textTMP != null) textTMP.fontSize = textSize;
    }

    void ToggleDebugger(bool isAwake = false)
    {
        if (!isAwake)
            collapsed = !collapsed;

        if (debuggerCanvas == null || textTMP == null)
            return;

        if (collapsed)
        {
            textTMP.text = "^";
            Canvas.ForceUpdateCanvases();

            float lineHeight = textTMP.GetPreferredValues(
                "D^",
                textTMP.rectTransform.rect.width,
                0
            ).y;

            float hiddenHeight = debuggerCanvas.rect.height - lineHeight * 2;

            debuggerCanvas.anchoredPosition = new Vector2(
                debuggerCanvas.anchoredPosition.x,
                openedY - hiddenHeight
            );
            headerHeight = 0;
            collapsedHeight = textTMP.GetPreferredValues(
                "^",
                textTMP.rectTransform.rect.width,
                0
            ).y;
        }
        else
        {
            debuggerCanvas.anchoredPosition = new Vector2(
                debuggerCanvas.anchoredPosition.x,
                openedY
            );
            scrollOffset = 0;
        }
    }
    void UpdateGestures()
    {
        if (Input.touchCount == 2)
        {
            HandlePinch();
            return;
        }

        if (Input.touchCount == 1)
        {
            HandleTouch(Input.GetTouch(0));
            return;
        }

        if (Input.touchCount == 0)
            HandleMouse();
    }

    void HandleTouch(Touch t)
    {
        switch (t.phase)
        {
            case TouchPhase.Began:
                BeginGesture(t.position);
                break;

            case TouchPhase.Moved:
                MoveGesture(t.position);
                break;

            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                EndGesture(t.position);
                break;
        }
    }

    void HandleMouse()
    {
        Vector2 pos = Input.mousePosition;

        if (Input.GetMouseButtonDown(0))
            BeginGesture(pos);
        else if (Input.GetMouseButton(0))
            MoveGesture(pos);
        else if (Input.GetMouseButtonUp(0))
            EndGesture(pos);

        float wheel = Input.mouseScrollDelta.y;
        if (Mathf.Abs(wheel) > .001f)
            Scroll(wheel * 9f);
    }

    void BeginGesture(Vector2 pos)
    {
        gestureStart = lastPosition = pos;
        gestureStarted = horizontalGesture = verticalGesture = false;
    }

    void MoveGesture(Vector2 pos)
    {
        if (collapsed || !IsLogArea(pos))
        {
            return;
        }
        
        Vector2 total = pos - gestureStart;
        Vector2 delta = pos - lastPosition;
        lastPosition = pos;

        if (!gestureStarted)
        {
            if (total.magnitude < gestureThreshold) return;

            gestureStarted = true;
            horizontalGesture = Mathf.Abs(total.x) > Mathf.Abs(total.y);
            verticalGesture = !horizontalGesture;
        }

        if (verticalGesture)
            Scroll(-delta.y);
        else
            ChangeTextSize(delta.x);
    }

    void EndGesture(Vector2 position)
    {
        if (!gestureStarted && IsLogArea(position, isHeader:true))
            ToggleDebugger();
        ResetGesture();
    }
    void HandlePinch()
    {
        if (collapsed) return;
        Touch a = Input.GetTouch(0);
        Touch b = Input.GetTouch(1);
        float distance = Vector2.Distance(a.position, b.position);

        if (a.phase == TouchPhase.Began ||
            b.phase == TouchPhase.Began ||
            previousPinchDistance <= 0)
        {
            previousPinchDistance = distance;
            return;
        }

        ChangeTextSize((distance - previousPinchDistance) * pinchSensitivity * .01f);
        previousPinchDistance = distance;
    }

    void ResetGesture()
    {
        gestureStarted = horizontalGesture = verticalGesture = false;
        previousPinchDistance = 0;
    }
    bool IsLogArea(Vector2 screenPosition, bool isHeader = false)
    {
        if (textTMP == null || debuggerCanvas == null)
            return false;
        Canvas canvas = debuggerCanvas.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera
            : null;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            debuggerCanvas,
            screenPosition,
            cam,
            out Vector2 canvasLocal))
            return false;

        if (collapsed)
            return canvasLocal.y <= -debuggerCanvas.rect.yMin;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            textTMP.rectTransform,
            screenPosition,
            cam,
            out Vector2 textLocal))
            return false;
        var rect = textTMP.rectTransform.rect;
        return !isHeader ? textLocal.y <= rect.yMax - headerHeight : (textLocal.y > rect.yMax - headerHeight && textLocal.y <= rect.yMax) && textLocal.x > rect.xMin && textLocal.x < rect.xMax;
    }
    static string Format(object v) =>
        v is Func<string> f ? f() : v?.ToString() ?? "null";

    public static void Log(string msg, MsgType type = MsgType.Debug)
    {
        if (Instance != null)
        {
            Instance.AddLog(msg, type);
            return;
        }

        if (type == MsgType.Warn) msg = $"<color=#FFCB55>{msg}</color>";
        else if (type == MsgType.Error) msg = $"<color=#FF0000>{msg}</color>";
        else if (type == MsgType.Success) msg = $"<color=#55FFAA>{msg}</color>";
        else if (type == MsgType.Info) msg = $"<color=#25D3FF>{msg}</color>";

        logHistory.AddRange(msg.Split('\n'));
        while (logHistory.Count > maxLines) logHistory.RemoveAt(0);
    }

    public static void LogError(string msg) => Log(msg, MsgType.Error);

    public static void Clear()
    {
        logHistory.Clear();
        if (Instance?.textTMP != null)
            Instance.textTMP.text = "";
    }

    public static void DisplayVar(string name, string text) => vars[name] = (text, true);
    public static void DisplayVar(string name, Func<string> getter) => vars[name] = (getter, true);

    public static void DisplayVars(string name, params object[] values) =>
        DisplayVar(name, () => values == null || values.Length == 0 ? "()" : "(" + string.Join(", ", values.Select(v => v?.ToString() ?? "null")) + ")");

    public static void DisplayVector(string name, Func<Vector3> getter) =>
        DisplayVar(name, () => { Vector3 v = getter(); return $"({v.x:F2}, {v.y:F2}, {v.z:F2})"; });

    public static void HideVar(string name)
    {
        if (vars.TryGetValue(name, out var v))
            vars[name] = (v.value, false);
    }

    public static void RemoveVar(string name) => vars.Remove(name);

    public static void DisplayProgressBar(string name, Func<float> getter, float maxProgress) =>
        progressBars[name] = (Func<float>)(() => Mathf.Clamp01((float)getter() / maxProgress));
    
    public static void DisplayProgressBar(string name, float currentProgress, float maxProgress)
    {
        if (currentProgress == maxProgress)
        {
            HideProgressBar(name);
            return;
        }

        progressBars[name] = Mathf.Clamp01((float)currentProgress / maxProgress);
    }
    public static bool isProggresBarDisplayed(string name) => progressBars.ContainsKey(name);
    public static bool isValueBarDisplayed(string name) => valueBars.ContainsKey(name);
    public static bool isVarDisplayed(string name) => vars.ContainsKey(name);


    struct ValueBar
    {
        public float value;
        public float min;
        public float max;
        public 
            ValueBar (float value, float min, float max)
        {
            this.value = value;
            this.min = min;
            this.max = max;
        }
    }
    public static void DisplayValueBar(string name, Func<float> getter, float minValue = 0, float maxValue = 1, float? minOutValue = null, float? maxOutValue = null) =>
        valueBars[name] = (Func<ValueBar>)(() => new ValueBar((Map(getter(), minValue, maxValue, minOutValue ?? minValue, maxOutValue ?? maxValue)), minOutValue ?? minValue, maxOutValue ?? maxValue));

    public static void DisplayValueBar(string name, float value, float minValue = 0, float maxValue = 1, float? minOutValue = null, float? maxOutValue = null) =>    
        valueBars[name] = new ValueBar((Map(value, minValue, maxValue, minOutValue ?? minValue, maxOutValue ?? maxValue)), minOutValue ?? minValue, maxOutValue ?? maxValue);
   
    static float Map(float v, float inMin, float inMax, float outMin, float outMax) => (Mathf.Clamp((v - inMin) * (outMax - outMin) / (inMax - inMin) + outMin, outMin, outMax));

    public static void HideProgressBar(string name) => progressBars.Remove(name);
    public static void HideValueBar(string name) => valueBars.Remove(name);
    void OnEnable() => Application.logMessageReceivedThreaded += HandleLogThreaded;
    void OnDisable() => Application.logMessageReceivedThreaded -= HandleLogThreaded;

    void HandleLogThreaded(string logString, string stackTrace, LogType type)
    {
        lock (logLock)
            logQueue.Enqueue((logString, stackTrace, type));
    }

    static MsgType Log2MsgType(LogType type) =>
        type == LogType.Error || type == LogType.Exception ? MsgType.Error :
        type == LogType.Warning || type == LogType.Assert ? MsgType.Warn :
        MsgType.Debug; 

    void CalculateDevider()
    {
        if (textTMP == null) return;

        float width = textTMP.rectTransform.rect.width;
        string result = "";

        while (textTMP.GetPreferredValues(result + "------", width, 0).x < width)
            result += "-";

        _devider = $"<color=#000>{result}</color>\n";
    }

    public void CopyToClipboard()
    {
        string logs = System.Text.RegularExpressions.Regex.Replace(string.Join("\n", logHistory), "<.*?>", "");
        Clipboard.Copy(logs);
        Debugger.Log("Logs copied to the buffer");
    }
}
