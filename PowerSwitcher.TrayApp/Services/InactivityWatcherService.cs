using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows.Forms;
using Application = System.Windows.Application;
using Timer = System.Timers.Timer;

namespace PowerSwitcher.TrayApp.Services;

public class InactivityWatcherService(IPowerManager pwrManager) : IDisposable {
    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo {
        public uint cbSize;
        public uint dwTime;
    }

    public event Action<bool> InactivityStateChanged;

    private Timer timer;
    private Guid schemaToRestore = Guid.Empty;
    private bool inactivitySwitchFired;

    private bool isEnabled;
    private Guid inactivityPlanGuid = Guid.Empty;
    private TimeSpan inactivityTimeout;

    public void Configure(bool enabled, Guid planGuid, TimeSpan timeout) {
        StopTimer();
        if(inactivitySwitchFired) {
            InactivityStateChanged?.Invoke(false);
        }

        inactivitySwitchFired = false;
        schemaToRestore = Guid.Empty;

        isEnabled = enabled;
        inactivityPlanGuid = planGuid;
        inactivityTimeout = timeout;

        if(enabled && planGuid != Guid.Empty && inactivityTimeout > TimeSpan.Zero) {
            StartTimer();
        }
    }

    public void NotifyAcSwitchFired(Guid acAppropriateGuid) {
        if(inactivitySwitchFired) {
            schemaToRestore = acAppropriateGuid;
        }
    }

    private void StartTimer() {
        timer = new Timer(TimeSpan.FromSeconds(8).TotalMilliseconds);
        timer.Elapsed += RunCheckForInactivity;
        timer.AutoReset = true;
        timer.Start();
    }

    private void StopTimer() {
        if(timer == null) return;
        timer.Stop();
        timer.Dispose();
        timer = null;
    }

    private void RunCheckForInactivity(object sender, ElapsedEventArgs e) {
        Debug.WriteLine("Checking for inactivity");
        Application.Current?.Dispatcher?.BeginInvoke(new Action(CheckInactivity));
    }

    private void CheckInactivity() {
        if(!isEnabled || inactivityPlanGuid == Guid.Empty || inactivityTimeout <= TimeSpan.Zero) {
            Debug.WriteLine("Check skipped");
            return;
        }

        var idleMs = GetIdleTime();

        Debug.WriteLine("Idle for: {0}", idleMs);

        if(!inactivitySwitchFired) {
            // FR-08: Switch to inactivity schema when idle long enough
            if(idleMs >= inactivityTimeout) {
                var currentGuid = pwrManager.CurrentSchema?.Guid ?? Guid.Empty;
                Debug.WriteLine("Inactivity detected, switching to schema \"{0}\" from \"{1}\"", inactivityPlanGuid, currentGuid);

                // FR-10: Don't switch if inactivity schema is already active
                if(currentGuid != Guid.Empty && currentGuid != inactivityPlanGuid) {
                    schemaToRestore = currentGuid;
                    pwrManager.SetPowerSchema(inactivityPlanGuid);
                    inactivitySwitchFired = true;
                    InactivityStateChanged?.Invoke(true);
                } else {
                    Debug.WriteLine("Inactivity schema is already active, skipped");
                }
            }
        } else {
            // FR-09: Restore previous schema when activity is detected
            if(idleMs < inactivityTimeout) {
                Debug.WriteLine("Inactivity cancelled, restoring the original schema: " + schemaToRestore);

                if(schemaToRestore != Guid.Empty) {
                    pwrManager.SetPowerSchema(schemaToRestore);
                }

                schemaToRestore = Guid.Empty;
                inactivitySwitchFired = false;
                InactivityStateChanged?.Invoke(false);
            }
        }
    }

    private static TimeSpan GetIdleTime() {
        var info = new LastInputInfo();
        info.cbSize = (uint)Marshal.SizeOf(info);
        GetLastInputInfo(ref info);

        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }

    public void Dispose() => StopTimer();
}