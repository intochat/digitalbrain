#:package Aspire.Hosting.AppHost
#:package Aspire.Hosting.Redis
#:package CommunityToolkit.Aspire.Hosting.Ollama
#:package YamlDotNet
#:sdk Aspire.AppHost.Sdk@13.5.0-preview.1.26310.9
#:project src/DigitalBrain.Hosting/DigitalBrain.Hosting.csproj

using System.IO;
using System.Reflection;

string yamlBoot = "brain.yaml";
string altYamlBoot = "os-on-yaml/brain.yaml";
if (!File.Exists(yamlBoot) && !File.Exists(altYamlBoot) && File.Exists(Path.Combine("final", altYamlBoot)))
    Directory.SetCurrentDirectory("final");

var inoHostSdkPath = Path.Combine("src", "DigitalBrain.Hosting", "bin", "Debug", "net11.0", "DigitalBrain.Hosting.dll");
if (!File.Exists(inoHostSdkPath))
    inoHostSdkPath = Path.GetFullPath(inoHostSdkPath);

var sdk = Assembly.LoadFrom(inoHostSdkPath);
var hostType = sdk.GetType("DigitalBrain.Hosting.Microsoft.Aspire.DigitalBrainInoHost", throwOnError: true);
var run = hostType!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
run!.Invoke(null, new object?[] { args });