using System.Collections.Immutable;
using Curvy.Core.Filters;

namespace Curvy.Core.Chain;

public record ChainResult(bool Bypassed, double PreampDb, ImmutableList<Filter> Filters);
