# Goal
I want an alternative to the veeery convoluted and annoying Home Assistant. Its just not what I need.
This tool is initially used to provide me with a dashboard for my homegrow setup. To achieve this, I want to utilize the preexisting [Esphome](https://esphome.io/) ecosystem since it provides a simple way to manage esp32 micro controllers and even allows OTA updates.
In addition to esphome, it should also support AC Infinity 69 (nice) controllers via bluetooth proxies. This has already been tested with the [BLE proxy](ESP_Home_interactor/integration_test/ACInfinity_BLE_proxy/ac-infinity-proxy.yaml)

## Must requirements
**Functionallity**
- Interact with esphome esp32 modules. It therefore needs to fully compatible with these types of sensor modules.
- It should have customizable light cycles (similar to automations in HASS). These are defined by
  - 1 day consists of two parts: A high and a low cycle
  - It needs a defined time where the cycles change. For example: "Phase on" - start 04:00, end 16:00
  - For each phase, all actors (fans, lamps) need to have defined states. These states then need to be automatically set.

**GUI**
- Have a graphical component. I want the dashboard to
  - Allow me to see the current sensor values
  - Show me the current light level (and manually override the current cycle if needed)
  - Change different light cycles

**Logging and notifications**
- Especially during early development, all events need to get logged.
- If possible, it should also have the possibility to notify me about problems such as when the light could not be turned on.

## Some links:
- https://github.com/esphome/aioesphomeapi/tree/main


Getting started:

```sh 
# Generate protobuf files
cd aioesphomeapi 
protoc --csharp_out=../protobuf_out api_options.proto api.proto
```