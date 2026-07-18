#:project ../../inolang/DigitalBrain.InoLang/DigitalBrain.InoLang.csproj

using DigitalBrain.InoLang;
using DigitalBrain.InoLang.Diagnostics;

// Throwaway validator: compiles each .ino path argument through the standalone
// InoLang compiler and prints diagnostics. Used to verify the widget-canvas
// demo neurons parse + link while the kernel/SDK build is broken.
var exit = 0;
foreach (var path in args)
{
    var source = File.ReadAllText(path);
    var result = InoCompiler.Compile(source);
    Console.WriteLine($"=== {Path.GetFileName(path)} ===");
    Console.WriteLine($"Success: {result.Success}");
    foreach (var d in result.Diagnostics)
        Console.WriteLine($"  [{d.Severity}] {d.Code}: {d.Message}");
    if (!result.Success) exit = 1;
}
return exit;
