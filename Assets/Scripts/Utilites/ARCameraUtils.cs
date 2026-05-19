using Unity.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public static class ARCameraUtils
{
    public static Texture2D ConvertToTexture(XRCpuImage image)
    {
        var format = TextureFormat.RGBA32;

        var conversionParams = new XRCpuImage.ConversionParams
        {
            inputRect = new RectInt(0, 0, image.width, image.height),
            outputDimensions = new Vector2Int(image.width, image.height),
            outputFormat = TextureFormat.RGBA32,
            transformation = XRCpuImage.Transformation.MirrorX
        };

        var raw = new byte[image.GetConvertedDataSize(conversionParams)];
        var rawSlice = new NativeArray<byte>(raw, Allocator.Temp);
        image.Convert(conversionParams, rawSlice);

        Texture2D tex = new Texture2D(image.width, image.height, format, false);
        tex.LoadRawTextureData(raw);
        tex.Apply();

        return tex;
    }
}
