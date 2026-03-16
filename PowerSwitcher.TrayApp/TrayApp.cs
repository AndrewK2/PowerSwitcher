using Petrroll.Helpers;
using PowerSwitcher.TrayApp.Configuration;
using PowerSwitcher.TrayApp.Resources;
using PowerSwitcher.TrayApp.Services;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using WF = System.Windows.Forms;


namespace PowerSwitcher.TrayApp
{

    public class TrayApp
    {

        #region PrivateObjects
        readonly WF.NotifyIcon _trayIcon;
        public event Action ShowFlyout;
        IPowerManager pwrManager;
        ConfigurationInstance<PowerSwitcherSettings> configuration;
        InactivityWatcherService inactivityWatcher;
        System.Drawing.Icon defaultIcon;
        System.Drawing.Icon invertedIcon;

        #endregion
        
        internal const int InactivityTimeout180 = 180;

        #region Contructor
        public TrayApp(IPowerManager powerManager, ConfigurationInstance<PowerSwitcherSettings> config, InactivityWatcherService inactivityWatcherService)
        {
            this.pwrManager = powerManager;
            pwrManager.PropertyChanged += PwrManager_PropertyChanged;
            inactivityWatcher = inactivityWatcherService;

            configuration = config;

            _trayIcon = new WF.NotifyIcon();
            _trayIcon.MouseClick += TrayIcon_MouseClick;

            defaultIcon = new System.Drawing.Icon(Application.GetResourceStream(new Uri("pack://application:,,,/PowerSwitcher.TrayApp;component/Tray.ico")).Stream, WF.SystemInformation.SmallIconSize);
            invertedIcon = CreateInvertedIcon(defaultIcon);
            _trayIcon.Icon = defaultIcon;
            _trayIcon.Text = string.Concat(AppStrings.AppName);
            _trayIcon.Visible = true;

            inactivityWatcher.InactivityStateChanged += isInactive => _trayIcon.Icon = isInactive ? invertedIcon : defaultIcon;

            this.ShowFlyout += (((App)Application.Current).MainWindow as MainWindow).ToggleWindowVisibility;

            //Run automatic on-off-AC change at boot
            powerStatusChanged();
        }

        public void CreateAltMenu()
        {
            var contextMenuRoot = new WF.ContextMenu();
            contextMenuRoot.Popup += ContextMenu_Popup;

            _trayIcon.ContextMenu = contextMenuRoot;

            var contextMenuRootItems = contextMenuRoot.MenuItems;
            contextMenuRootItems.Add("-");

            var contextMenuSettings = contextMenuRootItems.Add(AppStrings.Settings);
            contextMenuSettings.Name = "settings";

            var settingsOnACItem = contextMenuSettings.MenuItems.Add(AppStrings.SchemaToSwitchOnAc);
            settingsOnACItem.Name = "settingsOnAC";

            var settingsOffACItem = contextMenuSettings.MenuItems.Add(AppStrings.SchemaToSwitchOffAc);
            settingsOffACItem.Name = "settingsOffAC";

            var automaticSwitchItem = contextMenuSettings.MenuItems.Add(AppStrings.AutomaticOnOffACSwitch);
            automaticSwitchItem.Checked = configuration.Data.AutomaticOnACSwitch;
            automaticSwitchItem.Click += AutomaticSwitchItem_Click;

            #region Inactivity configuration

            var settingsInactivityItem = contextMenuSettings.MenuItems.Add(AppStrings.SchemaToSwitchOnInactivity);
            settingsInactivityItem.Name = "settingsInactivity";

            var settingsInactivityIntervalItem = contextMenuSettings.MenuItems.Add(AppStrings.AutomaticallyChangeSchemaWhenInactive);
            settingsInactivityIntervalItem.Name = "settingsInactivityInterval";

            var disabledItem = new WF.MenuItem("Disabled");
            disabledItem.Name = "inactivityIntervalDisabled";
            disabledItem.Click += InactivityDisabled_Click;
            settingsInactivityIntervalItem.MenuItems.Add(disabledItem);

            var intervalOptions = new[] {
                (seconds: 15,  label: "After 15 seconds"),
                (seconds: 60,  label: "After 1 minute"),
                (seconds: InactivityTimeout180, label: "After 3 minutes"),
                (seconds: 900, label: "After 15 minutes"),
            };

            foreach (var (seconds, label) in intervalOptions)
            {
                var intervalItem = new WF.MenuItem(label);
                intervalItem.Name = $"inactivityAfter{seconds}";
                var capturedSeconds = seconds;
                intervalItem.Click += (s, ea) => setInactivityTimeout(capturedSeconds);
                settingsInactivityIntervalItem.MenuItems.Add(intervalItem);
            }

            #endregion

            var automaticHideItem = contextMenuSettings.MenuItems.Add(AppStrings.HideFlyoutAfterSchemaChangeSwitch);
            automaticHideItem.Checked = configuration.Data.AutomaticFlyoutHideAfterClick;
            automaticHideItem.Click += AutomaticHideItem_Click;

            var onlyDefaultSchemasItem = contextMenuSettings.MenuItems.Add(AppStrings.ShowOnlyDefaultSchemas);
            onlyDefaultSchemasItem.Checked = configuration.Data.ShowOnlyDefaultSchemas;
            onlyDefaultSchemasItem.Click += OnlyDefaultSchemas_Click;

            var enableShortcutsToggleItem = contextMenuSettings.MenuItems.Add($"{AppStrings.ToggleOnShowrtcutSwitch} ({configuration.Data.ShowOnShortcutKeyModifier} + {configuration.Data.ShowOnShortcutKey})");
            enableShortcutsToggleItem.Enabled = !(Application.Current as App).HotKeyFailed;
            enableShortcutsToggleItem.Checked = configuration.Data.ShowOnShortcutSwitch;
            enableShortcutsToggleItem.Click += EnableShortcutsToggleItem_Click;

            var aboutItem = contextMenuRootItems.Add($"{AppStrings.About} ({Assembly.GetEntryAssembly().GetName().Version})");
            aboutItem.Click += About_Click;

            var exitItem = contextMenuRootItems.Add(AppStrings.Exit);
            exitItem.Click += Exit_Click;
        }

