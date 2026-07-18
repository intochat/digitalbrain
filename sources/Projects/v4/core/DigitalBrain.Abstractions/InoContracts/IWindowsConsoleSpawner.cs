namespace DigitalBrain.Abstractions.Ino;

public interface IWindowsConsoleSpawner
{
    bool SpawnConsole(string title, string command, string arguments);
}
