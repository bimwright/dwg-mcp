using System;
using System.Collections.Generic;
using System.Globalization;
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
        public const string EnvEnableCallLog = "BIMWRIGHT_DWG_ENABLE_CALL_LOG";
        public const string EnvEnableToolbaker = "BIMWRIGHT_DWG_ENABLE_TOOLBAKER";
        public const string EnvAllowLanBind = "BIMWRIGHT_DWG_ALLOW_LAN_BIND";
        public const string EnvLogLevel = "BIMWRIGHT_DWG_LOG_LEVEL";
        public const string EnvResponseBudgetBytes = "BIMWRIGHT_DWG_RESPONSE_BUDGET_BYTES";
        public const string EnvResponseWarnBytes = "BIMWRIGHT_DWG_RESPONSE_WARN_BYTES";
        public const string EnvResponseStrongWarnBytes = "BIMWRIGHT_DWG_RESPONSE_STRONG_WARN_BYTES";
        public const int DefaultResponseBudgetBytes = 80 * 1024 * 1024;
        public const int DefaultResponseWarnBytes = 1024 * 1024;
        public const int DefaultResponseStrongWarnBytes = 8 * 1024 * 1024;
        public const int MinResponseBudgetBytes = 4096;

        public string Target { get; set; }
        public List<string> Toolsets { get; set; }
        public bool? ReadOnly { get; set; }
        public bool? EnableSendCode { get; set; }
        public bool? EnableCallLog { get; set; }
        public bool? EnableToolbaker { get; set; }
        public bool? AllowLanBind { get; set; }
        public string LogLevel { get; set; }
        public int? ResponseBudgetBytes { get; set; }
        public int? ResponseWarnBytes { get; set; }
        public int? ResponseStrongWarnBytes { get; set; }

        /// <summary>
        /// Default config file the installer seeds and the server reads when no
        /// --config is passed: %LOCALAPPDATA%\Bimwright\dwg-mcp\dwgmcp.config.json.
        /// </summary>
        public static string DefaultConfigFilePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Bimwright", "dwg-mcp", "dwgmcp.config.json");

        [JsonIgnore] public bool ReadOnlyOrDefault => ReadOnly ?? false;
        [JsonIgnore] public bool EnableSendCodeOrDefault => EnableSendCode ?? true;
        [JsonIgnore] public bool EnableCallLogOrDefault => EnableCallLog ?? false;
        [JsonIgnore] public bool EnableToolbakerOrDefault => EnableToolbaker ?? true;
        [JsonIgnore] public bool AllowLanBindOrDefault => AllowLanBind ?? false;
        [JsonIgnore] public int ResponseBudgetBytesOrDefault => ResponseBudgetBytes ?? DefaultResponseBudgetBytes;
        [JsonIgnore] public int ResponseWarnBytesOrDefault => ResponseWarnBytes ?? Math.Min(DefaultResponseWarnBytes, ResponseBudgetBytesOrDefault);
        [JsonIgnore] public int ResponseStrongWarnBytesOrDefault => ResponseStrongWarnBytes ?? Math.Min(DefaultResponseStrongWarnBytes, ResponseBudgetBytesOrDefault);

        public void ValidateResponseBudget()
        {
            if (ResponseBudgetBytesOrDefault < MinResponseBudgetBytes)
                throw new ArgumentException("ResponseBudgetBytes must be at least 4096 bytes.", nameof(ResponseBudgetBytes));
            if (ResponseWarnBytesOrDefault <= 0)
                throw new ArgumentException("ResponseWarnBytes must be positive.", nameof(ResponseWarnBytes));
            if (ResponseStrongWarnBytesOrDefault <= 0)
                throw new ArgumentException("ResponseStrongWarnBytes must be positive.", nameof(ResponseStrongWarnBytes));
            if (ResponseWarnBytesOrDefault > ResponseStrongWarnBytesOrDefault)
                throw new ArgumentException("ResponseWarnBytes must not exceed ResponseStrongWarnBytes.", nameof(ResponseWarnBytes));
            if (ResponseStrongWarnBytesOrDefault > ResponseBudgetBytesOrDefault)
                throw new ArgumentException("ResponseStrongWarnBytes must not exceed ResponseBudgetBytes.", nameof(ResponseStrongWarnBytes));
        }

        public static DwgMcpConfig Load(
            string[] args = null,
            string configFilePath = null,
            Func<string, string> envLookup = null)
        {
            args = args ?? Array.Empty<string>();
            envLookup = envLookup ?? Environment.GetEnvironmentVariable;

            var cliConfig = GetOptionValue(args, "--config");
            var path = !string.IsNullOrWhiteSpace(cliConfig)
                ? cliConfig
                : (configFilePath ?? DefaultConfigFilePath);
            var config = LoadJson(path);

            ApplyEnv(config, envLookup);
            ApplyCli(config, args);
            config.ValidateResponseBudget();
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
            ApplyBool(envLookup(EnvEnableCallLog), value => config.EnableCallLog = value);
            ApplyBool(envLookup(EnvEnableToolbaker), value => config.EnableToolbaker = value);
            ApplyBool(envLookup(EnvAllowLanBind), value => config.AllowLanBind = value);
            ApplyString(envLookup(EnvLogLevel), value => config.LogLevel = value);
            ApplyBytes(envLookup(EnvResponseBudgetBytes), EnvResponseBudgetBytes, value => config.ResponseBudgetBytes = value);
            ApplyBytes(envLookup(EnvResponseWarnBytes), EnvResponseWarnBytes, value => config.ResponseWarnBytes = value);
            ApplyBytes(envLookup(EnvResponseStrongWarnBytes), EnvResponseStrongWarnBytes, value => config.ResponseStrongWarnBytes = value);
        }

        private static void ApplyCli(DwgMcpConfig config, string[] args)
        {
            ApplyString(GetOptionValue(args, "--target"), value => config.Target = value);
            ApplyCsv(GetOptionValue(args, "--toolsets"), value => config.Toolsets = value);
            ApplyString(GetOptionValue(args, "--log-level"), value => config.LogLevel = value);
            ApplyCliBytes(args, "--response-budget-bytes", value => config.ResponseBudgetBytes = value);
            ApplyCliBytes(args, "--response-warn-bytes", value => config.ResponseWarnBytes = value);
            ApplyCliBytes(args, "--response-strong-warn-bytes", value => config.ResponseStrongWarnBytes = value);

            // Each explicit pair overrides JSON/env; the last CLI occurrence wins.
            foreach (var arg in args)
            {
                if (string.Equals(arg, "--enable-send-code", StringComparison.OrdinalIgnoreCase)) config.EnableSendCode = true;
                if (string.Equals(arg, "--disable-send-code", StringComparison.OrdinalIgnoreCase)) config.EnableSendCode = false;
                if (string.Equals(arg, "--enable-call-log", StringComparison.OrdinalIgnoreCase)) config.EnableCallLog = true;
                if (string.Equals(arg, "--disable-call-log", StringComparison.OrdinalIgnoreCase)) config.EnableCallLog = false;
            }

            if (HasFlag(args, "--read-only"))
            {
                config.ReadOnly = true;
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

        private static void ApplyBytes(string raw, string name, Action<int> set)
        {
            if (raw == null) return;
            // Only ASCII decimal digits: no signs, whitespace, suffixes or culture-dependent separators.
            var digits = raw.Length > 0;
            foreach (var character in raw)
                if (character < '0' || character > '9') digits = false;
            if (!digits || !int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw new ArgumentException(name + " requires an ASCII decimal integer byte count from 0 to 2147483647.", name);
            set(value);
        }

        private static void ApplyCliBytes(string[] args, string name, Action<int> set)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 == args.Length || args[i + 1] == null || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException(name + " requires a byte-count value.", name);
                ApplyBytes(args[++i], name, set);
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
