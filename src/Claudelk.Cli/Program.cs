// Claudelk CLI — control an ELK-BLEDOM LED strip from the terminal.
//
// Inspired by claude-lamp (MIT, bobek-balinek). Protocol port informed by
// b1scoito/elk-led-controller (MIT) and TheSylex's prior work. See NOTICE.md.

using System.Globalization;

namespace Claudelk.Cli;

internal static class Program
{
    // Wall-clock ceiling for the whole command. The InTheHand BLE calls mostly
    // take no CancellationToken and, once the Windows Bluetooth stack wedges,
    // block forever — so a cooperative token alone can't free us. Two layers:
    //   1. a CancellationTokenSource that fires at `timeout` so a slow-but-alive
    //      adapter cancels cleanly (Dispatcher catches it and returns 124);
    //   2. a hard watchdog at `timeout + grace` that force-exits the process when
    //      a truly hung adapter ignores cancellation.
    // Without (2), hooks invoked with "async": true pile up as zombie processes.
    private const int DefaultTimeoutSeconds = 15;
    private const int MinTimeoutSeconds = 1;
    private static readonly TimeSpan HardKillGrace = TimeSpan.FromSeconds(2);

    /// <summary>Exit code reported when a command times out (conventional "timed out" code).</summary>
    internal const int TimeoutExitCode = 124;

    private static async Task<int> Main(string[] args)
    {
        var timeout = ResolveTimeout();
        using var cts = new CancellationTokenSource(timeout);

        var work = Dispatcher.RunAsync(args, cts.Token);
        var finished = await Task.WhenAny(work, Task.Delay(timeout + HardKillGrace, CancellationToken.None));
        if (finished != work)
        {
            var seconds = timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
            Console.Error.WriteLine(
                $"Error: timed out after {seconds}s — the Bluetooth adapter may be hung. " +
                "Toggle Bluetooth off/on in Windows Settings, or restart the Bluetooth Support Service.");
            Console.Error.Flush();
            // Hard kill: the abandoned BLE task is still stuck in native code, so
            // returning normally could leave the process alive. Exit outright.
            Environment.Exit(TimeoutExitCode);
        }

        return await work;
    }

    private static TimeSpan ResolveTimeout()
    {
        var raw = Environment.GetEnvironmentVariable("CLAUDELK_TIMEOUT_SECONDS");
        if (!string.IsNullOrEmpty(raw)
            && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            && seconds >= MinTimeoutSeconds)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.FromSeconds(DefaultTimeoutSeconds);
    }
}
