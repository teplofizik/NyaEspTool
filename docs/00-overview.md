# ESP32 Programmer for .NET — Overview / План

## 1. Цель проекта

Сделать нативный .NET-программатор для ESP32 (по аналогии с `esptool` / `esptool.py`),
достаточный для первой реальной задачи: **прошить ESP32-CAM** (залить
`bootloader.bin`, `partitions.bin`, `boot_app0.bin`, `firmware.bin` по нужным offset'ам).

Не цель — 100% копия esptool на первом этапе. Сначала узкий, но рабочий срез,
архитектура закладывается с расширяемостью на другие чипы (S2/S3/C3) и на
espefuse/espsecure в будущем.

## 2. Ключевые решения (зафиксировано)

| Вопрос | Решение |
| --- | --- |
| Форма | `Core`-библиотека + `Cli` (GUI — позже, поверх Core) |
| Глубина MVP | ROM-загрузчик + RAM-загрузка **stub** loader'а (как esptool по умолчанию) |
| Платформа | .NET 8 (`net8.0`), `System.IO.Ports` |
| Платформы ОС | Windows в первую очередь, но код кросс-платформенный |

## 3. Область MVP (scope)

**Делаем:**
- Открытие serial-порта, управление DTR/RTS, автоматический вход в download mode.
- SLIP-фрейминг, command/response пакеты, checksum, статусы/ошибки.
- `SYNC`, детект чипа по magic-регистру (`0x40001000 == 0x00F01D83`).
- RAM-загрузка stub loader для ESP32 и ожидание `OHAI`.
- `SPI_ATTACH` + `SPI_SET_PARAMS`.
- `CHANGE_BAUDRATE` (ускорение после синхронизации).
- Запись во flash: `FLASH_BEGIN` / `FLASH_DATA` / `FLASH_END` (+ сжатый deflate-вариант позже).
- Верификация через `SPI_FLASH_MD5`.
- Стирание: `ERASE_FLASH`, `ERASE_REGION`.
- Чтение flash: `READ_FLASH` (через stub).
- Разбор firmware image header (magic `0xE9`) — для `chip-info`/валидации и merge.
- CLI: `list-ports`, `chip-info`, `flash-id`, `flash`, `read-flash`, `erase-flash`, `verify`.

**НЕ делаем в MVP (позже):**
- espefuse / espsecure (eFuse, secure boot, flash encryption).
- Чипы кроме ESP32 (S2/S3/C3/C6/H2...) — закладываем интерфейс, но не реализуем.
- Прошивка через USB-JTAG-Serial (внутренний USB у S3/C3).
- GUI.

## 4. Функциональные требования

1. **Определение портов** — список доступных COM/`/dev/tty*` с описанием.
2. **Авто-Reset в загрузчик** — последовательность DTR/RTS (классический ESP32),
   с fallback на ручной режим.
3. **Идентификация** — имя чипа, ревизия, MAC, размер/тип flash (JEDEC ID).
4. **Прошивка** — N образов по заданным offset'ам, с опцией `--flash-size detect`
   и `--flash-mode/-freq`, опционально компрессия (deflate), опциональный верифицирующий MD5.
5. **Backup/restore** — чтение диапазона flash в файл, запись файла обратно.
6. **Стирание** — весь чип или регион.
7. **Прогресс и понятные ошибки** — прогрессбар/колбэк, человекочитаемые сообщения.
8. **Логирование** — уровень `trace` c дампом «по проводу» (как `esptool --trace`), крайне
   полезно для отладки протокола.

## 5. Архитектура (высокоуровнево)

```
Esp32Flasher.Cli  ─┐
                   ├─► Esp32Flasher.Core
(будущий GUI)    ─┘        │
                           ├── Serial:   ISerialTransport, SystemSerialTransport, DtrRtsReset
                           ├── Slip:     SlipCodec (encode/decode, framing, escaping)
                           ├── Protocol: EspCommand enum, packet builder/parser, Crc/Checksum, Status
                           ├── Loaders:  EspLoader (ROM), EspStubLoader, StubBlob (ESP32)
                           ├── Chips:    Esp32Chip (magic, flash params, memory map), IChip
                           └── Imaging:  FirmwareImage (parser: header/segments/footer), BinMerger
```

Nothing ниже не зависит от CLI. `Core` не знает про `System.Console`.

## 6. Структура решения

```
Esp32Flasher.sln
├─ src/
│  ├─ Esp32Flasher.Core/
│  └─ Esp32Flasher.Cli/
├─ tests/
│  └─ Esp32Flasher.Core.Tests/
└─ docs/
   ├─ 00-overview.md   (этот файл)
   ├─ protocol.md
   ├─ app-api.md
   ├─ esp32cam.md
   └─ roadmap.md
```

## 7. Ссылки и референсы

**Официальные доки Espressif**
- Serial Protocol (ESP32): https://docs.espressif.com/projects/esptool/en/latest/esp32/advanced-topics/serial-protocol.html
- Firmware Image Format: https://docs.espressif.com/projects/esptool/en/latest/esp32/advanced-topics/firmware-image-format.html
- Boot Mode Selection: https://docs.espressif.com/projects/esptool/en/latest/esp32/advanced-topics/boot-mode-selection.html
- Entering the Bootloader: https://docs.espressif.com/projects/esptool/en/latest/esp32/esptool/entering-bootloader.html
- Flasher Stub: https://docs.espressif.com/projects/esptool/en/latest/esp32/esptool/flasher-stub.html
- SPI Flash Modes: https://docs.espressif.com/projects/esptool/en/latest/esp32/advanced-topics/spi-flash-modes.html

**Исходники и эталоны**
- esptool (Python, эталон): https://github.com/espressif/esptool
- esp-serial-flasher (C, Espressif): https://github.com/espressif/esp-serial-flasher
- stub loader: `esptool/targets/stub_flasher/stub_flasher_32.json` (сегменты + entry point)
- esp32 target: https://github.com/espressif/esptool/blob/master/esptool/targets/esp32.py — magic `0x00F01D83`, таблицы flash size/freq

**Готовые .NET-реализации (референс, не зависимости)**
- EspDotNet (C#, NuGet `EspDotNet`): https://github.com/KooleControls/EspDotNet
- ESPTool (C#, NuGet `ESPTool`): https://github.com/multitone/ESPTool (зеркало ghi-electronics/duelink_esptool)
- nanoframework/nanoFirmwareFlasher: https://github.com/nanoframework/nanofirmwareflasher

**Прочее**
- SLIP (RFC 1055): https://datatracker.ietf.org/doc/html/rfc1055
- `System.IO.Ports`: https://learn.microsoft.com/dotnet/api/system.io.ports.serialport
- ESP32 Datasheet (strapping pins): https://www.espressif.com/sites/default/files/documentation/esp32_datasheet_en.pdf
- ESP32-CAM (AI-Thinker) схема/pinout + Arduino core.

## 8. Открытые вопросы (решить до/во время реализации)

- Сжатие (deflate) в MVP или v1? Влияет на загрузку stub и на `FLASH_DEFL_*`.
- Верификация MD5 по умолчанию on/off (esptool - on).
- Как хранить stub-blob: встроенный `byte[]`/`EmbeddedResource` из `stub_flasher_32.json`.
- Поддержка `--before default_reset` vs `no_reset` (ручной boot) с первого дня — да.

## 9. Критерий готовности MVP

`Esp32Flasher.Cli flash --port COMx --chip esp32 --baud 921600 -z \
  0x1000 bootloader.bin 0x8000 partitions.bin 0xe000 boot_app0.bin 0x10000 firmware.bin`

успешно прошивает ESP32-CAM, затем MD5-верификация проходит, чип перезагружается и
стартует прошивка. Параллельно `esptool` на тех же файлах даёт тот же результат.
