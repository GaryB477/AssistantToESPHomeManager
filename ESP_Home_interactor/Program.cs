using System.Runtime.InteropServices;
using ESP_Home_Interactor;
using ESP_Home_Interactor.Config;
using ESP_Home_Interactor.Entities;
using ESP_Home_Interactor.helper;

var espConfigs = await Config.Read("/Users/marc/git/private/HomePeter/ESP_Home_interactor/config.json");
var ESPs = espConfigs.ESPNode.Select(esp => new EspBase(esp)).ToList();

foreach (EspBase esp in ESPs)
{
   Console.WriteLine(esp.Host);
   await esp.Init();
   await esp.Sync();
}

EspBase switchESP = ESPs.First(esp => esp.Host == "192.168.0.26");
var switchEntity = switchESP.SwitchEntities.FirstOrDefault(s => s.Name == "onboardLED", null);
if (switchEntity == null) throw new Exception("Could not find switch");

Console.WriteLine();

await switchEntity.SetStateAsync(switchESP.Connection, true);
await switchESP.Sync();
await switchEntity.SetStateAsync(switchESP.Connection, false);
await switchESP.Sync();

Console.WriteLine();

foreach (EspBase esp in ESPs)
{
   await esp.Cleanup();
}
