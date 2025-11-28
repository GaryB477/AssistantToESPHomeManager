using ESP_Home_Interactor;

var host = "192.168.0.26";
var port = 6053;

ESPBase espBase = new ESPBase(host, port);
await espBase.Init();
await espBase.Run();