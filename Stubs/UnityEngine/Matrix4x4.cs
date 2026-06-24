namespace UnityEngine
{
    public struct Matrix4x4
    {
        private float[] m;

        private void EnsureInit()
        {
            if (m == null) m = new float[16];
        }

        public float this[int row, int col]
        {
            get { EnsureInit(); return m[row + col * 4]; }
            set { EnsureInit(); m[row + col * 4] = value; }
        }

        public static Matrix4x4 identity
        {
            get
            {
                var mat = new Matrix4x4();
                mat.EnsureInit();
                mat[0, 0] = 1; mat[1, 1] = 1; mat[2, 2] = 1; mat[3, 3] = 1;
                return mat;
            }
        }

        public void SetColumn(int index, Vector4 column)
        {
            EnsureInit();
            this[0, index] = column.x;
            this[1, index] = column.y;
            this[2, index] = column.z;
            this[3, index] = column.w;
        }

        public Vector4 GetColumn(int index)
        {
            EnsureInit();
            return new Vector4(this[0, index], this[1, index], this[2, index], this[3, index]);
        }

        public Quaternion rotation => Quaternion.identity;
    }
}
