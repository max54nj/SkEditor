# Addon Dependency System - Implementation Summary

## Overview
The addon loading system has been completely reworked to support proper dependency management between addons, including version constraints, circular dependency detection, and proper load ordering.

## New Features

### 1. **Version-Dependent Addon Dependencies**
- Full npm-style version range support:
  - Exact versions: `"1.2.3"`
  - Caret ranges: `"^1.2.3"` (compatible with left-most non-zero digit)
  - Tilde ranges: `"~1.2.3"` (patch-level changes only)
  - Comparison operators: `">=1.0.0"`, `">1.0.0"`, `"<=2.0.0"`, `"<2.0.0"`
  - X-ranges: `"1.2.x"`, `"1.x"`, `"*"`
  - Compound ranges: `">=1.0.0 <2.0.0"`

### 2. **Circular Dependency Detection**
- Automatically detects circular dependencies during addon loading
- Prevents loading addons with circular dependencies
- Shows detailed error messages indicating the circular dependency chain

### 3. **Optional Dependencies**
- Addons can specify optional dependencies that don't prevent loading if missing
- New API methods to check if optional dependencies are available at runtime:
  - `SkEditorAPI.Addons.IsAddonAvailable(string identifier)`
  - `SkEditorAPI.Addons.IsAddonAvailable(string identifier, string versionRange)`

### 4. **Dependency-Ordered Loading**
- Uses topological sorting to ensure addons load in the correct order
- All dependencies (and their dependencies) are loaded before dependents
- Guarantees proper initialization order

### 5. **Enhanced Error Reporting**
- New error types:
  - `IncompatibleAddonVersion`: When a dependency version doesn't match requirements
  - `CircularDependency`: When circular dependencies are detected
  - `MissingAddonDependency`: When required dependencies are missing
- Errors are displayed in the existing addon error UI
- Placeholder for marketplace navigation button (ready for implementation)

### 6. **Cascade Disabling**
- When disabling an addon that other addons depend on:
  - Shows a warning dialog listing all dependent addons
  - Asks for confirmation before proceeding
  - Automatically disables all dependent addons if confirmed
  - Can be canceled to keep the addon enabled

## New Files Created

1. **`VersionParser.cs`**: Handles npm-style version range parsing and comparison
2. **`AddonDependencyResolver.cs`**: Resolves dependencies, detects circular dependencies, and determines load order

## Modified Files

1. **`AddonDependency.cs`**: Added `VersionRange` property for version constraints
2. **`LoadingErrors.cs`**: Added new error types for dependency issues
3. **`IAddons.cs`**: Added `IsAddonAvailable` methods
4. **`Addons.cs`**: Implemented `IsAddonAvailable` methods
5. **`AddonLoader.cs`**: 
   - Refactored `LoadAddonsFromFiles()` to use dependency resolution
   - Added `LoadAddonFromFileWithoutEnabling()` for two-phase loading
   - Updated `DisableAddon()` to check for dependents and show warning dialog

## Usage Examples

### For Addon Developers

#### Example 1: Library Addon with Optional Dependencies
```csharp
public class MyLibraryAddon : IAddon
{
    public string Name => "My Library";
    public string Identifier => "my-library";
    public string Version => "1.0.0";
    
    public List<IDependency> GetDependencies()
    {
        return
        [
            // Optional dependency on another library
            new AddonDependency("another-lib", "^2.0.0", isRequired: false)
        ];
    }
    
    public void OnEnable()
    {
        // Check if optional dependency is available
        if (SkEditorAPI.Addons.IsAddonAvailable("another-lib", "^2.0.0"))
        {
            // Use the optional dependency
        }
        else
        {
            // Provide fallback functionality
        }
    }
}
```

#### Example 2: Addon Depending on a Library
```csharp
public class MyFeatureAddon : IAddon
{
    public string Name => "My Feature";
    public string Identifier => "my-feature";
    public string Version => "1.0.0";
    
    public List<IDependency> GetDependencies()
    {
        return
        [
            // Required dependency on library addon
            new AddonDependency("my-library", "^1.0.0", isRequired: true)
        ];
    }
    
    public void OnEnable()
    {
        // "my-library" is guaranteed to be loaded and enabled before this
        // You can safely use its functionality
    }
}
```

#### Example 3: Complex Version Requirements
```csharp
public List<IDependency> GetDependencies()
{
    return
    [
        // Exact version
        new AddonDependency("exact-lib", "2.1.0"),
        
        // Compatible with major version 3
        new AddonDependency("compat-lib", "^3.0.0"),
        
        // Patch updates only
        new AddonDependency("stable-lib", "~1.5.2"),
        
        // Range with operators
        new AddonDependency("range-lib", ">=2.0.0 <3.0.0"),
        
        // Any version
        new AddonDependency("any-lib", "*"),
        
        // Optional dependency
        new AddonDependency("optional-lib", "^1.0.0", isRequired: false)
    ];
}
```

## Technical Details

### Load Process Flow
1. **Discovery**: All addon DLLs are found and instantiated
2. **Validation**: Dependencies are validated for presence and version compatibility
3. **Resolution**: Topological sort determines correct load order
4. **Circular Check**: Any circular dependencies cause loading to fail for affected addons
5. **Enabling**: Addons are enabled in dependency order

### Version Comparison Logic
- Follows semantic versioning principles
- `^1.2.3` allows: 1.2.3, 1.2.4, 1.3.0, 1.9.9 (but not 2.0.0)
- `~1.2.3` allows: 1.2.3, 1.2.4, 1.2.99 (but not 1.3.0)
- `^0.2.3` allows: 0.2.3, 0.2.4 (but not 0.3.0) - special handling for 0.x versions

## NuGet Dependencies
The existing NuGet dependency system has been kept as-is for backward compatibility. It continues to work unchanged.

## Future Enhancements (Placeholders Ready)
- Marketplace navigation button in error UI for missing dependencies
- Automatic dependency download from marketplace
- Dependency update suggestions

## Migration Guide for Existing Addons
Existing addons will continue to work without changes. To add dependency support:

1. Update your `GetDependencies()` method to return addon dependencies
2. Specify version ranges as needed
3. Use `IsAddonAvailable()` for optional dependencies

No breaking changes were introduced!

