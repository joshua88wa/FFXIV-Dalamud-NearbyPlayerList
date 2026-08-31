using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using NearbyPlayerList.Windows;

namespace NearbyPlayerList;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/npl";

    private readonly WindowSystem windowSystem = new("NearbyPlayerList");
    private readonly Configuration config;
    private readonly ListWindow listWindow;
    private readonly ConfigWindow configWindow;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Service>();

        this.config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.config.Initialize(pluginInterface);

        var scanner = new PlayerScanner(this.config);
        this.listWindow = new ListWindow(this.config, scanner) { IsOpen = this.config.ShowWindow };
        this.configWindow = new ConfigWindow(this.config, this.listWindow);

        this.windowSystem.AddWindow(this.listWindow);
        this.windowSystem.AddWindow(this.configWindow);

        Service.Commands.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Toggle the nearby player list. /npl config opens settings, /npl center brings the list back on screen.",
        });

        pluginInterface.UiBuilder.Draw += this.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += this.OpenConfig;
        pluginInterface.UiBuilder.OpenMainUi += this.ToggleList;
    }

    public void Dispose()
    {
        Service.Interface.UiBuilder.Draw -= this.Draw;
        Service.Interface.UiBuilder.OpenConfigUi -= this.OpenConfig;
        Service.Interface.UiBuilder.OpenMainUi -= this.ToggleList;

        Service.Commands.RemoveHandler(CommandName);

        this.windowSystem.RemoveAllWindows();
    }

    private void Draw()
    {
        this.listWindow.IsOpen = this.config.ShowWindow;
        this.windowSystem.Draw();
    }

    private void OpenConfig() => this.configWindow.IsOpen = true;

    private void ToggleList()
    {
        this.config.ShowWindow = !this.config.ShowWindow;
        this.config.Save();
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "config":
            case "settings":
                this.OpenConfig();
                break;
            case "center":
                this.listWindow.RequestCenter();
                break;
            default:
                this.ToggleList();
                break;
        }
    }
}
