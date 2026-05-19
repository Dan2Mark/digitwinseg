using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class TextureProcessor
{
    public static Texture2D ResizeTextureGPU(Texture2D src, int width, int height)
    {
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 0);
        Graphics.Blit(src, rt);

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        return tex;
    }
    public static Texture2D FlipVertical(Texture2D tex)
    {
        Texture2D flipped = new Texture2D(tex.width, tex.height);
        for (int y = 0; y < tex.height; y++)
            flipped.SetPixels(0, tex.height - 1 - y, tex.width, 1, tex.GetPixels(0, y, tex.width, 1));
        flipped.Apply();
        return flipped;
    }
}
