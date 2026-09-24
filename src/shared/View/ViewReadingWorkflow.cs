using System;
using Newtonsoft.Json.Linq;

namespace Bimwright.Dwg.Plugin.View
{
    internal static class ViewReadingWorkflow
    {
        internal static JObject Run(JObject source, JObject region, bool restoring,
            Func<JObject> readContext, Action<JObject> applyCamera, Func<JObject, JObject> capture)
        {
            var before = (JObject)readContext().DeepClone();
            ViewReadingContract.ValidateSource(source, before, restoring);
            var desired = restoring ? (JObject)source["camera"].DeepClone()
                : ViewReadingContract.RegionCamera(source, region);
            try
            {
                applyCamera(desired);
                var applied = readContext();
                ViewReadingContract.ValidateApplied(before, applied, desired);
                var result = capture(applied);
                result[restoring ? "restored_from_capture_id" : "parent_capture_id"] = (string)source["capture_id"];
                if (!restoring) result["source_region"] = region.DeepClone();
                return result;
            }
            catch (Exception failure)
            {
                // Restore only while still on the same drawing/viewport. Never switch
                // documents or undo another actor's drawing changes to recover a view.
                try
                {
                    var current = readContext();
                    if (!JToken.DeepEquals(before["document"], current["document"]) ||
                        !JToken.DeepEquals(before["viewport"], current["viewport"]))
                        throw new InvalidOperationException("Drawing context changed; view restoration skipped.");
                    applyCamera((JObject)before["camera"]);
                    ViewReadingContract.ValidateApplied(before, readContext(), (JObject)before["camera"]);
                }
                catch (Exception restoreFailure)
                {
                    throw new InvalidOperationException(failure.Message + " View restoration failed: " + restoreFailure.Message);
                }
                throw new InvalidOperationException(failure.Message + " Previous view restored.", failure);
            }
        }
    }
}
