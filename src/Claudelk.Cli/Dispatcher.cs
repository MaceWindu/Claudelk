using System.Globalization;
using Claudelk.Core.Bluetooth;
using Claudelk.Core.Configuration;

namespace Claudelk.Cli;

internal static class Dispatcher
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "scan" => await ScanAsync(args, cancellationToken),
                "pair" => await PairAsync(args, cancellationToken),
                "ensure" => await EnsureAsync(args, cancellationToken),
                "on" => await SimpleCommandAsync(args, (d, ct) => d.TurnOnAsync(ct), cancellationToken),
                "off" => await SimpleCommandAsync(args, (d, ct) => d.TurnOffAsync(ct), cancellationToken),
                "color" => await ColorAsync(args, cancellationToken),
                "blink" => await BlinkAsync(args, cancellationToken),
                "brightness" => await BrightnessAsync(args, cancellationToken),
                "speed" => await SpeedAsync(args, cancellationToken),
                "effect" => await EffectAsync(args, cancellationToken),
                "temp" => await TempAsync(args, cancellationToken),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (OperationCanceledException)
        {
            // The watchdog (Program.Main) prints the timeout message and force-exits;
            // if cancellation surfaces here first, report it the same way.
            Console.Error.WriteLine("Error: operation cancelled (timed out — the Bluetooth adapter may be hung).");
            return Program.TimeoutExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 2;
        }
    }

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: {cmd}");
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            Claudelk — ELK-BLEDOM controller

            Usage:
              claudelk <command> [arguments]

            Commands:
              scan [--debug]             Discover nearby ELK-BLEDOM strips (--debug lists all BLE adverts)
              pair <device-id>           Save a device as the default target
              ensure [--color <#RRGGBB>] Verify connection; reconnect/repair-pair/power-on if needed, then set colour
                                         (default colour: #ffffff)
              on                         Power on the saved device
              off                        Power off the saved device
              color <#RRGGBB | R G B>    Set RGB color
              blink <#RRGGBB> [pulses] [ms] [--end <#RRGGBB>]
                                         Pulse color (default 4×250ms), optionally end on a different color
              brightness <0-100>         Set brightness (RGB modes only)
              speed <0-100>              Set effect animation speed
              effect <0x80-0x9f>         Run a built-in effect by code
              temp <0-100>               Set color temperature

            Options:
              --device <id>              Target a specific device id (overrides saved)

            Examples:
              claudelk scan
              claudelk pair be:ff:f0:01:04:a8
              claudelk color #ff8800
              claudelk blink "#ff0000" 10 250 --end "#ffffff"
              claudelk brightness 60
            """);
    }

    private static async Task<int> ScanAsync(string[] args, CancellationToken cancellationToken)
    {
        var debug = args.Any(a => a is "--debug" or "-v");
        Console.WriteLine("Scanning for ELK-BLEDOM devices...");

        var allSeen = new List<IBluetoothDevice>();
        var devices = await ElkBledomScanner.ScanAsync(
            onSeen: debug ? d => allSeen.Add(d) : null,
            cancellationToken: cancellationToken);

        if (debug)
        {
            Console.WriteLine();
            Console.WriteLine($"All BLE devices seen ({allSeen.Count}):");
            Console.WriteLine($"{"Id",-40}  Name");
            Console.WriteLine(new string('-', 60));
            foreach (var d in allSeen)
                Console.WriteLine($"{d.Id,-40}  {(string.IsNullOrEmpty(d.Name) ? "(no name)" : d.Name)}");
            Console.WriteLine();
        }

        if (devices.Count == 0)
        {
            Console.WriteLine("No ELK-BLEDOM-compatible devices found.");
            Console.WriteLine("Tips:");
            Console.WriteLine("  - Power-cycle the strip and keep it within ~5m.");
            Console.WriteLine("  - Make sure no other app/phone is currently paired.");
            Console.WriteLine("  - Re-run with --debug to see every BLE advert this PC picked up.");
            return 0;
        }

        Console.WriteLine($"Found {devices.Count} compatible device(s):");
        Console.WriteLine($"{"Id",-40}  Name");
        Console.WriteLine(new string('-', 60));
        foreach (var d in devices)
            Console.WriteLine($"{d.Id,-40}  {d.Name}");
        return 0;
    }

    private static async Task<int> PairAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("pair requires a device id (use 'scan' first).");
            return 1;
        }

        var id = args[1];
        Console.WriteLine($"Connecting to {id} to verify...");
        using var device = await ElkBledomDevice.ConnectByIdAsync(id, cancellationToken: cancellationToken);
        try
        {
            await device.PairWithWindowsAsync(cancellationToken);
            Console.WriteLine("Registered with Windows (fast reconnects enabled).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Note: Windows pairing skipped ({ex.Message}). " +
                              "Subsequent commands may still need a brief BLE scan.");
        }

        var config = UserConfig.Load();
        config.LastDeviceId = device.Id;
        config.LastDeviceName = device.Name;
        config.Save();
        Console.WriteLine($"Saved {device.Name} ({device.Id}) as default.");
        return 0;
    }

    private static async Task<int> EnsureAsync(string[] args, CancellationToken cancellationToken)
    {
        var colorHex = ExtractOption(args, "--color") ?? "#ffffff";
        if (!TryParseHex(colorHex, out var r, out var g, out var b))
        {
            Console.Error.WriteLine("ensure: --color expects '#RRGGBB'.");
            return 1;
        }

        using var device = await ResolveDeviceAsync(args, cancellationToken);

        // Restore the paired-list fast path if ConnectByIdAsync had to fall back
        // to an advertisement scan. PairAsync is a no-op when already paired.
        await device.PairWithWindowsAsync(cancellationToken);

        // Power on explicitly: this firmware silently drops SetColor writes that
        // arrive while the strip is in the off state.
        await device.TurnOnAsync(cancellationToken);
        await device.SetColorAsync(r, g, b, cancellationToken);
        Console.WriteLine($"Ensured {device.Name} ({device.Id}) on, colour #{r:X2}{g:X2}{b:X2}.");
        return 0;
    }

    private static async Task<int> SimpleCommandAsync(string[] args, Func<ElkBledomDevice, CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        using var device = await ResolveDeviceAsync(args, cancellationToken);
        await action(device, cancellationToken);
        return 0;
    }

    private static async Task<int> ColorAsync(string[] args, CancellationToken cancellationToken)
    {
        var rest = RemoveOption(args, "--device");
        if (rest.Length < 2)
        {
            Console.Error.WriteLine("color requires '#RRGGBB' or three values 'R G B'.");
            return 1;
        }

        byte r, g, b;
        if (rest.Length == 2 && rest[1].StartsWith('#'))
        {
            if (!TryParseHex(rest[1], out r, out g, out b))
            {
                Console.Error.WriteLine("Invalid hex color. Use '#RRGGBB'.");
                return 1;
            }
        }
        else if (rest.Length < 4
                 || !byte.TryParse(rest[1], out r)
                 || !byte.TryParse(rest[2], out g)
                 || !byte.TryParse(rest[3], out b))
        {
            Console.Error.WriteLine("color: could not parse arguments.");
            return 1;
        }

        using var device = await ResolveDeviceAsync(args, cancellationToken);
        await device.SetColorAsync(r, g, b, cancellationToken);
        Console.WriteLine($"Color set to #{r:X2}{g:X2}{b:X2}.");
        return 0;
    }

    private static async Task<int> BlinkAsync(string[] args, CancellationToken cancellationToken)
    {
        // Read --end's value from the original args before RemoveOption deletes the flag.
        var endHex = ExtractOption(args, "--end");
        var rest = RemoveOption(RemoveOption(args, "--end"), "--device");

        if (rest.Length < 2 || !rest[1].StartsWith('#') || !TryParseHex(rest[1], out var r, out var g, out var b))
        {
            Console.Error.WriteLine("blink requires '#RRGGBB' as the first argument.");
            return 1;
        }

        var pulses = rest.Length >= 3 && int.TryParse(rest[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) ? p : 4;
        var pulseMs = rest.Length >= 4 && int.TryParse(rest[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) ? ms : 250;

        (byte r, byte g, byte b)? endColor = null;
        if (!string.IsNullOrEmpty(endHex))
        {
            if (!TryParseHex(endHex, out var er, out var eg, out var eb))
            {
                Console.Error.WriteLine("--end: invalid hex color.");
                return 1;
            }

            endColor = (er, eg, eb);
        }

        using var device = await ResolveDeviceAsync(args, cancellationToken);
        await device.BlinkAsync(r, g, b, pulses, pulseMs, endColor, cancellationToken);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Blinked #{r:X2}{g:X2}{b:X2} {pulses}x") +
                          (endColor is null ? "." : string.Create(CultureInfo.InvariantCulture, $", ended on #{endColor.Value.r:X2}{endColor.Value.g:X2}{endColor.Value.b:X2}.")));
        return 0;
    }

    private static bool TryParseHex(string token, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        var hex = token.TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return false;
        r = (byte)((rgb >> 16) & 0xff);
        g = (byte)((rgb >> 8) & 0xff);
        b = (byte)(rgb & 0xff);
        return true;
    }

    private static string[] RemoveOption(string[] args, string name)
    {
        var result = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                i++;
                continue;
            }

            result.Add(args[i]);
        }

        return [.. result];
    }

    private static async Task<int> BrightnessAsync(string[] args, CancellationToken cancellationToken)
    {
        var rest = RemoveOption(args, "--device");
        if (rest.Length < 2 || !int.TryParse(rest[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
        {
            Console.Error.WriteLine("brightness requires a number 0-100.");
            return 1;
        }

        using var device = await ResolveDeviceAsync(args, cancellationToken);
        await device.SetBrightnessAsync(percent, cancellationToken);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Brightness set to {percent}%."));
        return 0;
    }

    private static async Task<int> SpeedAsync(string[] args, CancellationToken cancellationToken)
    {
        var rest = RemoveOption(args, "--device");
        if (rest.Length < 2 || !int.TryParse(rest[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
        {
            Console.Error.WriteLine("speed requires a number 0-100.");
            return 1;
        }

        using var device = await ResolveDeviceAsync(args, cancellationToken);
        await device.SetEffectSpeedAsync(percent, cancellationToken);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Effect speed set to {percent}%."));
        return 0;
    }

    private static async Task<int> EffectAsync(string[] args, CancellationToken cancellationToken)
    {
        var rest = RemoveOption(args, "--device");
        if (rest.Length < 2)
        {
            Console.Error.WriteLine("effect requires a code (e.g. 0x87).");
            return 1;
        }

        var token = rest[1];
        var style = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? NumberStyles.HexNumber
            : NumberStyles.Integer;
        if (style == NumberStyles.HexNumber) token = token[2..];
        if (!int.TryParse(token, style, CultureInfo.InvariantCulture, out var code))
        {
            Console.Error.WriteLine("effect: could not parse code.");
            return 1;
        }

        using var device = await ResolveDeviceAsync(args, cancellationToken);
        await device.SetEffectAsync(code, cancellationToken);
        Console.WriteLine($"Effect 0x{code:X2} engaged.");
        return 0;
    }

    private static async Task<int> TempAsync(string[] args, CancellationToken cancellationToken)
    {
        var rest = RemoveOption(args, "--device");
        if (rest.Length < 2 || !int.TryParse(rest[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            Console.Error.WriteLine("temp requires a number 0-100.");
            return 1;
        }

        using var device = await ResolveDeviceAsync(args, cancellationToken);
        await device.SetColorTemperatureAsync(value, cancellationToken);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Color temperature set to {value}."));
        return 0;
    }

    private static Task<ElkBledomDevice> ResolveDeviceAsync(string[] args, CancellationToken cancellationToken)
    {
        var explicitId = ExtractOption(args, "--device");
        if (!string.IsNullOrEmpty(explicitId))
            return ElkBledomDevice.ConnectByIdAsync(explicitId, cancellationToken: cancellationToken);

        var config = UserConfig.Load();
        if (string.IsNullOrEmpty(config.LastDeviceId))
            throw new InvalidOperationException(
                "No device paired. Run 'claudelk scan' then 'claudelk pair <id>' first.");

        return ElkBledomDevice.ConnectByIdAsync(config.LastDeviceId, cancellationToken: cancellationToken);
    }

    private static string? ExtractOption(string[] args, string name)
    {
        // Stop one short of the end — we always read args[i+1] as the option's value.
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}
