using Unity.Barracuda;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public abstract class NNManager : MonoBehaviour
{
    public NNModule[] modules;

    private Texture2D cameraTexture;

    public void Process(Texture2D inputTexture)
    {
        foreach (var module in modules)
        {
            Tensor input = module.PrepareInput(inputTexture);
            Tensor output = module.Run(input);
            module.ProcessOutput(output);

            input.Dispose();
            output.Dispose();
        }
    }
}
