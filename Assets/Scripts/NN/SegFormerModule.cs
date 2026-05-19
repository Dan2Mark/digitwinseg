using UnityEngine;
using Unity.Barracuda;

public class SegFormerModule : NNModule
{
    public int inputSize = 512;
    public Texture2D mask;

    public override Tensor PrepareInput(Texture2D source)
    {
        Texture2D resized = TextureProcessor.ResizeTextureGPU(source, inputSize, inputSize);
        return new Tensor(resized, channels: 3);

    }

    public override void ProcessOutput(Tensor output)
    {
        // Example: segmentation mask
        int classes = output.shape[1];
        int h = output.shape[2];
        int w = output.shape[3];

        // Convert to mask texture
        mask = new Texture2D(w, h);

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int bestClass = 0;
                float bestScore = float.MinValue;

                for (int c = 0; c < classes; c++)
                {
                    float score = output[0, c, y, x];
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestClass = c;
                    }
                }

                mask.SetPixel(x, y, ClassToColor(bestClass));
            }
        }
        mask = TextureProcessor.FlipVertical(mask);

        mask.Apply();
        OnSegmentationReady(mask);
        Debugger.Log($"Segmentation output: classes={classes}, w={w}, h={h}");
    }

    private Color ClassToColor(int cls)
    {
        // simple palette
        return new Color((cls * 53 % 255) / 255f, (cls * 97 % 255) / 255f, (cls * 199 % 255) / 255f);
    }

    protected virtual void OnSegmentationReady(Texture2D mask)
    {
        Debug.Log("Segmentation mask ready");
    }

}