        #endregion

        #region FlyoutRelated
        void TrayIcon_MouseClick(object sender, WF.MouseEventArgs e)
        {
            if (e.Button == WF.MouseButtons.Left)
            {
                ShowFlyout?.Invoke();
            }
        }

        #endregion

        #region SettingsTogglesRegion
        private void EnableShortcutsToggleItem_Click(object sender, EventArgs e)
        {
            WF.MenuItem enableShortcutsToggleItem = (WF.MenuItem)sender;

            configuration.Data.ShowOnShortcutSwitch = !configuration.Data.ShowOnShortcutSwitch;
            enableShortcutsToggleItem.Checked = configuration.Data.ShowOnShortcutSwitch;
            enableShortcutsToggleItem.Enabled = !(Application.Current as App).HotKeyFailed;

            configuration.Save();
        }

        private void AutomaticHideItem_Click(object sender, EventArgs e)
        {
            WF.MenuItem automaticHideItem = (WF.MenuItem)sender;

            configuration.Data.AutomaticFlyoutHideAfterClick = !configuration.Data.AutomaticFlyoutHideAfterClick;
            automaticHideItem.Checked = configuration.Data.AutomaticFlyoutHideAfterClick;

            configuration.Save();
        }

        private void OnlyDefaultSchemas_Click(object sender, EventArgs e)
        {
            WF.MenuItem onlyDefaultSchemasItem = (WF.MenuItem)sender;

            configuration.Data.ShowOnlyDefaultSchemas = !configuration.Data.ShowOnlyDefaultSchemas;
            onlyDefaultSchemasItem.Checked = configuration.Data.ShowOnlyDefaultSchemas;

            configuration.Save();
        }

        private void AutomaticSwitchItem_Click(object sender, EventArgs e)
        {
            WF.MenuItem automaticSwitchItem = (WF.MenuItem)sender;

            configuration.Data.AutomaticOnACSwitch = !configuration.Data.AutomaticOnACSwitch;
            automaticSwitchItem.Checked = configuration.Data.AutomaticOnACSwitch;

            if (configuration.Data.AutomaticOnACSwitch) { powerStatusChanged(); }

            configuration.Save();
        }

        #endregion

        #region AutomaticOnACSwitchRelated

