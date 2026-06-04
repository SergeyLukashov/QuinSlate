using System;

namespace Jott.AssetGenerator.Imaging;

/// <summary>
/// Separable Lanczos (windowed-sinc) image resampler. Operates in premultiplied
/// alpha so transparent edges do not bleed colour into the result, and applies
/// the filter independently along each axis. Produces sharp, faithful downscales
/// of crisp source artwork.
/// </summary>
public static class LanczosResampler
{
    private const int BytesPerPixel = 4;
    private const int MaxByte = 255;
    private const double MaxByteValue = 255.0;
    private const double Half = 0.5;
    private const double WeightEpsilon = 1e-8;

    /// <summary>
    /// Resizes straight-alpha BGRA pixels to the target dimensions.
    /// </summary>
    /// <param name="source">Source pixels, BGRA, straight alpha, top-down, stride = width * 4.</param>
    /// <param name="sourceWidth">Source width in pixels.</param>
    /// <param name="sourceHeight">Source height in pixels.</param>
    /// <param name="targetWidth">Target width in pixels.</param>
    /// <param name="targetHeight">Target height in pixels.</param>
    /// <param name="lobes">Number of Lanczos lobes (kernel radius); 3 is the usual sharp default.</param>
    /// <returns>Resized pixels, BGRA, straight alpha, top-down.</returns>
    public static byte[] Resize(byte[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, int lobes)
    {
        float[] premultiplied = Premultiply(source, sourceWidth, sourceHeight);
        float[] horizontal = ResizeHorizontal(premultiplied, sourceWidth, sourceHeight, targetWidth, lobes);
        float[] vertical = ResizeVertical(horizontal, targetWidth, sourceHeight, targetHeight, lobes);
        return Unpremultiply(vertical, targetWidth, targetHeight);
    }

    private static float[] Premultiply(byte[] source, int width, int height)
    {
        float[] result = new float[width * height * BytesPerPixel];
        for (int i = 0; i < result.Length; i += BytesPerPixel)
        {
            float alpha = source[i + 3];
            float factor = alpha / (float)MaxByteValue;
            result[i] = source[i] * factor;
            result[i + 1] = source[i + 1] * factor;
            result[i + 2] = source[i + 2] * factor;
            result[i + 3] = alpha;
        }

        return result;
    }

    private static byte[] Unpremultiply(float[] source, int width, int height)
    {
        byte[] result = new byte[width * height * BytesPerPixel];
        for (int i = 0; i < result.Length; i += BytesPerPixel)
        {
            int alpha = ClampToByte(source[i + 3]);
            if (alpha == 0)
            {
                continue;
            }

            float factor = alpha / (float)MaxByteValue;
            result[i] = (byte)ClampToByte(source[i] / factor);
            result[i + 1] = (byte)ClampToByte(source[i + 1] / factor);
            result[i + 2] = (byte)ClampToByte(source[i + 2] / factor);
            result[i + 3] = (byte)alpha;
        }

        return result;
    }

    private static float[] ResizeHorizontal(float[] source, int width, int height, int targetWidth, int lobes)
    {
        var contributions = ComputeContributions(width, targetWidth, lobes);
        float[] result = new float[targetWidth * height * BytesPerPixel];

        for (int y = 0; y < height; y++)
        {
            int sourceRow = y * width * BytesPerPixel;
            int targetRow = y * targetWidth * BytesPerPixel;
            for (int x = 0; x < targetWidth; x++)
            {
                int[] indices = contributions.Indices[x];
                float[] weights = contributions.Weights[x];
                float b = 0f, g = 0f, r = 0f, a = 0f;
                for (int k = 0; k < indices.Length; k++)
                {
                    int sample = sourceRow + indices[k] * BytesPerPixel;
                    float weight = weights[k];
                    b += source[sample] * weight;
                    g += source[sample + 1] * weight;
                    r += source[sample + 2] * weight;
                    a += source[sample + 3] * weight;
                }

                int destination = targetRow + x * BytesPerPixel;
                result[destination] = b;
                result[destination + 1] = g;
                result[destination + 2] = r;
                result[destination + 3] = a;
            }
        }

        return result;
    }

    private static float[] ResizeVertical(float[] source, int width, int height, int targetHeight, int lobes)
    {
        var contributions = ComputeContributions(height, targetHeight, lobes);
        float[] result = new float[width * targetHeight * BytesPerPixel];
        int rowStride = width * BytesPerPixel;

        for (int x = 0; x < width; x++)
        {
            int column = x * BytesPerPixel;
            for (int y = 0; y < targetHeight; y++)
            {
                int[] indices = contributions.Indices[y];
                float[] weights = contributions.Weights[y];
                float b = 0f, g = 0f, r = 0f, a = 0f;
                for (int k = 0; k < indices.Length; k++)
                {
                    int sample = indices[k] * rowStride + column;
                    float weight = weights[k];
                    b += source[sample] * weight;
                    g += source[sample + 1] * weight;
                    r += source[sample + 2] * weight;
                    a += source[sample + 3] * weight;
                }

                int destination = (y * width + x) * BytesPerPixel;
                result[destination] = b;
                result[destination + 1] = g;
                result[destination + 2] = r;
                result[destination + 3] = a;
            }
        }

        return result;
    }

    private static Contributions ComputeContributions(int sourceLength, int targetLength, int lobes)
    {
        int[][] indices = new int[targetLength][];
        float[][] weights = new float[targetLength][];

        double scale = (double)sourceLength / targetLength;
        double filterScale = scale > 1.0 ? scale : 1.0;
        double support = lobes * filterScale;

        for (int o = 0; o < targetLength; o++)
        {
            double center = (o + Half) * scale - Half;
            int left = (int)Math.Ceiling(center - support);
            int right = (int)Math.Floor(center + support);

            int count = right - left + 1;
            int[] sampleIndices = new int[count];
            float[] sampleWeights = new float[count];
            double total = 0.0;

            for (int i = 0; i < count; i++)
            {
                int source = left + i;
                double weight = Lanczos((source - center) / filterScale, lobes);
                sampleIndices[i] = Clamp(source, 0, sourceLength - 1);
                sampleWeights[i] = (float)weight;
                total += weight;
            }

            if (Math.Abs(total) > WeightEpsilon)
            {
                for (int i = 0; i < count; i++)
                {
                    sampleWeights[i] = (float)(sampleWeights[i] / total);
                }
            }

            indices[o] = sampleIndices;
            weights[o] = sampleWeights;
        }

        return new Contributions(indices, weights);
    }

    private static double Lanczos(double x, int lobes)
    {
        double t = Math.Abs(x);
        if (t < WeightEpsilon)
        {
            return 1.0;
        }

        if (t >= lobes)
        {
            return 0.0;
        }

        double piT = Math.PI * t;
        return lobes * Math.Sin(piT) * Math.Sin(piT / lobes) / (piT * piT);
    }

    private static int ClampToByte(float value)
    {
        int rounded = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return Clamp(rounded, 0, MaxByte);
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        if (value < minimum)
        {
            return minimum;
        }

        if (value > maximum)
        {
            return maximum;
        }

        return value;
    }

    private sealed class Contributions
    {
        public Contributions(int[][] indices, float[][] weights)
        {
            Indices = indices;
            Weights = weights;
        }

        public int[][] Indices { get; }

        public float[][] Weights { get; }
    }
}
