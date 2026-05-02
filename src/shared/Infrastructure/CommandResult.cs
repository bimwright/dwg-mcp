namespace Bimwright.Dwg.Plugin
{
    public class CommandResult
    {
        public bool Ok { get; set; }
        public object Result { get; set; }
        public string Error { get; set; }

        public static CommandResult Success(object result) =>
            new CommandResult { Ok = true, Result = result };
        public static CommandResult Fail(string error) =>
            new CommandResult { Ok = false, Error = error };
    }
}
