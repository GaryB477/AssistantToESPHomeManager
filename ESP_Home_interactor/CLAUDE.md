# ESP_Home_interactor

## Architecture

This project is a C# console application that communicates with ESPHome devices using the native API protocol over TCP sockets with Protocol Buffers.

## Project Structure
- `Program.cs` - Main application entry point with TCP connection and message handling
- `protobuf_out/` - Generated C# classes from Protocol Buffer definitions
  - `Api.cs` - Main protobuf message types (HelloRequest, HelloResponse, DisconnectRequest, etc.)
  - `ApiOptions.cs` - Protocol options
- `python_reference/` - Reference Python implementation from aioesphomeapi
  - `client.py` - High-level API client
  - `connection.py` - Low-level connection handling and frame protocol
  - `client_base.py` - Base client functionality
  - `host_resolver.py` - Host resolution logic

## Code Guidelines
- Keep implementation simple without unnecessary complexity or classes
- Use async/await for all I/O operations
- Follow the Python reference implementation patterns from `python_reference/connection.py`
- Handle errors gracefully with try-catch blocks
- Always perform proper disconnect sequence before closing socket

## Dependencies
- Google.Protobuf - For protobuf serialization/deserialization
- System.Net.Sockets - For TCP socket communication

## Reference
Based on ESPHome Native API protocol: https://esphome.io/components/api.html