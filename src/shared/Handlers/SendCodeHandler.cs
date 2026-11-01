using System;
using System.IO;
using System.Linq;
using System.Threading;
using Bimwright.Dwg.Plugin;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
#if ACAD2025_OR_GREATER
using Bimwright.Dwg.Plugin.Scripting;
#endif
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class SendCodeHandler : IAcadCommand
    {
        private const int ExecutionTimeoutMilliseconds = 30000;
        private static readonly string[] Imports = {
            "System", "System.Collections.Generic", "System.Linq",
            "Autodesk.AutoCAD.ApplicationServices", "Autodesk.AutoCAD.DatabaseServices",
            "Autodesk.AutoCAD.EditorInput", "Autodesk.AutoCAD.Geometry"
        };
#if ACAD2025_OR_GREATER
        private static readonly Lazy<IsolatedScriptRuntime> Runtime = new Lazy<IsolatedScriptRuntime>(() =>
            new IsolatedScriptRuntime(Path.GetDirectoryName(typeof(SendCodeHandler).Assembly.Location)));
#endif

        public string Name => "send_code";
        public string Description => "Execute C# code against the AutoCAD API.";
        public CommandSchema Schema => CommandSchemas.SendCode;

        public class Globals
        {
            public Document doc;
            public Database db;
            public Editor ed;
        }

        public class GlobalsWithData : Globals
        {
            public JToken data;
        }

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var code = (string)parameters?["code"];
            if (string.IsNullOrWhiteSpace(code)) return CommandResult.Fail("code parameter is required");
            JToken data;
            bool compileOnly;
            try
            {
                ScriptInputContract.Validate(parameters);
                data = ScriptInputContract.ParseData(parameters?["data_json"]?.Value<string>());
                compileOnly = parameters?.Value<bool?>("compile_only") ?? false;
            }
            catch (ArgumentException ex) { return CommandResult.Fail(ex.Message); }

#if ACAD2025_OR_GREATER
            var validationError = Runtime.Value.Validate(code);
#else
            var validationError = Bimwright.Dwg.Scripting.ScriptEngine.Validate(code);
#endif
            if (validationError != null) return CommandResult.Fail(validationError);

            Globals globals = data == null ? new Globals() : new GlobalsWithData { data = data };
            globals.doc = doc; globals.db = doc.Database; globals.ed = doc.Editor;
            var imports = data == null ? Imports : Imports.Concat(new[] { "Newtonsoft.Json.Linq" }).ToArray();
            var originalOut = Console.Out;
            var captured = new StringWriter();
            Console.SetOut(captured);
            System.Collections.Generic.Dictionary<string, object> payload = null;
            try
            {
                var refs = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)).ToArray();
                using (var cts = new CancellationTokenSource(ExecutionTimeoutMilliseconds))
                {
#if ACAD2025_OR_GREATER
                    payload = Runtime.Value.Execute(code, globals, refs, imports, compileOnly, cts.Token);
#else
                    payload = Bimwright.Dwg.Scripting.ScriptEngine.Execute(code, globals, refs, imports, compileOnly, cts.Token);
#endif
                }
                payload["result"] = SerializeResult(payload["result"]);
                if (payload["error"] is string error) payload["error"] = ErrorSanitizer.Sanitize(error);
                if (payload["diagnostics"] is System.Collections.Generic.Dictionary<string, object>[] diagnostics)
                    foreach (var diagnostic in diagnostics)
                        diagnostic["message"] = ErrorSanitizer.Sanitize((string)diagnostic["message"]);
            }
            catch (OperationCanceledException) { return CommandResult.Fail("execution cancelled after 30s"); }
            catch (Exception ex)
            {
                var inner = ex is AggregateException && ex.InnerException != null ? ex.InnerException : ex;
                payload = payload ?? new System.Collections.Generic.Dictionary<string, object>();
                payload["ok"] = false;
                payload["result"] = null;
                payload["error"] = ErrorSanitizer.Sanitize(inner.GetType().Name + ": " + inner.Message);
            }
            finally { Console.SetOut(originalOut); }
            payload["stdout"] = captured.ToString();
            payload["script_inputs_version"] = ScriptInputContract.Version;
            return CommandResult.Success(payload);
        }

        private static object SerializeResult(object value)
        {
            if (value == null)
                return null;
            if (value is JToken token)
                return token;
            return JToken.FromObject(value, JsonSerializer.Create(new JsonSerializerSettings
            {
                ContractResolver = new DtoContractResolver()
            }));
        }

        private sealed class DtoContractResolver : DefaultContractResolver
        {
            protected override JsonContract CreateContract(Type objectType)
            {
                // Resolve each runtime type before Newtonsoft walks its properties,
                // including host objects nested in DTOs, collections or derived types.
                for (var type = objectType; type != null; type = type.BaseType)
                {
                    if (IsHostType(type))
                        throw new JsonSerializationException("Return JSON-safe DTO values instead of AutoCAD/COM objects.");
                }
                if (objectType.GetInterfaces().Any(IsHostType))
                    throw new JsonSerializationException("Return JSON-safe DTO values instead of AutoCAD/COM objects.");
                return base.CreateContract(objectType);
            }

            private static bool IsHostType(Type type)
                => type.IsCOMObject || (type.Namespace?.StartsWith("Autodesk.", StringComparison.Ordinal) ?? false);
        }
    }
}
