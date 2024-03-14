using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GCPlugin.Services
{
    public sealed class GCNotifications : IDisposable
    {
        private IPluginLog Logger { get; }
        public GCNotifications(IPluginLog logger)
        {
            this.Logger = logger;
            new Thread(this.GCNotificationsThread).Start();
        }

        public bool Approaching { get; private set; }
        public bool Disposed { get; private set; }

        private void GCNotificationsThread()
        {
            this.Logger.Verbose("GC Notifications Started");

            SpinWait spinWait = new();
            GCNotificationStatus status;
            while (!this.Disposed)
            {
                status = GC.WaitForFullGCApproach();
                if (status is GCNotificationStatus.Succeeded)
                {
                    this.Approaching = true;
                    this.Logger.Verbose("GC Approaching");
                    this.FullGCApproaching?.Invoke();

                    status = GC.WaitForFullGCComplete();
                    this.Approaching = false;
                    if (status is GCNotificationStatus.Succeeded)
                    {
                        this.Logger.Verbose("GC Complete");
                        this.FullGCComplete?.Invoke();
                    }
                    else if (status is GCNotificationStatus.Canceled)
                    {
                        this.Logger.Verbose("GC Cancelled");
                        this.FullGCCancelled?.Invoke();
                    }
                }
                else if (status is GCNotificationStatus.Canceled)
                {
                    this.Logger.Verbose("GC Cancelled");
                    this.FullGCCancelled?.Invoke();
                }
                spinWait.SpinOnce();
            }
            this.Logger.Verbose("GC Notifications Stopped");
        }

        public event Action? FullGCApproaching;
        public event Action? FullGCComplete;
        public event Action? FullGCCancelled;

        public void Dispose() => this.Disposed = true;
    }
}
