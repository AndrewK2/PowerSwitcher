using System;
using System.Runtime.InteropServices;
using System.Timers;
using System.Windows;

namespace PowerSwitcher.TrayApp.Services {
    public class InactivityWatcherService : IDisposable {
        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO {
            public uint cbSize;
            public uint dwTime;
        }

        public event Action<bool> InactivityStateChanged;

        private readonly IPowerManager _pwrManager;
        private Timer _timer;
        private Guid _schemaToRestore = Guid.Empty;
        private bool _inactivitySwitchFired = false;

        private bool _isEnabled = false;
        private Guid _inactivityPlanGuid = Guid.Empty;
        private int _timeoutSeconds = 0;

        public InactivityWatcherService(IPowerManager pwrManager) {
            _pwrManager = pwrManager;
        }

        public void Configure(bool enabled, Guid planGuid, int timeoutSeconds) {
            StopTimer();
            if (_inactivitySwitchFired) { InactivityStateChanged?.Invoke(false); }
            _inactivitySwitchFired = false;
            _schemaToRestore = Guid.Empty;

            _isEnabled = enabled;
            _inactivityPlanGuid = planGuid;
            _timeoutSeconds = timeoutSeconds;

            if(enabled && planGuid != Guid.Empty && timeoutSeconds > 0) {
                StartTimer();
            }
        }

        // FR-14: Called when AC/battery auto-switch fires while inactivity switch is active
        public void NotifyAcSwitchFired(Guid acAppropriateGuid) {
            if(_inactivitySwitchFired) {
                _schemaToRestore = acAppropriateGuid;
            }
        }

        private void StartTimer() {
            _timer = new Timer(15000); // Poll every 15 seconds
            _timer.Elapsed += OnTimerElapsed;
            _timer.AutoReset = true;
            _timer.Start();
        }

        private void StopTimer() {
            if(_timer == null) return;
            _timer.Stop();
            _timer.Dispose();
            _timer = null;
        }

        private void OnTimerElapsed(object sender, ElapsedEventArgs e) {
            Application.Current?.Dispatcher?.BeginInvoke(new Action(CheckInactivity));
        }

        private void CheckInactivity() {
            if(!_isEnabled || _inactivityPlanGuid == Guid.Empty || _timeoutSeconds <= 0) return;

            uint idleMs = GetIdleTimeMs();
            uint timeoutMs = (uint)(_timeoutSeconds * 1000);

            if(!_inactivitySwitchFired) {
                // FR-08: Switch to inactivity schema when idle long enough
                if(idleMs >= timeoutMs) {
                    var currentGuid = _pwrManager.CurrentSchema?.Guid ?? Guid.Empty;

                    // FR-10: Don't switch if inactivity schema is already active
                    if(currentGuid != Guid.Empty && currentGuid != _inactivityPlanGuid) {
                        _schemaToRestore = currentGuid;
                        _pwrManager.SetPowerSchema(_inactivityPlanGuid);
                        _inactivitySwitchFired = true;
                        InactivityStateChanged?.Invoke(true);
                    }
                }
            } else {
                // FR-09: Restore previous schema when activity is detected
                if(idleMs < timeoutMs) {
                    if(_schemaToRestore != Guid.Empty) {
                        _pwrManager.SetPowerSchema(_schemaToRestore);
                    }

                    _schemaToRestore = Guid.Empty;
                    _inactivitySwitchFired = false;
                    InactivityStateChanged?.Invoke(false);
                }
            }
        }

        private static uint GetIdleTimeMs() {
            var info = new LASTINPUTINFO();
            info.cbSize = (uint)Marshal.SizeOf(info);
            GetLastInputInfo(ref info);
            return unchecked((uint)Environment.TickCount - info.dwTime);
        }

        public void Dispose() {
            StopTimer();
        }
    }
}