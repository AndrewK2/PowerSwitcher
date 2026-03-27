using Petrroll.Helpers;
using PowerSwitcher.TrayApp.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace PowerSwitcher.TrayApp.Configuration
{
    public enum TrayIconColorTint { None, Green, Yellow, Orange, Red, Blue, Violet }

    [Serializable]
    public class SchemaColorEntry
    {
        public Guid SchemaGuid { get; set; }
        public TrayIconColorTint ColorTint { get; set; }
    }

    [Serializable]
    public class PowerSwitcherSettings : ObservableObject
    {
        //I know that everything should be observable but it's not neccessary so let's leave it as it is for now

        public bool AutomaticFlyoutHideAfterClick { get; set; } = true;
        public bool AutomaticOnACSwitch { get; set; } = false;
        public Guid AutomaticPlanGuidOnAC { get; set; } = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        public Guid AutomaticPlanGuidOffAC { get; set; } = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a");

        //TODO: Fix so that it can be changed during runtime
        public Key ShowOnShortcutKey { get; set; } = Key.L;
        public KeyModifier ShowOnShortcutKeyModifier { get; set; } = KeyModifier.Shift | KeyModifier.Win;

        bool showOnShortcutSwitch = false;
        public bool ShowOnShortcutSwitch { get { return showOnShortcutSwitch; } set { showOnShortcutSwitch = value; RaisePropertyChangedEvent(nameof(ShowOnShortcutSwitch)); } }

        bool showOnlyDefaultSchemas = false;
        public bool ShowOnlyDefaultSchemas { get { return showOnlyDefaultSchemas; } set { showOnlyDefaultSchemas = value; RaisePropertyChangedEvent(nameof(ShowOnlyDefaultSchemas)); } }

        public bool InactivitySwitchEnabled { get; set; } = false;
        public Guid InactivityPlanGuid { get; set; } = Guid.Empty;
        public int InactivityTimeoutSeconds { get; set; } = TrayApp.InactivityTimeout180;
        public TrayIconColorTint InactivityIconColorTint { get; set; } = TrayIconColorTint.Green;

        public List<SchemaColorEntry> SchemaIconTints { get; set; } = [];

        public TrayIconColorTint GetSchemaTint(Guid guid) => SchemaIconTints.FirstOrDefault(e => e.SchemaGuid == guid)?.ColorTint ?? TrayIconColorTint.None;

        public void SetSchemaTint(Guid guid, TrayIconColorTint tint) {
            var entry = SchemaIconTints.FirstOrDefault(e => e.SchemaGuid == guid);
            if(entry == null) {
                entry = new SchemaColorEntry { SchemaGuid = guid };
                SchemaIconTints.Add(entry);
            }

            entry.ColorTint = tint;
        }
    }
}
