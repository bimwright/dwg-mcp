using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Bimwright.Dwg.Plugin.Scripting;

try
{
    foreach (var name in new[] { "Microsoft.CodeAnalysis", "Microsoft.CodeAnalysis.CSharp" })
        AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "host", name + ".dll"));
    var runtime = new IsolatedScriptRuntime(Path.Combine(AppContext.BaseDirectory, "engine"));
    var globals = new ProbeGlobals { Counter = 40, Marker = "shared-host" };
    var imports = new[] { "System", "System.Linq" };
    (object Value, string Error) Run(string code) => runtime.Evaluate(code, globals, AppDomain.CurrentDomain.GetAssemblies(), imports, CancellationToken.None);
    if (runtime.Validate("1+1") != null) throw new Exception("Simple code refused.");
    var math = Run("1+1");
    var shared = Run("Counter += 2; return new { value = Counter, marker = Marker };");
    var thread = Run("Environment.CurrentManagedThreadId");
    var compile = Run("MissingScriptSymbol");
    var runtimeError = "";
    try { Run("throw new InvalidOperationException(\"script-failure\");"); }
    catch (InvalidOperationException ex) { runtimeError = ex.Message; }
    var data = new {
        math = math.Value, error = math.Error, globalsCounter = globals.Counter,
        shared = JsonSerializer.SerializeToElement(shared.Value),
        sameThread = (int)thread.Value == Environment.CurrentManagedThreadId,
        compileError = compile.Error, runtimeError,
        asyncGuard = runtime.Validate("await System.Threading.Tasks.Task.Delay(1);") != null,
        loadGuard = runtime.Validate("#load \"external.csx\"") != null,
        hostCore = AssemblyLoadContext.Default.Assemblies.Single(a => a.GetName().Name == "Microsoft.CodeAnalysis").GetName().Version.ToString(),
        engineCore = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Microsoft.CodeAnalysis" && AssemblyLoadContext.GetLoadContext(a)?.Name == "dwg-mcp-scripting").GetName().Version.ToString()
    };
    Console.WriteLine(JsonSerializer.Serialize(data));
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }

public sealed class ProbeGlobals
{
    public int Counter;
    public string Marker;
}
