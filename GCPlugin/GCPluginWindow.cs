using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using GCPlugin.Services;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GCPlugin
{
    public sealed class GCPluginWindow : Window, IDisposable
    {
        private static readonly GCLatencyMode[] s_latencyModes = new GCLatencyMode[]
        {
            GCLatencyMode.Batch, GCLatencyMode.Interactive, GCLatencyMode.LowLatency, GCLatencyMode.SustainedLowLatency
        };

        private static readonly string[] s_latencyModeStrings = new string[]
        {
            "Batch", "Interactive (Default)", "Low Latency", "Sustained Low Latency"
        };

        private readonly Timer _recentAllocatedTimer;

        private long _lastCollectedMemory;
        private bool _separateThread;
        private bool _compactHeap;
        private bool _waitFinalizers;
        private bool _postFinalizerCollect;

        private long _lastTotalAllocated;
        private long _recentAllocated;

        private int _nextPressure; // can't do long
        private long _totalPressure;

        private IPluginLog Logger { get; }
        private GCNotifications Notifications { get; }
        private GCCollectionsListener Events { get; }

        public GCPluginWindow(IPluginLog logger, GCNotifications notifications, GCCollectionsListener listener) : base("GC Utilities")
        {
            this.Logger = logger;
            this.Notifications = notifications;
            this.Events = listener;

            this._recentAllocatedTimer = new(this.RecentAllocatedTimer, null, 0, 1000);
        }

        private void RecentAllocatedTimer(object? _)
        {
            var totalAllocated = GC.GetTotalAllocatedBytes();
            this._recentAllocated = totalAllocated - this._lastTotalAllocated;
            this._lastTotalAllocated = totalAllocated;
        }
        
        public override void Draw()
        {
            if (ImGui.CollapsingHeader("Collection Controls", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();
                this.DrawCollectionControls();
                ImGui.Unindent();
            }

            if (ImGui.CollapsingHeader("Memory Information"))
            {
                ImGui.Indent();
                this.DrawMemoryInfo();
                ImGui.Unindent();
            }
            else if (ImGui.IsItemHovered()) ImGui.SetTooltip("WARNING: High Overhead");

            if (ImGui.CollapsingHeader("Recent Collection Events"))
            {
                ImGui.Indent();
                this.DrawCollectionEvents();
                ImGui.Unindent();
            }
            
            if (ImGui.CollapsingHeader("Configuration Variables"))
            {
                ImGui.Indent();
                this.DrawConfigurationVariables();
                ImGui.Unindent();
            }
        }

        public void RunBasicCollection()
        {
            var before = GC.GetTotalMemory(false);
            GC.Collect();
            var after = GC.GetTotalMemory(false);
            this._lastCollectedMemory = after - before;
        }

        private void RunCollection(Action action)
        {
            if (!this._separateThread) this.RunCollectionCore(action);
            else Task.Run(() => this.RunCollectionCore(action));
        }
        
        private void RunCollectionCore(Action action)
        {
            var before = GC.GetTotalMemory(false);

            this.RunCollectionCore2(action);

            if (this._waitFinalizers)
            {
                GC.WaitForPendingFinalizers();
                if (this._postFinalizerCollect)
                {
                    this.RunCollectionCore2(action);
                }
            }
            var after = GC.GetTotalMemory(false);
            this._lastCollectedMemory = after - before;
        }

        private void RunCollectionCore2(Action action)
        {
            if (this._compactHeap) GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            action();
        }

        private void DrawCollectionControls()
        {
            var latencyIndex = Array.IndexOf(s_latencyModes, GCSettings.LatencyMode);
            if (ImGui.Combo("Latency Mode", ref latencyIndex, s_latencyModeStrings, s_latencyModeStrings.Length))
            {
                try
                {
                    GCSettings.LatencyMode = s_latencyModes[latencyIndex];
                }
                catch (Exception ex)
                {
                    this.Logger.Error(ex, "Exception setting latency mode");
                }
            }

            ImGui.TextUnformatted($"Manual Memory Pressure: {ByteUtils.BytesToString(this._totalPressure)}");
            if (ImGui.InputInt("Bytes", ref this._nextPressure, 1024, 1048576))
            {
                this._nextPressure = Math.Max(0, this._nextPressure);
            }

            ImGui.SameLine();
            if (ImGui.Button("Add"))
            {
                GC.AddMemoryPressure(this._nextPressure);
                this._totalPressure += this._nextPressure;
            }

            ImGui.SameLine();
            if (ImGui.Button("Remove"))
            {
                GC.RemoveMemoryPressure(this._nextPressure);
                this._totalPressure -= this._nextPressure;
            }

            ImGui.Separator();

            if (ImGui.Button("Force Collection")) this.RunCollection(GC.Collect);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("GC.Collect()");

            ImGui.SameLine();
            if (ImGui.Button("Force Aggressive Collection")) this.RunCollection(() => GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true)\nAggressive collection mode");

            ImGui.Checkbox("Compact Large Heap", ref this._compactHeap);

            ImGui.Checkbox("Wait for finalizers", ref this._waitFinalizers);
            if (this._waitFinalizers)
            {
                ImGui.SameLine();
                ImGui.Checkbox("Run collection again after finalizers", ref this._postFinalizerCollect);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Catch eligible finalized objects");
            }

            ImGui.Checkbox("Run collection in a separate thread", ref this._separateThread);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("May not have any effect");

            ImGui.Separator();

            ImGui.BeginGroup();
            if (ImGui.BeginTable("gc_basic_info_1", 2))
            {
                ImGui.TableSetupColumn("Key", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Last Collected");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(ByteUtils.BytesToString(this._lastCollectedMemory));

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("GC Memory");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(ByteUtils.BytesToString(GC.GetTotalMemory(false)));

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("GC Approaching");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(this.Notifications.Approaching.ToString());

                ImGui.EndTable();
            }
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            if (ImGui.BeginTable("gc_basic_info_2", 2))
            {
                ImGui.TableSetupColumn("Key", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Total Allocated");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(ByteUtils.BytesToString(GC.GetTotalAllocatedBytes()));

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Last Second");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(ByteUtils.BytesToString(this._recentAllocated));

                ImGui.EndTable();
            }
            ImGui.EndGroup();
        }

        private void DrawMemoryInfo()
        {
            ImGui.BeginGroup();
            if (ImGui.BeginTable("gc_memory_1", 2))
            {
                ImGui.TableSetupColumn("Key", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Working Set");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(ByteUtils.BytesToString(Process.GetCurrentProcess().WorkingSet64));

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Private Memory");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(ByteUtils.BytesToString(Process.GetCurrentProcess().PrivateMemorySize64));

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Virtual Memory");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(ByteUtils.BytesToString(Process.GetCurrentProcess().VirtualMemorySize64));

                ImGui.EndTable();
            }
            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();
            if (ImGui.BeginTable("gc_memory_2", 2))
            {
                ImGui.TableSetupColumn("Key", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed);

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Paged Memory");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(ByteUtils.BytesToString(Process.GetCurrentProcess().PagedMemorySize64));

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Paged Sys Memory");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(ByteUtils.BytesToString(Process.GetCurrentProcess().PagedSystemMemorySize64));

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted("Non-paged Sys Memory");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(ByteUtils.BytesToString(Process.GetCurrentProcess().NonpagedSystemMemorySize64));

                ImGui.EndTable();
            }
            ImGui.EndGroup();
        }

        private void DrawCollectionEvents()
        {
            var maxCount = this.Events.MaxEventCount;
            if (ImGui.SliderInt("Event Count", ref maxCount, 1, 1000)) this.Events.MaxEventCount = maxCount;

            if (ImGui.BeginTable("gc_events", 6))
            {
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Timestamp", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Gen", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Duration", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableHeadersRow();
                foreach (var ev in this.Events.Reverse())
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(ev.Id);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted($"{ev.Timestamp:u}");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted($"{ev.Reason}");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted($"{ev.Generation}");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted($"{ev.Type}");
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted($"{ev.Duration:F3}ms");
                }
                ImGui.EndTable();
            }
        }

        private void DrawConfigurationVariables()
        {
            var configuration = GC.GetConfigurationVariables();
            if (ImGui.BeginTable("gc_config", 3))
            {
                ImGui.TableSetupColumn("Key", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed);
                //ImGui.TableHeadersRow();

                foreach (var (key, value) in configuration)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(key);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(value.GetType().Name);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(value.ToString());
                }
                ImGui.EndTable();
            }
        }

        public void Dispose()
        {
            this._recentAllocatedTimer.Dispose();
            if (this._totalPressure > 0)
            {
                GC.RemoveMemoryPressure(this._totalPressure);
            }
            else if (this._totalPressure < 0)
            {
                GC.AddMemoryPressure(-this._totalPressure);
            }
            this._totalPressure = 0;
        }
    }
}
