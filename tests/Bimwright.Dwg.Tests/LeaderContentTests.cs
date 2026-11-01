using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Bimwright.Dwg.Plugin.Cad;
using Xunit;

namespace Bimwright.Dwg.Tests;

public sealed class LeaderContentTests
{
    [Theory]
    [InlineData(ContentType.NoneContent)]
    [InlineData(ContentType.BlockContent)]
    public void Describing_non_text_leaders_never_reads_the_native_mtext_getter(ContentType content)
    {
        var leader = new MLeader {
            ContentType = content,
            TextReadMTextFactory = () => throw new InvalidOperationException("No native MText exists.")
        };
        var result = (IDictionary<string, object>)CadEntityProperties.Describe(leader, null, true);
        Assert.Equal(string.Empty, result["text"]);
        Assert.Null(result["text_height"]);
        Assert.Equal(0, leader.TextReadCloneReads);
    }
}
