using System;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.View;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class CaptureViewContractTests
    {
        private const string Fingerprint = "113ef273-5d10-4a08-a178-924265c70749";

        [Fact]
        public void Fingerprint_binding_accepts_optional_or_equivalent_guid_format()
        {
            CaptureViewContract.ValidateExpectedDocument(null, Fingerprint);
            CaptureViewContract.ValidateExpectedDocument("{" + Fingerprint.ToUpperInvariant() + "}", Fingerprint);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("another drawing.dwg")]
        public void Invalid_document_binding_is_not_silently_ignored(string expected)
        {
            Assert.Throws<ArgumentException>(() => CaptureViewContract.ValidateExpectedDocument(expected, Fingerprint));
        }

        [Fact]
        public void Switched_document_is_rejected()
        {
            Assert.Throws<InvalidOperationException>(() =>
                CaptureViewContract.ValidateExpectedDocument(Guid.NewGuid().ToString(), Fingerprint));
        }

        [Theory]
        [InlineData("document", "fingerprint", "different-document")]
        [InlineData("document", "layout", "Layout1")]
        [InlineData("document", "space_handle", "2A")]
        [InlineData("viewport", "number", 3)]
        [InlineData("viewport", "screen_width", 1280)]
        [InlineData("camera", "height", 23000)]
        [InlineData("camera", "twist_radians", 0.5)]
        [InlineData("camera", "perspective", true)]
        public void Context_changes_reject_an_image_before_publication(string section, string field, object value)
        {
            var before = Context();
            var after = (JObject)before.DeepClone();
            after[section][field] = JToken.FromObject(value);
            Assert.Throws<InvalidOperationException>(() => CaptureViewContract.EnsureUnchanged(before, after));
        }

        [Fact]
        public void Panning_is_detected_even_when_size_is_unchanged()
        {
            var before = Context();
            var after = (JObject)before.DeepClone();
            after["camera"]["center_dcs"][0] = 100;
            Assert.Throws<InvalidOperationException>(() => CaptureViewContract.EnsureUnchanged(before, after));
        }

        [Fact]
        public void Stable_context_can_be_serialized_and_compared_without_native_objects()
        {
            var before = Context();
            CaptureViewContract.EnsureUnchanged(before, JObject.Parse(before.ToString()));
        }

        [Fact]
        public void Result_preserves_actual_bitmap_size_and_camera_without_mutating_context()
        {
            var context = Context();
            var result = CaptureViewContract.BuildResult(context, "capture.png", "png",
                2401, 842, 2400, 842, "image-hash", "2026-09-23T10:00:00Z", 53);

            Assert.Equal(2401, (int)result["width"]); // Observed native rounding: requested != actual.
            Assert.Equal(2400, (int)result["requested_size"]["width"]);
            Assert.Equal(842, (int)result["height"]);
            Assert.Equal(Fingerprint, (string)result["document"]["fingerprint"]);
            Assert.True(JToken.DeepEquals(context["camera"], result["camera"]));
            Assert.Equal("autocad_document_preview", (string)result["source"]);
            Assert.Equal("active_document", (string)result["capture_scope"]);
            Assert.Equal("active_viewport", (string)result["camera_scope"]);
            Assert.Equal("image-hash", (string)result["sha256"]);
            Assert.Equal("2026-09-23T10:00:00Z", (string)result["captured_at"]);
            Assert.True(Guid.TryParse((string)result["capture_id"], out _));
            Assert.Null(context["output_path"]);
            result["camera"]["height"] = 1;
            Assert.Equal(100000, (int)context["camera"]["height"]);
        }

        [Theory]
        [InlineData(0, 842)]
        [InlineData(2400, 0)]
        public void Empty_native_image_is_not_reported_as_success(int width, int height)
        {
            Assert.Throws<InvalidOperationException>(() => CaptureViewContract.BuildResult(
                Context(), "capture.png", "png", width, height, 2400, 842, "hash", "time", 0));
        }

        [Fact]
        public void Capture_schema_accepts_document_binding_and_preserves_existing_arguments()
        {
            var parameters = new JObject
            {
                ["pixel_size"] = 3200, ["expected_document_fingerprint"] = Fingerprint,
                ["output_path"] = "C:\\Temp\\capture.png", ["overwrite_existing"] = false,
                ["allow_repo_output"] = false, ["image_format"] = "png"
            };
            Assert.True(SchemaValidator.Validate("capture_view_image", parameters, CommandSchemas.CaptureViewImage).Ok);
            parameters["expected_document_fingerprint"] = 123;
            Assert.False(SchemaValidator.Validate("capture_view_image", parameters, CommandSchemas.CaptureViewImage).Ok);
        }

        private static JObject Context() => new JObject
        {
            ["document"] = new JObject { ["fingerprint"] = Fingerprint, ["layout"] = "Model", ["space_handle"] = "1F" },
            ["viewport"] = new JObject { ["number"] = 2, ["screen_width"] = 3200, ["screen_height"] = 1122 },
            ["camera"] = new JObject
            {
                ["center_dcs"] = new JArray(0, 0), ["target_wcs"] = new JArray(171000, 30000, -14500),
                ["direction_wcs"] = new JArray(0, 0, 1), ["width"] = 280000, ["height"] = 100000,
                ["twist_radians"] = 0, ["perspective"] = false
            }
        };
    }
}
