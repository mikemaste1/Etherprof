namespace Etherprof.Core;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Lightweight, self-contained QR Code generator supporting Byte encoding and Error Correction Level M.
/// Produces a boolean matrix [row, col] where true = black module, false = white module.
/// </summary>
public static class QrCodeGenerator
{
    // Version parameters for Level M (ECC level Medium: ~15% recovery)
    // Format: (Version, TotalCodewords, EccCodewordsPerBlock, NumBlocksGroup1, DataCodewordsBlockG1, NumBlocksGroup2, DataCodewordsBlockG2)
    private readonly struct VersionInfo
    {
        public readonly int Version;
        public readonly int TotalDataBytes;
        public readonly int EccBytesPerBlock;
        public readonly int Blocks1;
        public readonly int DataBytesBlock1;
        public readonly int Blocks2;
        public readonly int DataBytesBlock2;
        public readonly int[] AlignmentPatterns;

        public VersionInfo(int v, int totalData, int eccPerBlock, int b1, int db1, int b2, int db2, int[] align)
        {
            Version = v;
            TotalDataBytes = totalData;
            EccBytesPerBlock = eccPerBlock;
            Blocks1 = b1;
            DataBytesBlock1 = db1;
            Blocks2 = b2;
            DataBytesBlock2 = db2;
            AlignmentPatterns = align;
        }
    }

    private static readonly VersionInfo[] Versions =
    [
        new VersionInfo(1,  16, 10, 1, 16, 0,  0, []),
        new VersionInfo(2,  28, 16, 1, 28, 0,  0, [6, 18]),
        new VersionInfo(3,  44, 26, 1, 44, 0,  0, [6, 22]),
        new VersionInfo(4,  64, 18, 2, 32, 0,  0, [6, 26]),
        new VersionInfo(5,  86, 24, 2, 43, 0,  0, [6, 30]),
        new VersionInfo(6, 108, 16, 4, 27, 0,  0, [6, 34]),
        new VersionInfo(7, 124, 18, 4, 31, 0,  0, [6, 22, 38]),
        new VersionInfo(8, 154, 22, 2, 38, 2, 39, [6, 24, 42]),
        new VersionInfo(9, 182, 22, 3, 36, 2, 37, [6, 26, 46]),
        new VersionInfo(10, 216, 26, 4, 43, 1, 44, [6, 28, 50]),
    ];

    // Precomputed Galois Field tables (GF(256), poly 0x11D)
    private static readonly byte[] ExpTable = new byte[512];
    private static readonly byte[] LogTable = new byte[256];

