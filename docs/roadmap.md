# Roadmap — план реализации

Идём вертикальными срезами: сначала «дойти до чипа», потом «прошить», потом «удобство».

## M0. Каркас решения
- `dotnet new sln`, проекты `Core`, `Cli`, `Core.Tests` (`net8.0`).
- `Directory.Build.props` (LangVersion, Nullable, TreatWarningsAsErrors).
- CI-заготовка (build + test), `.editorconfig`.

## M1. Транспорт + SLIP + пакеты
- `ISerialTransport`, `SystemSerialTransport`, `SerialPorts.List()`.
- `SlipCodec` (encode/decode, escaping).
- `EspPacket`/`EspResponse`, сборка/парсинг, `Checksum` (seed 0xEF).
- Юнит-тесты (в т.ч. golden-байты из `esptool --trace`).
- **Проверка:** `list` показывает порт; round-trip пакетов проходит тесты.

## M2. SYNC + детект чипа (ROM)
- ClassicReset (DTR/RTS), `SYNC` с ретраями.
- `READ_REG(0x40001000)` → ESP32 `0x00F01D83`, имя/ревизия/MAC.
- `READ_FLASH_ID` (через регистры SPI) → JEDEC + размер.
- **Проверка:** `chip-info` на реальной ESP32-CAM печатает имя и 4 МБ.

## M3. Stub loader
- Загрузка `stub_flasher_32.json` как embedded resource → `MEM_BEGIN/DATA/END`.
- Ожидание `OHAI`, переключение `EspLoader` → `EspStubLoader`.
- `SPI_ATTACH` + `SPI_SET_PARAMS`, `CHANGE_BAUDRATE` + переоткрытие на новой скорости.
- `--no-stub` fallback.
- **Проверка:** после загрузки stub команды идут на 921600.

## M4. Flash write + verify (ядро MVP)
- `FLASH_BEGIN/DATA/END`, блок 16 КБ, паддинг `0xFF`, ретраи блока.
- Мультиобраз (список offset→bytes), правильный reboot только на последнем.
- `SPI_FLASH_MD5`-верификация.
- Прогресс через `IProgress<FlashProgress>`.
- **Проверка:** `flash` прошивает ESP32-CAM; `esptool verify` совпадает.

## M5. Read / Erase + польза
- `ERASE_FLASH`, `ERASE_REGION`.
- `READ_FLASH` (stub, raw-пакеты + ack), сохранение дампа.
- `FirmwareImage` парсер (header/segments/footer), `chip-info --image file.bin`,
  `ImageMerger` (merge bin).
- **Проверка:** backup/restore полного 4 МБ образа совпадает по MD5.

## M6. Полировка MVP
- `--trace` (дамп TX/RX «по проводу»).
- Человекочитаемые ошибки (`EspError`), коды возврата CLI.
- README + docs согласованы, help у CLI.
- **Проверка:** полный сценарий ESP32-CAM «с нуля» по `docs/esp32cam.md`.

---

## v1 (после MVP)
- Deflate-сжатие (`FLASH_DEFL_*`) как основной путь (в MVP — опция).
- Другие чипы: ESP32-S3, C3, C6 (свои stub + magic/GET_SECURITY_INFO).
- `GET_SECURITY_INFO`, flash encryption detection (только чтение статусов).
- GUI (Avalonia) поверх `Core`: порт, чип-инфо, выбор файлов/offset'ов, прогресс, лог.

## v2 (идеи)
- espefuse / espsecure (eFuse, подписи, шифрование) — большой отдельный кусок.
- USB-JTAG-Serial reset (внутренний USB у S3/C3).
- Автосканирование и «прошивка одной кнопкой» из образа (merge + write + verify).
- Интеграция с PlatformIO/Arduino build outputs (авто-парсинг offset'ов из esptool args).

## Порядок работ в репозитории
1. M0 → M1 (чистое ядро, тесты) — можно делать/проверять без железа.
2. M2 → M4 (нужно железо ESP32-CAM + кабель).
3. M5 → M6, затем v1.
