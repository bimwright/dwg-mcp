using System;
using Bimwright.Dwg.Plugin;
using Bimwright.Dwg.Plugin.Cad;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    public class ImageAttachInputTests
    {
        [Theory]
        [InlineData(@"C:\Projects\site\base_map.png")]
        [InlineData(@"d:\scans\sketch.JPG")]
        [InlineData(@"C:/images/aerial.tiff")]
        [InlineData(@"C:\images\logo.bmp")]
        [InlineData(@"C:\images\photo.jpeg")]
        [InlineData(@"C:\images\scan.tif")]
        public void Accepts_fully_qualified_local_paths_with_allowed_extensions(string path)
        {
            Assert.True(ImageAttachInput.TryValidatePath(path, out var error), error);
        }

        [Theory]
        [InlineData(null, "required")]
        [InlineData("  ", "required")]
        [InlineData("https://example.com/map.png", "URL")]
        [InlineData("file://C:/map.png", "URL")]
        [InlineData(@"\\server\share\map.png", "UNC")]
        [InlineData(@"\\?\C:\map.png", "UNC")]
        [InlineData("//server/share/map.png", "UNC")]
        [InlineData(@"images\map.png", "fully qualified")]
        [InlineData(@"C:map.png", "fully qualified")]
        [InlineData(@"\map.png", "fully qualified")]
        [InlineData(@"1:\map.png", "fully qualified")]
        [InlineData(@"C:\images\*.png", "not allowed")]
        [InlineData(@"C:\images\map.png:stream", "not allowed")]
        [InlineData(@"C:\images\map.pdf", "unsupported image type")]
        [InlineData(@"C:\images\map", "unsupported image type")]
        public void Refuses_other_paths(string path, string expected)
        {
            Assert.False(ImageAttachInput.TryValidatePath(path, out var error));
            Assert.Contains(expected, error);
        }

        [Theory]
        [InlineData(0d, null, 0d, "width")]
        [InlineData(-1d, null, 0d, "width")]
        [InlineData(double.PositiveInfinity, null, 0d, "width")]
        [InlineData(double.NaN, null, 0d, "width")]
        [InlineData(10d, 0d, 0d, "height")]
        [InlineData(10d, -2d, 0d, "height")]
        [InlineData(10d, null, double.NaN, "rotation_degrees")]
        [InlineData(10d, null, double.NegativeInfinity, "rotation_degrees")]
        public void Refuses_invalid_sizes(double width, double? height, double rotation, string field)
        {
            Assert.False(ImageAttachInput.TryValidateSize(width, height, rotation, out var error));
            Assert.Contains(field, error);
        }

        [Fact]
        public void Height_follows_pixel_aspect_ratio_unless_given()
        {
            Assert.Equal(141.75, ImageAttachInput.ResolveHeight(283.5, null, 2000, 1000), 9);
            Assert.Equal(50d, ImageAttachInput.ResolveHeight(283.5, 50d, 2000, 1000), 9);
            Assert.Throws<ArgumentException>(() => ImageAttachInput.ResolveHeight(10, null, 0, 100));
        }

        [Theory]
        [InlineData(0d, 30d, 0d, 0d, 20d)]
        [InlineData(90d, 0d, 30d, -20d, 0d)]
        [InlineData(180d, -30d, 0d, 0d, -20d)]
        public void Orientation_rotates_width_and_height_vectors_counter_clockwise(double degrees, double ux, double uy, double vx, double vy)
        {
            ImageAttachInput.Orientation(30d, 20d, degrees, out var aUx, out var aUy, out var aVx, out var aVy);
            Assert.Equal(ux, aUx, 9); Assert.Equal(uy, aUy, 9);
            Assert.Equal(vx, aVx, 9); Assert.Equal(vy, aVy, 9);
        }

        [Fact]
        public void Schema_requires_path_insertion_and_width()
        {
            foreach (var missing in new[] { "path", "insertion", "width" })
            {
                var parameters = JObject.Parse("{\"path\":\"C:\\\\a.png\",\"insertion\":{\"x\":0,\"y\":0},\"width\":10}");
                parameters.Remove(missing);
                var result = SchemaValidator.Validate("attach_image", parameters, CommandSchemas.AttachImage);
                Assert.False(result.Ok);
                Assert.Contains(missing, result.Error);
            }
            var ok = SchemaValidator.Validate("attach_image",
                JObject.Parse("{\"path\":\"C:\\\\a.png\",\"insertion\":{\"x\":0,\"y\":0},\"width\":10,\"height\":5.5,\"rotation_degrees\":15,\"layer\":\"BASE\",\"send_to_back\":true}"),
                CommandSchemas.AttachImage);
            Assert.True(ok.Ok, ok.Error);
            var badFlag = SchemaValidator.Validate("attach_image",
                JObject.Parse("{\"path\":\"C:\\\\a.png\",\"insertion\":{\"x\":0,\"y\":0},\"width\":10,\"send_to_back\":\"yes\"}"),
                CommandSchemas.AttachImage);
            Assert.False(badFlag.Ok);
        }
    }
}
