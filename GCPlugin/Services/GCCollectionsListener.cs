using Dalamud.Plugin.Services;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.EventPipe;
using Microsoft.Diagnostics.Tracing.Parsers;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;
using System.Collections.Concurrent;
using System.Collections;

namespace GCPlugin.Services
{
    // Based on: https://learn.microsoft.com/en-us/dotnet/core/diagnostics/diagnostics-client-library

    public sealed class GCCollectionsListener : IDisposable, IEnumerable<GCCollectionData>
    {
        private static readonly IEnumerable<EventPipeProvider> s_providers = new EventPipeProvider[]
        {
            new("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, (long)ClrTraceEventParser.Keywords.GC)
        };

        private readonly ConcurrentDictionary<string, GCCollectionData> _events = new();
        private readonly ConcurrentQueue<string> _idQueue = new();
        private int _idQueueCount;

        private EventPipeSession? _session;

        private IPluginLog Logger { get; }

        public GCCollectionsListener(IPluginLog logger)
        {
            Logger = logger;

            new Thread(this.ListenerThread).Start();
        }

        public int MaxEventCount { get; set; } = 10;
        public bool Disposed { get; private set; }

        public void ListenerThread()
        {
            this.Logger.Info("GC Collections Listener Started");
            try
            {
                var client = new DiagnosticsClient(Environment.ProcessId);
                var session = client.StartEventPipeSession(s_providers, false, 16);
                this._session = session;
                var source = new EventPipeEventSource(session.EventStream);

                source.Clr.GCStart += this.Clr_GCStart;
                source.Clr.GCStop += this.Clr_GCStop;

                try
                {
                    source.Process();
                }
                finally
                {
                    source.Clr.GCStart -= this.Clr_GCStart;
                    source.Clr.GCStop -= this.Clr_GCStop;
                }
            }
            catch (NullReferenceException) when (this.Disposed) { /* Swallow: Happens on Dispose */ }
            catch (Exception ex)
            {
                this.Logger.Error(ex, "GC Collections Listener Exception");
            }
            this.Logger.Info("GC Collections Listener Stopped");
        }

        private void Clr_GCStart(GCStartTraceData obj)
        {
            var id = $"{obj.ClrInstanceID}-{obj.Count}";
            var ev = new GCCollectionData()
            {
                Id = id,
                Timestamp = obj.TimeStamp,
                Reason = obj.Reason,
                Generation = obj.Depth,
                Type = obj.Type,
                StartTime = obj.TimeStampRelativeMSec
            };
            this._events[id] = ev;
            this._idQueue.Enqueue(id);
            this._idQueueCount++;

            if (this.MaxEventCount == 0) return;
            while (this._idQueueCount > Math.Max(0, this.MaxEventCount))
            {
                if (this._idQueue.TryDequeue(out id)) this._events.TryRemove(id, out ev);
                this._idQueueCount--;
            }
        }

        private void Clr_GCStop(GCEndTraceData obj)
        {
            var id = $"{obj.ClrInstanceID}-{obj.Count}";
            if (!this._events.TryGetValue(id, out var ev)) return;
            ev.EndTime = obj.TimeStampRelativeMSec;
        }

        private void Clr_All(TraceEvent traceEvent)
        {
            if (traceEvent.Level == TraceEventLevel.Informational) this.Logger.Verbose(traceEvent.ToString());
            else if (traceEvent.Level == TraceEventLevel.Warning) this.Logger.Warning(traceEvent.ToString());
            else if (traceEvent.Level == TraceEventLevel.Error) this.Logger.Error(traceEvent.ToString());
            else if (traceEvent.Level == TraceEventLevel.Critical) this.Logger.Fatal(traceEvent.ToString());
        }

        public IEnumerator<GCCollectionData> GetEnumerator()
        {
            foreach (var id in this._idQueue)
            {
                if (!this._events.TryGetValue(id, out var ev)) continue;
                yield return ev;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

        public void Dispose()
        {
            this.Disposed = true;
            this._session?.Dispose();
        }
    }
}