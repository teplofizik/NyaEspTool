# ESP32 Flasher (.NET)

Нативный .NET-программатор для ESP-чипов по аналогии с `esptool`: прошивка, чтение,
стирание и диагностика через UART или встроенный USB-Serial-JTAG. Без внешних
зависимостей — только `System.IO.Ports`.

Сгенерировано с помощью DeepSeek 4.1 Flash

> Статус: рабочий MVP. Проверено на реальном железе: ESP32, ESP32-S3, ESP32-C3, ESP32-C6.

## Возможности

- Определение чипа (ESP32 / S3 / C3 / C6), ревизии, MAC и размера flash (JEDEC ID).
- Загрузка stub-флешера в RAM (быстрее и функциональнее ROM-загрузчика).
- Прошивка нескольких образов по offset'ам с прогрессом и MD5-верификацией.
- Flash mode / frequency / size, включая `detect`, с патчингом заголовка bootloader.
- Чтение flash в файл (backup), стирание чипа и региона.
- Разбор firmware image (`image-info`).
- Режимы входа в bootloader: `default_reset`, `usb_reset` (USB-Serial-JTAG), `no_reset`.
- `--trace` — дамп трафика «по проводу» для отладки протокола.

## Требования

- .NET 8 (SDK для сборки, runtime для запуска).
- Windows / Linux / macOS (основной таргет — Windows).
- USB-UART адаптер (CP210x/CH340/CH343) **или** встроенный USB-Serial-JTAG.

## Сборка и запуск

```powershell
dotnet build Esp32Flasher.slnx -c Release
dotnet test  Esp32Flasher.slnx
```

Запуск CLI (любой из вариантов):

```powershell
dotnet run --project src/Esp32Flasher.Cli -c Release -- <command> ...
# либо напрямую
src\Esp32Flasher.Cli\bin\Release\net8.0\Esp32Flasher.Cli.exe <command> ...
```

> В примерах ниже `esp32flash` — условное имя CLI (запуск через `dotnet run ... --` или путь к exe).
> `esp32flash list` == `dotnet run --project src/Esp32Flasher.Cli -c Release -- list`.

## Быстрый старт

```powershell
# 1. Список портов
esp32flash list

# 2. Информация о чипе и flash (загружает stub)
esp32flash chip-info --port COM23

# 3. JEDEC ID flash-чипа
esp32flash flash-id --port COM23

# 4. Прошивка ESP32-CAM (4 образа)
esp32flash flash --port COM23 --flash-size detect --flash-mode dio --flash-freq 80m `
  0x1000 bootloader.bin 0x8000 partitions.bin 0xe000 boot_app0.bin 0x10000 firmware.bin

# 5. Backup всего образа (4 МБ)
esp32flash read-flash --port COM23 0x0 0x400000 -o backup.bin

# 6. Инфо по образу без устройства
esp32flash image-info firmware.bin
```

## Команды

| Команда | Назначение |
| --- | --- |
| `list` | Список доступных COM-портов |
| `chip-info --port <p>` | Чип, ревизия, MAC, размер flash, статус stub |
| `flash-id --port <p>` | JEDEC ID flash-чипа (manufacturer/type/capacity/size) |
| `flash --port <p> <offset> <file> [...]` | Запись одного или нескольких образов |
| `read-flash --port <p> <offset> <size> [-o <file>]` | Чтение региона flash в файл |
| `erase-flash --port <p> --force` | Стирание всего чипа |
| `erase-region --port <p> <offset> <size>` | Стирание региона |
| `image-info <file>` | Разбор firmware image (header/segments/checksum/SHA256) |

### Общие опции

- `--port <COMx>` — обязательна для команд с устройством.
- `--baud <N>` — стартовая скорость (по умолчанию `115200`).
- `--flash-baud <N>` — скорость после загрузки stub (по умолчанию `921600`; `chip-info` остаётся на `115200`).
- `--before default_reset|usb_reset|no_reset` — как входить в bootloader (см. ниже).
- `--no-reset` — алиас `--before no_reset`.
- `--no-stub` — не грузить stub (работа только командами ROM; чтение flash недоступно).
- `--trace` — печатать TX/RX «по проводу» в stderr (`chip-info`).

### Опции `flash`

- `--flash-size keep|detect|1MB|2MB|4MB|8MB|16MB` (по умолчанию `keep`).
- `--flash-mode keep|qio|qout|dio|dout` (по умолчанию `keep`).
- `--flash-freq keep|80m|40m|26m|20m` (по умолчанию `keep`).
- `--no-verify` — пропустить MD5-верификацию.
- `--chip <name>` — принимается для совместимости (чип определяется автоматически).
- Пары `<offset> <file>`: offset в hex (`0x1000`) или десятичном виде.

`--flash-mode/-freq/-size` применяются **только к образу по offset'у bootloader**
(ESP32: `0x1000`, S3/C3/C6: `0x0`) — как в esptool; SHA256 пересчитывается, если он есть.

### Коды возврата

`0` — успех, `1` — ошибка операции, `2` — ошибка аргументов.

## Поддерживаемые чипы

| Чип | Chip-ID / Magic | Bootloader offset | SPI base | MAC |
| --- | --- | --- | --- | --- |
| ESP32 | magic `0x00F01D83` | `0x1000` | `0x3FF42000` | efuse words |
| ESP32-S3 | id 9 | `0x0` | `0x60002000` | `0x60007044` |
| ESP32-C3 | id 5 | `0x0` | `0x60002000` | `0x60008844` |
| ESP32-C6 | id 13 | `0x0` | `0x60003000` | `0x600B0844` |

ESP32 определяется по magic-регистру; S3/C3/C6 — по `GET_SECURITY_INFO (0x14)` (chip-id).
ESP32-S2 пока не поддерживается.

## Вход в bootloader (reset)

- **`default_reset`** — классическая последовательность DTR/RTS (как esptool `ClassicReset`).
  Работает на платах с авто-reset (DevKit, ESP32-CAM-MB и т.п.).
- **`usb_reset`** — для чипов, подключённых через **встроенный USB-Serial-JTAG** (S3/C3/C6).
  При reset устройство перечислится — программатор сам переоткрывает порт.
- **`no_reset`** — чип уже в download mode (перевели вручную).

Ручной вход (если нет авто-reset): зажать **BOOT** (GPIO0→GND), коротко нажать **RESET (EN)**,
отпустить BOOT, затем запускать с `--before no_reset`.

> Если плата без схемы авто-reset (например, «голый» модуль), классический reset не сработает —
> увидите `boot:0xNN (SPI_FAST_FLASH_BOOT)` в boot-логе и ошибку `NoSync`.

## ESP32-CAM

Стандартная карта флеша (Arduino ESP32 core / ESP-IDF):

| Offset | Файл |
| --- | --- |
| `0x1000` | `bootloader.bin` (`bootloader_dio_80m.bin`) |
| `0x8000` | `partitions.bin` |
| `0xe000` | `boot_app0.bin` |
| `0x10000` | `firmware.bin` (`*.ino.bin`) |

```powershell
esp32flash flash --port COM23 --flash-size detect --flash-mode dio --flash-freq 80m `
  0x1000 bootloader.bin 0x8000 partitions.bin 0xe000 boot_app0.bin 0x10000 firmware.bin
```

