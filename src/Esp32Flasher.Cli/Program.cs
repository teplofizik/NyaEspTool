using Esp32Flasher.Core;
using Esp32Flasher.Core.Chips;
using Esp32Flasher.Core.Errors;
using Esp32Flasher.Core.Imaging;
using Esp32Flasher.Core.Loaders;
using Esp32Flasher.Core.Reset;
using Esp32Flasher.Core.Serial;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    PrintUsage();
    return 0;
}

try
{
    return args[0] switch
    {
        "list" => ListPorts(),
        "chip-info" => await ChipInfoCommandAsync(args[1..]),
        "flash-id" => await FlashIdCommandAsync(args[1..]),
        "flash" => await FlashCommandAsync(args[1..]),
        "read-flash" => await ReadFlashCommandAsync(args[1..]),
        "erase-flash" => await EraseFlashCommandAsync(args[1..]),
        "erase-region" => await EraseRegionCommandAsync(args[1..]),
        "image-info" => ImageInfoCommand(args[1..]),
        _ => UnknownCommand(args[0]),
    };
}
catch (EspException ex)
{
    Console.Error.WriteLine($"error [{ex.Error}]: {ex.Message}");
    return 1;
}

static int UnknownCommand(string command)
{
    Console.Error.WriteLine($"Unknown command: {command}");
    PrintUsage();
    return 2;
}

static int ListPorts()
{
    IReadOnlyList<SerialPortInfo> ports = SerialPorts.List();
    if (ports.Count == 0)
    {
        Console.WriteLine("No serial ports found.");
        return 0;
    }

    foreach (SerialPortInfo port in ports)
    {
        Console.WriteLine(port.Description is null
            ? port.PortName
            : $"{port.PortName}\t{port.Description}");
    }

    return 0;
}

static async Task<int> ChipInfoCommandAsync(string[] args)
{
    string? port = null;
    bool trace = false;
    bool noStub = false;
    int baud = 115200;
    ResetMode before = ResetMode.Default;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--port":
                port = args[++i];
                break;
            case "--baud":
                baud = int.Parse(args[++i]);
                break;
            case "--trace":
                trace = true;
                break;
            case "--no-reset":
                before = ResetMode.NoReset;
                break;
            case "--before":
                if (!TryParseBefore(args[++i], out before))
                {
                    Console.Error.WriteLine($"Invalid --before: {args[i - 1]}");
                    return 2;
                }

                break;
            case "--no-stub":
                noStub = true;
                break;
            default:
                Console.Error.WriteLine($"Unknown option: {args[i]}");
                return 2;
        }
    }

    if (port is null)
    {
        Console.Error.WriteLine("--port is required.");
        return 2;
    }

    var options = new EspConnectionOptions
    {
        PortName = port,
        InitialBaud = baud,
        FlashBaud = null,
        LoadStub = !noStub,
        BeforeReset = before,
        Trace = trace ? PrintTrace : null,
    };

    await using Esp32Connection connection = await Esp32Connection.OpenAsync(options);
    var chip = connection.Chip;

    Console.WriteLine($"Chip:     {chip.Name}");
    Console.WriteLine($"Revision: v{chip.RevisionMajor}.{chip.RevisionMinor}");
    Console.WriteLine($"MAC:      {chip.MacAddress}");
    uint flashId = await connection.ReadFlashIdAsync();
    uint? sizeBytes = Esp32Chip.DecodeFlashSizeBytes(flashId);
    Console.WriteLine($"Flash:    {(sizeBytes is uint s ? $"{s / 1024 / 1024} MB" : "unknown")} (id 0x{flashId:X6})");
    Console.WriteLine($"Stub:     {(connection.StubRunning ? "running" : "disabled")}");

    if (connection.StubRunning)
    {
        await connection.HardResetAsync();
    }

    return 0;
}

static void PrintTrace(EspTraceEntry entry)
{
    string arrow = entry.Direction == EspTraceDirection.Tx ? "->" : "<-";
    Console.Error.WriteLine($"{arrow} {Convert.ToHexString(entry.Data)}");
}

