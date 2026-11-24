namespace Curvy.Core.Filters;

public static class Response
{
    public static double[] Evaluate(IEnumerable<Filter> filters, IReadOnlyList<double> freqs)
    {
        var active = filters.Where(f => f.Enabled).ToList();
        var result = new double[freqs.Count];
        for (int i = 0; i < freqs.Count; i++)
        {
            double sum = 0;
            foreach (var f in active) sum += Biquad.MagnitudeDb(f, freqs[i]);
            result[i] = sum;
        }
        return result;
    }
}
