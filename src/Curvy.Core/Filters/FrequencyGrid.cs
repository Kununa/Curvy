namespace Curvy.Core.Filters;

public static class FrequencyGrid
{
    public static double[] LogSpaced(int count = 512, double minHz = 20, double maxHz = 20000)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        var result = new double[count];
        if (count == 1) { result[0] = minHz; return result; }
        double logMin = Math.Log(minHz), logMax = Math.Log(maxHz);
        for (int i = 0; i < count; i++)
            result[i] = Math.Exp(logMin + (logMax - logMin) * i / (count - 1));
        result[0] = minHz;
        result[count - 1] = maxHz;
        return result;
    }
}
