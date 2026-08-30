using System;
using Unity.Collections;
using Unity.Sentis;
using UnityEngine;
using UnityEngine.UI;

public class Segmentator : MonoBehaviour
{
    [Header("Model")][SerializeField] private ModelAsset modelAsset;
    [Header("Display")] public RawImage displayImage; public bool enableDisplay = true;

    private Worker worker;
    private Model model;
    private Texture2D displayTexture;

    private const int Size = 512, Pixels = Size * Size, Classes = 7;

    private readonly Color32[] palette =
    {
        new(70,190,255,255), new(255,170,50,255), new(255,255,255,255),
        new(255,0,0,255), new(0,50,255,255), new(255,0,255,255),
        new(0,0,0,255)
    };

    private void Awake()
    {
        model = ModelLoader.Load(modelAsset);
        worker = new Worker(model, BackendType.CPU);

        if (enableDisplay)
        {
            displayTexture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            displayTexture.SetPixels32(new Color32[Pixels]);
            displayTexture.Apply();
            displayImage.texture = displayTexture;
            displayImage.enabled = true;
        }
    }

    public byte[] RunSegmentation(Texture sourceTexture)
    {
        try
        {
            Texture2D inputTexture = PrepareInputTexture(sourceTexture);
            Tensor<float> inputTensor = CreateInputTensor(inputTexture);

            worker.Schedule(inputTensor);
            Tensor<float> output = worker.PeekOutput() as Tensor<float>;
            if (output == null) throw new Exception("Sentis output is null");

            Tensor<float> outCpu = output.ReadbackAndClone();
            float[] data = outCpu.DownloadToArray();

            byte[] result = new byte[Pixels];
            Color32[] displayPixels = enableDisplay ? new Color32[Pixels] : null;

            for (int y = 0; y < Size; y++)
            {
                int srcY = Size - 1 - y;

                for (int x = 0; x < Size; x++)
                {
                    int srcIndex = srcY * Size + x;
                    int dstIndex = y * Size + x;

                    int best = 0;
                    float bestVal = data[srcIndex];

                    for (int c = 1; c < Classes; c++)
                    {
                        float v = data[c * Pixels + srcIndex];

                        if (v > bestVal)
                        {
                            bestVal = v;
                            best = c;
                        }
                    }

                    result[dstIndex] = (byte)best;

                    if (enableDisplay)
                        displayPixels[dstIndex] = palette[best];
                }
            }

            outCpu.Dispose();
            inputTensor.Dispose();
            if (inputTexture != sourceTexture) Destroy(inputTexture);

            if (enableDisplay)
            {
                displayTexture.SetPixels32(displayPixels);
                displayTexture.Apply();
            }

            return result;
        }
        catch (Exception e)
        {
            Debugger.Log(e.Message);
            Debug.LogError("SENTIS ERROR:\n" + e);
            return null;
        }
    }

    private Tensor<float> CreateInputTensor(Texture2D tex)
    {
        Color32[] px = tex.GetPixels32();
        float[] arr = new float[3 * Pixels];
        int plane = Size * Size;
        for (int y = 0; y < Size; y++)
        {
            int srcY = Size - 1 - y;

            for (int x = 0; x < Size; x++)
            {
                int srcIndex = srcY * Size + x;
                int dstIndex = y * Size + x;

                Color32 p = px[srcIndex];

                arr[dstIndex] =
                    (p.r / 255f - 0.485f) / 0.229f;

                arr[plane + dstIndex] =
                    (p.g / 255f - 0.456f) / 0.224f;

                arr[2 * plane + dstIndex] =
                    (p.b / 255f - 0.406f) / 0.225f;
            }
        }

        return new Tensor<float>(new TensorShape(1, 3, Size, Size), arr);
    }

    private Texture2D PrepareInputTexture(Texture src)
    {
        RenderTexture rt = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        bool prev = GL.sRGBWrite; GL.sRGBWrite = false;

        Graphics.Blit(src, rt);
        GL.sRGBWrite = prev;

        RenderTexture prevRT = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        tex.Apply();

        RenderTexture.active = prevRT;
        RenderTexture.ReleaseTemporary(rt);

        System.IO.File.WriteAllBytes(
            System.IO.Path.Combine(Application.persistentDataPath, "unity_input.png"),
            tex.EncodeToPNG()
        );

        return tex;
    }

    private void OnDestroy()
    {
        worker?.Dispose();
        if (displayTexture != null) Destroy(displayTexture);
    }
}
