namespace UnityEngine
{
    public struct Vector4
    {
        public float x, y, z, w;

        public Vector4(float x, float y, float z, float w)
        {
            this.x = x; this.y = y; this.z = z; this.w = w;
        }

        public static implicit operator Vector3(Vector4 v) => new Vector3(v.x, v.y, v.z);
    }
}
