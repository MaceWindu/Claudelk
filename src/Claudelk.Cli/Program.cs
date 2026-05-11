// Claudelk CLI — control an ELK-BLEDOM LED strip from the terminal.
//
// Inspired by claude-lamp (MIT, bobek-balinek). Protocol port informed by
// b1scoito/elk-led-controller (MIT) and TheSylex's prior work. See NOTICE.md.

namespace Claudelk.Cli;

internal static class Program
{
    private static Task<int> Main(string[] args) => Dispatcher.RunAsync(args);
}
