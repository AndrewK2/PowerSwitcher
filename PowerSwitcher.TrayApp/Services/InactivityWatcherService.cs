using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows;

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
    private int timeoutSeconds;

    public void Configure(bool enabled, Guid planGuid, TimeSpan timeout) {
        StopTimer();
        if(inactivitySwitchFired) {
            InactivityStateChanged?.Invoke(false);
        }

        inactivitySwitchFired = false;
        schemaToRestore = Guid.Empty;

        isEnabled = enabled;
        inactivityPlanGuid = planGuid;
        timeoutSeconds = (int)timeout.TotalSeconds;

        if(enabled && planGuid != Guid.Empty && timeoutSeconds > 0) {
            StartTimer();
        }
    }

    public void NotifyAcSwitchFired(Guid acAppropriateGuid) {
        if(inactivitySwitchFired) {
            schemaToRestore = acAppropriateGuid;
        }
    }

    private void StartTimer() {
        timer = new Timer(15000); // Poll every 15 seconds
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
        if(!isEnabled || inactivityPlanGuid == Guid.Empty || timeoutSeconds <= 0) return;

        var idleMs = GetIdleTimeMs();
        var timeoutMs = (uint)(timeoutSeconds * 1000);

        if(!inactivitySwitchFired) {
            // FR-08: Switch to inactivity schema when idle long enough
            if(idleMs >= timeoutMs) {
                var currentGuid = pwrManager.CurrentSchema?.Guid ?? Guid.Empty;

                // FR-10: Don't switch if inactivity schema is already active
                if(currentGuid != Guid.Empty && currentGuid != inactivityPlanGuid) {
                    schemaToRestore = currentGuid;
                    pwrManager.SetPowerSchema(inactivityPlanGuid);
                    inactivitySwitchFired = true;
                    InactivityStateChanged?.Invoke(true);
                }
            }
        } else {
            // FR-09: Restore previous schema when activity is detected
            if(idleMs < timeoutMs) {
                if(schemaToRestore != Guid.Empty) {
                    pwrManager.SetPowerSchema(schemaToRestore);
                }

                schemaToRestore = Guid.Empty;
                inactivitySwitchFired = false;
                InactivityStateChanged?.Invoke(false);
            }
        }
    }

    private static uint GetIdleTimeMs() {
        var info = new LastInputInfo();
        info.cbSize = (uint)Marshal.SizeOf(info);
        GetLastInputInfo(ref info);
        return unchecked((uint)Environment.TickCount - info.dwTime);
    }

    public void Dispose() => StopTimer();
}