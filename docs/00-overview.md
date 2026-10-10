# ESP32 Programmer for .NET — Overview

## 1. Цель проекта

Нативный .NET-программатор для ESP-чипов по аналогии с `esptool`: прошивка, чтение,
стирание и диагностика через UART или встроенный USB-Serial-JTAG. Без внешних
зависимостей (только `System.IO.Ports`).

Изначально цель была узкой — прошить ESP32-CAM. Сейчас поддерживаются **ESP32, ESP32-S3,
ESP32-C3, ESP32-C6**, и это по-прежнему не 100% копия esptool: реализован практический
срез (прошивка/чтение/erase/диагностика), без eFuse/secure boot.

## 2. Ключевые решения

| Вопрос | Решение |
| --- | --- |
| Форма | `Core`-библиотека + `Cli` (GUI — позже, поверх Core) |
| Глубина | ROM-загрузчик + RAM-загрузка **stub** loader'а (как esptool по умолчанию) |
| Платформа | .NET 8 (`net8.0`), `System.IO.Ports` |
| Платформы ОС | Windows в первую очередь, код кросс-платформенный |
| Чипы | ESP32 (magic), ESP32-S3/C3/C6 (`GET_SECURITY_INFO` chip-id) |
| Reset | `default_reset` (DTR/RTS), `usb_reset` (USB-Serial-JTAG), `no_reset` |

## 3. Что реализовано

**Протокол**
- Открытие порта, управление DTR/RTS, вход в download mode (классический и USB-JTAG).
- SLIP-фрейминг, command/response пакеты, checksum (seed `0xEF`), статусы/ошибки.
- `SYNC`, детект чипа (`GET_SECURITY_INFO` → chip-id, fallback на magic-регистр).
- RAM-загрузка stub loader'а + ожидание `OHAI`.
- `SPI_ATTACH`, `SPI_SET_PARAMS`, `CHANGE_BAUDRATE`.
- Запись: `FLASH_BEGIN/DATA/END`, блоки 16 КБ, паддинг `0xFF`, ретраи.
- Верификация `SPI_FLASH_MD5`.
- Стирание: `ERASE_FLASH`, `ERASE_REGION`.
- Чтение: `READ_FLASH` (raw-кадры + ack + MD5-дайджест).
- `WRITE_REG`/`READ_REG` + `READ_FLASH_ID` (JEDEC) через регистры SPI.

**Чипы**
- ESP32, ESP32-S3, ESP32-C3, ESP32-C6: имя, ревизия, MAC, размер flash.
- Per-chip: SPI-база, bootloader offset (`0x1000` для ESP32, `0x0` для остальных),
  efuse-база, stub-блоб, схема ревизии.

**Образы**
- `FirmwareImage` (header/segments/checksum/SHA256), `image-info`.
- Патчинг заголовка bootloader под `--flash-mode/-freq/-size` (+ пересчёт SHA256).

**CLI**
- `list`, `chip-info`, `flash-id`, `flash`, `read-flash`, `erase-flash`, `erase-region`,
  `image-info`; `--trace`, `--before`, `--flash-*`, `--no-stub`, `--no-verify`.

**Проверено на железе**
- ESP32 (ESP32-CAM, 4 МБ) — полная прошивка 4 образов + verify.
- ESP32 (16 МБ, CP210x) — chip-info/flash-id.
- ESP32-S3 (CH343) — detect/stub/read/write.
- ESP32-C3 (USB-JTAG) — detect/stub/read/write.
- ESP32-C6 (USB-JTAG) — detect/stub/read/write.

## 4. Не делаем (пока)

- espefuse / espsecure (eFuse, secure boot, flash encryption).
- Deflate-сжатие (`FLASH_DEFL_*`) — запись несжатая.
- ESP32-S2, ESP8266.
- merge-bin и полноценный парсинг partition table.
- `monitor` (чтение порта), USB-OTG reset.
- GUI.

## 5. Архитектура

```
Esp32Flasher.Cli  ─┐
                   ├─► Esp32Flasher.Core
(будущий GUI)    ─┘        │
                           ├── Serial:   ISerialTransport, SystemSerialTransport, SerialPorts
                           ├── Slip:     SlipCodec, SlipDecoder
                           ├── Protocol: EspCommand, EspPacket, EspResponse, EspChecksum
                           ├── Reset:    ResetMode, ResetSequence (Classic/Hard/UsbJtag)
                           ├── Loaders:  FrameReader, EspLoader
                           ├── Chips:    ChipConfig, ChipRegistry, StubBlob, StubBlobs
                           ├── Imaging:  FirmwareImage, ImageFlashParams, FlashImage
                           └── (root):   Esp32Connection, EspConnectionOptions, FlashOptions, FlashProgress
```

`Core` не зависит от CLI и не пишет в `System.Console`.

## 6. Структура решения

```
Esp32Flasher.slnx
├─ src/
│  ├─ Esp32Flasher.Core/
│  │  └─ Resources/            esp32_stub.json, esp32s3/esp32c6/esp32c3_stub.json (+ лицензии)
│  └─ Esp32Flasher.Cli/
├─ tests/
│  └─ Esp32Flasher.Core.Tests/
├─ docs/                       этот файл, protocol.md, app-api.md, esp32cam.md, roadmap.md
└─ README.md                   практическое руководство по использованию
```

## 7. Ссылки и референсы

**Официальные доки Espressif**
- Serial Protocol: https://docs.espressif.com/projects/esptool/en/latest/esp32/advanced-topics/serial-protocol.html
- Firmware Image Format: https://docs.espressif.com/projects/esptool/en/latest/esp32/advanced-topics/firmware-image-format.html
- Boot Mode Selection: https://docs.espressif.com/projects/esptool/en/latest/esp32/advanced-topics/boot-mode-selection.html
- Flasher Stub: https://docs.espressif.com/projects/esptool/en/latest/esp32/esptool/flasher-stub.html

**Исходники и эталоны**
- esptool: https://github.com/espressif/esptool
- stub-блобы: `esptool/targets/stub_flasher/2/{esp32,esp32s3,esp32c6,esp32c3}.json` (MIT/Apache)
- targets: `esptool/targets/{esp32,esp32s3,esp32c3,esp32c6}.py` (magic, chip-id, SPI-регистры, efuse)
- esp-serial-flasher (C): https://github.com/espressif/esp-serial-flasher

**Готовые .NET-реализации (референс, не зависимости)**
- EspDotNet: https://github.com/KooleControls/EspDotNet
- ESPTool: https://github.com/multitone/ESPTool
- nanoFirmwareFlasher: https://github.com/nanoframework/nanofirmwareflasher

**Прочее**
- SLIP (RFC 1055): https://datatracker.ietf.org/doc/html/rfc1055
- `System.IO.Ports`: https://learn.microsoft.com/dotnet/api/system.io.ports.serialport

## 8. Критерий готовности (достигнут)

Прошивка ESP32-CAM четырьмя образами с MD5-верификацией и последующим запуском —
выполняется (`README.md`, `docs/esp32cam.md`). Дополнительно проверены S3/C3/C6
(детект, stub, чтение, запись).
