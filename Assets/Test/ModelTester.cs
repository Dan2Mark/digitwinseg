using System;
using Unity.Sentis;
using UnityEngine;

public class ModelTester : MonoBehaviour
{
    [SerializeField] private ModelAsset modelAsset;
    [SerializeField] private Texture2D testImage;

    private const int Size = 512;

    private void Start()
    {
        TestTensor();
    }

    private void TestTensor()
    {
        try
        {
            Debug.Log($"TEST IMAGE: {testImage.width}x{testImage.height}"); Model model = ModelLoader.Load(modelAsset);

            model.AddOutput("tensor10", 10);
            model.AddOutput("tensor13", 13);
            model.AddOutput("tensor15", 15);
            Tensor<float> input = CreateInputTensor(testImage); 
            Worker worker = new Worker(model, BackendType.CPU); worker.Schedule(input);
            TestOutput(worker, "tensor10");
            TestOutput(worker, "tensor13");
            TestOutput(worker, "tensor15");

            input.Dispose(); worker.Dispose(); } catch (Exception e) { Debug.LogError("MODEL TEST ERROR:\n" + e); } } 
    
    private void TestOutput(Worker worker, string name) { Tensor<float> output = worker.PeekOutput(name) as Tensor<float>; if (output == null) throw new Exception($"Output '{name}' is null"); Debug.Log($"{name} SHAPE: {output.shape}"); Tensor<float> cpu = output.ReadbackAndClone(); float[] data = cpu.DownloadToArray(); float min = float.MaxValue; float max = float.MinValue; double sum = 0.0; for (int i = 0; i < data.Length; i++) { float v = data[i]; if (v < min) min = v; if (v > max) max = v; sum += v; } Debug.Log($"{name} LENGTH: {data.Length}"); Debug.Log($"{name} MIN: {min}"); Debug.Log($"{name} MAX: {max}"); Debug.Log($"{name} MEAN: {sum / data.Length}"); Debug.Log($"{name} FIRST 20:"); for (int i = 0; i < Mathf.Min(20, data.Length); i++) Debug.Log($"{name}[{i}] = {data[i]}"); cpu.Dispose(); }
    private Tensor<float> CreateInputTensor(Texture2D tex)
    {
        Color32[] pixels = tex.GetPixels32();

        int plane = Size * Size;
        float[] data = new float[3 * plane];

        for (int y = 0; y < Size; y++)
        {
            int srcY = Size - 1 - y;

            for (int x = 0; x < Size; x++)
            {
                int srcIndex = srcY * Size + x;
                int dstIndex = y * Size + x;

                Color32 p = pixels[srcIndex];

                data[dstIndex] =
                    (p.r / 255f - 0.485f) / 0.229f;

                data[plane + dstIndex] =
                    (p.g / 255f - 0.456f) / 0.224f;

                data[2 * plane + dstIndex] =
                    (p.b / 255f - 0.406f) / 0.225f;
            }
        }

        return new Tensor<float>(
            new TensorShape(1, 3, Size, Size),
            data
        );
    }
}
