# ESPHome Native API Protocol Buffer Documentation

This directory contains the Protocol Buffer definitions for the ESPHome Native API.

## Overview

The ESPHome Native API is a TCP-based protocol that allows clients (like Home Assistant) to communicate with ESPHome devices. Messages are encoded using Protocol Buffers (protobuf) for efficient binary serialization.

## Protocol Structure

### Message Format

Each message follows this binary format:
1. **Zero byte** (0x00) - Message delimiter
2. **VarInt** - Size of the message object (excluding type)
3. **VarInt** - Message type ID
4. **Protobuf message** - The actual message encoded as protobuf

### Connection Flow

1. **Hello** - Client sends `HelloRequest` (ID: 1) with client info and API version
2. **Hello Response** - Server responds with `HelloResponse` (ID: 2) containing server info and selected protocol version
3. **Authentication** - Client sends `AuthenticationRequest` (ID: 3) with password
4. **Auth Response** - Server responds with `AuthenticationResponse` (ID: 4) indicating success/failure
5. **Communication** - Once authenticated, both parties can exchange messages
6. **Disconnect** - Either party can send `DisconnectRequest` (ID: 5) to close connection gracefully

> **Note:** If authentication fails, the connection must be closed immediately without sending a disconnect message.

## Core Messages

### Connection Management

| Message | ID | Direction | Description |
|---------|-------|-----------|-------------|
| `HelloRequest` | 1 | Client→Server | Initial connection with client info and API version |
| `HelloResponse` | 2 | Server→Client | Server info and negotiated API version |
| `AuthenticationRequest` | 3 | Client→Server | Password for authentication |
| `AuthenticationResponse` | 4 | Server→Client | Authentication result |
| `DisconnectRequest` | 5 | Both | Request to close connection |
| `DisconnectResponse` | 6 | Both | Acknowledgement before closing |
| `PingRequest` | 7 | Both | Keep-alive/latency check |
| `PingResponse` | 8 | Both | Ping acknowledgement |
| `DeviceInfoRequest` | 9 | Client→Server | Request device information |
| `DeviceInfoResponse` | 10 | Server→Client | Device details (name, MAC, version, etc.) |

### Entity Discovery and State

| Message | ID | Direction | Description |
|---------|-------|-----------|-------------|
| `ListEntitiesRequest` | 11 | Client→Server | Request list of all entities |
| `ListEntitiesDoneResponse` | 19 | Server→Client | Indicates entity list is complete |
| `SubscribeStatesRequest` | 20 | Client→Server | Subscribe to entity state changes |

## Entity Types

The API supports the following entity types with corresponding List/State/Command messages:

### Sensors (Read-only)
- **Binary Sensor** (ID: 12, 21) - On/off sensors
- **Sensor** (ID: 16, 25) - Numeric sensors with unit of measurement
- **Text Sensor** (ID: 18, 27) - String state sensors
- **Event** (ID: 107, 108) - Event entities

### Controls (Read-write)
- **Switch** (ID: 17, 26, 33) - On/off control
- **Light** (ID: 15, 24, 32) - Lighting control with color modes, brightness, effects
- **Fan** (ID: 14, 23, 31) - Fan control with speed, oscillation, direction
- **Cover** (ID: 13, 22, 30) - Window/blind control with position
- **Climate** (ID: 46, 47, 48) - HVAC control
- **Lock** (ID: 58, 59, 60) - Lock control
- **Valve** (ID: 109, 110, 111) - Valve control with position

### Input Entities
- **Button** (ID: 61, 62) - Pressable button
- **Number** (ID: 49, 50, 51) - Numeric input with min/max/step
- **Select** (ID: 52, 53, 54) - Dropdown selection
- **Text** (ID: 97, 98, 99) - Text input
- **Date** (ID: 100, 101, 102) - Date input
- **Time** (ID: 103, 104, 105) - Time input
- **DateTime** (ID: 112, 113, 114) - Date+time input

