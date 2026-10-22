namespace Etherprof.App;

using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Etherprof.Core;

public static class QrCodeImageHelper
{
    /// <summary>
    /// Generates a WPF BitmapSource representing a QR code for the given text.
    /// </summary>
    public static BitmapSource? CreateQrCodeBitmap(string text, int scale = 6, int margin = 4)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            bool[,] matrix = QrCodeGenerator.GenerateMatrix(text);
            int matrixSize = matrix.GetLength(0);
            int totalDimension = (matrixSize + margin * 2) * scale;

            int[] pixels = new int[totalDimension * totalDimension];
            int white = unchecked((int)0xFFFFFFFF);
            int black = unchecked((int)0xFF000000);
            Array.Fill(pixels, white);

            for (int r = 0; r < matrixSize; r++)
            {
                for (int c = 0; c < matrixSize; c++)
                {
                    if (matrix[r, c])
                    {
                        int startY = (r + margin) * scale;
                        int startX = (c + margin) * scale;

                        for (int sy = 0; sy < scale; sy++)
                        {
                            int rowOffset = (startY + sy) * totalDimension;
                            for (int sx = 0; sx < scale; sx++)
                            {
                                pixels[rowOffset + startX + sx] = black;
                            }
                        }
                    }
                }
            }

            var wb = new WriteableBitmap(totalDimension, totalDimension, 96, 96, PixelFormats.Bgr32, null);
            wb.WritePixels(new Int32Rect(0, 0, totalDimension, totalDimension), pixels, totalDimension * 4, 0);
            wb.Freeze();
            return wb;
        }
        catch
        {
            return null;
        }
    }
}