static async Task<int> FlashCommandAsync(string[] args)
{
    string? port = null;
    int baud = 115200;
    int? flashBaud = 921600;
    bool noStub = false;
    bool noVerify = false;
    ResetMode before = ResetMode.Default;
    string flashModeArg = "keep";
    string flashFreqArg = "keep";
    string flashSizeArg = "keep";
    var positional = new List<string>();

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--port":
                port = args[++i];
                break;
            case "--baud":
                baud = int.Parse(args[++i]);
                break;
            case "--flash-baud":
                flashBaud = int.Parse(args[++i]);
                break;
            case "--flash-size":
                flashSizeArg = args[++i];
                break;
            case "--flash-mode":
                flashModeArg = args[++i];
                break;
            case "--flash-freq":
                flashFreqArg = args[++i];
                break;
            case "--no-stub":
                noStub = true;
                break;
            case "--no-verify":
                noVerify = true;
                break;
            case "--no-reset":
                before = ResetMode.NoReset;
                break;
            case "--before":
                if (!TryParseBefore(args[++i], out before))
                {
                    Console.Error.WriteLine($"Invalid --before: {args[i - 1]}");
                    return 2;
                }

                break;
            case "--chip":
                i++;
                break;
            default:
                if (args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return 2;
                }

                positional.Add(args[i]);
                break;
        }
    }

    if (port is null)
    {
        Console.Error.WriteLine("--port is required.");
        return 2;
    }

    if (positional.Count == 0 || positional.Count % 2 != 0)
    {
        Console.Error.WriteLine("Expected pairs of <offset> <file>.");
        return 2;
    }

    var images = new List<FlashImage>();
    for (int k = 0; k < positional.Count; k += 2)
    {
        uint offset = ParseAddress(positional[k]);
        string file = positional[k + 1];
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"File not found: {file}");
            return 2;
        }

        images.Add(FlashImage.FromFile(offset, file));
    }

    var options = new EspConnectionOptions
    {
        PortName = port,
        InitialBaud = baud,
        FlashBaud = flashBaud,
        LoadStub = !noStub,
        BeforeReset = before,
    };

    var progress = new Progress<FlashProgress>(PrintProgress);

    if (!TryParseFlashMode(flashModeArg, out FlashMode? mode))
    {
        Console.Error.WriteLine($"Invalid --flash-mode: {flashModeArg}");
        return 2;
    }

    if (!TryParseFlashFreq(flashFreqArg, out FlashFrequency? frequency))
    {
        Console.Error.WriteLine($"Invalid --flash-freq: {flashFreqArg}");
        return 2;
    }

    await using Esp32Connection connection = await Esp32Connection.OpenAsync(options);
    Console.Error.WriteLine($"Chip: {connection.Chip.Name} (v{connection.Chip.RevisionMajor}.{connection.Chip.RevisionMinor}), "
        + $"stub: {(connection.StubRunning ? "running" : "disabled")}");

    uint flashSizeBytes = 4 * 1024 * 1024;
    FlashSize? size = null;
    bool detect = string.Equals(flashSizeArg, "detect", StringComparison.OrdinalIgnoreCase);
    if (!string.Equals(flashSizeArg, "keep", StringComparison.OrdinalIgnoreCase) && !detect)
    {
        flashSizeBytes = ParseSize(flashSizeArg);
        size = Esp32Chip.SizeFromBytes(flashSizeBytes);
    }
    else
    {
        uint? detected = await connection.DetectFlashSizeAsync();
        if (detected is uint detectedBytes)
        {
            flashSizeBytes = detectedBytes;
            Console.Error.WriteLine($"Flash size: {detectedBytes / 1024 / 1024} MB");
            if (detect)
            {
                size = Esp32Chip.SizeFromBytes(detectedBytes);
            }
        }
        else
        {
            Console.Error.WriteLine("Could not detect flash size, using 4MB.");
        }
    }

    await connection.FlashAsync(
        images,
        new FlashOptions
        {
            Verify = !noVerify,
            Reboot = true,
            FlashSizeBytes = flashSizeBytes,
            Mode = mode,
            Frequency = frequency,
            Size = size,
        },
        progress);

    Console.Error.WriteLine();
    Console.WriteLine("Done.");
    return 0;
}

