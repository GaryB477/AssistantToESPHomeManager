Goal: This should be used as a simple C# library to interact with esp32 running esphome.

Some links:

- https://github.com/esphome/aioesphomeapi/tree/main

TODOs:

- [ ] Implement wrappter for the
  protobuff [file](https://github.com/esphome/aioesphomeapi/blob/main/aioesphomeapi/api.proto)

Getting started:

```sh 
# Generate protobuf files
cd aioesphomeapi 
protoc --csharp_out=../protobuf_out api_options.proto api.proto
```