namespace IPC.DB.Options
{
    public sealed class DbConnectionOptions
    {
        public string Provider { get; set; } = "SqlServer";
        public string ConnectionString { get; set; } = string.Empty;
    }
}
