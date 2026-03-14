// Polyfill for System.HashCode which is not available in netstandard2.0
#if !NET6_0_OR_GREATER
namespace System
{
    /// <summary>
    /// Minimal polyfill of System.HashCode for netstandard2.0.
    /// </summary>
    internal struct HashCode
    {
        private int _hash;
        private bool _initialized;

        public void Add<T>(T value)
        {
            var h = value?.GetHashCode() ?? 0;
            if (!_initialized)
            {
                _hash = h;
                _initialized = true;
            }
            else
            {
                _hash = unchecked(_hash * -1521134295 + h);
            }
        }

        public int ToHashCode() => _hash;

        public static int Combine<T1>(T1 v1)
        {
            var hc = new HashCode();
            hc.Add(v1);
            return hc.ToHashCode();
        }

        public static int Combine<T1, T2>(T1 v1, T2 v2)
        {
            var hc = new HashCode();
            hc.Add(v1);
            hc.Add(v2);
            return hc.ToHashCode();
        }

        public static int Combine<T1, T2, T3>(T1 v1, T2 v2, T3 v3)
        {
            var hc = new HashCode();
            hc.Add(v1);
            hc.Add(v2);
            hc.Add(v3);
            return hc.ToHashCode();
        }

        public static int Combine<T1, T2, T3, T4>(T1 v1, T2 v2, T3 v3, T4 v4)
        {
            var hc = new HashCode();
            hc.Add(v1);
            hc.Add(v2);
            hc.Add(v3);
            hc.Add(v4);
            return hc.ToHashCode();
        }

        public static int Combine<T1, T2, T3, T4, T5>(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5)
        {
            var hc = new HashCode();
            hc.Add(v1);
            hc.Add(v2);
            hc.Add(v3);
            hc.Add(v4);
            hc.Add(v5);
            return hc.ToHashCode();
        }

        public static int Combine<T1, T2, T3, T4, T5, T6>(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6)
        {
            var hc = new HashCode();
            hc.Add(v1);
            hc.Add(v2);
            hc.Add(v3);
            hc.Add(v4);
            hc.Add(v5);
            hc.Add(v6);
            return hc.ToHashCode();
        }

        public static int Combine<T1, T2, T3, T4, T5, T6, T7>(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6, T7 v7)
        {
            var hc = new HashCode();
            hc.Add(v1);
            hc.Add(v2);
            hc.Add(v3);
            hc.Add(v4);
            hc.Add(v5);
            hc.Add(v6);
            hc.Add(v7);
            return hc.ToHashCode();
        }

        public static int Combine<T1, T2, T3, T4, T5, T6, T7, T8>(T1 v1, T2 v2, T3 v3, T4 v4, T5 v5, T6 v6, T7 v7, T8 v8)
        {
            var hc = new HashCode();
            hc.Add(v1);
            hc.Add(v2);
            hc.Add(v3);
            hc.Add(v4);
            hc.Add(v5);
            hc.Add(v6);
            hc.Add(v7);
            hc.Add(v8);
            return hc.ToHashCode();
        }
    }
}
#endif
