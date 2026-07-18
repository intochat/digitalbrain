using System.Text;
using System.Security.Cryptography;
using System.Collections.Concurrent;
using DigitalBrain.InoLang;
using DigitalBrain.InoLang.Linking;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Kernel.Gateway;
using DigitalBrain.Runtime.Ui;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Kernel.Runtime.Watchers
{
    public sealed class InoFilesystemWatcher : IHostedService, IDisposable
    {
        private readonly IInterpretedNeuronRegistry _interpretedRegistry;
        private readonly IGrainFactory _grainFactory;
        private readonly IContractCatalog _catalog;
        private readonly HomeFeedBus _homeFeedBus;
        private readonly ILogger<InoFilesystemWatcher> _logger;
        private FileSystemWatcher? _watcher;
        private readonly ConcurrentDictionary<string, string> _lastProcessedHashes = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _pendingDebounces = new(StringComparer.OrdinalIgnoreCase);

        public InoFilesystemWatcher(
            IInterpretedNeuronRegistry interpretedRegistry,
            IGrainFactory grainFactory,
            IContractCatalog catalog,
            HomeFeedBus homeFeedBus,
            ILogger<InoFilesystemWatcher> logger)
        {
            _interpretedRegistry = interpretedRegistry;
            _grainFactory = grainFactory;
            _catalog = catalog;
            _homeFeedBus = homeFeedBus;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var watchedDir = GetWatchedDirectory();
            _logger.LogInformation("Starting InoFilesystemWatcher monitoring directory: {Directory}", watchedDir);

            _watcher = new FileSystemWatcher(watchedDir, "*.ino")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileChanged;
            _watcher.Changed += OnFileChanged;
            _watcher.Renamed += OnFileRenamed;

            // Proactively load existing files on startup
            _ = Task.Run(async () =>
            {
                try
                {
                    if (Directory.Exists(watchedDir))
                    {
                        var files = Directory.GetFiles(watchedDir, "*.ino");
                        foreach (var file in files)
                        {
                            await ProcessFileAsync(file);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing existing files on startup in {Directory}", watchedDir);
                }
            });

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping InoFilesystemWatcher");
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileChanged;
                _watcher.Changed -= OnFileChanged;
                _watcher.Renamed -= OnFileRenamed;
                _watcher.Dispose();
            }

            foreach (var cts in _pendingDebounces.Values)
            {
                try { cts.Cancel(); cts.Dispose(); } catch {}
            }
            _pendingDebounces.Clear();

            return Task.CompletedTask;
        }

        private string GetWatchedDirectory()
        {
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../inolang"));
            if (!Directory.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create directory {Path}, attempting fallback to workspace root/inolang", path);
                    path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "inolang"));
                    if (!Directory.Exists(path))
                    {
                        try { Directory.CreateDirectory(path); } catch {}
                    }
                }
            }
            return path;
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            DebounceFileChange(e.FullPath);
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            DebounceFileChange(e.FullPath);
        }

        private void DebounceFileChange(string filePath)
        {
            if (!filePath.EndsWith(".ino", StringComparison.OrdinalIgnoreCase))
                return;

            var newCts = new CancellationTokenSource();
            var oldCts = _pendingDebounces.AddOrUpdate(filePath, newCts, (key, old) =>
            {
                try { old.Cancel(); old.Dispose(); } catch {}
                return newCts;
            });

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(250, newCts.Token);
                    if (!newCts.IsCancellationRequested)
                    {
                        await ProcessFileAsync(filePath);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Debounced by a newer write
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during debounced processing of file {FilePath}", filePath);
                }
                finally
                {
                    newCts.Dispose();
                    _pendingDebounces.TryRemove(new KeyValuePair<string, CancellationTokenSource>(filePath, newCts));
                }
            });
        }

        private async Task ProcessFileAsync(string filePath)
        {
            // Simple retry to handle files momentarily locked by standard IDE save behaviors
            string? content = null;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (!File.Exists(filePath)) return;
                    content = await File.ReadAllTextAsync(filePath);
                    break;
                }
                catch (IOException)
                {
                    await Task.Delay(50);
                }
            }

            if (content == null)
            {
                _logger.LogWarning("InoFilesystemWatcher: Could not read file {FilePath} because it was locked or deleted", filePath);
                return;
            }

            string hashStr;
            using (var sha = SHA256.Create())
            {
                var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(content));
                hashStr = Convert.ToHexString(hashBytes);
            }

            if (_lastProcessedHashes.TryGetValue(filePath, out var lastHash) && string.Equals(lastHash, hashStr, StringComparison.Ordinal))
            {
                // Unchanged, skip processing
                return;
            }

            _logger.LogInformation("Processing changed Ino file {FilePath}", filePath);

            var compiled = InoCompiler.Compile(content, _catalog);
            if (!compiled.Success || compiled.Linked is null || compiled.Plan is null)
            {
                var errors = string.Join(" | ", compiled.Diagnostics.Select(d => d.Code + " " + d.Message));
                _logger.LogWarning("InoFilesystemWatcher: Compilation failed for {FilePath}: {Errors}", filePath, errors);
                return;
            }

            try
            {
                var registration = LinkedPortCatalogContributor.BuildRegistration(content, compiled.Linked);
                await _interpretedRegistry.RegisterDynamicAsync(registration);

                var scriptSource = InoToScriptTranspiler.Transpile(compiled.Plan);
                var fqn = compiled.Linked.Doc.Fqn;

                var newSpec = new DynamicNeuronSpec(
                    Id: new NeuronId(fqn),
                    FeatureText: "",
                    RoslynScript: scriptSource,
                    CreatedAt: DateTimeOffset.UtcNow,
                    Status: DynamicNeuronStatus.Promoted
                );

                var grain = _grainFactory.GetGrain<IDynamicNeuron>(fqn);
                await grain.LoadAsync(newSpec);

                _lastProcessedHashes[filePath] = hashStr;
                _logger.LogInformation("Successfully hot-swapped neuron {Fqn} from file {FilePath}", fqn, filePath);

                // Dynamically broadcast the newly compiled UI layout so the Flutter UI updates instantly!
                try
                {
                    var entry = LinkedPortCatalogContributor.BuildEntry(registration.Descriptor, compiled.Linked);
                    if (!string.IsNullOrEmpty(entry.UiLayoutJson))
                    {
                        var rfwCard = new RfwCard(
                            LibraryName: "uikit",
                            RootWidget: "UiKit",
                            DataJson: entry.UiLayoutJson
                        )
                        {
                            Headers = SynapseMetadata.Create(
                                synapseId: Guid.NewGuid(),
                                correlationId: Guid.NewGuid(),
                                causationId: Guid.Empty,
                                callerNeuronId: Guid.Empty,
                                callerNeuronType: fqn,
                                receiverNeuronId: Guid.Empty,
                                receiverNeuronType: "HomeFeed",
                                timestamp: DateTimeOffset.UtcNow
                            )
                        };
                        await _homeFeedBus.BroadcastAsync(rfwCard);
                        _logger.LogInformation("Successfully broadcast dynamic RfwCard for neuron {Fqn} to the HomeFeedBus", fqn);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to broadcast dynamically hot-swapped UI for neuron {Fqn}", fqn);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register or hot-swap compiled neuron from file {FilePath}", filePath);
            }
        }

        public void Dispose()
        {
            if (_watcher != null)
            {
                _watcher.Dispose();
            }
            foreach (var cts in _pendingDebounces.Values)
            {
                cts.Dispose();
            }
        }
    }
}
