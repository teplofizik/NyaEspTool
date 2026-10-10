# ESP32-CAM — конкретика для прошивки

## 1. Что за железо

- Модуль **ESP32-CAM (AI-Thinker)**: чип **ESP32-D0WDQ6**, обычно **4 МБ** внешней flash,
  OV2640 камера, нет onboard USB.
- Часто идёт с платой-адаптером **ESP32-CAM-MB** (Micro-USB) с чипом **CH340** и
  схемой авто-reset (DTR→GPIO0, RTS→EN), как на DevKitC.
- Без MB-платы нужен внешний USB-UART (FTDI/CP2102) и **ручной** вход в boot:
  замкнуть **GPIO0 → GND** и подать питание / нажать reset; отпустить GPIO0 после старта.

Схема auto-reset требует конденсатор между EN и GND; на части сторонних плат его нет →
авто-reset нестабилен, тогда используем `--before no_reset` и держим BOOT руками.

## 2. Артефакты сборки (Arduino ESP32 core / ESP-IDF)

Стандартная карта флеша:

| Offset | Файл | Назначение |
| --- | --- | --- |
| `0x1000` | `bootloader.bin` (или `bootloader_dio_80m.bin`) | 2nd stage bootloader |
| `0x8000` | `partitions.bin` | таблица разделов |
| `0xe000` | `boot_app0.bin` | OTA data (загрузка factory/ota_0) |
| `0x10000` | `firmware.bin` | приложение (sketch) |

Для ESP-IDF добавляются `nvs` (`0x9000`), `phy_init`, `ota_data` и т.д.

## 3. Рекомендуемая команда

Через наш CLI:

```
esp32flash flash --port COM5 --flash-size detect --flash-mode dio --flash-freq 80m \
  0x1000 bootloader.bin \
  0x8000 partitions.bin \
  0xe000 boot_app0.bin \
  0x10000 firmware.bin
```

Эталон (esptool) для сверки:

```
esptool.py --chip esp32 --port COM5 --baud 921600 \
  --before default_reset --after hard_reset \
  write_flash -z --flash_mode dio --flash_freq 80m --flash_size detect \
  0x1000 bootloader.bin 0x8000 partitions.bin 0xe000 boot_app0.bin 0x10000 firmware.bin
```

Почему такие опции:
- `--flash-mode dio` / `--flash-freq 80m` — «правят» заголовок bootloader-образа и
  пересчитывают SHA256; используются значения из сборки.
- `--flash-size detect` — определить размер по JEDEC ID (для CAM обычно 4 МБ).
- Скорость: SYNC идёт на `115200` (по умолчанию), после загрузки stub — на `--flash-baud`
  (по умолчанию `921600`). Можно поднять `--baud`, но не все адаптеры это любят.
- `-z` (deflate) в нашем CLI пока не поддерживается — запись несжатая.

> Для S3/C3/C6 тот же подход, но bootloader лежит по offset `0x0` (не `0x1000`).

## 4. Backup / restore

```
esp32flash read-flash --port COM5 0x0 0x400000 -o backup.bin
esp32flash flash --port COM5 0x0 backup.bin
```

## 5. Частые проблемы

| Симптом | Причина / решение |
| --- | --- |
| `NoSync` / чип не отвечает | Не вошёл в boot: подтянуть GPIO0 к GND, попробовать `--before no_reset`. |
| Компилируется, но не стартует | Не залит `boot_app0.bin` (0xe000) или неверный offset app. |
| Flash определяется меньше/больше | Неверный `--flash-size`; для CAM с 4 МБ — `detect`. |
| Обрывы на 921600 | Дешёвый USB-UART/длинный кабель → снизить до 460800/115200. |
| `SPI_Set_Params` ломает запись | Неверный `total_size` в `SPI_SET_PARAMS` (должен совпадать с реальным flash). |
| Auto-reset «залипает» в reset | Драйвер CH340 дёрнул RTS/DTR при open; проверить `Handshake.None` и схему. |

## 6. Проверка результата

1. Наш `flash` + `verify` (MD5) — OK.
2. `esptool.py verify_flash` на тех же файлах — OK.
3. Чип после `hard_reset` печатает boot-лог на 115200 и поднимает приложение
   (для CAM-примера — Wi-Fi/камера, в зависимости от скетча).
