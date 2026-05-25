using System;
using System.IO;
using System.Linq;
using System.Threading;
using Bimwright.Dwg.Plugin;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.Handlers
{
    public class SendCodeHandler : IAcadCommand
    {
        private const int ExecutionTimeoutMilliseconds = 30000;
        private const int AbortGraceMilliseconds = 5000;

        public string Name => "send_code";
        public string Description => "Execute opt-in C# code against the AutoCAD API.";
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

                Exception executionError = null;
                using (var cts = new CancellationTokenSource())
                using (var completed = new ManualResetEventSlim(false))
                {
                    var worker = new Thread(() =>
                    {
                        try
                        {
                            CSharpScript.EvaluateAsync(code, options, globals, cancellationToken: cts.Token)
                                .GetAwaiter()
                                .GetResult();
                        }
                        catch (Exception ex)
                        {
                            executionError = ex;
                        }
                        finally
                        {
                            completed.Set();
                        }
                    })
                    {
                        IsBackground = true,
                        Name = "Bimwright.Dwg.SendCode"
                    };

                    worker.Start();

                    if (!completed.Wait(ExecutionTimeoutMilliseconds))
                    {
                        cts.Cancel();
                        try
                        {
                            worker.Abort();
                        }
                        catch (ThreadStateException)
                        {
                        }
                        catch (PlatformNotSupportedException)
                        {
                        }

                        if (!completed.Wait(AbortGraceMilliseconds))
                            return CommandResult.Fail("execution timeout after 30s; script did not stop");

                        return CommandResult.Fail("execution cancelled after 30s");
                    }

                    if (executionError != null)
                        throw executionError;
                }

                return CommandResult.Success(new
                {
                    ok = true,
                    stdout = captured.ToString(),
                    error = (string)null
                });
            }
            catch (CompilationErrorException ex)
            {
                return CommandResult.Success(new
                {
                    ok = false,
                    stdout = captured.ToString(),
                    error = "compile error: " + string.Join("\n", ex.Diagnostics)
                });
            }
            catch (OperationCanceledException)
            {
                return CommandResult.Fail("execution cancelled after 30s");
            }
            catch (AggregateException ex) when (ex.InnerException != null)
            {
                return CommandResult.Success(new
                {
                    ok = false,
                    stdout = captured.ToString(),
                    error = $"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}"
                });
            }
            catch (Exception ex)
            {
                return CommandResult.Success(new
                {
                    ok = false,
                    stdout = captured.ToString(),
                    error = $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"
                });
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }
    }
}
