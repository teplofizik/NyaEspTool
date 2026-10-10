# Roadmap

Идём вертикальными срезами: сначала «дойти до чипа», потом «прошить», потом «удобство».
Ниже — статус на текущий момент. ✅ — сделано и проверено.

## ✅ M0. Каркас решения
- `Esp32Flasher.slnx`, проекты `Core`, `Cli`, `Core.Tests` (`net8.0`).
- `Directory.Build.props`, `.editorconfig`.

## ✅ M1. Транспорт + SLIP + пакеты
- `ISerialTransport`, `SystemSerialTransport`, `SerialPorts.List()`.
- `SlipCodec` (encode + stateless decode), `SlipDecoder` (потоковый).
- `EspPacket`/`EspResponse`, `EspChecksum` (seed `0xEF`).
- Юнит-тесты (в т.ч. golden-байты из `esptool --trace`).

## ✅ M2. SYNC + детект чипа (ROM)
- `ClassicReset` (DTR/RTS), `SYNC` с ретраями, `FrameReader` (read-pump).
- `READ_REG(0x40001000)` → ESP32 `0x00F01D83`, имя/ревизия/MAC.
- `READ_FLASH_ID` (через SPI-регистры) → JEDEC + размер.
- Проверено на ESP32-CAM.

## ✅ M3. Stub loader
- Встроенный stub-блоб → `MEM_BEGIN/DATA/END`, ожидание `OHAI`.
- `SPI_ATTACH` + `SPI_SET_PARAMS`, `CHANGE_BAUDRATE`.
- `--no-stub` fallback.

## ✅ M4. Flash write + verify
- `FLASH_BEGIN/DATA/END`, блок 16 КБ, паддинг `0xFF`, ретраи.
- Мультиобраз (offset→bytes), reboot в конце.
- `SPI_FLASH_MD5`-верификация, `IProgress<FlashProgress>`.
- Проверено: прошивка ESP32-CAM.

## ✅ M5. Read / Erase / образы
- `ERASE_FLASH`, `ERASE_REGION`.
- `READ_FLASH` (raw-кадры + ack + MD5-дайджест).
- `FirmwareImage` (header/segments/footer/SHA256), `image-info`.

## ✅ M6. Режимы + полировка
- `--flash-mode/-freq/-size` (+ `detect`), патчинг заголовка bootloader с пересчётом SHA256.
- `--trace` (дамп TX/RX), человекочитаемые ошибки (`EspError`), коды возврата.
- `README.md` + docs.

## ✅ M7. Мульти-чип + USB-JTAG
- `ChipConfig`/`ChipRegistry`: ESP32 (magic), ESP32-S3/C3/C6 (`GET_SECURITY_INFO` chip-id).
- Per-chip: SPI-регистры, bootloader offset, efuse/MAC, схема ревизии, stub-блобы.
- `--before default_reset|usb_reset|no_reset`; USB-JTAG reset + переподключение порта.
- Проверено на железе: ESP32, ESP32-S3, ESP32-C3, ESP32-C6.

---

## v1 (в работе)
- ✅ Другие чипы: ESP32-S3, C3, C6.
- ✅ USB-JTAG reset (S3/C3/C6).
- ⬜ Deflate-сжатие (`FLASH_DEFL_*`) — быстрее прошивка.
- ⬜ ESP32-S2 (magic fallback + свой stub).
- ⬜ merge-bin и парсинг partition table.
- ⬜ `monitor` (чтение порта), USB-OTG reset.

## v2 (идеи)
- espefuse / espsecure (eFuse, подписи, шифрование).
- GUI (Avalonia) поверх `Core`.
- «Прошивка одной кнопкой» из merged-образа.
- Интеграция с PlatformIO/Arduino build outputs (авто-offset'ы).

## Порядок работ (исторически)
1. M0 → M1 — чистое ядро, тесты без железа.
2. M2 → M4 — железо ESP32-CAM.
3. M5 → M7 — read/erase, режимы, мульти-чип.
