// QrEncoder.cs
// QR コード（モデル 2）を作る。誤り訂正は M（約 15%）、文字は UTF-8 のバイトのまま（8 ビットバイトモード）、型番は 1〜10。
// Discord の招待 URL（30 文字ほど）を、ワールドに置く画像にするためのもの。外部のライブラリは使わない。
// Unity に依存しない（System だけ）。Unity の外でも試せるように、古い C# の書き方だけを使う。
// 手順は ISO/IEC 18004 に沿う（データの符号化、リード・ソロモン符号、ブロックの組み替え、配置、マスクの選択）。

using System;
using System.Collections.Generic;
using System.Text;

namespace NagoSupporterGate.EditorTools
{
    public static class QrEncoder
    {
        // 誤り訂正 M の、型番ごとのブロックの組み立て: { 1 ブロックの誤り訂正の符号の数, 1 群目のブロック数, 1 群目のデータ数, 2 群目のブロック数, 2 群目のデータ数 }
        private static readonly int[][] EccM = new int[][]
        {
            null,
            new int[] { 10, 1, 16, 0, 0 },
            new int[] { 16, 1, 28, 0, 0 },
            new int[] { 26, 1, 44, 0, 0 },
            new int[] { 18, 2, 32, 0, 0 },
            new int[] { 24, 2, 43, 0, 0 },
            new int[] { 16, 4, 27, 0, 0 },
            new int[] { 18, 4, 31, 0, 0 },
            new int[] { 22, 2, 38, 2, 39 },
            new int[] { 22, 3, 36, 2, 37 },
            new int[] { 26, 4, 43, 1, 44 },
        };

        // 位置合わせのパターンの中心（型番ごと）
        private static readonly int[][] AlignPositions = new int[][]
        {
            null,
            new int[0],
            new int[] { 6, 18 },
            new int[] { 6, 22 },
            new int[] { 6, 26 },
            new int[] { 6, 30 },
            new int[] { 6, 34 },
            new int[] { 6, 22, 38 },
            new int[] { 6, 24, 42 },
            new int[] { 6, 26, 46 },
            new int[] { 6, 28, 50 },
        };

        public const int MaxVersion = 10;

        /// <summary>文字列を QR の模様にする。戻り値は [y, x]、true が黒。周りの余白（クワイエットゾーン）は含まない</summary>
        public static bool[,] Encode(string text)
        {
            int version;
            return Encode(text, out version);
        }

        public static bool[,] Encode(string text, out int version)
        {
            byte[] data = Encoding.UTF8.GetBytes(text ?? "");
            version = 0;
            for (int v = 1; v <= MaxVersion; v++)
            {
                int bits = 4 + (v <= 9 ? 8 : 16) + data.Length * 8;
                if (bits <= DataCodewords(v) * 8) { version = v; break; }
            }
            if (version == 0) throw new ArgumentException("QR に入りきらない長さです（" + data.Length + " バイト）");

            byte[] codewords = AddErrorCorrection(EncodeData(data, version), version);
            int size = 17 + 4 * version;
            bool[,] modules = new bool[size, size];
            bool[,] isFunction = new bool[size, size];
            DrawFunctionPatterns(modules, isFunction, version);
            DrawCodewords(modules, isFunction, codewords);

            // マスクは、8 通りのうち、見分けにくさの点数がいちばん低いものを選ぶ
            int best = 0;
            int bestScore = int.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                ApplyMask(modules, isFunction, mask);
                DrawFormatBits(modules, isFunction, mask);
                int score = Penalty(modules);
                if (score < bestScore) { best = mask; bestScore = score; }
                ApplyMask(modules, isFunction, mask);   // 同じマスクをもう一度かけると元に戻る
            }
            ApplyMask(modules, isFunction, best);
            DrawFormatBits(modules, isFunction, best);
            return modules;
        }

        private static int DataCodewords(int version)
        {
            int[] e = EccM[version];
            return e[1] * e[2] + e[3] * e[4];
        }

        // ---- データの符号化 ----

        private static byte[] EncodeData(byte[] data, int version)
        {
            List<bool> bits = new List<bool>();
            Append(bits, 0x4, 4);                                   // 8 ビットバイトモード
            Append(bits, data.Length, version <= 9 ? 8 : 16);       // 文字数
            foreach (byte b in data) Append(bits, b, 8);
            int capacity = DataCodewords(version) * 8;
            Append(bits, 0, Math.Min(4, capacity - bits.Count));    // 終端
            while (bits.Count % 8 != 0) bits.Add(false);
            for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) Append(bits, pad, 8);

