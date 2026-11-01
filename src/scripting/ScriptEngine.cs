using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Bimwright.Dwg.Scripting
{
    // The boundary uses only shared runtime types. Autodesk globals stay in the host assembly.
    public static class ScriptEngine
    {
        public static string Validate(string code)
        {
            var syntax = CSharpSyntaxTree.ParseText(code, CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));
            if (syntax.GetRoot().DescendantNodes(descendIntoTrivia: true).Any(n => n.IsKind(SyntaxKind.LoadDirectiveTrivia)))
                return "send_code does not support #load; provide the complete synchronous snippet inline.";
            if (syntax.GetRoot().DescendantTokens().Any(t => t.IsKind(SyntaxKind.AwaitKeyword) || t.IsKind(SyntaxKind.AsyncKeyword)))
                return "send_code requires synchronous code; async/await is not supported. Keep AutoCAD API calls on the calling thread.";
            return null;
        }

        public static (object Value, string Error) Evaluate(
            string code, object globals, Assembly[] assemblies, string[] imports, CancellationToken cancellationToken)
        {
            var options = Options(globals, assemblies, imports);
            try
            {
                return (CSharpScript.EvaluateAsync(code, options, globals, globals.GetType(), cancellationToken)
                    .GetAwaiter().GetResult(), null);
            }
            catch (CompilationErrorException ex)
            {
                return (null, "compile error: " + string.Join("\n", ex.Diagnostics));
            }
        }

        public static Dictionary<string, object> Execute(
            string code, object globals, Assembly[] assemblies, string[] imports, bool compileOnly, CancellationToken token)
        {
            var options = Options(globals, assemblies, imports).WithFilePath("script")
                .WithEmitDebugInformation(true).WithFileEncoding(System.Text.Encoding.UTF8);
            var script = CSharpScript.Create(code, options, globals.GetType());
            var errors = script.Compile(token).Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            var diagnostics = errors.Take(20).Select(d => {
                var span = d.Location.GetMappedLineSpan();
                return new Dictionary<string, object> {
                    ["source"] = "script", ["line"] = span.StartLinePosition.Line + 1,
                    ["column"] = span.StartLinePosition.Character + 1, ["code"] = d.Id,
                    ["message"] = d.GetMessage(), ["severity"] = "error"
                };
            }).ToArray();
            var result = new Dictionary<string, object> {
                ["ok"] = errors.Length == 0, ["result"] = null,
                ["error"] = errors.Length == 0 ? null : "compile error: " + string.Join("\n", errors.Select(d => d.ToString())),
                ["compiled"] = errors.Length == 0, ["compile_only"] = compileOnly, ["executed"] = false,
                ["diagnostics"] = diagnostics, ["location"] = null
            };
            if (errors.Length != 0 || compileOnly) return result;
            result["executed"] = true;
            try { result["result"] = script.RunAsync(globals, token).GetAwaiter().GetResult().ReturnValue; }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                var inner = ex.GetBaseException();
                result["ok"] = false;
                result["error"] = inner.GetType().Name + ": " + inner.Message;
                var frame = new System.Diagnostics.StackTrace(inner, true).GetFrames()?
                    .FirstOrDefault(f => f.GetFileLineNumber() > 0 && !string.IsNullOrEmpty(f.GetFileName()));
                if (frame != null)
                    result["location"] = new Dictionary<string, object> {
                        ["source"] = "script", ["line"] = frame.GetFileLineNumber(),
                        ["column"] = frame.GetFileColumnNumber() > 0 ? (object)frame.GetFileColumnNumber() : null
                    };
            }
            return result;
        }


        private static ScriptOptions Options(object globals, Assembly[] assemblies, string[] imports)
        {
            var compilerAssemblies = new[] {
                typeof(Compilation).Assembly, typeof(CSharpCompilation).Assembly,
                typeof(ScriptOptions).Assembly, typeof(CSharpScript).Assembly
            };
            var compilerNames = new HashSet<string>(compilerAssemblies.Select(a => a.GetName().Name), StringComparer.OrdinalIgnoreCase);
            var references = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in assemblies)
            {
                if (assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location)) continue;
                var name = assembly.GetName().Name;
                if (!compilerNames.Contains(name) && !references.ContainsKey(name)) references.Add(name, assembly);
            }
            foreach (var assembly in compilerAssemblies) references[assembly.GetName().Name] = assembly;
            references[globals.GetType().Assembly.GetName().Name] = globals.GetType().Assembly;
            return ScriptOptions.Default.WithSourceResolver(null)
                .WithReferences(references.Values).WithImports(imports);
        }
    }
}