static void PrintProgress(FlashProgress progress)
{
    string item = progress.CurrentItem is null ? string.Empty : $" {progress.CurrentItem}";
    string line = progress.BytesTotal > 0
        ? $"\r{progress.Stage,-7}{item} {progress.BytesDone}/{progress.BytesTotal} ({progress.Fraction:P0})"
        : $"\r{progress.Stage,-7}{item}";
    Console.Error.Write(line);
}

static async Task<int> ReadFlashCommandAsync(string[] args)
{
    string? port = null;
    string? output = null;
    int baud = 115200;
    int? flashBaud = 921600;
    bool noStub = false;
    ResetMode before = ResetMode.Default;
    var positional = new List<string>();

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--port":
                port = args[++i];
                break;
            case "--baud":
                baud = int.Parse(args[++i]);
                break;
            case "--flash-baud":
                flashBaud = int.Parse(args[++i]);
                break;
            case "-o":
            case "--out":
                output = args[++i];
                break;
            case "--no-stub":
                noStub = true;
                break;
            case "--no-reset":
                before = ResetMode.NoReset;
                break;
            case "--before":
                if (!TryParseBefore(args[++i], out before))
                {
                    Console.Error.WriteLine($"Invalid --before: {args[i - 1]}");
                    return 2;
                }

                break;
            default:
                if (args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return 2;
                }

                positional.Add(args[i]);
                break;
        }
    }

    if (port is null || positional.Count != 2)
    {
        Console.Error.WriteLine("Usage: read-flash --port <port> <offset> <size> -o <file>");
        return 2;
    }

    uint offset = ParseAddress(positional[0]);
    uint size = ParseSize(positional[1]);
    output ??= $"flash_{offset:X}_{size:X}.bin";

    var options = new EspConnectionOptions
    {
        PortName = port,
        InitialBaud = baud,
        FlashBaud = flashBaud,
        LoadStub = !noStub,
        BeforeReset = before,
    };

    var progress = new Progress<FlashProgress>(PrintProgress);
    await using Esp32Connection connection = await Esp32Connection.OpenAsync(options);
    byte[] data = await connection.ReadFlashAsync(offset, size, progress);
    await File.WriteAllBytesAsync(output, data);
    Console.Error.WriteLine();
    Console.WriteLine($"Read {data.Length} bytes from 0x{offset:X8} into {output}");
    return 0;
}

static async Task<int> EraseFlashCommandAsync(string[] args)
{
    string? port = null;
    int baud = 115200;
    int? flashBaud = 921600;
    bool noStub = false;
    ResetMode before = ResetMode.Default;
    bool force = false;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--port":
                port = args[++i];
                break;
            case "--baud":
                baud = int.Parse(args[++i]);
                break;
            case "--flash-baud":
                flashBaud = int.Parse(args[++i]);
                break;
            case "--no-stub":
                noStub = true;
                break;
            case "--no-reset":
                before = ResetMode.NoReset;
                break;
            case "--before":
                if (!TryParseBefore(args[++i], out before))
                {
                    Console.Error.WriteLine($"Invalid --before: {args[i - 1]}");
                    return 2;
                }

                break;
            case "--force":
                force = true;
                break;
            default:
                Console.Error.WriteLine($"Unknown option: {args[i]}");
                return 2;
        }
    }

    if (port is null)
    {
        Console.Error.WriteLine("--port is required.");
        return 2;
    }

    if (!force)
    {
        Console.Error.WriteLine("erase-flash erases the ENTIRE flash. Re-run with --force to confirm.");
        return 2;
    }

    var options = new EspConnectionOptions
    {
        PortName = port,
        InitialBaud = baud,
        FlashBaud = flashBaud,
        LoadStub = !noStub,
        BeforeReset = before,
    };

    await using Esp32Connection connection = await Esp32Connection.OpenAsync(options);
    Console.Error.WriteLine("Erasing flash...");
    await connection.EraseFlashAsync();
    Console.WriteLine("Flash erased.");
    return 0;
}

