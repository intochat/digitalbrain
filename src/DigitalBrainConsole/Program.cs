using DigitalBrainConsole;

await using var brain = await Brain.CreateAsync(args);
await ConsoleSession.RunAsync(brain);
