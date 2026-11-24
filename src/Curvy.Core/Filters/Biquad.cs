using System.Numerics;

namespace Curvy.Core.Filters;

/// <summary>RBJ Audio EQ Cookbook biquads, as used by Equalizer APO.</summary>
public static class Biquad
{
    public const double SampleRate = 48000;

    public static double MagnitudeDb(Filter f, double freqHz)
    {
        var (b0, b1, b2, a0, a1, a2) = Coefficients(f);
        double w = 2 * Math.PI * freqHz / SampleRate;
        var z1 = Complex.FromPolarCoordinates(1, -w);
        var z2 = Complex.FromPolarCoordinates(1, -2 * w);
        var num = b0 + b1 * z1 + b2 * z2;
        var den = a0 + a1 * z1 + a2 * z2;
        return 20 * Math.Log10(num.Magnitude / den.Magnitude);
    }

    private static (double b0, double b1, double b2, double a0, double a1, double a2) Coefficients(Filter f)
    {
        double A = Math.Pow(10, f.GainDb / 40);
        double w0 = 2 * Math.PI * f.FrequencyHz / SampleRate;
        double cos = Math.Cos(w0), sin = Math.Sin(w0);
        double alpha = sin / (2 * f.Q);
        switch (f.Type)
        {
            case FilterType.Peaking:
                return (1 + alpha * A, -2 * cos, 1 - alpha * A,
                        1 + alpha / A, -2 * cos, 1 - alpha / A);
            case FilterType.LowShelf:
            {
                double s = 2 * Math.Sqrt(A) * alpha;
                return (A * ((A + 1) - (A - 1) * cos + s),
                        2 * A * ((A - 1) - (A + 1) * cos),
                        A * ((A + 1) - (A - 1) * cos - s),
                        (A + 1) + (A - 1) * cos + s,
                        -2 * ((A - 1) + (A + 1) * cos),
                        (A + 1) + (A - 1) * cos - s);
            }
            case FilterType.HighShelf:
            {
                double s = 2 * Math.Sqrt(A) * alpha;
                return (A * ((A + 1) + (A - 1) * cos + s),
                        -2 * A * ((A - 1) + (A + 1) * cos),
                        A * ((A + 1) + (A - 1) * cos - s),
                        (A + 1) - (A - 1) * cos + s,
                        2 * ((A - 1) - (A + 1) * cos),
                        (A + 1) - (A - 1) * cos - s);
            }
            default: throw new ArgumentOutOfRangeException(nameof(f));
        }
    }
}