### Special Entities
- **Camera** (ID: 43, 44, 45) - Image streaming
- **Media Player** (ID: 63, 64, 65) - Media playback control
- **Siren** (ID: 55, 56, 57) - Siren/alarm control
- **Alarm Control Panel** (ID: 94, 95, 96) - Alarm system control
- **Update** (ID: 116, 117, 118) - OTA update management

## Advanced Features

### Bluetooth Proxy (ID: 66-88, 93, 126-127)
Allows ESPHome device to act as Bluetooth LE proxy:
- Advertisement scanning
- GATT operations (read, write, notify)
- Device connection management
- Pairing/unpairing

### Voice Assistant (ID: 89-92, 106, 115, 119-123)
Voice assistant integration:
- Audio streaming
- Wake word detection
- Timer management
- Announcements
- Configuration management

### Home Assistant Integration
- **Services** (ID: 34, 35, 41, 42, 130) - Custom service calls
- **State Import** (ID: 38-40) - Import HA entity states
- **Time Sync** (ID: 36, 37) - Time synchronization
- **Logs** (ID: 28, 29) - Log streaming

### Z-Wave Proxy (ID: 128-129)
Z-Wave device proxying through ESPHome.

### Noise Encryption (ID: 124-125)
Encrypted communication support.

## Message Options

Messages use custom options defined in `api_options.proto`:

### Message-level Options
- `id` - Unique message type identifier
- `source` - Message direction (CLIENT/SERVER/BOTH)
- `ifdef` - Conditional compilation flag
- `log` - Whether to log this message
- `no_delay` - Disable Nagle's algorithm for this message
- `base_class` - Base class for generated code

### Field-level Options
- `field_ifdef` - Conditional compilation for specific field
- `fixed_array_size` - Fixed-size array for bytes fields
- `pointer_to_buffer` - Zero-copy pointer optimization
- `container_pointer` - Zero-copy container optimization
- `fixed_vector` - Use FixedVector instead of std::vector
- `no_zero_copy` - Disable zero-copy optimization

## API Versioning

The protocol uses semantic versioning with major and minor versions:
- **Major version** - Breaking changes in base protocol
- **Minor version** - Breaking changes in individual messages

Clients must check version compatibility and adapt accordingly.

## Common Patterns

### Entity Identification
Entities are identified by:
- `key` (fixed32) - Unique entity identifier
- `object_id` (string) - Internal ID
- `name` (string) - Human-readable name
- `device_id` (uint32) - Associated device (optional)

### State Reporting
State messages use `missing_state` boolean to indicate if the entity has a valid state:
- `missing_state = false` - State is valid
- `missing_state = true` - No state available yet

### Command Messages
Commands typically include:
- `key` - Target entity
- `has_*` flags - Indicates which fields are set
- Actual values for the command

### Entity Categories
- `ENTITY_CATEGORY_NONE` - Normal entity
- `ENTITY_CATEGORY_CONFIG` - Configuration entity
- `ENTITY_CATEGORY_DIAGNOSTIC` - Diagnostic entity

## File Structure

```
aioesphomeapi/
├── api.proto          # Main protocol definitions
└── api_options.proto  # Custom protobuf options
```

## Implementation Notes

1. **Connection Setup**: Both client and server must negotiate API version during handshake
2. **Error Handling**: Failed authentication closes connection immediately without disconnect message
3. **Keep-alive**: Use ping/pong messages to maintain connection
4. **State Updates**: Subscribe to states once, server pushes updates as they occur
5. **Entity Discovery**: List entities once at connection start, cached by client

## Resources

- [ESPHome Native API Documentation](https://esphome.io/components/api.html)
- [Protocol Buffer Language Guide](https://protobuf.dev/programming-guides/proto3/)

## Version History

This protocol definition is compatible with:
- ESPHome API Version: 1.x
- Protocol Buffer Syntax: proto3 (api.proto), proto2 (api_options.proto)
