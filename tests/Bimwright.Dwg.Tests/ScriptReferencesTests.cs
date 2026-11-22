using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Threading;
using Bimwright.Dwg.Scripting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Bimwright.Dwg.Tests;

public sealed class ScriptReferenceGlobalsFixture { }

// rvt-mcp #38: a loaded DLL whose metadata Roslyn cannot read (the CLR still loads it) must not break every send_code compile.
public sealed class ScriptReferencesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dwgmcp-refs-" + Guid.NewGuid().ToString("N"));

    public ScriptReferencesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        try { Directory.Delete(_dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // ECMA-335 requires #Blob[0] == 0x00. A non-zero byte breaks System.Reflection.Metadata ("Read out of bounds"),
    // while the CLR treats the nil public key of an unsigned library as empty and still loads the file.
    private string WriteTamperedLibrary(string name)
    {
        var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var compilation = CSharpCompilation.Create(name,
            new[] { CSharpSyntaxTree.ParseText("namespace " + name + " { public class Gate { } }") },
            platform.Select(p => MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(_dir, name + ".dll");
        var emitted = compilation.Emit(path);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        var bytes = File.ReadAllBytes(path);
        int offset;
        using (var pe = new PEReader(new MemoryStream(bytes)))
            offset = pe.PEHeaders.MetadataStartOffset + pe.GetMetadataReader().GetHeapMetadataOffset(HeapIndex.Blob);
        Assert.Equal(0, bytes[offset]);
        bytes[offset] = 0xCE;
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Send_code_skips_a_loaded_dll_with_unreadable_metadata_and_lists_it()
    {
        var bad = new AssemblyLoadContext("issue38", isCollectible: true).LoadFromAssemblyPath(WriteTamperedLibrary("DwgIssue38Tampered"));
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Append(bad).ToArray();

        var result = ScriptEngine.Execute("1 + 1", new ScriptReferenceGlobalsFixture(), assemblies, new[] { "System" }, false, CancellationToken.None);

        Assert.True((bool)result["ok"], (string)result["error"]);
        Assert.Equal(2, result["result"]);
        var skipped = Assert.IsType<Dictionary<string, object>[]>(result["skipped_references"]);
        Assert.Contains(skipped, s => (string)s["name"] == "DwgIssue38Tampered");
    }

    [Fact]
    public void Send_code_without_unreadable_dlls_reports_no_skipped_references()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)).ToArray();

        var result = ScriptEngine.Execute("1 + 1", new ScriptReferenceGlobalsFixture(), assemblies, new[] { "System" }, false, CancellationToken.None);

        Assert.True((bool)result["ok"], (string)result["error"]);
        Assert.False(result.ContainsKey("skipped_references"));
    }
}
