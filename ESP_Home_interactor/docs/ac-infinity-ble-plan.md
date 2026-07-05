# Plan: AC Infinity Controller 69 über C# ESPHome-Client steuern

## Ziel

Den AC Infinity Controller 69 **offline** aus dieser C#-App heraus steuern (Fan-Level pro Port),
indem wir über den ESP32-Bluetooth-Proxy sprechen. Damit ersetzen wir Home Assistant + die
`hunterjm/ac-infinity-hacs` Integration vollständig.

Das löst zwei bestehende Probleme des HA-Setups auf einmal:

1. **Version-Skew-Instabilität** — HA 2025.5.2 spricht API 1.10, ESP32-Firmware (ESPHome 2026.5.1)
   erwartet 1.14+, HA droppt die Proxy-Verbindung alle ~30s (`CONNECTION_CLOSED errno=128`).
   Diese App schickt selbst API 1.13 (`ESPBase.SendHelloWorld`), kein HA im Spiel → weg.
2. **Nur 1 Fan statt 2** — die hunterjm-Integration erstellt genau eine Fan-Entity pro Controller
   und kann keine einzelnen Ports adressieren. Eigener Code kann Port 0/1/… direkt ansprechen.

## Ausgangslage (Stand: Kontext dieser Session)

### Was diese App schon kann
- ESPHome Native-API über TCP + Protobuf: Framing (`ESPHomeConnection`), VarInt, Preamble.
- Hello/Auth-Flow, Entity-Discovery (`ListEntities`), State-Subscribe (`ESPBase`).
- Switch-Control als Referenz für den Command-Weg.
- **Alle** Protobuf-Typen sind generiert (`protobuf_out/Api.cs`), inklusive der Bluetooth-Proxy-
  Nachrichten (Message-IDs 66–88, siehe `Types.cs`). Nur die **Handhabung** fehlt — implementiert
  sind bisher ausschliesslich sensor / binary_sensor / switch.

### ESP32-Proxy (anderes Repo: `ACInfinityBLEConnector/ac-infinity-proxy.yaml`)
- Board `dfrobot_firebeetle2_esp32e`, ESPHome 2026.5.1, `bluetooth_proxy: active: true`.
- `wifi: power_save_mode: none` (nötig gegen BLE/WiFi-Coexistence-Latenz).
- **`api: encryption: key:` ist gesetzt** → siehe Stolperstein Encryption unten.
- Erreichbar via mDNS `ac-infinity-proxy.local` (IP wechselt per DHCP, war .17/.18).

### Controller-Fakten (verifiziert)
- BLE-MAC: `18:8B:0E:F2:4C:32` — für `BluetoothDeviceRequest.address` als **uint64** kodieren
  (die 6 MAC-Bytes big-endian in ein uint64).
- Advertised **ohne Namen**, erkennbar über **manufacturer_id 2306 (0x0902)**.
- Custom-Services: `0xFFFF` (Control / Fan-Write-Char), `70D51000-2C7F-4E75-AE8A-D758951CE4E0`, `0x180A`.
- Erlaubt nur **eine** aktive BLE-Verbindung → die Phone-App muss geschlossen sein.
- ESP32 sieht den Controller (RSSI -33), GATT-Connect + Service-Discovery lokal bereits bewiesen.

## Referenzen
- `python_reference/` in diesem Repo (aioesphomeapi — Connection/Client-Muster).
- `aioesphomeapi/api.proto` — Bluetooth-Message-Definitionen (schon im Repo).
- `hunterjm/ac-infinity-ble` (Python) — das AC-Infinity-Anwendungsprotokoll (CRC16-Frames).

---

## Architektur der Erweiterung

Zwei neue Schichten oben auf der bestehenden `ESPHomeConnection`:

```
Program / GUI
  └─ AcInfinityController      (Anwendungsprotokoll: set_level(port, level), Frames, CRC16)
       └─ BleProxyClient       (ESPHome BT-Proxy GATT-Flow: connect, services, notify, write)
            └─ ESPHomeConnection (bestehend: TCP + Protobuf-Framing)
```

**Wichtige Modelländerung:** Die bestehende App ist one-shot (connect → tun → drain → disconnect,
Polling via `DataAvailable`). BLE braucht eine **persistente Verbindung mit asynchroner
Notify-Schleife**. Das erfordert einen echten Dispatch-Loop (ein Reader-Task, der nach
Message-Type verteilt und auf `TaskCompletionSource`-Antworten wartet), statt des Drain-Modells.

---

## Phasen

### Phase 0 — Vorklärungen (blockierend, vor dem Coden)
- [ ] **Encryption-Entscheid** (siehe Stolperstein 1): entweder Proxy auf Plaintext-API umstellen
      *oder* Noise-Handshake in C# bauen. Empfehlung: erst Plaintext (schnell), Noise später.
