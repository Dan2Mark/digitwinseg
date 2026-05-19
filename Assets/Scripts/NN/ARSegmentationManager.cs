using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.UI;
using Unity.Barracuda;

public class ARSegmentationManager : NNManager
{
    public ARCameraManager cameraManager;
    public RawImage overlay;
    public string segmentationModuleType = "segmentation";
    [Range(0f, 1f)] public float alpha = 0.35f;

    private Texture2D mask;

    void Update()
    {
        if (!cameraManager.TryAcquireLatestCpuImage(out var image))
            return;
        Texture2D frame = ARCameraUtils.ConvertToTexture(image);
        image.Dispose();

        Process(frame);

        Destroy(frame);
    }
    public void Process(Texture2D frame)
    {
        foreach (var module in modules)
        {
            if (module == null) continue;

            Tensor input = module.PrepareInput(frame);
            Tensor output = module.Run(input);

            Debugger.Log($"Module: {module.GetType().Name}");
            if (module.GetType().Name == "SegFormerModule")
            {
                module.ProcessOutput(output);
                mask = ((SegFormerModule)module).mask;
            }

            input.Dispose();
            output.Dispose();
        }
        Debugger.Log("Mask received: " + (mask != null));

        if (mask != null)
        {
            overlay.texture = mask;
            // Явно передаем текстуру в материал шейдера
            overlay.material.SetTexture("_MainTex", mask);
            overlay.material.SetFloat("_Alpha", alpha);

        }
    }
}
