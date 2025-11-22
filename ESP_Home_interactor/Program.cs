// See https://aka.ms/new-console-template for more information

Console.WriteLine("Hello, World!");

/*
 async def main():
       """Connect to an ESPHome device and wait for state changes."""
       cli = aioesphomeapi.APIClient("api_test.local", 6053, "MyPassword")
   
       await cli.connect(login=True)
   
       def change_callback(state):
           """Print the state changes of the device.."""
           print(state)
   
       # Subscribe to the state changes
       cli.subscribe_states(change_callback)
   
   loop = asyncio.get_event_loop()
   try:
       asyncio.ensure_future(main())
       loop.run_forever()
   except KeyboardInterrupt:
       pass
   finally:
       loop.close() 
*/