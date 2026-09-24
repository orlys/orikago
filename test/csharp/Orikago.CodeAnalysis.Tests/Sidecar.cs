namespace Orikago.CodeAnalysis.Tests;

/// <summary>
/// Sidecar wiring for the test run.
/// </summary>
/// <remarks>
/// Orikago.CodeAnalysis resolves the orikagoc sidecar via OrikagoToolResolver, whose
/// documented priority chain is: explicit path arg > ORIKAGO_GOC env var > next to the
/// Orikago.CodeAnalysis assembly > PATH. The integrator sets ORIKAGO_GOC to the built
/// orikagoc executable before running these tests, so the env-var branch of the
/// resolver is what these tests exercise. This guard fails fast with a clear message
/// when ORIKAGO_GOC is set but points at nothing, instead of letting every test die
/// with a less obvious process-start error.
/// </remarks>
internal static class Sidecar
{
    public static void EnsureConfigured()
    {
        var sidecarPath = Environment.GetEnvironmentVariable("ORIKAGO_GOC");
        if (!string.IsNullOrWhiteSpace(sidecarPath) && !File.Exists(sidecarPath))
        {
            // ORIKAGO_GOC points at nothing: fail fast with a message that says so
            throw new InvalidOperationException(
                $"ORIKAGO_GOC is set to '{sidecarPath}' but no file exists at that path. " +
                "Point it at the built orikagoc executable (or unset it to fall back " +
                "to assembly-adjacent / PATH resolution).");
        }
    }
}