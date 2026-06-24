namespace UnityEngine
{
    public enum TextureFormat
    {
        RGBA32 = 4
    }

    public class Texture2D
    {
        public string name;
        public int width { get; }
        public int height { get; }
        public Color32[] pixels;

        public Texture2D(int width, int height, TextureFormat format, bool mipChain)
        {
            this.width = width;
            this.height = height;
            pixels = new Color32[width * height];
        }

        public void SetPixels32(Color32[] colors) => pixels = colors;
        public void Apply(bool updateMipmaps) { }
    }
}
