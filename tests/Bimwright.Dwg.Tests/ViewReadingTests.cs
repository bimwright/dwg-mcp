using System;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.View;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ViewReadingTests
    {
        internal static JObject Context() => JObject.Parse(@"{
          'document': {'name':'test.dwg','fingerprint':'113ef273-5d10-4a08-a178-924265c70749',
            'session_id':'process-document-1','observed_revision':0,'layout':'Model','space_handle':'1F','tile_mode':true},
          'viewport': {'number':2,'active_tiled_viewports':1,'screen_width':1600,'screen_height':800},
          'camera': {'center_dcs':[100,200],'target_wcs':[0,0,0],'direction_wcs':[0,0,1],
            'width':2000.0,'height':1000.0,'twist_radians':0.0,'perspective':false,'lens_length':50.0,
            'front_clip_enabled':false,'back_clip_enabled':false,'front_clip_distance':0.0,'back_clip_distance':0.0}
        }");

        internal static JObject Capture(JObject context = null) => CaptureViewContract.BuildResult(
            context ?? Context(), "capture.png", "png", 1600, 800, 1600, 800, "hash", "time", 1);

        private static JObject Region() => JObject.Parse("{'left':0.6,'top':0.1,'right':0.8,'bottom':0.3}");

        [Fact]
        public void Image_rectangle_maps_top_right_to_positive_DCS_axes_and_fits_with_margin()
        {
            var camera = ViewReadingContract.RegionCamera(Capture(), Region());
            Assert.Equal(500, (double)camera["center_dcs"][0], 8);
            Assert.Equal(500, (double)camera["center_dcs"][1], 8);
            Assert.Equal(440, (double)camera["width"], 8);
            Assert.Equal(220, (double)camera["height"], 8);
        }

        [Fact]
        public void Tall_region_is_fully_fitted_without_distorting_viewport()
        {
            var camera = ViewReadingContract.RegionCamera(Capture(),
                JObject.Parse("{'left':0.4,'top':0.1,'right':0.6,'bottom':0.9}"));
            Assert.Equal(880, (double)camera["height"], 8);
            Assert.Equal(1760, (double)camera["width"], 8);
        }

        [Theory]
        [InlineData("{'left':-0.1,'top':0,'right':1,'bottom':1}")]
        [InlineData("{'left':0,'top':0,'right':1.1,'bottom':1}")]
        [InlineData("{'left':0.8,'top':0,'right':0.2,'bottom':1}")]
        [InlineData("{'left':0,'top':0.5,'right':1,'bottom':0.5}")]
        [InlineData("{'left':0,'top':0,'right':0.0001,'bottom':1}")]
        [InlineData("{'left':'0','top':0,'right':1,'bottom':1}")]
        [InlineData("{'left':0,'top':0,'right':1}")]
        [InlineData("{}")]
        public void Invalid_regions_fail_before_navigation(string json)
        {
            Assert.Throws<ArgumentException>(() => ViewReadingContract.RegionCamera(Capture(), JObject.Parse(json)));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void Nonfinite_coordinates_are_refused(double value)
        {
            var region = Region();
            region["left"] = value;
            Assert.Throws<ArgumentException>(() => ViewReadingContract.RegionCamera(Capture(), region));
        }

        [Theory]
        [InlineData("document", "tile_mode", false)]
        [InlineData("viewport", "active_tiled_viewports", 2)]
        [InlineData("camera", "perspective", true)]
        [InlineData("camera", "twist_radians", 0.5)]
        [InlineData("camera", "front_clip_enabled", true)]
        [InlineData("camera", "back_clip_enabled", true)]
        [InlineData("camera", "width", 1500)]
        public void Unsupported_views_remain_capturable_but_not_navigable(string section, string key, object value)
        {
            var context = Context();
            context[section][key] = JToken.FromObject(value);
            var capture = Capture(context);
            Assert.False((bool)capture["region_navigation"]["supported"]);
            Assert.NotEmpty((string)capture["region_navigation"]["reason"]);
            Assert.Throws<InvalidOperationException>(() => ViewReadingContract.ValidateSource(capture, context, false));
        }

        [Fact]
        public void Oblique_and_bottom_views_are_refused()
        {
            foreach (var direction in new[] { new JArray(1, 0, 1), new JArray(0, 0, -1) })
            {
                var context = Context();
                context["camera"]["direction_wcs"] = direction;
                Assert.NotNull(ViewReadingContract.UnsupportedReason(context));
            }
        }

        [Fact]
        public void Native_image_aspect_mismatch_disables_mapping()
        {
            var result = CaptureViewContract.BuildResult(Context(), "image.png", "png", 800, 800, 1600, 800, "hash", "time", 0);
            Assert.False((bool)result["region_navigation"]["supported"]);
        }

        [Fact]
        public void Screensize_underreport_quirk_no_longer_blocks_navigation()
        {
            // Observed live: SCREENSIZE reports 2560x817 while the real drawable canvas
            // is ~2562.2 px wide, so camera aspect (3.1360) exceeds the old ±2 px check.
            var context = Context();
            context["viewport"]["screen_width"] = 2560;
            context["viewport"]["screen_height"] = 817;
            context["camera"]["width"] = 3136.029411764706;
            context["camera"]["height"] = 1000.0;
            var capture = CaptureViewContract.BuildResult(context, "c.png", "png", 1601, 511, 1601, 511, "h", "t", 0);
            Assert.True((bool)capture["region_navigation"]["supported"]);
        }

        [Fact]
        public void Camera_aspect_beyond_scaled_tolerance_is_still_refused()
        {
            var context = Context();
            context["viewport"]["screen_width"] = 2560;
            context["viewport"]["screen_height"] = 817;
            context["camera"]["width"] = 3200.0;
            context["camera"]["height"] = 1000.0;
            var capture = CaptureViewContract.BuildResult(context, "c.png", "png", 1635, 511, 1635, 511, "h", "t", 0);
            Assert.False((bool)capture["region_navigation"]["supported"]);
            Assert.Equal("Camera and viewport aspect ratios do not agree.", (string)capture["region_navigation"]["reason"]);
        }

        [Fact]
        public void Sub_percent_native_image_drift_is_tolerated_but_real_mismatch_is_not()
        {
            var context = Context();
            context["viewport"]["screen_width"] = 2560;
            context["viewport"]["screen_height"] = 817;
            context["camera"]["width"] = 3136.029411764706;
            context["camera"]["height"] = 1000.0;
            var ok = CaptureViewContract.BuildResult(context, "c.png", "png", 1601, 511, 1601, 511, "h", "t", 0);
            Assert.True((bool)ok["region_navigation"]["supported"]);
            var bad = CaptureViewContract.BuildResult(context, "c.png", "png", 1590, 511, 1601, 511, "h", "t", 0);
            Assert.False((bool)bad["region_navigation"]["supported"]);
            Assert.Equal("Native image and camera aspect ratios do not agree.", (string)bad["region_navigation"]["reason"]);
        }

        [Theory]
        [InlineData("document", "session_id", "another-open-copy")]
        [InlineData("document", "observed_revision", 1)]
        [InlineData("document", "fingerprint", "another-dwg")]
        [InlineData("document", "layout", "Layout1")]
        [InlineData("viewport", "number", 3)]
        [InlineData("viewport", "screen_width", 1700)]
        public void Stale_sources_fail_before_applying_or_capturing_even_during_restore(string section, string key, object value)
        {
            var current = Context();
            current[section][key] = JToken.FromObject(value);
            foreach (bool restore in new[] { false, true })
            {
                int calls = 0;
                Assert.Throws<InvalidOperationException>(() => ViewReadingWorkflow.Run(Capture(), Region(), restore,
                    () => current, camera => calls++, applied => { calls++; return Capture(); }));
                Assert.Equal(0, calls);
            }
        }

        [Fact]
        public void Changed_camera_requires_fresh_capture_or_explicit_restore()
        {
            var current = Context();
            current["camera"]["center_dcs"] = new JArray(999, 999);
            Assert.Throws<InvalidOperationException>(() => ViewReadingContract.ValidateSource(Capture(), current, false));
            ViewReadingContract.ValidateSource(Capture(), current, true);
        }

        [Fact]
        public void Two_level_navigation_and_restore_keep_lineage_and_fresh_ids()
        {
            var root = Capture();
            var current = Context();
            JObject Navigate(JObject source, bool restoring) => ViewReadingWorkflow.Run(source, Region(), restoring,
                () => (JObject)current.DeepClone(), camera => current["camera"] = camera.DeepClone(),
                applied => { Assert.True(JToken.DeepEquals(applied, current)); return Capture(current); });
            var child = Navigate(root, false);
            var grandchild = Navigate(child, false);
            Assert.Equal((string)root["capture_id"], (string)child["parent_capture_id"]);
            Assert.Equal((string)child["capture_id"], (string)grandchild["parent_capture_id"]);
            Assert.True(JToken.DeepEquals(Region(), grandchild["source_region"]));
            var restored = Navigate(root, true);
            Assert.True(JToken.DeepEquals(root["camera"], restored["camera"]));
            Assert.Equal((string)root["capture_id"], (string)restored["restored_from_capture_id"]);
            Assert.NotEqual((string)root["capture_id"], (string)restored["capture_id"]);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Failed_or_ignored_zoom_never_captures_and_restores_original_view(bool throwOnZoom)
        {
            var current = Context();
            int applies = 0, captures = 0;
            var failure = Assert.Throws<InvalidOperationException>(() => ViewReadingWorkflow.Run(Capture(), Region(), false,
                () => current,
                camera =>
                {
                    applies++;
                    if (applies == 1)
                    {
                        if (throwOnZoom) throw new InvalidOperationException("native zoom failed");
                    }
                    else current["camera"] = camera.DeepClone();
                }, applied => { captures++; return Capture(current); }));
            Assert.Equal(0, captures);
            Assert.Equal(2, applies);
            Assert.Contains("Previous view restored", failure.Message);
        }

        [Fact]
        public void Capture_failure_restores_original_camera()
        {
            var current = Context();
            Assert.Throws<InvalidOperationException>(() => ViewReadingWorkflow.Run(Capture(), Region(), false,
                () => current, camera => current["camera"] = camera.DeepClone(),
                applied => throw new InvalidOperationException("native capture failed")));
            Assert.True(JToken.DeepEquals(Context()["camera"], current["camera"]));
        }

        [Fact]
        public void Context_switch_during_zoom_skips_capture_and_does_not_restore_into_other_drawing()
        {
            var current = Context();
            int applies = 0, captures = 0;
            var failure = Assert.Throws<InvalidOperationException>(() => ViewReadingWorkflow.Run(Capture(), Region(), false,
                () => current,
                camera => { applies++; current["document"]["session_id"] = "other"; },
                applied => { captures++; return Capture(); }));
            Assert.Equal(1, applies);
            Assert.Equal(0, captures);
            Assert.Contains("restoration skipped", failure.Message);
        }

        [Fact]
        public void History_is_bounded_expiring_and_defensively_copied()
        {
            var time = DateTimeOffset.UtcNow;
            var store = new ViewCaptureStore(2, () => time);
            var root = Capture(); var child = Capture(); var next = Capture();
            string rootId = (string)root["capture_id"], childId = (string)child["capture_id"];
            store.Remember(root);
            root["camera"]["width"] = 1;
            Assert.Equal(2000, (double)store.Get(rootId)["camera"]["width"]);
            var fetched = store.Get(rootId); fetched["camera"]["width"] = 2;
            Assert.Equal(2000, (double)store.Get(rootId)["camera"]["width"]);
            store.Remember(child); store.Remember(next);
            Assert.Throws<InvalidOperationException>(() => store.Get(rootId));
            Assert.NotNull(store.Get(childId));
            time = time.AddMinutes(30);
            Assert.Throws<InvalidOperationException>(() => store.Get(childId));
        }

        [Fact]
        public void Navigation_wire_schema_requires_source_and_region()
        {
            var request = new JObject { ["source_capture_id"] = "id", ["region"] = Region() };
            Assert.True(SchemaValidator.Validate("inspect_view_region", request, CommandSchemas.InspectViewRegion).Ok);
            request.Remove("region");
            Assert.False(SchemaValidator.Validate("inspect_view_region", request, CommandSchemas.InspectViewRegion).Ok);
            Assert.True(SchemaValidator.Validate("restore_view", request, CommandSchemas.RestoreView).Ok);
            request["source_capture_id"] = " ";
            Assert.False(SchemaValidator.Validate("restore_view", request, CommandSchemas.RestoreView).Ok);
        }
    }
}