Полная команда в терминах esptool для сверки:

```
esptool.py --chip esp32 --port COM23 --baud 921600 write_flash -z --flash_mode dio `
  --flash_freq 80m --flash_size detect `
  0x1000 bootloader.bin 0x8000 partitions.bin 0xe000 boot_app0.bin 0x10000 firmware.bin
```

Если bootloader собран под другой режим, `--flash-mode/-freq/-size` поправят его заголовок.

## Библиотека (Core API)

`Esp32Flasher.Core` не зависит от CLI и консоли.

```csharp
using Esp32Flasher.Core;

await using var esp = await Esp32Connection.OpenAsync(new EspConnectionOptions
{
    PortName = "COM23",
    FlashBaud = 921600,
    LoadStub = true,
});

Console.WriteLine($"{esp.Chip.Name} v{esp.Chip.RevisionMajor}.{esp.Chip.RevisionMinor}, "
    + $"{esp.Chip.MacAddress}");

await esp.FlashAsync(
    new[]
    {
        FlashImage.FromFile(0x1000, "bootloader.bin"),
        FlashImage.FromFile(0x8000, "partitions.bin"),
        FlashImage.FromFile(0xe000, "boot_app0.bin"),
        FlashImage.FromFile(0x10000, "firmware.bin"),
    },
    new FlashOptions { Verify = true, Size = Esp32Flasher.Core.Imaging.FlashSize.MB4 });
```

Ключевые типы: `Esp32Connection`, `EspConnectionOptions`, `FlashImage`, `FlashOptions`,
`FlashProgress`, `ChipInfo`; транспорт — `ISerialTransport` / `SystemSerialTransport`.

## Структура репозитория

```
Esp32Flasher.slnx
├─ src/Esp32Flasher.Core/      библиотека (протокол, чипы, образы, транспорт)
│  └─ Resources/               встроенные stub-блобы (esp32/esp32s3/esp32c6/esp32c3)
├─ src/Esp32Flasher.Cli/       консольный программатор
├─ tests/Esp32Flasher.Core.Tests/
└─ docs/                       проектные доки
   ├─ 00-overview.md           план, scope, ссылки
   ├─ protocol.md              описание протокола (SLIP, команды, checksum)
   ├─ app-api.md               дизайн Core API и CLI
   ├─ esp32cam.md              рецепт прошивки ESP32-CAM
   └─ roadmap.md               этапы
```

## Ограничения / планы

- Нет deflate-сжатия (`FLASH_DEFL_*`) — пока запись несжатая.
- Нет espefuse/espsecure (eFuse, secure boot, шифрование).
- Нет ESP32-S2; merge-bin и парсинг partition table — частично.
- `monitor` (чтение порта) и USB-OTG reset — в планах.

## Лицензии

Stub-блобы (`Resources/*.json`) взяты из [esptool](https://github.com/espressif/esptool)
(`targets/stub_flasher/2`) под MIT/Apache — см. `Resources/stub-LICENSE-*.txt`.
