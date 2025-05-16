using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Windowing;
using Serilog;
using SkEditor.API;
using SkEditor.Controls;
using SkEditor.Utilities;
using SkEditor.Views.Marketplace;
using SkEditor.Views.Marketplace.Types;

namespace SkEditor.Views;

public partial class MarketplaceWindow : AppWindow
{
    public const string MarketplaceUrl = "https://marketplace-skeditor.vercel.app/";

    public MarketplaceWindow()
    {
        InitializeComponent();
        Focusable = true;

        Instance = this;

        ItemListBox.SelectionChanged += OnSelectedItemChanged;
        Loaded += (_, _) => Task.Run(LoadItems);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    public static MarketplaceWindow Instance { get; private set; } = null!;

    private async Task LoadItems()
    {
        try
        {
            await foreach (MarketplaceItem item in MarketplaceLoader.GetItems())
            {
                Dispatcher.UIThread.Post(() =>
                {
                    MarketplaceListItem listItem = new()
                    {
                        ItemName = item.ItemName,
                        ItemShortDescription = item.ItemShortDescription,
                        ItemImageUrl = item.ItemImageUrl,
                        Tag = item
                    };
                    ItemListBox.Items.Add(listItem);
                });
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to load Marketplace items");
            await SkEditorAPI.Windows.ShowError(Translation.Get("MarketplaceLoadFailed"));
        }
    }

    private void OnSelectedItemChanged(object? sender, SelectionChangedEventArgs e)
    {
        ItemView.IsVisible = false;
        MarketplaceListItem? listItem = (MarketplaceListItem?)ItemListBox.SelectedItem;

        MarketplaceItem? item = (MarketplaceItem?)listItem?.Tag;
        if (item == null) return;

        item.Marketplace = this;


        HideAllButtons();

        bool shouldShowInstallButton = false;
        bool shouldShowUninstallButton = false;

        /*
         * if (item is AddonItem addonItem)
        {
            IAddon addon = SkEditorAPI.Addons.GetAddon(addonItem.ItemFileUrl);
            string name = Path.GetFileNameWithoutExtension(addonItem.ItemFileUrl);

            if (addon != null || SkEditorAPI.Core.GetAppConfig().AddonsToDisable.Contains(name))
            {
                shouldShowUninstallButton = true;
                if (addon != null) ItemView.UpdateButton.IsVisible = !addon.Version.Equals(item.ItemVersion);
                ItemView.UninstallButton.IsEnabled = !SkEditorAPI.Core.GetAppConfig().AddonsToDelete.Contains(name);
                ItemView.DisableButton.IsVisible = !SkEditorAPI.Core.GetAppConfig().AddonsToDisable.Contains(name);
                ItemView.EnableButton.IsVisible = !ItemView.DisableButton.IsVisible;
            }
            else
            {
                shouldShowInstallButton = true;
            }
        }
        else
         */

        if (item is SyntaxItem or ThemeItem or ThemeWithSyntaxItem or AddonItem)
        {
            bool installed = item.IsInstalled();
            shouldShowUninstallButton = installed;
            shouldShowInstallButton = !installed;
        }

        ItemView.InstallButton.IsVisible = shouldShowInstallButton;
        ItemView.UninstallButton.IsVisible = shouldShowUninstallButton;

        ItemView.IsVisible = true;

        ItemView.ItemName = item.ItemName;
        ItemView.ItemVersion = item.ItemVersion;
        ItemView.ItemAuthor = item.ItemAuthor;
        ItemView.ItemShortDescription = item.ItemShortDescription;
        ItemView.ItemLongDescription = item.ItemLongDescription;
        ItemView.ItemImageUrl = item.ItemImageUrl;

        ItemView.InstallButton.CommandParameter = ItemView.DisableButton.CommandParameter =
            ItemView.UninstallButton.CommandParameter
                = ItemView.EnableButton.CommandParameter = ItemView.UpdateButton.CommandParameter = item;

        ItemView.ManageButton.IsVisible = false;
        ItemView.InstallButton.Command = new AsyncRelayCommand(item.Install);
        ItemView.UninstallButton.Command = new AsyncRelayCommand(item.Uninstall);
        switch (item)
        {
            case ZipAddonItem zipAddonItem:
                ItemView.DisableButton.Command = new RelayCommand(zipAddonItem.Disable);
                ItemView.EnableButton.Command = new RelayCommand(zipAddonItem.Enable);
                ItemView.UpdateButton.Command = new AsyncRelayCommand(zipAddonItem.Update);
                break;
            case AddonItem addonItem2:
                ItemView.DisableButton.IsVisible = false;
                ItemView.EnableButton.IsVisible = false;
                ItemView.UpdateButton.IsVisible = false;
                ItemView.UninstallButton.IsVisible = false;

                ItemView.ManageButton.IsVisible = addonItem2.IsInstalled();
                ItemView.ManageButton.Command = new RelayCommand(() =>
                {
                    Close();
                    AddonItem.Manage();
                });
                break;
        }
    }

    public void RefreshCurrentSelection()
    {
        OnSelectedItemChanged(null, null!);
    }

    public void HideAllButtons()
    {
        Button[] buttons =
        [
            ItemView.InstallButton, ItemView.UninstallButton, ItemView.DisableButton, ItemView.EnableButton,
            ItemView.UpdateButton
        ];

        foreach (Button button in buttons)
        {
            button.IsVisible = false;
            button.IsEnabled = true;
        }
    }
}