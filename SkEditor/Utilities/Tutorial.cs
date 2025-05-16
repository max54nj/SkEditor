﻿using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using FluentAvalonia.UI.Controls;
using SkEditor.API;

namespace SkEditor.Utilities;

public static class Tutorial
{
    public static async Task ShowTutorial()
    {
        if (!SkEditorAPI.Core.GetAppConfig().FirstTime)
        {
            return;
        }

        SkEditorAPI.Core.GetAppConfig().FirstTime = false;
        SkEditorAPI.Core.GetAppConfig().Save();

        object? icon = null;
        Application.Current?.TryGetResource("SkEditorIcon", ThemeVariant.Default, out icon);
        PathIconSource? iconSource = icon as PathIconSource;

        ContentDialogResult result = await SkEditorAPI.Windows.ShowDialog("Welcome to the SkEditor!",
            "Do you want to read a quick intro?\nIf you're new to the app, it's a good idea ;)",
            iconSource, primaryButtonText: "Yes", cancelButtonText: "No");

        if (result == ContentDialogResult.Primary)
        {
            await ShowTutorialMessage("Settings",
                "On the top menu, you'll find a cog icon. Click on it to open the settings, where you can customize the app to your liking!",
                iconSource);
            await ShowTutorialMessage("Bugs and suggestions",
                "If you encounter a bug or have ideas to share, you can reach out to us on GitHub or Discord. You'll find the links in the 'About' section within the settings!",
                iconSource);
            await ShowTutorialMessage("Marketplace",
                "Looking to expand the app's capabilities? Explore the Marketplace! From there, you can install addons, syntax highlightings and themes.\nAccess it by going to the top menu, selecting 'Other', and then clicking on 'Marketplace'.",
                iconSource);
            await ShowTutorialMessage("Themes",
                "Not a fan of the app's appearance? You can customize almost every color using themes! Go to Settings -> Personalization -> Theme to find out more.\nYou can also download themes from the Marketplace.",
                iconSource);
            await ShowTutorialMessage("Enjoy!", "We hope you will enjoy the program! Happy using!", iconSource);
        }
    }

    private static async Task ShowTutorialMessage(string title, string message, PathIconSource? iconSource)
    {
        await SkEditorAPI.Windows.ShowDialog(title, message, iconSource, translate: false);
    }
}