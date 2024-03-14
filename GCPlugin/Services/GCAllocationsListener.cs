using Dalamud.Plugin.Services;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace GCPlugin.Services
{
    public sealed class GCAllocationsListener
    {
        private EventPipeSession? _session;

        private IPluginLog Logger { get; }
        public GCAllocationsListener(AllocationListenerLevel level, IPluginLog logger)
        {
            this.Logger = logger;

            if (level == AllocationListenerLevel.None) return;
            var keywords = level switch
            {
                AllocationListenerLevel.Some => (long)ClrTraceEventParser.Keywords.GCSampledObjectAllocationLow,
                AllocationListenerLevel.Most => (long)ClrTraceEventParser.Keywords.GCSampledObjectAllocationHigh,
                AllocationListenerLevel.All => (long)ClrTraceEventParser.Keywords.GCAllObjectAllocation,
                _ => 0L
            };
            keywords |= (long)ClrTraceEventParser.Keywords.GCHeapAndTypeNames;

            // This one doesn't seem to work at this time, commented for now (TODO)
            //new Thread(() => this.ListenerThread(new List<EventPipeProvider>() { new("Microsoft-Windows-DotNETRuntime", System.Diagnostics.Tracing.EventLevel.Informational, keywords) })).Start();
        }

        public AllocationListenerLevel Level { get; }
        public bool Disposed { get; private set; }

        public void ListenerThread(IEnumerable<EventPipeProvider> providers)
        {
            this.Logger.Info("GC Allocations Listener Started");
            try
            {
                var client = new DiagnosticsClient(Environment.ProcessId);
                var session = client.StartEventPipeSession(providers, false, 16);
                this._session = session;
                var source = new EventPipeEventSource(session.EventStream);

                source.Clr.GCSampledObjectAllocation += this.Clr_GCSampledObjectAllocation;
                source.Clr.All += this.Clr_All;

                try
                {
                    source.Process();
                }
                finally
                {
                    source.Clr.GCSampledObjectAllocation -= this.Clr_GCSampledObjectAllocation;
                    source.Clr.All -= this.Clr_All;
                }
            }
            catch (NullReferenceException) when (this.Disposed) { /* Swallow: Happens on Dispose */ }
            catch (Exception ex)
            {
                this.Logger.Error(ex, "GC Events Listener Exception");
            }
            this.Logger.Info("GC Allocations Listener Stopped");
        }

        private void Clr_GCSampledObjectAllocation(GCSampledObjectAllocationTraceData obj)
        {
            this.Logger.Info(obj.ToString());
        }

        private void Clr_All(TraceEvent traceEvent)
        {
            if (traceEvent.Level == TraceEventLevel.Informational) this.Logger.Verbose(traceEvent.ToString());
            else if (traceEvent.Level == TraceEventLevel.Warning) this.Logger.Warning(traceEvent.ToString());
            else if (traceEvent.Level == TraceEventLevel.Error) this.Logger.Error(traceEvent.ToString());
            else if (traceEvent.Level == TraceEventLevel.Critical) this.Logger.Fatal(traceEvent.ToString());
        }

        public void Dispose()
        {
            this.Disposed = true;
            this._session?.Dispose();
        }

    }
}
