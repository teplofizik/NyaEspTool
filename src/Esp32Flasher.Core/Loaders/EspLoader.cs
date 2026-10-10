using System.Buffers.Binary;
using System.Security.Cryptography;
using Esp32Flasher.Core.Chips;
using Esp32Flasher.Core.Errors;
using Esp32Flasher.Core.Protocol;
using Esp32Flasher.Core.Reset;
using Esp32Flasher.Core.Serial;
using Esp32Flasher.Core.Slip;

namespace Esp32Flasher.Core.Loaders;

public sealed class EspLoader : IAsyncDisposable
{
    private static readonly byte[] SyncData = BuildSyncData();

    private const int FlashSectorSize = 0x1000;
    private const int ReadMaxInFlight = 64;

    private readonly ISerialTransport _transport;
    private readonly EspConnectionOptions _options;
    private readonly FrameReader _reader;

    public EspLoader(ISerialTransport transport, EspConnectionOptions options)
    {
        _transport = transport;
        _options = options;
        _reader = new FrameReader(transport);

        if (options.Trace is not null)
        {
            Traced += options.Trace;
        }
    }

    public ChipInfo? Chip { get; private set; }

    public ChipConfig ChipConfig { get; private set; } = ChipRegistry.Esp32;

    public event Action<EspTraceEntry>? Traced;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _reader.RawReceived += bytes =>
            Traced?.Invoke(new EspTraceEntry(EspTraceDirection.Rx, bytes.ToArray()));

        if (!_transport.IsOpen)
        {
            await _transport.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        _reader.Start();

        if (_options.BeforeReset != ResetMode.NoReset)
        {
            await ResetSequence.ClassicAsync(_transport, _options.ResetDelay, cancellationToken)
                .ConfigureAwait(false);
        }

        await SyncAsync(cancellationToken).ConfigureAwait(false);
        Chip = await DetectChipAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        for (int attempt = 0; attempt < _options.SyncAttempts; attempt++)
        {
            _reader.Reset();

            try
            {
                await WriteAsync(EspCommand.Sync, SyncData, 0, cancellationToken).ConfigureAwait(false);
                byte[]? frame = await _reader.ReadFrameAsync(_options.SyncTimeout, cancellationToken)
                    .ConfigureAwait(false);

                if (frame is not null
                    && EspResponse.TryParse(frame, out EspResponse? response)
                    && response.Command == EspCommand.Sync)
                {
                    return;
                }
            }
            catch (EspException ex) when (ex.Error == EspError.Timeout)
            {
            }
        }

        throw new EspException(
            EspError.NoSync,
            "Failed to sync with the device. Check the port, cable and boot mode (GPIO0 low on reset).");
    }

