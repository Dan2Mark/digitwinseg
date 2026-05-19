using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Debugger : MonoBehaviour
{
    public static Debugger Instance;

    [Header("UI")]
    public TextMeshProUGUI textTMP;

    private static string buffer = "";
    private const int maxLines = 20;

    void Awake()
    {
        Instance = this;
        Clear();
    }

    public static void Log(string msg)
    {
        buffer += msg + "\n";

        // Ограничиваем количество строк
        var lines = buffer.Split('\n');
        if (lines.Length > maxLines)
            buffer = string.Join("\n", lines, lines.Length - maxLines, maxLines);

        if (Instance != null)
        {
            if (Instance.textTMP != null)
                Instance.textTMP.text = buffer;
        }
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
}
