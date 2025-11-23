using ESP_Home_Interactor;

var host = "192.168.0.26";
var port = 6053;

ESP esp = new ESP(host, port);
await esp.Init();
await esp.Run();