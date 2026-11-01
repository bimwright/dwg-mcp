#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;
using System.Threading;

namespace Bimwright.Dwg.Plugin.Scripting
{
    internal sealed class IsolatedScriptRuntime
    {
        private readonly AssemblyLoadContext _context;
        private readonly MethodInfo _validate;
        private readonly MethodInfo _evaluate;
        private readonly MethodInfo _execute;

        internal IsolatedScriptRuntime(string directory)
        {
            _context = new ScriptLoadContext(directory);
            var engine = _context.LoadFromAssemblyPath(Path.Combine(directory, "Bimwright.Dwg.Scripting.dll"));
            var type = engine.GetType("Bimwright.Dwg.Scripting.ScriptEngine", throwOnError: true);
            _validate = type.GetMethod("Validate");
            _evaluate = type.GetMethod("Evaluate");
            _execute = type.GetMethod("Execute");
        }

        internal string Validate(string code) => (string)Invoke(_validate, new object[] { code });

        internal (object Value, string Error) Evaluate(
            string code, object globals, Assembly[] references, string[] imports, CancellationToken token)
            => ((object, string))Invoke(_evaluate, new object[] { code, globals, references, imports, token });

        internal Dictionary<string, object> Execute(string code, object globals, Assembly[] references, string[] imports, bool compileOnly, CancellationToken token)
            => (Dictionary<string, object>)Invoke(_execute, new object[] { code, globals, references, imports, compileOnly, token });

        private object Invoke(MethodInfo method, object[] arguments)
        {
            using (_context.EnterContextualReflection())
            {
                try { return method.Invoke(null, arguments); }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                    throw;
                }
            }
        }

        private sealed class ScriptLoadContext : AssemblyLoadContext
        {
            private readonly string _directory;
            internal ScriptLoadContext(string directory) : base("dwg-mcp-scripting", isCollectible: false)
                => _directory = directory;

            protected override Assembly Load(AssemblyName name)
            {
                // Compiler dependencies are one private set, even if the host resolved an older set first.
                if (name.Name == "Microsoft.CodeAnalysis" || name.Name == "Microsoft.CodeAnalysis.CSharp" ||
                    name.Name == "Microsoft.CodeAnalysis.Scripting" || name.Name == "Microsoft.CodeAnalysis.CSharp.Scripting")
                    return LoadFromAssemblyPath(Path.Combine(_directory, name.Name + ".dll"));

                // Keep Autodesk, globals, framework and other host types on their existing assembly instances.
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    if (GetLoadContext(assembly) != this && AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), name))
                        return assembly;
                return null;
            }
        }
    }
}
#endif
