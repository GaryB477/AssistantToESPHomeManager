using System.Runtime.InteropServices;
using ESP_Home_Interactor;
using ESP_Home_Interactor.Config;
using ESP_Home_Interactor.Entities;
using ESP_Home_Interactor.helper;

var espConfigs = await Config.Read("/Users/marc/git/private/HomePeter/ESP_Home_interactor/config.json");
var ESPs = espConfigs.ESPNode.Select(esp => new ESPBase(esp)).ToList();

foreach (var esp in ESPs)
{
   Console.WriteLine(esp.Host); 
}
// await espBase.Init();
// await espBase.Run();