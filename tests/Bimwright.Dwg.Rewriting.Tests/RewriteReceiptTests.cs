using System;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Rewriting;
using Xunit;

namespace Bimwright.Dwg.Rewriting.Tests
{
    public class RewriteReceiptTests
    {
        [Theory]
        [InlineData("DBText")]
        [InlineData("MText")]
        [InlineData("AttributeReference")]
        [InlineData("MLeader")]
        public void Update_receipt_reports_actual_normalized_and_scaled_output_height(string type)
        {
            var database = new Database();
            var entity = Add(database, Text(type, 2));
            var result = Execute(database, entity, RewriteAction.Update);

            Assert.True(result.Ok, result.Error);
            Assert.Equal("Translated", Contents(Read(database, result.NewHandle)));
            Assert.Equal(1.44, Height(Read(database, result.NewHandle)), 10);
            Assert.Equal(1.44, Convert.ToDouble(result.FinalTextHeight), 10);
        }

        [Fact]
        public void Style_only_receipt_reports_anchor_height_instead_of_cluster_median()
        {
            var database = new Database();
            var anchor = Add(database, Text("DBText", 4));
            var first = Add(database, Text("DBText", 2));
            var second = Add(database, Text("DBText", 2));
            var request = Request(anchor, RewriteAction.StyleOnly);
            request.DeleteHandles.AddRange(new[] { first.Handle.ToString(), second.Handle.ToString() });
            var result = RewriteExecutor.Execute(database, request, ObjectId.Null);

            Assert.True(result.Ok, result.Error);
            Assert.Equal(2.88, Height(Read(database, result.NewHandle)), 10);
            Assert.Equal(1.44, Height(first), 10);
            Assert.Equal(1.44, Height(second), 10);
            Assert.False(first.IsErased || second.IsErased);
            Assert.Equal(2.88, Convert.ToDouble(result.FinalTextHeight), 10);
        }

        [Theory]
        [InlineData(false, 1.44)]
        [InlineData(true, 2.4)]
        public void Collapse_receipt_measures_new_MText_after_sizing(bool explicitHeight, double expected)
        {
            var database = new Database();
            var anchor = Add(database, Text("DBText", 2));
            var fragment = Add(database, Text("DBText", 2));
            var request = Request(anchor, RewriteAction.Collapse);
            request.DeleteHandles.Add(fragment.Handle.ToString());
            if (explicitHeight) request.ExplicitTextHeight = 3;
            var result = RewriteExecutor.Execute(database, request, ObjectId.Null);

            Assert.True(result.Ok, result.Error);
            var output = Assert.IsType<MText>(Read(database, result.NewHandle));
            Assert.NotEqual(anchor.Handle.ToString(), result.NewHandle);
            Assert.True(anchor.IsErased && fragment.IsErased);
            Assert.Equal(expected, output.TextHeight, 10);
            Assert.Equal(expected, Convert.ToDouble(result.FinalTextHeight), 10);
        }

        [Fact]
        public void Block_rewrite_receipt_reports_stored_local_height_after_scale_two()
        {
            var database = new Database();
            var definition = database.Add(new BlockTableRecord());
            var anchor = Add(database, Text("DBText", 1), definition);
            var fragment = Add(database, Text("DBText", 1), definition);
            var request = Request(anchor, RewriteAction.RewriteInBlock);
            request.DeleteHandles.Add(fragment.Handle.ToString());
            request.BlockScale = 2;
            var result = RewriteExecutor.Execute(database, request, ObjectId.Null);

            Assert.True(result.Ok, result.Error);
            var output = Assert.IsType<MText>(Read(database, result.NewHandle));
            Assert.Equal(definition, output.OwnerId);
            Assert.True(anchor.IsErased && fragment.IsErased);
            Assert.Equal(0.72, output.TextHeight, 10);
            Assert.Equal(0.72, Convert.ToDouble(result.FinalTextHeight), 10);
        }

        [Fact]
        public void Unavailable_result_read_keeps_successful_write_and_reports_unknown_height()
        {
            var database = new Database();
            var anchor = Add(database, Text("DBText", 2));
            database.FailRepeatedLookupHandle = anchor.Handle.ToString();
            var result = Execute(database, anchor, RewriteAction.Update);

            Assert.True(result.Ok, result.Error);
            Assert.Equal("Translated", Contents(anchor));
            Assert.Equal(1.44, Height(anchor), 10);
            Assert.Null((object)result.FinalTextHeight);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-2.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void Invalid_output_height_is_unknown_without_turning_committed_text_write_into_failure(double height)
        {
            var database = new Database();
            var anchor = Add(database, Text("DBText", height));
            var request = Request(anchor, RewriteAction.Update);
            request.ApplyUnicodeStyle = false;
            var result = RewriteExecutor.Execute(database, request, ObjectId.Null);

            Assert.True(result.Ok, result.Error);
            Assert.Equal("Translated", Contents(anchor));
            Assert.Null((object)result.FinalTextHeight);
        }

        private static RewriteResult Execute(Database database, Entity entity, RewriteAction action)
            => RewriteExecutor.Execute(database, Request(entity, action), ObjectId.Null);
        private static RewriteRequest Request(Entity anchor, RewriteAction action)
            => new RewriteRequest { Action = action, AnchorHandle = anchor.Handle.ToString(), NewText = "Translated",
                ApplyUnicodeStyle = true, MedianHeight = 2, FinalScale = 0.8, MtextWidth = 20 };
        private static Entity Add(Database database, Entity entity, ObjectId owner = default)
        {
            database.Add(entity, owner.IsNull ? database.CurrentSpaceId : owner);
            return entity;
        }
        private static Entity Read(Database database, string handle)
        {
            var id = database.GetObjectId(false, new Handle(long.Parse(handle, NumberStyles.HexNumber)), 0);
            using (var transaction = database.TransactionManager.StartTransaction())
                return (Entity)transaction.GetObject(id, OpenMode.ForRead);
        }
        private static Entity Text(string type, double height)
        {
            switch (type)
            {
                case "DBText": return new DBText { Height = height };
                case "AttributeReference": return new AttributeReference { Height = height };
                case "MText": return new MText { TextHeight = height };
                case "MLeader": return new MLeader { TextHeight = height, MText = new MText { TextHeight = height } };
                default: throw new ArgumentException(type);
            }
        }
        private static double Height(Entity entity) => entity switch
        {
            DBText text => text.Height,
            MText text => text.TextHeight,
            MLeader leader => leader.TextHeight,
            _ => throw new ArgumentException(nameof(entity))
        };
        private static string Contents(Entity entity) => entity switch
        {
            DBText text => text.TextString,
            MText text => text.Contents,
            MLeader leader => leader.MText.Contents,
            _ => throw new ArgumentException(nameof(entity))
        };
    }
}