        private void PwrManager_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(IPowerManager.CurrentPowerStatus)) { powerStatusChanged(); }
        }

        private void powerStatusChanged()
        {
            if(!configuration.Data.AutomaticOnACSwitch) { return; }

            var currentPowerPlugStatus = pwrManager.CurrentPowerStatus;
            Guid schemaGuidToSwitch = default(Guid);

            switch (currentPowerPlugStatus)
            {
                case PowerPlugStatus.Online:
                    schemaGuidToSwitch = configuration.Data.AutomaticPlanGuidOnAC;
                    break;
                case PowerPlugStatus.Offline:
                    schemaGuidToSwitch = configuration.Data.AutomaticPlanGuidOffAC;
                    break;
                default:
                    break;
            }

            IPowerSchema schemaToSwitchTo = pwrManager.Schemas.FirstOrDefault(sch => sch.Guid == schemaGuidToSwitch);
            if(schemaToSwitchTo == null) { return; }

            pwrManager.SetPowerSchema(schemaToSwitchTo);

            // FR-14: If user is idle and inactivity switch has fired, update restore target
            inactivityWatcher?.NotifyAcSwitchFired(schemaGuidToSwitch);
        }

        #endregion

        #region ContextMenuItemRelatedStuff

        private void ContextMenu_Popup(object sender, EventArgs e)
        {
            clearPowerSchemasInTray();

            pwrManager.UpdateSchemas();

            // FR-09: Validate saved inactivity GUID; disable gracefully if plan no longer exists
            if (configuration.Data.InactivitySwitchEnabled)
            {
                var savedGuid = configuration.Data.InactivityPlanGuid;
                if (!pwrManager.Schemas.Any(s => s.Guid == savedGuid))
                {
                    configuration.Data.InactivitySwitchEnabled = false;
                    configuration.Data.InactivityPlanGuid = Guid.Empty;
                    configuration.Data.InactivityTimeoutSeconds = 0;
                    configuration.Save();
                    inactivityWatcher.Configure(false, Guid.Empty, TimeSpan.Zero);
                }
            }

            foreach (var powerSchema in pwrManager.Schemas)
            {
                updateTrayMenuWithPowerSchema(powerSchema);
            }

            var intervalMenu = _trayIcon.ContextMenu.MenuItems["settings"].MenuItems["settingsInactivityInterval"];
            var intervalActive = configuration.Data.InactivitySwitchEnabled && configuration.Data.InactivityTimeoutSeconds > 0;
            intervalMenu.Text = AppStrings.AutomaticallyChangeSchemaWhenInactive;
            foreach (WF.MenuItem item in intervalMenu.MenuItems) {
                item.Checked = item.Name == "inactivityIntervalDisabled" 
                    ? !intervalActive 
                    : intervalActive && configuration.Data.InactivityTimeoutSeconds == int.Parse(item.Name.Substring("inactivityAfter".Length));
            }
        }

        private void updateTrayMenuWithPowerSchema(IPowerSchema powerSchema)
        {
            var newItemMain = getNewPowerSchemaItem(
                powerSchema,
                (s, ea) => switchToPowerSchema(powerSchema),
                powerSchema.IsActive
                );
            _trayIcon.ContextMenu.MenuItems.Add(0, newItemMain);

            var newItemSettingsOffAC = getNewPowerSchemaItem(
                powerSchema,
                (s, ea) => setPowerSchemaAsOffAC(powerSchema),
                (powerSchema.Guid == configuration.Data.AutomaticPlanGuidOffAC)
                );
            _trayIcon.ContextMenu.MenuItems["settings"].MenuItems["settingsOffAC"].MenuItems.Add(0, newItemSettingsOffAC);

            var newItemSettingsOnAC = getNewPowerSchemaItem(
                powerSchema,
                (s, ea) => setPowerSchemaAsOnAC(powerSchema),
                (powerSchema.Guid == configuration.Data.AutomaticPlanGuidOnAC)
                );

            _trayIcon.ContextMenu.MenuItems["settings"].MenuItems["settingsOnAC"].MenuItems.Add(0, newItemSettingsOnAC);

            updateInactivityMenuWithPowerSchema(powerSchema);
        }

        private void updateInactivityMenuWithPowerSchema(IPowerSchema powerSchema)
        {
            var schemaItem = new WF.MenuItem(powerSchema.Name);
            schemaItem.Name = $"inactivityScheme{powerSchema.Guid}";
            schemaItem.Checked = configuration.Data.InactivityPlanGuid == powerSchema.Guid;
            schemaItem.Click += (s, ea) => setInactivityPlan(powerSchema);

            _trayIcon.ContextMenu.MenuItems["settings"].MenuItems["settingsInactivity"].MenuItems.Add(schemaItem);
        }

        private void clearPowerSchemasInTray()
        {
            for (int i = _trayIcon.ContextMenu.MenuItems.Count - 1; i >= 0; i--)
            {
                var item = _trayIcon.ContextMenu.MenuItems[i];
                if (item.Name.StartsWith("pwrScheme", StringComparison.Ordinal))
                {
                    _trayIcon.ContextMenu.MenuItems.Remove(item);
                }
            }

            _trayIcon.ContextMenu.MenuItems["settings"].MenuItems["settingsOffAC"].MenuItems.Clear();
            _trayIcon.ContextMenu.MenuItems["settings"].MenuItems["settingsOnAC"].MenuItems.Clear();
            _trayIcon.ContextMenu.MenuItems["settings"].MenuItems["settingsInactivity"].MenuItems.Clear();
        }

        private WF.MenuItem getNewPowerSchemaItem(IPowerSchema powerSchema, EventHandler clickedHandler, bool isChecked)
        {
            var newItemMain = new WF.MenuItem(powerSchema.Name);
            newItemMain.Name = $"pwrScheme{powerSchema.Guid}";
            newItemMain.Checked = isChecked;
            newItemMain.Click += clickedHandler;

            return newItemMain;
        }

        #endregion

        #region OnSchemaClickMethods
        private void setPowerSchemaAsOffAC(IPowerSchema powerSchema)
        {
            configuration.Data.AutomaticPlanGuidOffAC = powerSchema.Guid;
            configuration.Save();
        }

        private void setPowerSchemaAsOnAC(IPowerSchema powerSchema)
        {
            configuration.Data.AutomaticPlanGuidOnAC = powerSchema.Guid;
            configuration.Save();
        }

        private void switchToPowerSchema(IPowerSchema powerSchema)
        {
            pwrManager.SetPowerSchema(powerSchema);
        }

        private void setInactivityPlan(IPowerSchema schema)
        {
            configuration.Data.InactivityPlanGuid = schema.Guid;
            configuration.Save();
            inactivityWatcher.Configure(configuration.Data.InactivitySwitchEnabled, schema.Guid, TimeSpan.FromSeconds(configuration.Data.InactivityTimeoutSeconds));
        }

        private void InactivityDisabled_Click(object sender, EventArgs e)
        {
            configuration.Data.InactivitySwitchEnabled = false;
            configuration.Data.InactivityTimeoutSeconds = 0;
            configuration.Save();
            inactivityWatcher.Configure(false, configuration.Data.InactivityPlanGuid, TimeSpan.Zero);
        }

        private void setInactivityTimeout(int seconds)
        {
            configuration.Data.InactivityTimeoutSeconds = seconds;
            configuration.Data.InactivitySwitchEnabled = configuration.Data.InactivityPlanGuid != Guid.Empty;
            configuration.Save();
            inactivityWatcher.Configure(configuration.Data.InactivitySwitchEnabled, configuration.Data.InactivityPlanGuid, TimeSpan.FromSeconds(seconds));
        }
        #endregion

        #region IconHelpers

        private static System.Drawing.Icon CreateInvertedIcon(System.Drawing.Icon original)
        {
            using (var bmp = new System.Drawing.Bitmap(original.Width, original.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                    g.DrawIcon(original, 0, 0);

                var data = bmp.LockBits(
                    new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
                    System.Drawing.Imaging.ImageLockMode.ReadWrite,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                int bytes = Math.Abs(data.Stride) * bmp.Height;
                var pixels = new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, bytes);

                for (int i = 0; i < bytes; i += 4)
                {
                    if (pixels[i + 3] == 0) // transparent → opaque white background
                    {
                        pixels[i]     = 255; // B
                        pixels[i + 1] = 255; // G
                        pixels[i + 2] = 255; // R
                        pixels[i + 3] = 255; // A
                    }
                    else // visible → invert RGB
                    {
                        pixels[i]     = (byte)(255 - pixels[i]);     // B
                        pixels[i + 1] = (byte)(255 - pixels[i + 1]); // G
                        pixels[i + 2] = (byte)(255 - pixels[i + 2]); // R
                    }
                }

                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, bytes);
                bmp.UnlockBits(data);

                IntPtr hicon = bmp.GetHicon();
                var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hicon).Clone();
                DestroyIcon(hicon);
                return icon;
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        #endregion

        #region OtherItemsClicked

        void About_Click(object sender, EventArgs e)
        {
            Process.Start(AppStrings.AboutAppURL);
        }

        private void IconLicenceItem_Click(object sender, EventArgs e)
        {
            Process.Start(AppStrings.IconLicenceURL);
        }


        void Exit_Click(object sender, EventArgs e)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();

            pwrManager.Dispose();

            Application.Current.Shutdown();
        }
        #endregion

    }
}
