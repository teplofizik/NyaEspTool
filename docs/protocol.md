# Serial Protocol — что реализуем (кратко по существу)

Источник: https://docs.espressif.com/projects/esptool/en/latest/esp32/advanced-topics/serial-protocol.html
(и `esptool/targets/esp32.py`, `stub_flasher_32.json`). Здесь — только то, что нужно для MVP.

## 1. Транспорт

- Serial, формат **8N1**, стартовый baud **115200**. Позже поднимаем через `CHANGE_BAUDRATE`.
- Управление линиями: **DTR → GPIO0**, **RTS → EN/CHIP_PU** (на платах с авто-reset).
- Внимание (Windows/.NET): при открытии порта драйвер может дёрнуть DTR/RTS. Hardware flow
  control держать **выключенным** (`Handshake.None`).

## 2. Вход в download mode (ClassicReset)

Сигналы active-low. Классическая последовательность (порядок важен), соответствие
esptool `ClassicReset`:

```
DTR=0 (IO0 high)          # не тянем boot
RTS=1 (EN low)  sleep 100ms   # чип в reset
DTR=1 (IO0 low)
RTS=0 (EN high) sleep 50ms    # выходим из reset, IO0=low => bootloader
DTR=0 (IO0 high)          # отпускаем
```

Затем ждём boot-лог на 115200 и начинаем `SYNC`. Если авто-reset не сработал —
режим `no_reset`: пользователь сам держит BOOT при подаче питания.

## 3. SLIP-фрейминг

- Пакет обрамляется `0xC0` в начале и конце.
- Внутри тела: `0xC0 → 0xDB 0xDC`, `0xDB → 0xDB 0xDD`.
- Экранирование делается **после** расчёта длины и checksum, поэтому длина «по проводу»
  больше поля `Size`.
- Чтение: копим байты до первого `0xC0`, затем до следующего `0xC0`; между ними снимаем escape.

## 4. Формат пакетов (little-endian)

**Запрос (host → chip):**
| Offset | Поле | Значение |
| --- | --- | --- |
| 0 | Direction | `0x00` |
| 1 | Command | opcode |
| 2-3 | Size | длина Data |
| 4-7 | Checksum | 8-битная контрольная сумма (значаща только для `*_DATA`) |
| 8.. | Data | payload |

**Ответ (chip → host):**
| Offset | Поле | Значение |
| --- | --- | --- |
| 0 | Direction | `0x01` |
| 1 | Command | эхо запроса |
| 2-3 | Size | длина Data |
| 4-7 | Value | данные для `READ_REG` (иначе 0) |
| 8.. | Data | payload |

Чтение ответа: читаем пакеты, пока не встретим пакет с `Command == отправленной` (или timeout).

## 5. Checksum

- seed = `0xEF`, затем XOR по каждому байту секции «data to write».
- Применяется только к payload `MEM_DATA`/`FLASH_DATA`/`FLASH_DEFL_DATA` (не к первым 16 байтам).
- 8-битный результат кладём в поле Checksum заголовка (как 32-битное LE).
- Для остальных команд Checksum игнорируется (можно 0).

## 6. Статусы и ошибки

- **Stub loader**: последние 2 байта Data: `Status` (0 ok / 1 fail), `Error`.
  Error-коды `0xC*`, `0xFF` = unimplemented.
- **ROM loader (ESP32)**: последние **4** байта, значащи только первые два (Status, Error);
  хвостовые два — reserved. ROM-коды: `0x00..0x0F`, `0x64..0x69`.

## 7. Команды (подмножество для MVP)

| Opcode | Имя | Payload (запрос) |
| --- | --- | --- |
| `0x02` | FLASH_BEGIN | size_to_erase, num_packets, packet_size, offset |
| `0x03` | FLASH_DATA | data_len, seq, 0, 0, data… (Uses checksum) |
| `0x04` | FLASH_END | `0`=reboot, `1`=run user code |
| `0x05` | MEM_BEGIN | total_size, num_packets, packet_size, mem_offset |
| `0x06` | MEM_END | execute_flag, entry_point |
| `0x07` | MEM_DATA | data_len, seq, 0, 0, data… (Uses checksum) |
| `0x08` | SYNC | `07 07 12 20` + 32×`0x55` |
| `0x09` | WRITE_REG | address, value, mask, delay_us |
| `0x0a` | READ_REG | address (ответ в поле Value) |
| `0x0b` | SPI_SET_PARAMS | id, total_size, block_size, sector_size, page_size, status_mask |
| `0x0d` | SPI_ATTACH | 0 (normal); у ROM ещё второй word = 0 |
| `0x0f` | CHANGE_BAUDRATE | new_baud, old_baud (0 для ROM) |
| `0x10` | FLASH_DEFL_BEGIN | uncompressed_size, num_packets, packet_size, offset |
| `0x11` | FLASH_DEFL_DATA | data_len, seq, 0, 0, deflate(data)… |
| `0x12` | FLASH_DEFL_END | `0`/`1` |
| `0x13` | SPI_FLASH_MD5 | address, size, 0, 0 → 16 raw bytes (stub) / 32 hex ASCII (ROM) |
| `0xd0` | ERASE_FLASH | — (stub only) |
| `0xd1` | ERASE_REGION | offset, size (кратно sector size; stub only) |
| `0xd2` | READ_FLASH | offset, read_len, sector_size, packet_size, max_in_flight (stub only) |
| `0xd3` | RUN_USER_CODE | — (stub only) |

