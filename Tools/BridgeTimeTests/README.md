# Particle frame clock regression

Run `dotnet run --project Tools/BridgeTimeTests -c Release`.

Links the runtime BridgeTime source and checks that particle frame time does not
multiply with game ticks, still respects pause, honors explicit unscaled time,
does not catch up after enable/load, and works in menus. The default Tick clock
used by other components remains unchanged. This is a clock regression test,
not a player-device FPS benchmark.