static async Task<int> EraseRegionCommandAsync(string[] args)
{
    string? port = null;
    int baud = 115200;
    int? flashBaud = 921600;
    bool noStub = false;
    ResetMode before = ResetMode.Default;
    var positional = new List<string>();

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--port":
                port = args[++i];
                break;
            case "--baud":
                baud = int.Parse(args[++i]);
                break;
            case "--flash-baud":
                flashBaud = int.Parse(args[++i]);
                break;
            case "--no-stub":
                noStub = true;
                break;
            case "--no-reset":
                before = ResetMode.NoReset;
                break;
            case "--before":
                if (!TryParseBefore(args[++i], out before))
                {
                    Console.Error.WriteLine($"Invalid --before: {args[i - 1]}");
                    return 2;
                }

                break;
            default:
                if (args[i].StartsWith("--", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine($"Unknown option: {args[i]}");
                    return 2;
                }

                positional.Add(args[i]);
                break;
        }
    }

    if (port is null || positional.Count != 2)
    {
        Console.Error.WriteLine("Usage: erase-region --port <port> <offset> <size>");
        return 2;
    }

    uint offset = ParseAddress(positional[0]);
    uint size = ParseSize(positional[1]);

    var options = new EspConnectionOptions
    {
        PortName = port,
        InitialBaud = baud,
        FlashBaud = flashBaud,
        LoadStub = !noStub,
        BeforeReset = before,
    };

    await using Esp32Connection connection = await Esp32Connection.OpenAsync(options);
    await connection.EraseRegionAsync(offset, size);
    Console.WriteLine($"Erased {size} bytes at 0x{offset:X8}.");
    return 0;
}

static int ImageInfoCommand(string[] args)
{
    if (args.Length != 1)
    {
        Console.Error.WriteLine("Usage: image-info <file>");
        return 2;
    }

    if (!File.Exists(args[0]))
    {
        Console.Error.WriteLine($"File not found: {args[0]}");
        return 2;
    }

    FirmwareImage image = FirmwareImage.FromFile(args[0]);
    Console.WriteLine($"File:      {args[0]}");
    Console.WriteLine($"Entry:     0x{image.EntryPoint:X8}");
    Console.WriteLine($"Mode:      {image.FlashMode}");
    Console.WriteLine($"Flash:     {image.FlashSizeName} / {image.FlashFreqName}");
    Console.WriteLine($"ChipId:    {image.ChipId}");
    Console.WriteLine($"Segments:  {image.SegmentCount}");
    foreach (ImageSegment segment in image.Segments)
    {
        Console.WriteLine($"  - 0x{segment.Offset:X8}  {segment.Data.Length} bytes");
    }

    Console.WriteLine($"Checksum:  0x{image.Checksum:X2} (computed 0x{image.ComputeChecksum():X2})");
    Console.WriteLine($"SHA256:    {(image.HashAppended ? Convert.ToHexString(image.Sha256!) : "none")}");
    return 0;
}

static async Task<int> FlashIdCommandAsync(string[] args)
{
    string? port = null;
    int baud = 115200;
    int? flashBaud = 921600;
    bool noStub = false;
    ResetMode before = ResetMode.Default;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--port":
                port = args[++i];
                break;
            case "--baud":
                baud = int.Parse(args[++i]);
                break;
            case "--flash-baud":
                flashBaud = int.Parse(args[++i]);
                break;
            case "--no-stub":
                noStub = true;
                break;
            case "--no-reset":
                before = ResetMode.NoReset;
                break;
            case "--before":
                if (!TryParseBefore(args[++i], out before))
                {
                    Console.Error.WriteLine($"Invalid --before: {args[i - 1]}");
                    return 2;
                }

                break;
            default:
                Console.Error.WriteLine($"Unknown option: {args[i]}");
                return 2;
        }
    }

    if (port is null)
    {
        Console.Error.WriteLine("--port is required.");
        return 2;
    }

    var options = new EspConnectionOptions
    {
        PortName = port,
        InitialBaud = baud,
        FlashBaud = flashBaud,
        LoadStub = !noStub,
        BeforeReset = before,
    };

    await using Esp32Connection connection = await Esp32Connection.OpenAsync(options);
    uint id = await connection.ReadFlashIdAsync();
    uint? sizeBytes = Esp32Chip.DecodeFlashSizeBytes(id);
    Console.WriteLine($"Manufacturer: 0x{id & 0xFF:X2}");
    Console.WriteLine($"Memory type:  0x{(id >> 8) & 0xFF:X2}");
    Console.WriteLine($"Capacity:     0x{(id >> 16) & 0xFF:X2}");
    Console.WriteLine($"Size:         {(sizeBytes is uint s ? $"{s / 1024 / 1024} MB" : "unknown")}");
    return 0;
}

