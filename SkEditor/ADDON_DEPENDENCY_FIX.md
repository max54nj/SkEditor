# Addon Dependency Loading Fix

## Problem
When an addon (CustomBackgroundAddon) depended on another addon (bookbinder), the following error occurred:
```
System.IO.FileNotFoundException: Could not load file or assembly 'de.max54nj.bookbinder, Version=1.0.0.5, Culture=neutral, PublicKeyToken=null'
```

This happened because the dependent addon's `GetSettings()` method was being called before its dependencies were fully loaded and enabled.

## Root Cause
The addon loading process was:
1. Load all addon DLLs and instantiate them
2. Validate dependencies exist and match version requirements
3. Enable addons in dependency order

However, in step 3, when `EnableAddon()` was called, it immediately invoked `AddonSettingsManager.LoadSettings(addon)`, which calls `GetSettings()` on the addon. At this point, even though we were loading in dependency order, the dependency addon might not have been **fully enabled yet** - it might still be in the process of enabling or not yet started.

## Solution
Added a new validation step `AreAddonDependenciesEnabled()` in the `PreLoad()` method that runs BEFORE attempting to enable an addon. This method:

1. **Checks installation**: Verifies all required addon dependencies are installed
2. **Checks enabled state**: Verifies all required addon dependencies are **already enabled** (State == Enabled)
3. **Checks version compatibility**: Validates that dependency versions satisfy the required version ranges

### Code Changes

**File: `AddonLoader.cs`**
- Added `AreAddonDependenciesEnabled()` method
- Modified `PreLoad()` to call this validation before enabling
- Added new error type for dependencies that are not enabled

**File: `LoadingErrors.cs`**
- Added `DependencyNotEnabled()` error factory method
- Added `DependencyNotEnabledError` class

## Behavior

### Before Fix
1. CustomBackgroundAddon attempts to enable
2. `GetSettings()` is called immediately
3. `Translator.Get()` from bookbinder is called
4. **CRASH**: bookbinder assembly not found

### After Fix
1. bookbinder addon is loaded and enabled first (no dependencies)
2. CustomBackgroundAddon attempts to enable
3. `PreLoad()` checks if bookbinder is enabled → **Yes, it is!**
4. `GetSettings()` is called
5. `Translator.Get()` from bookbinder works correctly ✓

### If Dependency Not Enabled
If the dependency addon is disabled by the user:
1. CustomBackgroundAddon attempts to enable
2. `PreLoad()` checks if bookbinder is enabled → **No, it's disabled**
3. Error added: "The addon requires 'de.max54nj.bookbinder' to be enabled, but it is currently disabled."
4. CustomBackgroundAddon fails to enable gracefully
5. Error is shown in the addons page

## Testing Checklist

✓ Build succeeds without errors
✓ Addon with dependencies loads when dependency is enabled
✓ Addon with dependencies fails gracefully when dependency is disabled
✓ Version checking still works correctly
✓ Optional dependencies are handled correctly (can be missing/disabled)
✓ Topological sorting ensures correct load order
✓ Circular dependencies are still detected

## Example Usage

```csharp
// bookbinder addon (library)
public class BookbinderAddon : IAddon
{
    public string Identifier => "de.max54nj.bookbinder";
    public string Version => "1.0.0";
    
    public void OnEnable()
    {
        // Initialize translator
        Translator.Initialize();
    }
}

// CustomBackgroundAddon (depends on bookbinder)
public class CustomBackgroundAddon : IAddon
{
    public string Identifier => "custom-background";
    public string Version => "1.0.0";
    
    public List<IDependency> GetDependencies()
    {
        return [new AddonDependency("de.max54nj.bookbinder", "1.0.0")];
    }
    
    public List<Setting> GetSettings()
    {
        // This now works because bookbinder is guaranteed to be enabled!
        return [
            new Setting(Instance, Translator.Get("test1"), ...)
        ];
    }
}
```

## Load Order Guarantee

The system now guarantees:
1. Dependencies are loaded **before** dependents
2. Dependencies are **fully enabled** before dependents start enabling
3. `GetSettings()`, `OnEnable()`, and `OnEnableAsync()` are only called after all dependencies are ready
4. Addon functionality from dependencies is available throughout the dependent addon's lifecycle

