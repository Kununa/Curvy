namespace Curvy.Core.Tests;

public sealed class DoubleTol(double tolerance) : IEqualityComparer<double>
{
    public bool Equals(double x, double y) => Math.Abs(x - y) <= tolerance;
    public int GetHashCode(double obj) => 0;
}
