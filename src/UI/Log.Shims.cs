namespace UI
{
    public static partial class Log
    {
        // Alias to match older call sites that use Log.Info(...)
        public static void Info(string message) => Information(message);
    }
}