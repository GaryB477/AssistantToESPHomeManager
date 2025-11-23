# ESP_Home_interactor

## Architecture

This project is a C# console application that communicates with ESPHome devices using the native API protocol over TCP
sockets with Protocol Buffers.

## Project Structure

- `Program.cs` - Main application entry point
- `ESP.cs` - High-level ESPHome API client
    - Connection initialization (Init)
    - Device workflow orchestration (Run)
    - Hello and authentication flow
    - Entity discovery and state subscription
    - Switch control functionality
    - Message draining and cleanup
- `ESPHomeConnection.cs` - Low-level protocol connection handler
    - TCP socket and NetworkStream management
    - Protocol message framing (SendMessage, ReadMessage)
    - VarInt encoding/decoding
    - Preamble validation and error handling
    - Stream alignment management
- `protobuf_out/` - Generated C# classes from Protocol Buffer definitions
    - `Api.cs` - Main protobuf message types (HelloRequest, HelloResponse, DisconnectRequest, SwitchCommandRequest, etc.)
    - `ApiOptions.cs` - Protocol options
- `python_reference/` - Reference Python implementation from aioesphomeapi
    - `client.py` - High-level API client
    - `connection.py` - Low-level connection handling and frame protocol
    - `client_base.py` - Base client functionality
    - `host_resolver.py` - Host resolution logic
- `aioesphomeapi/README.md` - Protocol documentation including message types and IDs
- `integration_test/` - ESPHome test device configuration and C++ reference implementation

## Protocol Implementation

### Message Format
Each message follows this structure:
1. **Preamble** (0x00) - Message boundary marker
2. **VarInt** - Length of protobuf data (not including type)
3. **VarInt** - Message type ID
4. **Protobuf data** - The actual message payload

### Connection Flow
1. **Hello** - Exchange HelloRequest/HelloResponse (types 1/2)
2. **Authentication** - Send AuthenticationRequest, receive AuthenticationResponse (types 3/4)
3. **Entity Discovery** - List entities with ListEntitiesRequest (type 11)
4. **State Subscription** - Subscribe with SubscribeStatesRequest (type 20)
5. **Control** - Send commands (e.g., SwitchCommandRequest type 33)
6. **Disconnect** - Send DisconnectRequest/Response (types 5/6)

### Key Implementation Details

- **Stream Alignment**: Always check `stream.DataAvailable` before reading to prevent partial message reads
- **Timeout Handling**: Use `CancellationToken` instead of `Task.WhenAny` to avoid corrupting stream state
- **Message Draining**: Before disconnect, drain any pending messages to ensure clean shutdown
- **Error Handling**: Catch `EndOfStreamException` for connection closures and `InvalidDataException` for protocol errors

## Architecture

The codebase follows a layered architecture:

### Layer 1: Protocol Layer (ESPHomeConnection)
- Manages low-level TCP socket communication
- Handles protocol framing (preamble, VarInt, protobuf data)
- Ensures stream alignment and validates message structure
- Provides clean abstraction over raw socket operations

### Layer 2: API Layer (ESP)
- Implements ESPHome-specific workflows (hello, auth, discovery, control)
- Manages device state and entity tracking
- Orchestrates message sequences for operations
- Handles timeout management and message draining

### Layer 3: Application Layer (Program)
- Entry point that instantiates and runs the ESP client
- Can be extended for specific use cases

## Code Guidelines

- Keep implementation simple and maintain clear separation of concerns
- Low-level protocol logic belongs in `ESPHomeConnection`
- High-level workflows and business logic belong in `ESP`
- Use async/await for all I/O operations
- Follow the Python reference implementation patterns from `python_reference/connection.py`
- Handle errors gracefully with try-catch blocks
- Always perform proper disconnect sequence before closing socket
- Check `connection.DataAvailable` before attempting to read messages to maintain stream alignment
- Use visual indicators in console output (→ for sent, ← for received, ⚠ for warnings, ✓ for success)

## Testing

Run the application with `dotnet run` to test switch control functionality. The application will:
1. Connect to the ESPHome device
2. Authenticate
3. Discover entities
4. Subscribe to state updates
5. Toggle the first switch found (ON then OFF)
6. Cleanly disconnect

## Dependencies

- Google.Protobuf - For protobuf serialization/deserialization
- System.Net.Sockets - For TCP socket communication

## Reference

Based on ESPHome Native API protocol: https://esphome.io/components/api.html