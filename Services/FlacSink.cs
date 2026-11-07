using System.Security.Cryptography;

namespace CdRipper.Services;

/// <summary>
/// A small, self-contained FLAC encoder for 44.1 kHz / 16-bit / stereo audio.
/// Uses fixed predictors (orders 0-4), partitioned Rice coding and automatic stereo decorrelation.
/// </summary>
public sealed class FlacSink : IPcmSink
{
    const int BlockSize = 4096;

    FileStream? _stream;
    IncrementalHash? _md5;
    readonly byte[] _pending = new byte[BlockSize * 4];
    int _pendingLen;
    long _totalSamples;
    uint _frameIndex;
    int _minFrameBytes = int.MaxValue, _maxFrameBytes;
    int _firstBlockSize;
    readonly BitWriter _bits = new();
    readonly int[] _l = new int[BlockSize], _r = new int[BlockSize], _m = new int[BlockSize], _s = new int[BlockSize];

    public string FilePath { get; }
    public FlacSink(string path) => FilePath = path;

    public void Begin(long totalPcmBytes)
    {
        _stream = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        _md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        _stream.Write(new byte[] { (byte)'f', (byte)'L', (byte)'a', (byte)'C' });
        _stream.Write(new byte[] { 0x80, 0, 0, 34 }); // last metadata block, STREAMINFO, 34 bytes
        _stream.Write(new byte[34]);                  // filled in by Finish()
    }

    public void Write(byte[] buffer, int count)
    {
        _md5!.AppendData(buffer, 0, count);
        int offset = 0;
        while (offset < count)
        {
            int take = Math.Min(_pending.Length - _pendingLen, count - offset);
            Buffer.BlockCopy(buffer, offset, _pending, _pendingLen, take);
            _pendingLen += take;
            offset += take;
            if (_pendingLen == _pending.Length)
            {
                EncodeFrame(BlockSize);
                _pendingLen = 0;
            }
        }
    }

    public void Finish()
    {
        if (_pendingLen > 0)
        {
            EncodeFrame(_pendingLen / 4);
            _pendingLen = 0;
        }

        var si = new byte[34];
        int block = _frameIndex <= 1 ? Math.Max(_firstBlockSize, 16) : BlockSize;
        si[0] = (byte)(block >> 8); si[1] = (byte)block;          // min block size
        si[2] = (byte)(block >> 8); si[3] = (byte)block;          // max block size
        int minF = _minFrameBytes == int.MaxValue ? 0 : _minFrameBytes;
        si[4] = (byte)(minF >> 16); si[5] = (byte)(minF >> 8); si[6] = (byte)minF;
        si[7] = (byte)(_maxFrameBytes >> 16); si[8] = (byte)(_maxFrameBytes >> 8); si[9] = (byte)_maxFrameBytes;
        ulong packed = (44100UL << 44) | (1UL << 41) | (15UL << 36) | (ulong)_totalSamples;
        for (int i = 0; i < 8; i++) si[10 + i] = (byte)(packed >> (56 - 8 * i));
        var md5 = _md5!.GetHashAndReset();
        Buffer.BlockCopy(md5, 0, si, 18, 16);

        _stream!.Seek(8, SeekOrigin.Begin);
        _stream.Write(si);
        _stream.Flush();
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
        _md5?.Dispose();
        _md5 = null;
    }

    // ------------------------------------------------------------------ frames

    void EncodeFrame(int n)
    {
        for (int i = 0; i < n; i++)
        {
            int p = i * 4;
            int l = (short)(_pending[p] | (_pending[p + 1] << 8));
            int r = (short)(_pending[p + 2] | (_pending[p + 3] << 8));
            _l[i] = l; _r[i] = r;
            _m[i] = (l + r) >> 1;
            _s[i] = l - r;
        }

        var sl = Subframe.Analyze(_l, n, 16);
        var sr = Subframe.Analyze(_r, n, 16);
        var sm = Subframe.Analyze(_m, n, 16);
        var ss = Subframe.Analyze(_s, n, 17);

        int cInd = sl.Cost + sr.Cost, cLs = sl.Cost + ss.Cost, cRs = sr.Cost + ss.Cost, cMs = sm.Cost + ss.Cost;
        int best = Math.Min(Math.Min(cInd, cLs), Math.Min(cRs, cMs));
        int assign;
        Subframe a, b;
        if (best == cInd) { assign = 1; a = sl; b = sr; }
        else if (best == cLs) { assign = 8; a = sl; b = ss; }
        else if (best == cRs) { assign = 9; a = ss; b = sr; }
        else { assign = 10; a = sm; b = ss; }

        var w = _bits;
        w.Reset();
        w.Write(0xFFF8, 16);                                   // sync + fixed block size
        int bsCode = n == BlockSize ? 12 : 7;
        w.Write((uint)((bsCode << 4) | 9), 8);                 // block size code, 44.1 kHz
        w.Write((uint)((assign << 4) | (4 << 1)), 8);          // channel assignment, 16 bits/sample
        WriteUtf8(w, _frameIndex);
        if (bsCode == 7) w.Write((uint)(n - 1), 16);
        w.Write(Crc8(w.Buffer, w.Length), 8);

        a.Write(w);
        b.Write(w);
        w.Align();
        w.Write(Crc16(w.Buffer, w.Length), 16);

        _stream!.Write(w.Buffer, 0, w.Length);
        if (_frameIndex == 0) _firstBlockSize = n;
        _minFrameBytes = Math.Min(_minFrameBytes, w.Length);
        _maxFrameBytes = Math.Max(_maxFrameBytes, w.Length);
        _totalSamples += n;
        _frameIndex++;
    }

