using E2ETester.Core;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace E2ETester.Infrastructure;

// Safe to share: passwords are passed separately and never exported in templates.
public sealed record DatabaseConnectionDetails
{
    public string Alias { get; set; } = "local-db";
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.MySql;
    public string Host { get; set; } = "localhost";
    public int Port { get; set; }
    public string Database { get; set; } = "";
    public string Username { get; set; } = "";
    public string File { get; set; } = "";
    public bool WindowsAuthentication { get; set; }
    public bool ReadOnlyAccount { get; set; }
    public MySqlSslMode MySqlTlsMode { get; set; } = MySqlSslMode.Required;
    public static (DatabaseConnectionDetails Details, string Password) Read(DatabaseConnectionProfile profile)
    {
        var details = new DatabaseConnectionDetails { Alias = profile.Alias, Provider = profile.Provider, ReadOnlyAccount = profile.ReadOnlyCredentialsConfirmed };
        switch (profile.Provider)
        {
            case DatabaseProvider.MySql:
                var mysql = new MySqlConnectionStringBuilder(profile.ConnectionString);
                return (details with { Host = mysql.Server, Port = (int)mysql.Port, Database = mysql.Database, Username = mysql.UserID, MySqlTlsMode = mysql.SslMode }, mysql.Password);
            case DatabaseProvider.PostgreSql:
                var postgres = new NpgsqlConnectionStringBuilder(profile.ConnectionString);
                return (details with { Host = postgres.Host ?? "", Port = postgres.Port, Database = postgres.Database ?? "", Username = postgres.Username ?? "" }, postgres.Password ?? "");
            case DatabaseProvider.SqlServer:
                var sql = new SqlConnectionStringBuilder(profile.ConnectionString);
                var host = sql.DataSource; var port = 0; var comma = host.LastIndexOf(',');
                if (comma >= 0 && int.TryParse(host[(comma + 1)..], out port)) host = host[..comma];
                return (details with { Host = host, Port = port, Database = sql.InitialCatalog, Username = sql.UserID, WindowsAuthentication = sql.IntegratedSecurity }, sql.Password);
            case DatabaseProvider.Sqlite:
                var sqlite = new SqliteConnectionStringBuilder(profile.ConnectionString);
                return (details with { File = sqlite.DataSource }, sqlite.Password);
            default: throw new InvalidDataException("Choose a supported database type.");
        }
    }
    public string Build(string password, string? existing = null)
    {
        if (string.IsNullOrWhiteSpace(Alias)) throw new InvalidDataException("Enter a connection name.");
        if (Port is < 0 or > 65535) throw new InvalidDataException("Port must be between 1 and 65535, or 0 for the default.");
        if (Provider == DatabaseProvider.Sqlite)
        {
            if (!System.IO.File.Exists(File)) throw new InvalidDataException("Choose an existing SQLite database file.");
            return new SqliteConnectionStringBuilder(existing ?? "Mode=ReadWrite;Pooling=False") { DataSource = System.IO.Path.GetFullPath(File) }.ToString();
        }
        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(Database)) throw new InvalidDataException("Enter the server and database name supplied by your administrator.");
        return Provider switch
        {
            DatabaseProvider.MySql => new MySqlConnectionStringBuilder(existing ?? "SslMode=Required;ConnectionTimeout=15") { Server = Host, Port = (uint)(Port == 0 ? 3306 : Port), Database = Database, UserID = Username, Password = password, SslMode = MySqlTlsMode }.ToString(),
            DatabaseProvider.PostgreSql => new NpgsqlConnectionStringBuilder(existing ?? "SslMode=Require;Timeout=15") { Host = Host, Port = Port == 0 ? 5432 : Port, Database = Database, Username = Username, Password = password }.ToString(),
            DatabaseProvider.SqlServer => new SqlConnectionStringBuilder(existing ?? "Connect Timeout=15;Encrypt=True") { DataSource = Port == 0 ? Host : $"{Host},{Port}", InitialCatalog = Database, IntegratedSecurity = WindowsAuthentication, UserID = WindowsAuthentication ? "" : Username, Password = WindowsAuthentication ? "" : password }.ToString(),
            _ => throw new InvalidDataException("Choose a supported database type.")
        };
    }
}
