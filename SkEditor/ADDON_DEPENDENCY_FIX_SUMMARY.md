# Addon Dependency Resolution - Implementation Summary

## Problem
The SkEditor addon loader could not resolve dependencies between addons, causing the error:
```
Could not load file or assembly 'de.max54nj.bookbinder, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null'. 
The system cannot find the file specified.
```

This occurred even when:
- The dependency was correctly declared using `GetDependencies()` with `AddonDependency`
- The dependent addon (Bookbinder) was already loaded and enabled
- The dependency validation and resolution logic was in place

## Root Cause
The `AddonLoadContext` class existed but only resolved assemblies from its own addon directory. When addon A depended on addon B, the load context for addon A couldn't find addon B's assembly because it had no knowledge of other loaded addons.

## Solution Implemented

### 1. Enhanced AddonLoadContext.cs
Added inter-addon assembly resolution capabilities:

**Key Features:**
- **Static Registry System**: Maintains a global registry of all loaded addon contexts and their assemblies
- **Cross-Addon Resolution**: When an assembly can't be found in the addon's own directory, it searches through all other registered addon contexts
- **Thread-Safe Operations**: All registry operations are protected by locks
- **Proper Cleanup**: `Unregister()` method removes the context and its assemblies when an addon is unloaded

**How It Works:**
1. When an addon is loaded, its `AddonLoadContext` is registered in the static registry
2. When resolving an assembly:
   - First checks if already loaded (cached)
   - Then tries to resolve from its own directory
   - If not found, searches through all other registered addon directories
   - Finally falls back to default .NET resolution
3. When an addon is unloaded, its context is unregistered and all its assemblies are removed from the cache

### 2. Updated AddonLoader.cs
Modified to register/unregister load contexts at appropriate times:

**Changes Made:**
- Added `loadContext.Register()` call after successfully loading an addon in `LoadAddonFromFileWithoutEnabling()`
- Added `loadContext.Register()` call after successfully loading an addon in `LoadAddonFromFile()`
- Added `loadContext.Unregister()` call before unloading in `DeleteAddon()`

### 3. AddonMeta.cs
Already had the `LoadContext` property defined, so no changes were needed.

### 4. AddonDependencyResolver.cs
Already existed with full implementation for:
- `ValidateDependencies()` - Validates all declared dependencies exist and have compatible versions
- `ResolveDependencies()` - Sorts addons in dependency order, detects circular dependencies
- `GetDependents()` - Gets all addons that depend on a specific addon

## How Addons Now Load

1. **Discovery Phase**: All addon DLLs are found and loaded into `AddonLoadContext` instances
2. **Registration Phase**: Each load context is registered in the global registry
3. **Validation Phase**: Dependencies are validated using `AddonDependencyResolver.ValidateDependencies()`
4. **Resolution Phase**: Addons are sorted in dependency order using `AddonDependencyResolver.ResolveDependencies()`
5. **Enabling Phase**: Addons are enabled in dependency order, ensuring dependencies are loaded before dependents
6. **Runtime Resolution**: When an addon needs a type from another addon, the `AddonLoadContext` automatically finds and loads the required assembly

## Example Usage

### Addon A declares dependency on Addon B:
```csharp
public class AddonA : IAddon
{
    public string Identifier => "com.example.addonA";
    
    public List<IDependency> GetDependencies()
    {
        return new List<IDependency>
        {
            new AddonDependency("com.example.addonB", "1.0.0", isRequired: true)
        };
    }
    
    public void OnEnable()
    {
        // Can now use types from AddonB without assembly load errors
        var serviceFromB = new AddonB.SomeService();
    }
}
```

### What Happens:
1. Both addons are loaded and their contexts registered
2. Dependency validation confirms AddonB exists and version is compatible
3. Dependency resolution ensures AddonB is enabled before AddonA
4. When AddonA tries to use types from AddonB, the load context finds and loads the AddonB assembly automatically

## Benefits

✅ **Inter-Addon Dependencies Work**: Addons can reference and use types from other addons
✅ **Proper Load Order**: Dependencies are always loaded before dependents
✅ **Version Checking**: Compatible version ranges are validated
✅ **Circular Dependency Detection**: Prevents infinite loops
✅ **Clean Unloading**: Assemblies are properly tracked and cleaned up
✅ **Thread-Safe**: All operations on the registry are synchronized

## Testing Recommendations

1. Create two test addons where one depends on the other
2. Verify the dependent addon can instantiate types from the dependency
3. Test that disabling a dependency warns about dependents
4. Test circular dependency detection with 3+ addons
5. Test version compatibility checking
6. Test addon unloading and reloading

## Migration Notes for Addon Developers

No code changes required! The fix is transparent to addon developers. Simply continue declaring dependencies using:

```csharp
public List<IDependency> GetDependencies()
{
    return new List<IDependency>
    {
        new AddonDependency("other.addon.identifier", "^1.0.0", isRequired: true)
    };
}
```

The assembly resolution now works automatically.