- [ ] **GATT-Handles ermitteln**: Write-Handle (Service 0xFFFF) und Notify-Handle bestimmen.
      Einmalig via `BluetoothGATTGetServicesResponse` dumpen und notieren. Handles sind uint32,
      **keine UUIDs** — `BluetoothGATTWriteRequest.handle` will die Zahl.
- [ ] AC-Infinity-Frame-Format aus `hunterjm/ac-infinity-ble` genau extrahieren (Header, Command-Typ,
      Port-Parameter, Work-Type, CRC16-Variante/Polynom).

### Phase 1 — Dispatch-Loop (Refactor Fundament)
- [ ] Reader-Task in/neben `ESPHomeConnection`, der kontinuierlich `ReadMessage()` liest und nach
      `MessageType` verteilt.
- [ ] Request/Response-Korrelation via `TaskCompletionSource` (z. B. auf Connection-State,
      GetServicesDone, WriteResponse).
- [ ] Ping/Pong sauber beantworten (`PingRequest` → `PingResponse`), damit die Verbindung offen bleibt.
- [ ] Bestehenden Switch-/Sensor-Pfad auf den Loop umstellen (nicht zwei Modelle parallel).

### Phase 2 — BleProxyClient (ESPHome BT-Proxy-Flow)
- [ ] `BluetoothProxySubscribeRequest` (optional; nur nötig, wenn wir per Advertisement scannen
      statt die MAC hart zu setzen — MAC ist bekannt, also erstmal überspringbar).
- [ ] `BluetoothDeviceRequest{address, request_type=CONNECT}` senden → auf
      `BluetoothDeviceConnectionStateResponse{connected=true}` warten.
- [ ] `BluetoothGATTGetServicesRequest` → `...Response`-Stream sammeln bis
      `...GetServicesDoneResponse`; Write- und Notify-Handle auflösen.
- [ ] `BluetoothGATTNotifyRequest{address, handle}` → Notifications aktivieren; eingehende
      `BluetoothGATTNotifyDataResponse{handle, data}` im Loop verarbeiten.
- [ ] `BluetoothGATTWriteRequest{address, handle, response, data}` als Schreibprimitive.
- [ ] Sauberes Disconnect (`BluetoothDeviceRequest DISCONNECT`) + Fehlerpfade
      (`BluetoothGATTErrorResponse`).

### Phase 3 — AcInfinityController (Anwendungsprotokoll)
- [ ] CRC16-Berechnung portieren (Polynom/Init aus hunterjm bestätigen).
- [ ] Command-Frame-Builder: Header `[165, 0]`, Command-Typ 3, `set_level(work_type, level, port)`.
- [ ] `SetLevel(port, level)` und ggf. `GetState()` über Write + Notify-Antwort.
- [ ] **Multi-Port**: Port 0 und Port 1 getrennt ansteuern (der eigentliche Mehrwert ggü. hunterjm).

### Phase 4 — Integration & GUI-Anbindung
- [ ] Konfig: Proxy-Host/-Port + Controller-MAC in `config.json` / `appsettings.json`.
- [ ] Service-Layer (`Services/`) analog `EspDeviceService`, aber persistent verbunden.
- [ ] Blazor-GUI: pro Port ein Slider/Toggle. (Kommt nach dem GUI-Grundgerüst des Users.)

---

## Stolpersteine / Risiken

1. **Encryption (kritisch)** — Proxy-YAML hat `api: encryption: key:`. Diese App kann **kein Noise**
   (nur Plaintext-`HelloRequest`). Optionen:
   - (a) Encryption-Key im ESP-Config entfernen → Plaintext-API im LAN. Schnell, aber unverschlüsselt.
   - (b) Noise_NNpsk0-Handshake in C# implementieren. Sauberer, mehr Aufwand.
   Empfehlung: (a) zum Bauen/Testen, (b) optional später.
2. **GATT-Handles** — müssen am lebenden Controller aus GetServices ermittelt werden (Service 0xFFFF
   ist bekannt, konkrete Handle-Nummern noch offen).
3. **Nur 1 aktive BLE-Verbindung** — Phone-App muss zu sein, sonst kein Connect.
4. **Persistenz** — Reader-Loop muss Reconnect/Timeouts robust behandeln (BLE-Drops, WiFi).
5. **MAC → uint64** — korrekte Byte-Reihenfolge für `address` sicherstellen.

## Aufwandseinschätzung

Mittel. Das harte 80 % (Framing, Protobuf, Connect/Auth/Entity) steht. Grösster Neu-Aufwand:
Dispatch-Loop-Refactor (Phase 1) + BT-Proxy-Flow (Phase 2). Das AC-Protokoll selbst (Phase 3) ist
klein, sobald das CRC16-Format bestätigt ist.

## Offene Punkte für später
- Encryption-Entscheid (a vs. b).
- Konkrete GATT-Handles + bestätigtes CRC16-Format.
- Reconnect-Strategie bei BLE-Drops.
