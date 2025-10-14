using System;
using System.Collections.Generic;
using NuGet.Versioning;
using SkEditor.API;

namespace SkEditor.Utilities.InternalAPI.Classes;

public static class LoadingErrors
{
    public static IAddonLoadingError OutdatedSkEditor(Version minimalVersion)
    {
        return new OutdatedSkEditorError(minimalVersion);
    }

    public static IAddonLoadingError OutdatedAddon(Version maximalVersion)
    {
        return new OutdatedAddonError(maximalVersion);
    }

    public static IAddonLoadingError LoadingException(Exception exception)
    {
        return new LoadingExceptionError(exception);
    }

    public static IAddonLoadingError OutdatedDependency(string dependencyName, NuGetVersion target, NuGetVersion found)
    {
        return new OutdatedDependencyError(dependencyName, target, found);
    }

    public static IAddonLoadingError MissingAddonDependency(string addonIdentifier)
    {
        return new MissingAddonDependencyError(addonIdentifier);
    }

    public static IAddonLoadingError DependencyNotEnabled(string addonIdentifier)
    {
        return new DependencyNotEnabledError(addonIdentifier);
    }

    public static IAddonLoadingError FailedToLoadDependency(string dependencyName, string message)
    {
        return new FailedToLoadDependencyError(dependencyName, message);
    }

    public static IAddonLoadingError IncompatibleAddonVersion(string addonIdentifier, string requiredVersion, string foundVersion)
    {
        return new IncompatibleAddonVersionError(addonIdentifier, requiredVersion, foundVersion);
    }

    public static IAddonLoadingError CircularDependency(List<string> dependencyChain)
    {
        return new CircularDependencyError(dependencyChain);
    }


    private class OutdatedSkEditorError(Version minimalVersion) : IAddonLoadingError
    {
        public bool IsCritical => true;

        public string Message => "The addon requires a newer version of SkEditor (" + minimalVersion +
                                 " and you have " + SkEditorAPI.Core.GetAppVersion() + ").";
    }

    private class OutdatedAddonError(Version maximalVersion) : IAddonLoadingError
    {
        public bool IsCritical => true;

        public string Message => "The addon requires a newer version of itself (" + maximalVersion + " and you have " +
                                 SkEditorAPI.Core.GetAppVersion() + ").";
    }

    private class LoadingExceptionError(Exception exception) : IAddonLoadingError
    {
        public bool IsCritical => true;
        public string Message => "An exception occurred while loading the addon: " + exception.Message;
    }

    private class MissingAddonDependencyError(string addonIdentifier) : IAddonLoadingError
    {
        public bool IsCritical => true;
        public string Message => "The addon requires the addon '" + addonIdentifier + "' to be loaded.";
    }

    private class DependencyNotEnabledError(string addonIdentifier) : IAddonLoadingError
    {
        public bool IsCritical => true;
        public string Message => "The addon requires '" + addonIdentifier + "' to be enabled, but it is currently disabled.";
    }

    private class OutdatedDependencyError(string dependencyName, NuGetVersion target, NuGetVersion found)
        : IAddonLoadingError
    {
        public bool IsCritical => true;

        public string Message => "The addon requires the dependency '" + dependencyName + "' to be at least version " +
                                 target + ", but you have version " + found + ".";
    }

    private class FailedToLoadDependencyError(string dependencyName, string message) : IAddonLoadingError
    {
        public bool IsCritical => true;
        public string Message => "Failed to load the dependency '" + dependencyName + "': " + message;
    }

    private class IncompatibleAddonVersionError(string addonIdentifier, string requiredVersion, string foundVersion) : IAddonLoadingError
    {
        public bool IsCritical => true;
        public string Message => $"The addon requires '{addonIdentifier}' version {requiredVersion}, but version {foundVersion} is installed.";
    }

    private class CircularDependencyError(List<string> dependencyChain) : IAddonLoadingError
    {
        public bool IsCritical => true;
        public string Message => "Circular dependency detected: " + string.Join(" -> ", dependencyChain) + " -> " + dependencyChain[0];
    }
}