    public async Task<ChipInfo> DetectChipAsync(CancellationToken cancellationToken = default)
    {
        ChipConfig? config = null;
        uint? chipId = await TryGetChipIdAsync(cancellationToken).ConfigureAwait(false);
        if (chipId is uint id)
        {
            config = ChipRegistry.FromChipId(id);
        }

        if (config is null)
        {
            uint magic = await ReadRegAsync(Esp32Chip.MagicAddress, cancellationToken).ConfigureAwait(false);
            config = ChipRegistry.FromMagic(magic);
        }

        if (config is null)
        {
            throw new EspException(
                EspError.ChipNotSupported,
                $"Unsupported chip (chip-id {(chipId is uint c ? $"0x{c:X}" : "n/a")}).");
        }

        ChipConfig = config;
        return await ReadChipInfoAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<uint?> TryGetChipIdAsync(CancellationToken cancellationToken)
    {
        try
        {
            EspResponse response = await SendCommandAsync(
                    EspCommand.GetSecurityInfo, null, 0, TimeSpan.FromMilliseconds(300), cancellationToken)
                .ConfigureAwait(false);
            byte[] data = response.Data.ToArray();
            if (data.Length >= 16)
            {
                return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12, 4));
            }

            return null;
        }
        catch (EspException)
        {
            return null;
        }
    }

    private async Task<ChipInfo> ReadChipInfoAsync(CancellationToken cancellationToken)
    {
        byte[] mac = await ReadMacAsync(cancellationToken).ConfigureAwait(false);
        int major = 0;
        int minor = 0;
        string name = ChipConfig.Name;

        if (ChipConfig.MacMode == MacReadMode.Esp32EfuseWords)
        {
            uint word3 = await ReadRegAsync(Esp32Chip.EfuseWord(3), cancellationToken).ConfigureAwait(false);
            uint word5 = await ReadRegAsync(Esp32Chip.EfuseWord(5), cancellationToken).ConfigureAwait(false);
            uint apbCtlDate = await ReadRegAsync(Esp32Chip.ApbCtlDateAddress, cancellationToken)
                .ConfigureAwait(false);

            int packageVersion = (int)((word3 >> 9) & 0x07);
            packageVersion += (int)((word3 >> 2) & 0x01) << 3;
            bool singleCore = (word3 & 0x01) != 0;

            int revBit0 = (int)((word3 >> 15) & 0x01);
            int revBit1 = (int)((word5 >> 20) & 0x01);
            int revBit2 = (int)((apbCtlDate >> 31) & 0x01);
            int combined = (revBit2 << 2) | (revBit1 << 1) | revBit0;
            major = combined switch
            {
                0 => 0,
                1 => 1,
                3 => 2,
                7 => 3,
                _ => 0,
            };
            minor = (int)((word5 >> 24) & 0x03);
            name = DescribePackage(packageVersion, major, singleCore);
        }

        return new ChipInfo(name, major, minor, mac);
    }

    public Task<uint> ReadRegAsync(uint address, CancellationToken cancellationToken = default)
        => ReadRegAsync(address, _options.CommandTimeout, cancellationToken);

    public async Task<EspResponse> SendCommandAsync(
        EspCommand command,
        byte[]? data = null,
        byte checksum = 0,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        await WriteAsync(command, data ?? Array.Empty<byte>(), checksum, cancellationToken)
            .ConfigureAwait(false);
        return await WaitForResponseAsync(command, timeout ?? _options.CommandTimeout, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task HardResetAsync(CancellationToken cancellationToken = default)
    {
        await ResetSequence.HardAsync(
                _transport, TimeSpan.FromMilliseconds(100), TimeSpan.Zero, cancellationToken)
            .ConfigureAwait(false);
    }

    public bool IsStub { get; private set; }

    public async Task LoadStubAsync(StubBlob stub, CancellationToken cancellationToken = default)
    {
        if (IsStub)
        {
            return;
        }

        await UploadSegmentAsync(stub.Text, stub.TextStart, cancellationToken).ConfigureAwait(false);
        await UploadSegmentAsync(stub.Data, stub.DataStart, cancellationToken).ConfigureAwait(false);
        await RunStubAsync(stub.Entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task ChangeBaudAsync(int baudRate, CancellationToken cancellationToken = default)
    {
        uint second = IsStub ? (uint)_transport.BaudRate : 0u;
        byte[] payload = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)baudRate);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), second);

        await SendCommandAsync(EspCommand.ChangeBaud, payload, 0, null, cancellationToken)
            .ConfigureAwait(false);

        _transport.BaudRate = baudRate;
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        _reader.Reset();
    }

    public async Task SpiAttachAsync(CancellationToken cancellationToken = default)
    {
        byte[] payload = new byte[IsStub ? 4 : 8];
        await SendCommandAsync(EspCommand.SpiAttach, payload, 0, null, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SetSpiParamsAsync(uint flashSizeBytes, CancellationToken cancellationToken = default)
    {
        byte[] payload = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), flashSizeBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8), 64 * 1024);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12), 4 * 1024);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(16), 256);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(20), 0xFFFF);

        await SendCommandAsync(EspCommand.SpiSetParams, payload, 0, null, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task UploadSegmentAsync(byte[] data, uint offset, CancellationToken cancellationToken)
    {
        int ramBlockSize = ChipConfig.RamBlockSize;
        int blocks = (data.Length + ramBlockSize - 1) / ramBlockSize;

        byte[] begin = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(begin, (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(begin.AsSpan(4), (uint)blocks);
        BinaryPrimitives.WriteUInt32LittleEndian(begin.AsSpan(8), (uint)ramBlockSize);
        BinaryPrimitives.WriteUInt32LittleEndian(begin.AsSpan(12), offset);
        await SendCommandAsync(EspCommand.MemBegin, begin, 0, null, cancellationToken)
            .ConfigureAwait(false);

        for (int sequence = 0; sequence < blocks; sequence++)
        {
            int from = sequence * ramBlockSize;
            int length = Math.Min(ramBlockSize, data.Length - from);
            await MemBlockAsync(data.AsMemory(from, length), (uint)sequence, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task MemBlockAsync(ReadOnlyMemory<byte> data, uint sequence, CancellationToken cancellationToken)
    {
        byte[] payload = new byte[16 + data.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, (uint)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), sequence);
        data.Span.CopyTo(payload.AsSpan(16));

        byte checksum = EspChecksum.Compute(data.Span);
        await SendCommandAsync(EspCommand.MemData, payload, checksum, null, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RunStubAsync(uint entryPoint, CancellationToken cancellationToken)
    {
        byte[] payload = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, entryPoint == 0 ? 1u : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4), entryPoint);
        await WriteAsync(EspCommand.MemEnd, payload, 0, cancellationToken).ConfigureAwait(false);

        DateTime deadline = DateTime.UtcNow + _options.StubStartTimeout;
        while (true)
        {
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw new EspException(EspError.StubError, "Failed to start stub flasher (no OHAI response).");
            }

            byte[]? frame = await _reader.ReadFrameAsync(remaining, cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                throw new EspException(EspError.StubError, "Failed to start stub flasher (no OHAI response).");
            }

            if (frame.Length == 4 && frame[0] == 0x4F && frame[1] == 0x48 && frame[2] == 0x41 && frame[3] == 0x49)
            {
                IsStub = true;
                return;
            }
        }
    }

    public async Task WriteFlashAsync(
        FlashImage image,
        FlashOptions options,
        IProgress<FlashProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        byte[] data = Align4(image.Data);
        uint offset = image.Offset;
        int blockSize = (int)options.PacketSizeBytes;
        int numBlocks = (data.Length + blockSize - 1) / blockSize;
        uint imageSize = (uint)data.Length;

        progress?.Report(new FlashProgress(FlashStage.Erase, 0, data.Length, image.Name));
        await FlashBeginAsync(imageSize, offset, (uint)blockSize, (uint)numBlocks, cancellationToken)
            .ConfigureAwait(false);

        for (int sequence = 0; sequence < numBlocks; sequence++)
        {
            int from = sequence * blockSize;
            int length = Math.Min(blockSize, data.Length - from);
            byte[] block = new byte[blockSize];
            Array.Fill(block, (byte)0xFF);
            Array.Copy(data, from, block, 0, length);

            await FlashBlockAsync(block, (uint)sequence, options.MaxRetriesPerBlock, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(new FlashProgress(FlashStage.Write, from + length, data.Length, image.Name));
        }

        if (options.Verify)
        {
            byte[] expected = MD5.HashData(data);
            byte[] actual = await FlashMd5Async(offset, imageSize, cancellationToken).ConfigureAwait(false);
            if (!expected.AsSpan().SequenceEqual(actual))
            {
                throw new EspException(
                    EspError.VerificationFailed,
                    $"MD5 mismatch at 0x{offset:X8}: expected {Convert.ToHexString(expected)}, "
                    + $"got {Convert.ToHexString(actual)}.");
            }

            progress?.Report(new FlashProgress(FlashStage.Verify, data.Length, data.Length, image.Name));
        }
    }

    public async Task FlashBeginAsync(
        uint eraseSize, uint offset, uint blockSize, uint numBlocks,
        CancellationToken cancellationToken = default)
    {
        byte[] payload = new byte[(IsStub ? 5 : 4) * 4];
        WriteUInt32(payload, 0, eraseSize);
        WriteUInt32(payload, 4, numBlocks);
        WriteUInt32(payload, 8, blockSize);
        WriteUInt32(payload, 12, offset);
        if (IsStub)
        {
            WriteUInt32(payload, 16, 0);
        }

        await SendCommandAsync(EspCommand.FlashBegin, payload, 0, null, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task FlashFinishAsync(bool reboot, CancellationToken cancellationToken = default)
    {
        byte[] payload = new byte[4];
        WriteUInt32(payload, 0, reboot ? 0u : 1u);
        await SendCommandAsync(EspCommand.FlashEnd, payload, 0, null, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<byte[]> FlashMd5Async(uint offset, uint size, CancellationToken cancellationToken = default)
    {
        byte[] payload = new byte[16];
        WriteUInt32(payload, 0, offset);
        WriteUInt32(payload, 4, size);

        TimeSpan timeout = TimeSpan.FromSeconds(3 + (8.0 * size / (1024 * 1024)));
        EspResponse response = await SendCommandAsync(
                EspCommand.SpiFlashMd5, payload, 0, timeout, cancellationToken)
            .ConfigureAwait(false);

        byte[] data = response.Data.ToArray();
        if (data.Length < 16)
        {
            throw new EspException(EspError.CommandFailed, "MD5 response payload is too short.");
        }

        return data[..16];
    }

    public async Task EraseFlashAsync(CancellationToken cancellationToken = default)
    {
        await SendCommandAsync(
                EspCommand.EraseFlash, null, 0, TimeSpan.FromSeconds(120), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task EraseRegionAsync(uint offset, uint size, CancellationToken cancellationToken = default)
    {
        byte[] payload = new byte[8];
        WriteUInt32(payload, 0, offset);
        WriteUInt32(payload, 4, size);

        TimeSpan timeout = TimeSpan.FromSeconds(Math.Max(3.0, 40.0 * size / 1_000_000));
        await SendCommandAsync(EspCommand.EraseRegion, payload, 0, timeout, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<byte[]> ReadFlashAsync(
        uint offset,
        uint length,
        IProgress<FlashProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsStub)
        {
            throw new EspException(
                EspError.CommandFailed, "Reading flash requires the stub loader (do not pass --no-stub).");
        }

        byte[] payload = new byte[16];
        WriteUInt32(payload, 0, offset);
        WriteUInt32(payload, 4, length);
        WriteUInt32(payload, 8, FlashSectorSize);
        WriteUInt32(payload, 12, ReadMaxInFlight);
        await SendCommandAsync(EspCommand.ReadFlash, payload, 0, null, cancellationToken)
            .ConfigureAwait(false);

        using var buffer = new MemoryStream((int)length);
        while (buffer.Length < length)
        {
            byte[]? frame = await _reader.ReadFrameAsync(_options.CommandTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (frame is null)
            {
                throw new EspException(EspError.Timeout, "Timed out while reading flash data.");
            }

            buffer.Write(frame, 0, frame.Length);

            byte[] ack = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(ack, (uint)buffer.Length);
            await _transport.WriteAsync(SlipCodec.Encode(ack), cancellationToken).ConfigureAwait(false);

            progress?.Report(new FlashProgress(FlashStage.Read, buffer.Length, length));
        }

        byte[] result = buffer.ToArray();
        if (result.Length != length)
        {
            throw new EspException(
                EspError.CommandFailed, $"Read more than expected ({result.Length} > {length} bytes).");
        }

        byte[]? digest = await _reader.ReadFrameAsync(_options.CommandTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (digest is null || digest.Length != 16)
        {
            throw new EspException(EspError.CommandFailed, "Expected a 16-byte MD5 digest from the device.");
        }

        byte[] expected = MD5.HashData(result);
        if (!expected.AsSpan().SequenceEqual(digest))
        {
            throw new EspException(EspError.VerificationFailed, "Read-back MD5 mismatch.");
        }

        return result;
    }

    private async Task FlashBlockAsync(byte[] block, uint sequence, int retries, CancellationToken cancellationToken)
    {
        byte[] payload = new byte[16 + block.Length];
        WriteUInt32(payload, 0, (uint)block.Length);
        WriteUInt32(payload, 4, sequence);
        block.CopyTo(payload, 16);
        byte checksum = EspChecksum.Compute(block);

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                await SendCommandAsync(EspCommand.FlashData, payload, checksum, null, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }
            catch (EspException) when (attempt < retries)
            {
            }
        }
    }

    private static byte[] Align4(byte[] data)
    {
        int remainder = data.Length % 4;
        if (remainder == 0)
        {
            return data;
        }

        byte[] padded = new byte[data.Length + (4 - remainder)];
        data.CopyTo(padded, 0);
        for (int i = data.Length; i < padded.Length; i++)
        {
            padded[i] = 0xFF;
        }

        return padded;
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
        => BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), value);

    private async Task<uint> ReadRegAsync(uint address, TimeSpan timeout, CancellationToken cancellationToken)
    {
        byte[] data = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(data, address);
        EspResponse response = await SendCommandAsync(
                EspCommand.ReadReg, data, 0, timeout, cancellationToken)
            .ConfigureAwait(false);
        return response.Value;
    }

    public async Task WriteRegAsync(
        uint address, uint value, uint mask = 0xFFFFFFFF, uint delayUs = 0,
        CancellationToken cancellationToken = default)
    {
        byte[] payload = new byte[16];
        WriteUInt32(payload, 0, address);
        WriteUInt32(payload, 4, value);
        WriteUInt32(payload, 8, mask);
        WriteUInt32(payload, 12, delayUs);
        await SendCommandAsync(EspCommand.WriteReg, payload, 0, null, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<uint> ReadFlashIdAsync(CancellationToken cancellationToken = default)
        => RunSpiFlashCommandAsync(0x9F, 24, cancellationToken);

    public async Task<uint> RunSpiFlashCommandAsync(
        byte command, int readBits, CancellationToken cancellationToken = default)
    {
        const uint SpiUsrCommand = 1u << 31;
        const uint SpiUsrMiso = 1u << 28;
        const uint SpiCmdUsr = 1u << 18;
        const int Usr2CommandLenShift = 28;

        uint baseAddress = ChipConfig.SpiRegBase;
        uint spiCmd = baseAddress + 0x00;
        uint spiUsr = baseAddress + (uint)ChipConfig.SpiUsrOffs;
        uint spiUsr2 = baseAddress + (uint)ChipConfig.SpiUsr2Offs;
        uint spiMisoDlen = baseAddress + (uint)ChipConfig.SpiMisoDlenOffs;
        uint spiW0 = baseAddress + (uint)ChipConfig.SpiW0Offs;

        uint oldUsr = await ReadRegAsync(spiUsr, cancellationToken).ConfigureAwait(false);
        uint oldUsr2 = await ReadRegAsync(spiUsr2, cancellationToken).ConfigureAwait(false);

        if (readBits > 0)
        {
            await WriteRegAsync(spiMisoDlen, (uint)(readBits - 1), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        uint flags = SpiUsrCommand | (readBits > 0 ? SpiUsrMiso : 0u);
        await WriteRegAsync(spiUsr, flags, cancellationToken: cancellationToken).ConfigureAwait(false);
        await WriteRegAsync(spiUsr2, ((uint)7 << Usr2CommandLenShift) | command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        await WriteRegAsync(spiW0, 0, cancellationToken: cancellationToken).ConfigureAwait(false);
        await WriteRegAsync(spiCmd, SpiCmdUsr, cancellationToken: cancellationToken).ConfigureAwait(false);

        bool done = false;
        for (int i = 0; i < 10; i++)
        {
            uint commandRegister = await ReadRegAsync(spiCmd, cancellationToken).ConfigureAwait(false);
            if ((commandRegister & SpiCmdUsr) == 0)
            {
                done = true;
                break;
            }
        }

        if (!done)
        {
            throw new EspException(EspError.CommandFailed, "SPI command did not complete in time.");
        }

        uint status = await ReadRegAsync(spiW0, cancellationToken).ConfigureAwait(false);
        await WriteRegAsync(spiUsr, oldUsr, cancellationToken: cancellationToken).ConfigureAwait(false);
        await WriteRegAsync(spiUsr2, oldUsr2, cancellationToken: cancellationToken).ConfigureAwait(false);
        return status;
    }

    private async Task<byte[]> ReadMacAsync(CancellationToken cancellationToken)
    {
        byte[] raw = new byte[8];

        if (ChipConfig.MacMode == MacReadMode.Esp32EfuseWords)
        {
            uint word2 = await ReadRegAsync(ChipConfig.EfuseReadBase + 8, cancellationToken).ConfigureAwait(false);
            uint word1 = await ReadRegAsync(ChipConfig.EfuseReadBase + 4, cancellationToken).ConfigureAwait(false);
            BinaryPrimitives.WriteUInt32BigEndian(raw, word2);
            BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(4), word1);
        }
        else
        {
            uint mac0 = await ReadRegAsync(ChipConfig.MacRegister, cancellationToken).ConfigureAwait(false);
            uint mac1 = await ReadRegAsync(ChipConfig.MacRegister + 4, cancellationToken).ConfigureAwait(false);
            BinaryPrimitives.WriteUInt32BigEndian(raw, mac1);
            BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(4), mac0);
        }

        return raw[2..8];
    }

    private async Task WriteAsync(
        EspCommand command, ReadOnlyMemory<byte> data, byte checksum, CancellationToken cancellationToken)
    {
        byte[] packet = EspPacket.Build(command, data.Span, checksum);
        byte[] framed = SlipCodec.Encode(packet);
        Traced?.Invoke(new EspTraceEntry(EspTraceDirection.Tx, framed));
        await _transport.WriteAsync(framed, cancellationToken).ConfigureAwait(false);
    }

    private async Task<EspResponse> WaitForResponseAsync(
        EspCommand command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw new EspException(EspError.Timeout, $"Timed out waiting for response to {command}.");
            }

            byte[]? frame = await _reader.ReadFrameAsync(remaining, cancellationToken).ConfigureAwait(false);
            if (frame is null)
            {
                throw new EspException(EspError.Timeout, $"Timed out waiting for response to {command}.");
            }

            if (EspResponse.TryParse(frame, out EspResponse? response) && response.Command == command)
            {
                return response;
            }
        }
    }

    private static string DescribePackage(int packageVersion, int major, bool singleCore)
    {
        bool rev3 = major == 3;
        return packageVersion switch
        {
            0 => singleCore ? "ESP32-S0WDQ6" : rev3 ? "ESP32-D0WDQ6-V3" : "ESP32-D0WDQ6",
            1 => singleCore ? "ESP32-S0WD" : rev3 ? "ESP32-D0WD-V3" : "ESP32-D0WD",
            2 => "ESP32-D2WD",
            4 => "ESP32-U4WDH",
            5 => rev3 ? "ESP32-PICO-V3" : "ESP32-PICO-D4",
            6 => "ESP32-PICO-V3-02",
            7 => "ESP32-D0WDR2-V3",
            _ => "Unknown ESP32",
        };
    }

    private static byte[] BuildSyncData()
    {
        byte[] data = new byte[36];
        data[0] = 0x07;
        data[1] = 0x07;
        data[2] = 0x12;
        data[3] = 0x20;
        for (int i = 4; i < data.Length; i++)
        {
            data[i] = 0x55;
        }

        return data;
    }

    public async ValueTask DisposeAsync()
    {
        await _reader.DisposeAsync().ConfigureAwait(false);
    }

    public void Stop()
    {
        _reader.Stop();
    }
}

public enum EspTraceDirection
{
    Tx,
    Rx,
}

public readonly record struct EspTraceEntry(EspTraceDirection Direction, byte[] Data);