    static void WriteUtf8(BitWriter w, uint v)
    {
        if (v < 0x80) { w.Write(v, 8); return; }
        int extra = v < 0x800 ? 1 : v < 0x10000 ? 2 : v < 0x200000 ? 3 : v < 0x4000000 ? 4 : 5;
        w.Write((uint)(((0xFF << (7 - extra)) & 0xFF) | (int)(v >> (6 * extra))), 8);
        for (int i = extra - 1; i >= 0; i--)
            w.Write(0x80u | ((v >> (6 * i)) & 0x3F), 8);
    }

    // ------------------------------------------------------------------ CRCs

    static readonly byte[] Crc8Table = BuildCrc8();
    static readonly ushort[] Crc16Table = BuildCrc16();

    static byte[] BuildCrc8()
    {
        var t = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            int c = i;
            for (int k = 0; k < 8; k++) c = (c & 0x80) != 0 ? ((c << 1) ^ 0x07) & 0xFF : (c << 1) & 0xFF;
            t[i] = (byte)c;
        }
        return t;
    }

    static ushort[] BuildCrc16()
    {
        var t = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            int c = i << 8;
            for (int k = 0; k < 8; k++) c = (c & 0x8000) != 0 ? ((c << 1) ^ 0x8005) & 0xFFFF : (c << 1) & 0xFFFF;
            t[i] = (ushort)c;
        }
        return t;
    }

    static uint Crc8(byte[] data, int length)
    {
        int c = 0;
        for (int i = 0; i < length; i++) c = Crc8Table[c ^ data[i]];
        return (uint)c;
    }

    static uint Crc16(byte[] data, int length)
    {
        int c = 0;
        for (int i = 0; i < length; i++) c = ((c << 8) & 0xFFFF) ^ Crc16Table[(c >> 8) ^ data[i]];
        return (uint)c;
    }

    // ------------------------------------------------------------------ bit writer

    sealed class BitWriter
    {
        byte[] _buf = new byte[1 << 16];
        int _len;
        ulong _acc;
        int _nbits;

        public byte[] Buffer => _buf;
        public int Length => _len;

        public void Reset() { _len = 0; _acc = 0; _nbits = 0; }

        void Put(byte b)
        {
            if (_len == _buf.Length) Array.Resize(ref _buf, _len * 2);
            _buf[_len++] = b;
        }

        public void Write(uint value, int bits)
        {
            if (bits == 0) return;
            _acc = (_acc << bits) | (value & (uint)((1UL << bits) - 1));
            _nbits += bits;
            while (_nbits >= 8)
            {
                _nbits -= 8;
                Put((byte)(_acc >> _nbits));
            }
            _acc &= (1UL << _nbits) - 1;
        }

        /// <summary>q zero bits followed by a one.</summary>
        public void Unary(int q)
        {
            while (q >= 31) { Write(0, 31); q -= 31; }
            Write(1, q + 1);
        }

        public void Align()
        {
            if (_nbits > 0) Write(0, 8 - _nbits);
        }
    }

    // ------------------------------------------------------------------ subframes

    sealed class Subframe
    {
        const int MaxParam = 14;

        public int Cost;            // size in bits
        int _type;                  // 0 constant, 1 verbatim, 2 fixed predictor
        int _order, _bps, _n, _p;
        int[] _x = Array.Empty<int>();
        int[] _ks = Array.Empty<int>();

        static int Residual(int[] x, int i, int order) => order switch
        {
            0 => x[i],
            1 => x[i] - x[i - 1],
            2 => x[i] - 2 * x[i - 1] + x[i - 2],
            3 => x[i] - 3 * x[i - 1] + 3 * x[i - 2] - x[i - 3],
            _ => x[i] - 4 * x[i - 1] + 6 * x[i - 2] - 4 * x[i - 3] + x[i - 4],
        };

        static uint ZigZag(int r) => (uint)((r << 1) ^ (r >> 31));

        public static Subframe Analyze(int[] x, int n, int bps)
        {
            var sf = new Subframe { _x = x, _n = n, _bps = bps };

            bool constant = true;
            for (int i = 1; i < n; i++)
                if (x[i] != x[0]) { constant = false; break; }
            if (constant) { sf._type = 0; sf.Cost = 8 + bps; return sf; }

            int verbatim = 8 + n * bps;
            sf._type = 1;
            sf.Cost = verbatim;
            if (n < 16) return sf;

            // Pick the predictor order with the smallest total residual magnitude.
            int bestOrder = 0;
            long bestSum = long.MaxValue;
            for (int order = 0; order <= 4; order++)
            {
                long sum = 0;
                for (int i = order; i < n; i++) sum += Math.Abs(Residual(x, i, order));
                if (sum < bestSum) { bestSum = sum; bestOrder = order; }
            }

            // Largest partition order (max 6) that divides the block and leaves > order samples per partition.
            int pmax = 0;
            for (int p = 1; p <= 6; p++)
                if (n % (1 << p) == 0 && (n >> p) > bestOrder) pmax = p;
            int fine = 1 << pmax, psz = n >> pmax;

            // Cost of every Rice parameter for every finest partition, so coarser levels can be summed.
            var table = new long[fine * (MaxParam + 1)];
            var counts = new int[fine];
            for (int i = bestOrder; i < n; i++)
            {
                int part = i / psz;
                counts[part]++;
                uint u = ZigZag(Residual(x, i, bestOrder));
                int baseIdx = part * (MaxParam + 1);
                for (int k = 0; k <= MaxParam; k++) table[baseIdx + k] += u >> k;
            }

            long bestBits = long.MaxValue;
            int bestP = 0;
            int[] bestKs = Array.Empty<int>();
            for (int p = 0; p <= pmax; p++)
            {
                int groups = 1 << p, step = fine >> p;
                long total = 2 + 4;
                var ks = new int[groups];
                for (int g = 0; g < groups; g++)
                {
                    long minCost = long.MaxValue;
                    int minK = 0;
                    for (int k = 0; k <= MaxParam; k++)
                    {
                        long c = 0;
                        for (int j = g * step; j < (g + 1) * step; j++)
                            c += table[j * (MaxParam + 1) + k] + (long)counts[j] * (k + 1);
                        if (c < minCost) { minCost = c; minK = k; }
                    }
                    ks[g] = minK;
                    total += 4 + minCost;
                }
                if (total < bestBits) { bestBits = total; bestP = p; bestKs = ks; }
            }

            long fixedCost = 8 + (long)bestOrder * bps + bestBits;
            if (fixedCost < verbatim)
            {
                sf._type = 2;
                sf._order = bestOrder;
                sf._p = bestP;
                sf._ks = bestKs;
                sf.Cost = (int)fixedCost;
            }
            return sf;
        }

        public void Write(BitWriter w)
        {
            switch (_type)
            {
                case 0:
                    w.Write(0, 8); // padding bit, type 000000, no wasted bits
                    w.Write((uint)_x[0], _bps);
                    break;
                case 1:
                    w.Write(1 << 1, 8); // type 000001 (verbatim)
                    for (int i = 0; i < _n; i++) w.Write((uint)_x[i], _bps);
                    break;
                default:
                    w.Write((uint)((8 + _order) << 1), 8); // type 001xxx (fixed, order xxx)
                    for (int i = 0; i < _order; i++) w.Write((uint)_x[i], _bps);
                    w.Write(0, 2);               // Rice coding, 4-bit parameters
                    w.Write((uint)_p, 4);
                    int psz = _n >> _p;
                    for (int part = 0; part < (1 << _p); part++)
                    {
                        int k = _ks[part];
                        w.Write((uint)k, 4);
                        int start = part == 0 ? _order : part * psz;
                        int end = (part + 1) * psz;
                        for (int i = start; i < end; i++)
                        {
                            uint u = ZigZag(Residual(_x, i, _order));
                            w.Unary((int)(u >> k));
                            if (k > 0) w.Write(u & ((1u << k) - 1), k);
                        }
                    }
                    break;
            }
        }
    }
}