## 8. Последовательность инициализации

1. Открыть порт (115200), сделать ClassicReset, подождать ~100ms.
2. Слать `SYNC` (payload из 36 байт) с интервалом ~100ms, пока не получим валидный ответ
   (ответ вида `01 08 04 00 07 12 55 55 00 00`). Обычно достаточно 3-7 попыток.
3. Определить чип: `READ_REG(0x40001000)`.
   - ESP32 → `0x00F01D83`.
   - (для будущих чипов — свой magic либо `GET_SECURITY_INFO 0x14`).
4. (ROM) `SPI_ATTACH(0)`, `SPI_SET_PARAMS(...)`.
5. Загрузить stub (см. §9) → дождаться `OHAI`.
6. (опц.) `CHANGE_BAUDRATE(new, old)`, затем сменить baud у порта и `flush_input()`.
7. Далее все команды идут на stub loader.

## 9. Загрузка stub loader (RAM download)

- Источник сегментов: `esptool/targets/stub_flasher/2/esp32.json` (лицензия MIT/Apache,
  встраивается как `Resources/esp32_stub.json`): поля `entry`, `text`, `text_start`,
  `data`, `data_start`, `bss_start` (bss обычно не грузим).
- Формируем последовательность:
  1. `MEM_BEGIN(total_size, num_packets, packet_size=0x4000, mem_offset=text_start)`
  2. `MEM_DATA` для каждого блока (16 КБ), checksum обязательна.
  3. `MEM_END(execute=1, entry_point=entry)`
- Успех = приходит unsolicited SLIP-пакет `C0 4F 48 41 49 C0` = `"OHAI"` (единственный
  неответный пакет от чипа).
- `--no-stub`: пропускаем этот шаг, работаем только командами ROM.

## 10. SPI Set Params (значения по умолчанию для ESP32)

Из `esptool` (`flash_set_parameters`):
`id=0`, `total_size` = размер по `--flash-size detect` (из JEDEC ID, иначе 4 МБ),
`block_size=64 KB`, `sector_size=4 KB`, `page_size=256`, `status_mask=0xFFFF`.

## 11. Запись во flash

1. Разбить данные по блокам по **16 КБ** (`packet_size`); последний блок **дополнить `0xFF`**
   до размера блока.
2. `FLASH_BEGIN(size_to_erase, num_packets, packet_size, offset)`
   (ROM сам делает erase; stub делает just-in-time erase при записи).
3. `FLASH_DATA` для каждого блока: `data_len, seq(с 0), 0, 0, data` + checksum.
   Ответ `Status` проверяем; при ошибке — ретрай блока (как esptool).
4. `FLASH_END(reboot=0)` — если надо продолжить другими командами; `reboot=1` — в конце.
5. Несколько образов: повторяем с нужными offset'ами; `reboot=1` только на последнем.

**Сжатый вариант** (то же, но `FLASH_DEFL_*`): тело сжимаем gzip-deflate (zlib, уровень 9),
`uncompressed_size` = исходный размер блока до паддинга. В MVP — опционально.

## 12. Верификация

- После записи образа: `SPI_FLASH_MD5(offset, size, 0, 0)`.
- Сравнить с MD5 исходных данных (**до** паддинга `0xFF` — на чипе `FLASH_END`/erase вернёт
  реальное содержимое; в esptool сравнивается MD5 записанных байт без паддинга). Проверять
  подробно по эталону `esptool/flash_verify`.
- На stub ответ = 16 сырых байт; на ROM = 32 hex-символа.

## 13. Чтение flash (stub, `READ_FLASH 0xd2`)

1. `READ_FLASH(offset, read_len, sector_size=4096, packet_size, max_in_flight)`.
2. Чип шлёт **сырые** SLIP-пакеты с данными (без заголовка/метаданных).
3. После каждого принятого пакета host шлёт 4-байтный raw ack (число принятых байт).
4. В конце чип шлёт 16-байтный MD5 всего прочитанного (тоже raw SLIP-пакет).
5. Затем возвращается обычный command/response режим.

## 14. Точки, где чаще всего ломается (заметки для отладки)

- Забыли снять escape/пересчитать checksum после экранирования.
- Не прочитали «лишние» пакеты: `SYNC` при старте приходит несколько раз → читаем до `Command==SYNC`.
- Драйвер USB-serial дёргает RTS/DTR при open → чип уходит в reset loop. Держать `Handshake.None`,
  не читать до открытия, при необходимости явно снять линии.
- После `CHANGE_BAUDRATE` надо почистить входной буфер (мусор на смене baud).
- Поле `total_size` в `SPI_SET_PARAMS` при неверном значении ломает запись на чипах >4 МБ.
