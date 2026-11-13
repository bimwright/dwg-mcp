using System;
using System.IO;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Handlers;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

public class DocumentChangeCaptureTests
{
    [Fact]
    public void Non_atomic_batch_keeps_request_indices_after_validation_rejects_a_step()
    {
        var path = Path.GetTempFileName();
        try
        {
            var document = new Document
            {
                Name = path,
                IsNamedDrawing = true,
                Database = new Database { FingerprintGuid = Guid.NewGuid().ToString() }
            };
            var handler = new BatchExecuteHandler((_, command, _) =>
                command == "invalid_command" ? CommandResult.Fail("unknown command")
                : DocumentChangeCapture.ExecuteHandler(command, () =>
                {
                    DocumentChangeCapture.Erased("1A", new JObject { ["type"] = "Line" });
                    return CommandResult.Success(null);
                }));
            var request = JObject.Parse("{commands:[{cmd:'erase_entities'},{cmd:'invalid_command'},{cmd:'erase_entities'}]}");
            var capture = new DocumentChangeCapture();
            var batch = capture.Run(document, "batch_execute", () => handler.Execute(document, request));

            Assert.True(batch.Ok);
            Assert.Equal(new[] { 0, 2 }, capture.Result["deltas"].Select(delta => delta.Value<int>("step_index")));
        }
        finally { File.Delete(path); }
    }
}
