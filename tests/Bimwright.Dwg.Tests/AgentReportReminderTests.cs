using Bimwright.Dwg.Server;
using ModelContextProtocol.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Bimwright.Dwg.Tests;

public class AgentReportReminderTests
{
    [Fact]
    public void Full_reminder_is_retried_until_delivered_then_shortens_until_a_report_succeeds()
    {
        var reminder = new AgentReportReminder(() => true);
        string Write(string payload = "created") => reminder.Serialize("create_line", new McpResponse { Ok = true, Result = payload });
        static CallToolResult Reply(string text) => new() { Content = new[] { new TextContentBlock { Text = text } } };
        static string Prompt(string text) => JObject.Parse(text).Value<string>("_report");

        var withoutReminder = Reply(reminder.Serialize("get_drawing_info", new McpResponse { Ok = true, Result = new string('x', 5000) }));
        var cap = ResponseBudget.Measure(withoutReminder);
        var first = Write(new string('x', 5000));
        Assert.Equal(AgentReportReminder.Text, Prompt(first));
        var delivered = ResponseBudget.Apply(Reply(first), new DwgMcpConfig { ResponseBudgetBytes = cap }, false, "dwg_create_line");
        var deliveredText = Assert.IsType<TextContentBlock>(Assert.Single(delivered.Content)).Text;
        Assert.Null(Prompt(deliveredText));
        reminder.RecordDelivery(deliveredText);

        var second = Write();
        Assert.Equal(AgentReportReminder.Text, Prompt(second));
        reminder.RecordDelivery(second);
        Assert.Equal(AgentReportReminder.ShortText, Prompt(Write()));

        reminder.Serialize("report_task_result", new McpResponse { Ok = false });
        Assert.Equal(AgentReportReminder.ShortText, Prompt(Write()));
        Assert.Null(Prompt(reminder.Serialize("report_task_result", new McpResponse { Ok = true })));
        Assert.Equal(AgentReportReminder.Text, Prompt(Write()));
    }
}
