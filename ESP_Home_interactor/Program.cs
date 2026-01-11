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
   await esp.InitConnection();
   await esp.FetchAllSensorEntities();
   await esp.Sync();
}

EspBase? switchESP = ESPs.FirstOrDefault(esp => esp.Host == "192.168.0.26");
var switchEntity = switchESP?.SwitchEntities.FirstOrDefault(s => s.Name == "test_switch");
Console.WriteLine( $"Got value for sensor {switchEntity?.ObjectId}: {switchEntity.GetValue()}");
foreach (EspBase esp in ESPs)
{
   await esp.Cleanup();
}
