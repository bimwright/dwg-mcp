using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Bimwright.Dwg.Plugin;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    [Collection("Script handlers")]
    public class DocumentTargetGuardTests
    {
        [Fact]
        public void Reopening_same_file_has_a_different_document_lifetime()
        {
            var first = new Document { Name = "same.dwg" };
            var reopened = new Document { Name = "same.dwg" };
            Assert.Equal(DocumentTargetGuard.Id(first), DocumentTargetGuard.Id(first));
            Assert.NotEqual(DocumentTargetGuard.Id(first), DocumentTargetGuard.Id(reopened));
        }

        [Fact]
        public void Host_rejects_old_gateway_writes_and_wrong_process_identity()
        {
            DocumentTargetGuard.CheckInstance(null, "get_drawing_info");
            Assert.Contains("PAIRING_REQUIRED", Assert.Throws<InvalidOperationException>(() => DocumentTargetGuard.CheckInstance(null, "send_code")).Message);
            var expected = DocumentTargetGuard.Capture(null);
            expected["selection_confirmed"] = true;
            DocumentTargetGuard.CheckInstance(expected, "send_code");
            expected["process_start_utc"] = DateTime.UtcNow.AddDays(-10);
            Assert.Contains("TARGET_CHANGED", Assert.Throws<InvalidOperationException>(() => DocumentTargetGuard.CheckInstance(expected, "send_code")).Message);
            expected = DocumentTargetGuard.Capture(null);
            expected["pid"] = -1;
            Assert.Throws<InvalidOperationException>(() => DocumentTargetGuard.CheckInstance(expected, "get_drawing_info"));
        }

        [Fact]
        public void Document_identity_is_checked_on_UI_execution_after_queueing()
        {
            var previous = Application.DocumentManager;
            var first = new Document { Name = "first.dwg" };
            var second = new Document { Name = "second.dwg" };
            var expected = DocumentTargetGuard.Capture(first);
            // Use the production invoker's UI queue, not just a direct guard call.
            Application.DocumentManager = new DocumentCollection { MdiActiveDocument = first };
            DocumentInvoker.Initialize();
            using var started = new ManualResetEventSlim();
            try
            {
                var pending = Task.Run(() =>
                {
                    started.Set();
                    return DocumentInvoker.Invoke(doc => { doc.MutationCount++; return true; }, expectedTarget: expected, command: "send_code");
                });
                Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
                Application.DocumentManager.MdiActiveDocument = second;
                // Pump AutoCAD's Idle event until the queued command completes.
                Assert.True(SpinWait.SpinUntil(() => { Application.RaiseIdle(); return pending.IsCompleted; }, TimeSpan.FromSeconds(3)));
                var error = Assert.IsType<InvalidOperationException>(Assert.Single(pending.Exception.InnerExceptions));
                Assert.Contains("DOCUMENT_CHANGED", error.Message);
                Assert.Equal(0, first.MutationCount);
                Assert.Equal(0, second.MutationCount);
            }
            finally { DocumentInvoker.Shutdown(); Application.DocumentManager = previous; }
        }
    }
}
