using System;
using System.IO;
using System.Linq;
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
        public string Name => "send_code";

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

                var task = CSharpScript.EvaluateAsync(code, options, globals);
                if (!task.Wait(TimeSpan.FromSeconds(30)))
                    return CommandResult.Fail("execution timeout (30s)");

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
