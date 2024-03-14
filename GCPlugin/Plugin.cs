using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using GCPlugin.Services;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace GCPlugin
{

    public sealed class Plugin : IDalamudPlugin
    {
        private readonly WindowSystem _windowSystem = new();
        private readonly GCNotifications _notifications;
        private readonly GCCollectionsListener _collections;
        private readonly GCAllocationsListener _allocations;
        private readonly GCPluginWindow _mainWindow;

        private DalamudPluginInterface PluginInterface { get; }
        private ICommandManager Commands { get; }
        
        public Plugin(DalamudPluginInterface pluginInterface, IPluginLog logger, ICommandManager commands)
        {
            this.PluginInterface = pluginInterface;
            this.Commands = commands;

            this.PluginInterface.UiBuilder.Draw += this._windowSystem.Draw;

            this._notifications = new(logger);
            this._collections = new(logger);
            this._allocations = new(AllocationListenerLevel.Some, logger);

            this._mainWindow = new(logger, this._notifications, this._collections);
            this._mainWindow.IsOpen = pluginInterface.Reason == PluginLoadReason.Installer || true;
            this._windowSystem.AddWindow(this._mainWindow);

            this.Commands.AddHandler("/gcutils", new(this.GcUtilsCommand) { HelpMessage = "Open the GC Utilities Window\n/gcutils collect → Run GC.Collect()" });
        }

        public void GcUtilsCommand(string command, string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments)) this._mainWindow.IsOpen = !this._mainWindow.IsOpen;
            if (arguments.Trim().Equals("collect", StringComparison.InvariantCultureIgnoreCase)) this._mainWindow.RunBasicCollection();
        }

        public void Dispose()
        {
            this.Commands.RemoveHandler("/gcutils");
            this._mainWindow.Dispose();
            this._allocations.Dispose();
            this._collections.Dispose();
            this._notifications.Dispose();
            this.PluginInterface.UiBuilder.Draw -= this._windowSystem.Draw;
        }
    }
}
