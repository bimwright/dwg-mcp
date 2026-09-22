using System;
using System.IO;
using System.Linq;
using System.Threading;
using Bimwright.Dwg.Plugin;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class SendCodeHandler : IAcadCommand
    {
        private const int ExecutionTimeoutMilliseconds = 30000;

        public string Name => "send_code";
        public string Description => "Execute C# code against the AutoCAD API.";
        public CommandSchema Schema => CommandSchemas.SendCode;

        public class Globals
        {
            public Document doc;
            public Database db;
            public Editor ed;
        }

        public CommandResult Execute(Document doc, JToken parameters)
        {
            var code = (string)parameters?["code"];
            if (string.IsNullOrWhiteSpace(code))
                return CommandResult.Fail("code parameter is required");

            // A blocking wait on EvaluateAsync does not keep script continuations on
            // the thread that owns DocumentLock. Reject before executing any statement.
            var syntax = CSharpSyntaxTree.ParseText(code, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
            if (syntax.GetRoot().DescendantTokens().Any(t => t.IsKind(SyntaxKind.AwaitKeyword) || t.IsKind(SyntaxKind.AsyncKeyword)))
                return CommandResult.Fail("send_code requires synchronous code; async/await is not supported. Keep AutoCAD API calls on the calling thread.");

            var originalOut = Console.Out;
            var captured = new StringWriter();
            Console.SetOut(captured);

            try
            {
                var refs = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                    .ToArray();
                var options = ScriptOptions.Default
                    .WithReferences(refs)
                    .WithImports(
                        "System",
                        "System.Collections.Generic",
                        "System.Linq",
                        "Autodesk.AutoCAD.ApplicationServices",
                        "Autodesk.AutoCAD.DatabaseServices",
                        "Autodesk.AutoCAD.EditorInput",
                        "Autodesk.AutoCAD.Geometry");

                var globals = new Globals { doc = doc, db = doc.Database, ed = doc.Editor };

                object result;
                using (var cts = new CancellationTokenSource(ExecutionTimeoutMilliseconds))
                {
                    try
                    {
                        // Run on the calling thread: Document.LockDocument() is
                        // thread-affine, so a worker thread would not hold the lock
                        // and DB writes would fail with eLockViolation. The token is
                        // cooperative — a script blocked in a native call keeps the
                        // executor queued until it returns (same posture as rvt-mcp).
                        result = CSharpScript.EvaluateAsync(code, options, globals, cancellationToken: cts.Token)
                            .GetAwaiter()
                            .GetResult();
                    }
                    catch (OperationCanceledException)
                    {
                        return CommandResult.Fail("execution cancelled after 30s");
                    }
                }

                return CommandResult.Success(new
                {
                    ok = true,
                    result = SerializeResult(result),
                    stdout = captured.ToString(),
                    error = (string)null
                });
            }
            catch (CompilationErrorException ex)
            {
                return CommandResult.Success(new
                {
                    ok = false,
                    result = (object)null,
                    stdout = captured.ToString(),
                    error = ErrorSanitizer.Sanitize("compile error: " + string.Join("\n", ex.Diagnostics))
                });
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                return CommandResult.Success(new
                {
                    ok = false,
                    result = (object)null,
                    stdout = captured.ToString(),
                    error = ErrorSanitizer.Sanitize($"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}")
                });
            }
            catch (Exception ex)
            {
                return CommandResult.Success(new
                {
                    ok = false,
                    result = (object)null,
                    stdout = captured.ToString(),
                    error = ErrorSanitizer.Sanitize($"{ex.GetType().Name}: {ex.Message}")
                });
            }
            finally
            {
                Console.SetOut(originalOut);
            }
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