            byte[] result = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++)
            {
                if (bits[i]) result[i >> 3] |= (byte)(0x80 >> (i & 7));
            }
            return result;
        }

        private static void Append(List<bool> bits, int value, int length)
        {
            for (int i = length - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
        }

        // ---- 誤り訂正（リード・ソロモン符号）とブロックの組み替え ----

        private static byte[] AddErrorCorrection(byte[] data, int version)
        {
            int[] e = EccM[version];
            int ecLength = e[0];
            int blockCount = e[1] + e[3];
            byte[] divisor = Generator(ecLength);
            List<byte[]> dataBlocks = new List<byte[]>();
            List<byte[]> ecBlocks = new List<byte[]>();
            int offset = 0;
            for (int i = 0; i < blockCount; i++)
            {
                int length = i < e[1] ? e[2] : e[4];
                byte[] block = new byte[length];
                Array.Copy(data, offset, block, 0, length);
                offset += length;
                dataBlocks.Add(block);
                ecBlocks.Add(Remainder(block, divisor));
            }

            // データの符号を、ブロックをまたいで 1 つずつ交互に並べ、そのあとに誤り訂正の符号を同じように並べる
            List<byte> result = new List<byte>();
            int maxData = Math.Max(e[2], e[4]);
            for (int i = 0; i < maxData; i++)
            {
                foreach (byte[] block in dataBlocks)
                {
                    if (i < block.Length) result.Add(block[i]);
                }
            }
            for (int i = 0; i < ecLength; i++)
            {
                foreach (byte[] block in ecBlocks) result.Add(block[i]);
            }
            return result.ToArray();
        }

        private static byte[] Generator(int degree)
        {
            byte[] result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < result.Length; j++)
                {
                    result[j] = (byte)Multiply(result[j], root);
                    if (j + 1 < result.Length) result[j] ^= result[j + 1];
                }
                root = Multiply(root, 0x02);
            }
            return result;
        }

        private static byte[] Remainder(byte[] data, byte[] divisor)
        {
            byte[] result = new byte[divisor.Length];
            foreach (byte b in data)
            {
                int factor = b ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[result.Length - 1] = 0;
                for (int i = 0; i < result.Length; i++) result[i] ^= (byte)Multiply(divisor[i], factor);
            }
            return result;
        }

        // GF(2^8) の掛け算（既約多項式 0x11D）
        private static int Multiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z;
        }

        // ---- 機能パターン（位置検出・タイミング・位置合わせ・形式情報・型番情報） ----

        private static void Set(bool[,] modules, bool[,] isFunction, int x, int y, bool dark)
        {
            modules[y, x] = dark;
            isFunction[y, x] = true;
        }

        private static void DrawFunctionPatterns(bool[,] modules, bool[,] isFunction, int version)
        {
            int size = modules.GetLength(0);
            for (int i = 0; i < size; i++)
            {
                Set(modules, isFunction, 6, i, i % 2 == 0);
                Set(modules, isFunction, i, 6, i % 2 == 0);
            }
            DrawFinder(modules, isFunction, 3, 3);
            DrawFinder(modules, isFunction, size - 4, 3);
            DrawFinder(modules, isFunction, 3, size - 4);

            int[] align = AlignPositions[version];
            int n = align.Length;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    // 位置検出のパターンと重なる 3 つの角は飛ばす
                    if ((i == 0 && j == 0) || (i == 0 && j == n - 1) || (i == n - 1 && j == 0)) continue;
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            Set(modules, isFunction, align[i] + dx, align[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                        }
                    }
                }
            }

            DrawFormatBits(modules, isFunction, 0);   // 場所を押さえておく（マスクを決めてから書き直す）
            DrawVersion(modules, isFunction, version);
        }

        private static void DrawFinder(bool[,] modules, bool[,] isFunction, int cx, int cy)
        {
            int size = modules.GetLength(0);
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || x >= size || y < 0 || y >= size) continue;
                    int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    Set(modules, isFunction, x, y, dist != 2 && dist != 4);
                }
            }
        }

        private static void DrawFormatBits(bool[,] modules, bool[,] isFunction, int mask)
        {
            int size = modules.GetLength(0);
            int data = (0 << 3) | mask;   // 誤り訂正 M は 00
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = ((data << 10) | rem) ^ 0x5412;

            for (int i = 0; i <= 5; i++) Set(modules, isFunction, 8, i, Bit(bits, i));
            Set(modules, isFunction, 8, 7, Bit(bits, 6));
            Set(modules, isFunction, 8, 8, Bit(bits, 7));
            Set(modules, isFunction, 7, 8, Bit(bits, 8));
            for (int i = 9; i < 15; i++) Set(modules, isFunction, 14 - i, 8, Bit(bits, i));

            for (int i = 0; i < 8; i++) Set(modules, isFunction, size - 1 - i, 8, Bit(bits, i));
            for (int i = 8; i < 15; i++) Set(modules, isFunction, 8, size - 15 + i, Bit(bits, i));
            Set(modules, isFunction, 8, size - 8, true);   // 常に黒の 1 つ
        }

        private static void DrawVersion(bool[,] modules, bool[,] isFunction, int version)
        {
            if (version < 7) return;
            int size = modules.GetLength(0);
            int rem = version;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = (version << 12) | rem;
            for (int i = 0; i < 18; i++)
            {
                bool bit = Bit(bits, i);
                int a = size - 11 + i % 3;
                int b = i / 3;
                Set(modules, isFunction, a, b, bit);
                Set(modules, isFunction, b, a, bit);
            }
        }

        private static bool Bit(int value, int i)
        {
            return ((value >> i) & 1) != 0;
        }

        // ---- データの配置とマスク ----

        private static void DrawCodewords(bool[,] modules, bool[,] isFunction, byte[] data)
        {
            int size = modules.GetLength(0);
            int i = 0;
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;   // 縦のタイミングパターンの列は飛ばす
                for (int vert = 0; vert < size; vert++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? size - 1 - vert : vert;
                        if (isFunction[y, x]) continue;
                        if (i < data.Length * 8)
                        {
                            modules[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                            i++;
                        }
                        // 余ったところ（残りのビット）は白のまま
                    }
                }
            }
        }

        private static void ApplyMask(bool[,] modules, bool[,] isFunction, int mask)
        {
            int size = modules.GetLength(0);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (isFunction[y, x]) continue;
                    bool invert;
                    switch (mask)
                    {
                        case 0: invert = (x + y) % 2 == 0; break;
                        case 1: invert = y % 2 == 0; break;
                        case 2: invert = x % 3 == 0; break;
                        case 3: invert = (x + y) % 3 == 0; break;
                        case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                        case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                        case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                        default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                    }
                    if (invert) modules[y, x] = !modules[y, x];
                }
            }
        }

        // 見分けにくさの点数（同じ色の連続、2x2 のかたまり、位置検出に似た並び、黒の割合）
        private static int Penalty(bool[,] m)
        {
            int size = m.GetLength(0);
            int score = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int a = 0; a < size; a++)
                {
                    int run = 1;
                    for (int b = 1; b < size; b++)
                    {
                        bool prev = pass == 0 ? m[a, b - 1] : m[b - 1, a];
                        bool cur = pass == 0 ? m[a, b] : m[b, a];
                        if (cur == prev) run++;
                        else
                        {
                            if (run >= 5) score += 3 + run - 5;
                            run = 1;
                        }
                    }
                    if (run >= 5) score += 3 + run - 5;
                    for (int b = 0; b + 10 < size; b++)
                    {
                        if (FinderLike(m, a, b, pass == 1)) score += 40;
                    }
                }
            }
            for (int y = 0; y + 1 < size; y++)
            {
                for (int x = 0; x + 1 < size; x++)
                {
                    bool c = m[y, x];
                    if (c == m[y, x + 1] && c == m[y + 1, x] && c == m[y + 1, x + 1]) score += 3;
                }
            }
            int dark = 0;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) if (m[y, x]) dark++;
            int total = size * size;
            int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;
            score += k * 10;
            return score;
        }

        private static readonly bool[] Pattern1 = { true, false, true, true, true, false, true, false, false, false, false };
        private static readonly bool[] Pattern2 = { false, false, false, false, true, false, true, true, true, false, true };

        private static bool FinderLike(bool[,] m, int a, int b, bool column)
        {
            bool p1 = true, p2 = true;
            for (int i = 0; i < 11; i++)
            {
                bool v = column ? m[b + i, a] : m[a, b + i];
                if (v != Pattern1[i]) p1 = false;
                if (v != Pattern2[i]) p2 = false;
            }
            return p1 || p2;
        }
    }
}