    static QrCodeGenerator()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            ExpTable[i] = (byte)x;
            ExpTable[i + 255] = (byte)x;
            LogTable[x] = (byte)i;
            x <<= 1;
            if ((x & 0x100) != 0) x ^= 0x11D;
        }
    }

    private static byte GfMultiply(byte a, byte b)
    {
        if (a == 0 || b == 0) return 0;
        return ExpTable[LogTable[a] + LogTable[b]];
    }

    /// <summary>
    /// Generates a QR Code matrix [row, col] for the given text using Error Correction Level M.
    /// </summary>
    public static bool[,] GenerateMatrix(string text)
    {
        if (string.IsNullOrEmpty(text))
            text = " ";

        byte[] textBytes = Encoding.UTF8.GetBytes(text);

        // Find smallest version that fits data (Mode: Byte)
        VersionInfo? selectedVer = null;
        foreach (var v in Versions)
        {
            // 4 bits mode + 8 bits count + data bits
            int requiredBits = 4 + 8 + (textBytes.Length * 8);
            if (requiredBits <= v.TotalDataBytes * 8)
            {
                selectedVer = v;
                break;
            }
        }

        if (selectedVer is null)
        {
            throw new ArgumentException($"Text is too long for QR versions 1-10 (Length: {textBytes.Length} bytes)");
        }

        var ver = selectedVer.Value;

        // 1. Data Bitstream Encoding
        var bitStream = new List<bool>();

        // Mode Indicator: Byte mode is 0100
        AppendBits(bitStream, 0b0100, 4);

        // Character Count: 8 bits for versions 1-9 (16 for 10-26 in byte mode, but 8 for 1-9, let's handle 10: 16 bits for ver 10)
        int countBits = ver.Version >= 10 ? 16 : 8;
        AppendBits(bitStream, textBytes.Length, countBits);

        // Data bytes
        foreach (byte b in textBytes)
        {
            AppendBits(bitStream, b, 8);
        }

        // Terminator (up to 4 zeroes)
        int maxDataBits = ver.TotalDataBytes * 8;
        int termLen = Math.Min(4, maxDataBits - bitStream.Count);
        for (int i = 0; i < termLen; i++) bitStream.Add(false);

        // Pad to 8-bit boundary
        while (bitStream.Count % 8 != 0 && bitStream.Count < maxDataBits)
        {
            bitStream.Add(false);
        }

        // Pad bytes (0xEC, 0x11 alternating)
        byte[] padBytes = [0xEC, 0x11];
        int padIdx = 0;
        while (bitStream.Count < maxDataBits)
        {
            AppendBits(bitStream, padBytes[padIdx % 2], 8);
            padIdx++;
        }

        // Convert bitstream to data codewords
        byte[] dataCodewords = new byte[ver.TotalDataBytes];
        for (int i = 0; i < ver.TotalDataBytes; i++)
        {
            byte val = 0;
            for (int bit = 0; bit < 8; bit++)
            {
                if (bitStream[i * 8 + bit])
                    val |= (byte)(1 << (7 - bit));
            }
            dataCodewords[i] = val;
        }

        // 2. Error Correction Blocks Generation
        int totalBlocks = ver.Blocks1 + ver.Blocks2;
        var dataBlocks = new byte[totalBlocks][];
        var eccBlocks = new byte[totalBlocks][];

        int offset = 0;
        for (int b = 0; b < totalBlocks; b++)
        {
            int blockSize = (b < ver.Blocks1) ? ver.DataBytesBlock1 : ver.DataBytesBlock2;
            dataBlocks[b] = new byte[blockSize];
            Array.Copy(dataCodewords, offset, dataBlocks[b], 0, blockSize);
            offset += blockSize;

            eccBlocks[b] = ComputeEcc(dataBlocks[b], ver.EccBytesPerBlock);
        }

        // 3. Interleaving
        var finalCodewords = new List<byte>();
        int maxDataBlockLen = Math.Max(ver.DataBytesBlock1, ver.DataBytesBlock2);
        for (int i = 0; i < maxDataBlockLen; i++)
        {
            for (int b = 0; b < totalBlocks; b++)
            {
                if (i < dataBlocks[b].Length)
                    finalCodewords.Add(dataBlocks[b][i]);
            }
        }

        for (int i = 0; i < ver.EccBytesPerBlock; i++)
        {
            for (int b = 0; b < totalBlocks; b++)
            {
                finalCodewords.Add(eccBlocks[b][i]);
            }
        }

        // 4. Matrix Population
        int moduleCount = 17 + 4 * ver.Version;
        var matrix = new bool[moduleCount, moduleCount];
        var isFunction = new bool[moduleCount, moduleCount];

        // Finder patterns
        AddFinderPattern(matrix, isFunction, 0, 0);
        AddFinderPattern(matrix, isFunction, moduleCount - 7, 0);
        AddFinderPattern(matrix, isFunction, 0, moduleCount - 7);

        // Alignment patterns
        if (ver.AlignmentPatterns.Length > 0)
        {
            foreach (int r in ver.AlignmentPatterns)
            {
                foreach (int c in ver.AlignmentPatterns)
                {
                    if (isFunction[r, c]) continue;
                    AddAlignmentPattern(matrix, isFunction, r, c);
                }
            }
        }

        // Timing patterns
        for (int i = 8; i < moduleCount - 8; i++)
        {
            bool val = (i % 2 == 0);
            if (!isFunction[6, i]) { matrix[6, i] = val; isFunction[6, i] = true; }
            if (!isFunction[i, 6]) { matrix[i, 6] = val; isFunction[i, 6] = true; }
        }

        // Dark module
        matrix[4 * ver.Version + 9, 8] = true;
        isFunction[4 * ver.Version + 9, 8] = true;

        // Reserve format info areas
        for (int i = 0; i < 9; i++)
        {
            if (i != 6) { isFunction[8, i] = true; isFunction[i, 8] = true; }
        }
        for (int i = 0; i < 8; i++)
        {
            isFunction[8, moduleCount - 1 - i] = true;
            isFunction[moduleCount - 1 - i, 8] = true;
        }

        // Place data bits in zig-zag
        var dataBits = new List<bool>();
        foreach (byte b in finalCodewords)
        {
            for (int bit = 7; bit >= 0; bit--)
            {
                dataBits.Add(((b >> bit) & 1) == 1);
            }
        }

        int dataIndex = 0;
        int col = moduleCount - 1;
        bool upward = true;

        while (col > 0)
        {
            if (col == 6) col--; // Skip timing column

            int rowStart = upward ? moduleCount - 1 : 0;
            int rowEnd = upward ? -1 : moduleCount;
            int rowStep = upward ? -1 : 1;

            for (int row = rowStart; row != rowEnd; row += rowStep)
            {
                for (int c = 0; c < 2; c++)
                {
                    int currCol = col - c;
                    if (!isFunction[row, currCol])
                    {
                        bool bit = dataIndex < dataBits.Count && dataBits[dataIndex++];

                        // Mask 0: (row + col) % 2 == 0
                        bool mask = ((row + currCol) % 2 == 0);
                        matrix[row, currCol] = bit ^ mask;
                    }
                }
            }

            upward = !upward;
            col -= 2;
        }

        // Format Information: Level M (00) + Mask 0 (000) = 00000
        // BCH error correction for 00000 -> 0x0000 ^ 0x5412 = 0x5412 (0101010000010010)
        // Bit representation for (Level M, Mask 0): 101010000010010
        int formatBits = 0b101010000010010;
        ApplyFormatBits(matrix, moduleCount, formatBits);

        return matrix;
    }

    private static void AppendBits(List<bool> stream, int value, int bitCount)
    {
        for (int i = bitCount - 1; i >= 0; i--)
        {
            stream.Add(((value >> i) & 1) == 1);
        }
    }

    private static byte[] ComputeEcc(byte[] data, int eccCount)
    {
        // Generator polynomial for eccCount
        byte[] gen = [1];
        for (int i = 0; i < eccCount; i++)
        {
            byte[] factor = [1, ExpTable[i]];
            byte[] nextGen = new byte[gen.Length + 1];
            for (int j = 0; j < gen.Length; j++)
            {
                nextGen[j] ^= GfMultiply(gen[j], factor[0]);
                nextGen[j + 1] ^= GfMultiply(gen[j], factor[1]);
            }
            gen = nextGen;
        }

        byte[] info = new byte[data.Length + eccCount];
        Array.Copy(data, info, data.Length);

        for (int i = 0; i < data.Length; i++)
        {
            byte coef = info[i];
            if (coef != 0)
            {
                for (int j = 0; j < gen.Length; j++)
                {
                    info[i + j] ^= GfMultiply(gen[j], coef);
                }
            }
        }

        byte[] ecc = new byte[eccCount];
        Array.Copy(info, data.Length, ecc, 0, eccCount);
        return ecc;
    }

    private static void AddFinderPattern(bool[,] matrix, bool[,] isFunction, int startCol, int startRow)
    {
        for (int r = 0; r < 7; r++)
        {
            for (int c = 0; c < 7; c++)
            {
                bool isBlack = (r == 0 || r == 6 || c == 0 || c == 6 || (r >= 2 && r <= 4 && c >= 2 && c <= 4));
                matrix[startRow + r, startCol + c] = isBlack;
                isFunction[startRow + r, startCol + c] = true;
            }
        }

        // Add 1-module separator ring
        int total = matrix.GetLength(0);
        for (int r = -1; r <= 7; r++)
        {
            for (int c = -1; c <= 7; c++)
            {
                int row = startRow + r;
                int col = startCol + c;
                if (row >= 0 && row < total && col >= 0 && col < total && !isFunction[row, col])
                {
                    matrix[row, col] = false;
                    isFunction[row, col] = true;
                }
            }
        }
    }

    private static void AddAlignmentPattern(bool[,] matrix, bool[,] isFunction, int centerRow, int centerCol)
    {
        for (int r = -2; r <= 2; r++)
        {
            for (int c = -2; c <= 2; c++)
            {
                bool isBlack = (Math.Abs(r) == 2 || Math.Abs(c) == 2 || (r == 0 && c == 0));
                matrix[centerRow + r, centerCol + c] = isBlack;
                isFunction[centerRow + r, centerCol + c] = true;
            }
        }
    }

    private static void ApplyFormatBits(bool[,] matrix, int size, int formatBits)
    {
        for (int i = 0; i < 15; i++)
        {
            bool bit = ((formatBits >> (14 - i)) & 1) == 1;

            // Around top-left finder
            int r1 = (i <= 5) ? i : (i == 6 ? 7 : 8);
            int c1 = 8;
            if (i >= 8)
            {
                r1 = 8;
                c1 = (i == 8) ? 7 : 14 - i;
            }
            matrix[r1, c1] = bit;

            // Split around bottom-left and top-right finders
            if (i < 7)
            {
                matrix[size - 1 - i, 8] = bit;
            }
            else
            {
                matrix[8, size - 8 + (i - 7)] = bit;
            }
        }
    }
}
