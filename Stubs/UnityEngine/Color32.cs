namespace UnityEngine
{
    public struct Color32
    {
        public byte r, g, b, a;

        public Color32(byte r, byte g, byte b, byte a)
        {
            this.r = r; this.g = g; this.b = b; this.a = a;
        }

        public override string ToString() => $"RGBA({r}, {g}, {b}, {a})";

        public override bool Equals(object obj) =>
            obj is Color32 c && r == c.r && g == c.g && b == c.b && a == c.a;

        public override int GetHashCode() => (r << 24) | (g << 16) | (b << 8) | a;
    }
}
