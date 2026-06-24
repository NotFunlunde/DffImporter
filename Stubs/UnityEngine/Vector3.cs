namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;

        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }

        public override string ToString() => $"({x:F2}, {y:F2}, {z:F2})";
    }
}
