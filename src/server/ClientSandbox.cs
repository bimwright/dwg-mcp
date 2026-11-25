using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Server
{
    internal sealed class ClientSandboxResult
    {
        internal static readonly ClientSandboxResult None = new ClientSandboxResult(false, null, Array.Empty<string>());

        internal ClientSandboxResult(bool detected, string kind, IReadOnlyList<string> evidence)
        {
            Detected = detected;
            Kind = kind;
            Evidence = evidence;
        }

        internal bool Detected { get; }
        internal string Kind { get; }
        internal IReadOnlyList<string> Evidence { get; }
    }

    /// <summary>
    /// FC-TGT-07: detects that the MCP client launched this gateway inside a Windows
    /// AppContainer, where it can neither read AutoCAD discovery files nor reach the host.
    /// The gateway only explains this; it never tries to leave the sandbox.
    /// Host-free: no AutoCAD reference.
    /// </summary>
    internal static class ClientSandbox
    {
        internal const string Code = "CLIENT_SANDBOXED";
        internal const string KindAppContainer = "appcontainer";
        internal const string EvidenceToken = "token_is_appcontainer";
        internal const string EvidencePath = "local_app_data_under_packages_ac";

        internal const string Message =
            "CLIENT_SANDBOXED: dwg-mcp is running inside your MCP client's sandbox (a Windows AppContainer), so it cannot see or reach AutoCAD on this computer. " +
            "The command was not sent. The sandbox is how your client protects your computer by limiting what agent tools can access; AutoCAD and dwg-mcp are working normally. " +
            "To use dwg-mcp, a person must choose to let this one MCP server run outside the sandbox in the client's settings: prefer a per-server sandbox exception; " +
            "otherwise use a client permission mode that runs local MCP servers unsandboxed. Do not loosen file or folder permissions to work around it. " +
            "Agents: report this to the user and do not try to bypass the sandbox.";

        // \Packages\<one segment>\AC followed by a separator or the end of the path.
        private static readonly Regex PackagesAc = new Regex(@"[\\/]Packages[\\/]+[^\\/]+[\\/]+AC(?:[\\/]|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Lazy<bool?> TokenProbe = new Lazy<bool?>(ProbeTokenIsAppContainer, LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly Lazy<string> OsLocalAppData = new Lazy<string>(() =>
        {
            try { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
            catch { return null; }
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>Test seam: when set, replaces OS detection for the current async flow only.</summary>
        internal static readonly AsyncLocal<ClientSandboxResult> Override = new AsyncLocal<ClientSandboxResult>();

        internal static ClientSandboxResult Classify(bool? tokenIsAppContainer, string resolvedLocalAppData, string discoveryRoot = null)
        {
            var evidence = new List<string>();
            if (tokenIsAppContainer == true) evidence.Add(EvidenceToken);
            if (IsUnderPackagesAc(resolvedLocalAppData) || IsUnderPackagesAc(discoveryRoot)) evidence.Add(EvidencePath);
            return evidence.Count == 0 ? ClientSandboxResult.None : new ClientSandboxResult(true, KindAppContainer, evidence);
        }

        internal static bool IsUnderPackagesAc(string path)
            => !string.IsNullOrWhiteSpace(path) && PackagesAc.IsMatch(path);

        /// <summary>Current process state; the token probe runs once per process.</summary>
        internal static ClientSandboxResult Detect(string discoveryRoot)
            => Override.Value ?? Classify(TokenProbe.Value, OsLocalAppData.Value, discoveryRoot);

        internal static JObject Report(ClientSandboxResult result, string dataDir) => new JObject
        {
            ["detected"] = result.Detected,
            ["kind"] = result.Kind,
            ["evidence"] = new JArray(result.Evidence),
            ["data_dir"] = dataDir
        };

        /// <summary>
        /// FC-ERR-03 / FC-TGT-07: tools that catch CLIENT_SANDBOXED into a JSON McpResponse
        /// (such as dwg_switch_target and change-history tools) still carry MCP isError=true on the wire.
        /// </summary>
        internal static void MarkError(CallToolResult result)
        {
            if (result == null || result.IsError == true) return;
            var text = result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text;
            if (string.IsNullOrWhiteSpace(text) || !text.Contains(Code, StringComparison.Ordinal)) return;
            try
            {
                if (JToken.Parse(text) is not JObject obj) return;
                if (obj["ok"]?.Type != JTokenType.Boolean || obj.Value<bool>("ok")) return;
                var error = obj["error"];
                if (error?.Type == JTokenType.String
                    && error.Value<string>().StartsWith(Code + ":", StringComparison.Ordinal))
                    result.IsError = true;
                else if (error is JObject errorObj
                    && string.Equals(errorObj.Value<string>("code"), Code, StringComparison.Ordinal))
                    result.IsError = true;
            }
            catch (JsonException) { }
        }

        internal static bool? ProbeTokenIsAppContainer()
        {
            if (!OperatingSystem.IsWindows()) return null;
            var token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out token)) return null;
                if (!GetTokenInformation(token, TokenIsAppContainer, out var value, sizeof(int), out _)) return null;
                return value != 0;
            }
            catch { return null; }
            finally { if (token != IntPtr.Zero) CloseHandle(token); }
        }

        private const uint TokenQuery = 0x0008;
        private const int TokenIsAppContainer = 29;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, out int tokenInformation,
            int tokenInformationLength, out int returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }
}
