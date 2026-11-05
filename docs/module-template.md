# Module Template

The `intochat-module` template scaffolds new DigitalBrain modules with standardized structure and testing targets.

## Usage

### In-Tree Scaffolding

When contributing directly to the OS codebase, scaffold an in-tree module:
```bash
dotnet new intochat-module -n <ModuleName> --ModuleId <vendor.name> --inTree true
```
This generates the module projects (`Contracts`, `src`, `tests`) without root build configuration files.

### Standalone Scaffolding

For standalone repositories or independent distribution:
```bash
dotnet new intochat-module -n <ModuleName> --ModuleId <vendor.name> --inTree false
```
This includes root `Directory.Build.props`, `Directory.Packages.props`, `global.json`, and `nuget.config`.
