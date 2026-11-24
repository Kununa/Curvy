using Xunit;

namespace Curvy.Core.Tests;

/// <summary>Serialises tests that set process-wide environment variables (CURVY_*).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EnvCollection
{
    public const string Name = "Process environment";
}
