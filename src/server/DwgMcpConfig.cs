using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Bimwright.Dwg.Server
{
    public class DwgMcpConfig
    {
        public const string EnvTarget = "BIMWRIGHT_DWG_TARGET";
        public const string EnvToolsets = "BIMWRIGHT_DWG_TOOLSETS";
        public const string EnvReadOnly = "BIMWRIGHT_DWG_READ_ONLY";
        public const string EnvEnableSendCode = "BIMWRIGHT_DWG_ENABLE_SEND_CODE";
        public const string EnvEnableToolbaker = "BIMWRIGHT_DWG_ENABLE_TOOLBAKER";
        public const string EnvAllowLanBind = "BIMWRIGHT_DWG_ALLOW_LAN_BIND";
        public const string EnvLogLevel = "BIMWRIGHT_DWG_LOG_LEVEL";

        public string Target { get; set; }
        public List<string> Toolsets { get; set; }
        public bool? ReadOnly { get; set; }
        public bool? EnableSendCode { get; set; }
        public bool? EnableToolbaker { get; set; }
        public bool? AllowLanBind { get; set; }
        public string LogLevel { get; set; }

        [JsonIgnore] public bool ReadOnlyOrDefault => ReadOnly ?? false;
        [JsonIgnore] public bool EnableSendCodeOrDefault => EnableSendCode ?? false;
        [JsonIgnore] public bool EnableToolbakerOrDefault => EnableToolbaker ?? true;
        [JsonIgnore] public bool AllowLanBindOrDefault => AllowLanBind ?? false;

        public static DwgMcpConfig Load(
            string[] args = null,
            string configFilePath = null,
            Func<string, string> envLookup = null)
        {
            args = args ?? Array.Empty<string>();
            envLookup = envLookup ?? Environment.GetEnvironmentVariable;

            var cliConfig = GetOptionValue(args, "--config");
            var path = string.IsNullOrWhiteSpace(cliConfig) ? configFilePath : cliConfig;
            var config = LoadJson(path);

            ApplyEnv(config, envLookup);
            ApplyCli(config, args);
            return config;
        }

        private static DwgMcpConfig LoadJson(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return new DwgMcpConfig();
            }

            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<DwgMcpConfig>(json) ?? new DwgMcpConfig();
        }

        private static void ApplyEnv(DwgMcpConfig config, Func<string, string> envLookup)
        {
            ApplyString(envLookup(EnvTarget), value => config.Target = value);
            ApplyCsv(envLookup(EnvToolsets), value => config.Toolsets = value);
            ApplyBool(envLookup(EnvReadOnly), value => config.ReadOnly = value);
            ApplyBool(envLookup(EnvEnableSendCode), value => config.EnableSendCode = value);
            ApplyBool(envLookup(EnvEnableToolbaker), value => config.EnableToolbaker = value);
            ApplyBool(envLookup(EnvAllowLanBind), value => config.AllowLanBind = value);
            ApplyString(envLookup(EnvLogLevel), value => config.LogLevel = value);
        }

        private static void ApplyCli(DwgMcpConfig config, string[] args)
        {
            ApplyString(GetOptionValue(args, "--target"), value => config.Target = value);
            ApplyCsv(GetOptionValue(args, "--toolsets"), value => config.Toolsets = value);
            ApplyString(GetOptionValue(args, "--log-level"), value => config.LogLevel = value);

            if (HasFlag(args, "--read-only"))
            {
                config.ReadOnly = true;
            }

            if (HasFlag(args, "--enable-send-code"))
            {
                config.EnableSendCode = true;
            }

            if (HasFlag(args, "--enable-toolbaker"))
            {
                config.EnableToolbaker = true;
            }

            if (HasFlag(args, "--disable-toolbaker"))
            {
                config.EnableToolbaker = false;
            }

            if (HasFlag(args, "--allow-lan-bind"))
            {
                config.AllowLanBind = true;
            }
        }

        private static void ApplyString(string raw, Action<string> set)
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                set(raw.Trim());
            }
        }

        private static void ApplyCsv(string raw, Action<List<string>> set)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var values = new List<string>();
            foreach (var item in raw.Split(','))
            {
                var trimmed = item.Trim();
                if (trimmed.Length > 0)
                {
                    values.Add(trimmed);
                }
            }
            set(values);
        }

        private static void ApplyBool(string raw, Action<bool> set)
        {
            if (TryParseBool(raw, out var value))
            {
                set(value);
            }
        }

        private static bool TryParseBool(string raw, out bool value)
        {
            value = false;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return false;
            }

            switch (raw.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    value = true;
                    return true;
                case "0":
                case "false":
                case "no":
                case "off":
                    value = false;
                    return true;
                default:
                    return false;
            }
        }

        private static bool HasFlag(string[] args, string name)
        {
            foreach (var arg in args)
            {
                if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string GetOptionValue(string[] args, string name)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
