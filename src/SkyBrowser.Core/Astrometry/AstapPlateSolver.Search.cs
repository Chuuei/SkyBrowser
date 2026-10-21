using System.Diagnostics;
using System.Globalization;
using SkyBrowser.Core.Configuration;
using SkyBrowser.Core.Imaging;

namespace SkyBrowser.Core.Astrometry;

public sealed partial class AstapPlateSolver
{
    private static async Task<string> FindSolutionAsync(ApplicationOptions options, string input, string directory,
        SolverInputHints hints, CancellationToken token, IProgress<ImageProcessingProgress>? progress)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.AstapTimeoutSeconds));
        try
        {
            foreach (bool nearby in new[] { true, false })
            {
                if (nearby && hints.Position is null)
                {
                    continue;
                }
                string output = Path.Combine(directory, nearby ? "nearby" : "solution");
                var start = CreateStartInfo(options, input, output, nearby ? hints.Position : null, nearby, hints.FieldHeightDegrees);
                progress?.Report(new(nearby
                    ? string.Create(CultureInfo.InvariantCulture, $"ASTAP blind solving · trying metadata hints near RA {hints.Position!.Value.RightAscension:0.#####}°, Dec {hints.Position.Value.Declination:+0.#####;-0.#####;0}°")
                    : "ASTAP blind solving · searching the whole sky"));

                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
                if (nearby)
                {
                    // Wrong metadata must not consume the entire user-configured time
                    // limit. Both attempts share one deadline, including pipe draining.
                    attempt.CancelAfter(TimeSpan.FromSeconds(Math.Min(20, options.AstapTimeoutSeconds / 4d)));
                }

                (int ExitCode, string Diagnostics) result;
                try
                {
                    result = await RunSolverAsync(start, attempt.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (nearby && !deadline.IsCancellationRequested)
                {
                    continue;
                }
                deadline.Token.ThrowIfCancellationRequested();
                if (nearby && result.ExitCode is 1 or 2)
                {
                    continue;
                }
                if (result.ExitCode != 0)
                {
                    string reason = result.ExitCode switch
                    {
                        1 => "No sky solution found. Try a field-height hint or a deeper star database.",
                        2 => "Not enough stars detected in this image.",
                        16 => "ASTAP could not read the temporary image.",
                        32 => "ASTAP could not find a star database. Install one or configure its folder in Options.",
                        33 => "ASTAP could not read its star database.",
                        _ => $"ASTAP exited with code {result.ExitCode}."
                    };
                    throw new InvalidOperationException(reason + (result.Diagnostics.Length == 0 ? "" : "\n" + result.Diagnostics));
                }
                return output;
            }
            throw new InvalidOperationException("ASTAP did not run a search.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException($"ASTAP exceeded the {options.AstapTimeoutSeconds}-second time limit. Adjust it in Options → Plate solving.");
        }
    }

    private static async Task<(int ExitCode, string Diagnostics)> RunSolverAsync(ProcessStartInfo start, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            throw new IOException("ASTAP could not be started.");
        }
        var stdout = ReadTailAsync(process.StandardOutput);
        var stderr = ReadTailAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process exited between cancellation and termination.
            }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            throw;
        }
        return (process.ExitCode, (await stdout.ConfigureAwait(false) + "\n" + await stderr.ConfigureAwait(false)).Trim());
    }
}
