﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace DataExtractionUtility.Properties {
    
    
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "17.4.0.0")]
    internal sealed partial class Settings : global::System.Configuration.ApplicationSettingsBase {
        
        private static Settings defaultInstance = ((Settings)(global::System.Configuration.ApplicationSettingsBase.Synchronized(new Settings())));
        
        public static Settings Default {
            get {
                return defaultInstance;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("127.0.0.1")]
        public string ServerIP {
            get {
                return ((string)(this["ServerIP"]));
            }
            set {
                this["ServerIP"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("38402")]
        public int HistorianStreamingPort {
            get {
                return ((int)(this["HistorianStreamingPort"]));
            }
            set {
                this["HistorianStreamingPort"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("PPA")]
        public string HistorianInstanceName {
            get {
                return ((string)(this["HistorianInstanceName"]));
            }
            set {
                this["HistorianInstanceName"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("6175")]
        public int HistorianGatewayPort {
            get {
                return ((int)(this["HistorianGatewayPort"]));
            }
            set {
                this["HistorianGatewayPort"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("10")]
        public int CsvMaxFileSizeMB {
            get {
                return ((int)(this["CsvMaxFileSizeMB"]));
            }
            set {
                this["CsvMaxFileSizeMB"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("500000")]
        public int CsvMaxRowCount {
            get {
                return ((int)(this["CsvMaxRowCount"]));
            }
            set {
                this["CsvMaxRowCount"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("1024")]
        public int CsvMaximumColumnCount {
            get {
                return ((int)(this["CsvMaximumColumnCount"]));
            }
            set {
                this["CsvMaximumColumnCount"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("-1")]
        public int CsvMaxFileCount {
            get {
                return ((int)(this["CsvMaxFileCount"]));
            }
            set {
                this["CsvMaxFileCount"] = value;
            }
        }
    }
}
