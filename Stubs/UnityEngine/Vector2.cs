namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;

        public Vector2(float x, float y) { this.x = x; this.y = y; }

        public override string ToString() => $"({x:F2}, {y:F2})";

        public override bool Equals(object obj) => obj is Vector2 v && x == v.x && y == v.y;
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 16);
    }
}