static bool TryParseBefore(string text, out ResetMode mode)
{
    switch (text.ToLowerInvariant())
    {
        case "default":
        case "default_reset":
            mode = ResetMode.Default;
            return true;
        case "usb":
        case "usb_reset":
        case "usb-jtag":
            mode = ResetMode.UsbJtag;
            return true;
        case "no":
        case "no_reset":
            mode = ResetMode.NoReset;
            return true;
        default:
            mode = ResetMode.Default;
            return false;
    }
}

static bool TryParseFlashMode(string text, out FlashMode? mode)
{
    switch (text.ToLowerInvariant())
    {
        case "keep":
            mode = null;
            return true;
        case "qio":
            mode = FlashMode.Qio;
            return true;
        case "qout":
            mode = FlashMode.Qout;
            return true;
        case "dio":
            mode = FlashMode.Dio;
            return true;
        case "dout":
            mode = FlashMode.Dout;
            return true;
        default:
            mode = null;
            return false;
    }
}

static bool TryParseFlashFreq(string text, out FlashFrequency? frequency)
{
    switch (text.ToLowerInvariant())
    {
        case "keep":
            frequency = null;
            return true;
        case "80m":
            frequency = FlashFrequency.MHz80;
            return true;
        case "40m":
            frequency = FlashFrequency.MHz40;
            return true;
        case "26m":
            frequency = FlashFrequency.MHz26;
            return true;
        case "20m":
            frequency = FlashFrequency.MHz20;
            return true;
        default:
            frequency = null;
            return false;
    }
}

static uint ParseAddress(string text)
{
    text = text.Trim();
    return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? Convert.ToUInt32(text[2..], 16)
        : uint.Parse(text);
}

static uint ParseSize(string text)
{
    text = text.Trim().ToUpperInvariant();
    if (text.EndsWith("MB", StringComparison.Ordinal))
    {
        return (uint)uint.Parse(text[..^2]) * 1024 * 1024;
    }

    if (text.EndsWith("KB", StringComparison.Ordinal))
    {
        return (uint)uint.Parse(text[..^2]) * 1024;
    }

    return ParseAddress(text);
}

static void PrintUsage()
{
    Console.WriteLine("esp32flash - ESP32 programmer for .NET");
    Console.WriteLine();
    Console.WriteLine("Usage: esp32flash <command> [options]");
    Console.WriteLine();
    Console.WriteLine("Commands:");
    Console.WriteLine("  list                                        List available serial ports");
    Console.WriteLine("  chip-info --port <port> [--baud N] [--trace] [--before default_reset|usb_reset|no_reset] [--no-stub]");
    Console.WriteLine("                                              Detect and print chip information");
    Console.WriteLine("  flash-id --port <port>                      Print SPI flash JEDEC ID and size");
    Console.WriteLine("  flash --port <port> [--baud N] [--flash-baud N]");
    Console.WriteLine("        [--flash-size keep|detect|1MB|2MB|4MB|8MB|16MB]");
    Console.WriteLine("        [--flash-mode keep|qio|qout|dio|dout] [--flash-freq keep|80m|40m|26m|20m]");
    Console.WriteLine("        [--no-stub] [--no-verify] [--before default_reset|usb_reset|no_reset] <offset> <file> [<offset> <file> ...]");
    Console.WriteLine("                                              Write one or more images to flash");
    Console.WriteLine("  read-flash --port <port> <offset> <size> [-o <file>]");
    Console.WriteLine("                                              Read flash region to a file");
    Console.WriteLine("  erase-flash --port <port> --force            Erase the entire flash");
    Console.WriteLine("  erase-region --port <port> <offset> <size>  Erase a flash region");
    Console.WriteLine("  image-info <file>                           Show firmware image header/segments");
}
