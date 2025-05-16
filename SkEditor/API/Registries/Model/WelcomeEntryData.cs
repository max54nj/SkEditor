﻿using CommunityToolkit.Mvvm.Input;

namespace SkEditor.API;

/// <summary>
///     Represent an entry in the welcome tab.
/// </summary>
/// <param name="name">The displayed name of the entry</param>
/// <param name="command">The command to be executed if the entry is clicked</param>
/// <param name="icon">The possibly-null icon of the entry</param>
public class WelcomeEntryData(string name, IRelayCommand command, object? icon = null)
{
    public string Name { get; } = name;
    public IRelayCommand Command { get; } = command;
    public object? Icon { get; } = icon;
}