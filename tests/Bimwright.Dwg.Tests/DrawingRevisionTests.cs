using System;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.View;
using Xunit;

namespace Bimwright.Dwg.Tests
{
    [Collection("Drawing revision")]
    public class DrawingRevisionTests : IDisposable
    {
        public DrawingRevisionTests() => DrawingRevision.Shutdown();
        public void Dispose() => DrawingRevision.Shutdown();

        [Fact]
        public void Repeated_context_reads_with_new_wrappers_keep_one_tracker()
        {
            var native = new Database();
            var doc = new Document { DatabaseFactory = native.NewWrapper };
            Assert.NotSame(doc.Database, doc.Database);
            Assert.Equal(doc.Database, doc.Database);
            var first = DrawingRevision.For(doc);
            var second = DrawingRevision.For(doc);
            Assert.Same(first, second);
            Assert.Equal(first.SessionId, second.SessionId);
            Assert.Equal(5, native.Subscribers);
            Assert.Equal(1, Application.DocumentManager.Subscribers);
        }

        [Theory]
        [InlineData("modified")]
        [InlineData("appended")]
        [InlineData("erased")]
        [InlineData("unappended")]
        [InlineData("reappended")]
        public void New_wrappers_still_invalidate_on_each_drawing_event(string name)
        {
            var native = new Database();
            var doc = new Document { DatabaseFactory = native.NewWrapper };
            var state = DrawingRevision.For(doc);
            doc.Database.Raise(name, new DBObject());
            Assert.Equal(1, DrawingRevision.For(doc).Revision);
            Assert.Equal(state.SessionId, DrawingRevision.For(doc).SessionId);
        }

        [Fact]
        public void Navigation_objects_do_not_invalidate_drawing_history()
        {
            var doc = new Document();
            var state = DrawingRevision.For(doc);
            doc.Database.Raise("modified", new Viewport());
            doc.Database.Raise("modified", new ViewportTableRecord());
            Assert.Equal(0, state.Revision);
        }

        [Fact]
        public void Documents_have_independent_sessions_and_revisions()
        {
            var firstDoc = new Document();
            var secondDoc = new Document();
            var first = DrawingRevision.For(firstDoc);
            var second = DrawingRevision.For(secondDoc);
            Assert.NotEqual(first.SessionId, second.SessionId);
            firstDoc.Database.Raise("modified", new DBObject());
            Assert.Equal(1, first.Revision);
            Assert.Equal(0, second.Revision);
        }

        [Fact]
        public void Closing_document_detaches_only_its_handlers()
        {
            var firstDoc = new Document();
            var secondDoc = new Document();
            var first = DrawingRevision.For(firstDoc);
            DrawingRevision.For(secondDoc);
            Application.DocumentManager.Destroy(firstDoc);
            Application.DocumentManager.Destroy(firstDoc);
            Assert.Equal(0, firstDoc.Database.Subscribers);
            Assert.Equal(5, secondDoc.Database.Subscribers);
            firstDoc.Database.Raise("modified", new DBObject());
            Assert.Equal(0, first.Revision);
        }

        [Fact]
        public void Shutdown_detaches_all_handlers_and_allows_fresh_session()
        {
            var doc = new Document();
            var other = new Document();
            var first = DrawingRevision.For(doc);
            DrawingRevision.For(other);
            DrawingRevision.Shutdown();
            DrawingRevision.Shutdown();
            Assert.Equal(0, doc.Database.Subscribers);
            Assert.Equal(0, other.Database.Subscribers);
            Assert.Equal(0, Application.DocumentManager.Subscribers);
            Assert.NotEqual(first.SessionId, DrawingRevision.For(doc).SessionId);
            Assert.Equal(5, doc.Database.Subscribers);
            Assert.Equal(1, Application.DocumentManager.Subscribers);
        }
    }

    [CollectionDefinition("Drawing revision", DisableParallelization = true)]
    public class DrawingRevisionCollection { }
}
