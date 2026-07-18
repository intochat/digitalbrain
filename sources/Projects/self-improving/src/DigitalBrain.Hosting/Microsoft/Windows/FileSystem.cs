// Moved to src/DigitalBrain.Sdk/Experiences/FileSystemConnectorGrain.cs per Task 2 (polyrepo-decomp plan).
// FileSystem connector extracted from Sdk (vision §4: extract Google/fs/http from experiences; GrainType now "filesystem").
// Old marker file only (src/DigitalBrain.Hosting/Microsoft/Windows left for history; type decl removed to prevent duplicate).
// pa-files base + all impl now in Connectors; Sdk no longer owns the FS grain. Callers use INeuron + bundle strings.
namespace DigitalBrain.Hosting.Microsoft.Windows;

// Marker only after T2 connectors extraction (FileSystem impl now FileSystemConnectorGrain in DigitalBrain.Sdk.Experiences).
// All prior members removed to avoid CS duplicate type with new location.

