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
    public string Build(string password)
    {
        if (string.IsNullOrWhiteSpace(Alias)) throw new InvalidDataException("Enter a connection name.");
        if (Port is < 0 or > 65535) throw new InvalidDataException("Port must be between 1 and 65535, or 0 for the default.");
        if (Provider == DatabaseProvider.Sqlite)
        {
            if (!System.IO.File.Exists(File)) throw new InvalidDataException("Choose an existing SQLite database file.");
            return new SqliteConnectionStringBuilder { DataSource = System.IO.Path.GetFullPath(File), Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString();
        }
        if (string.IsNullOrWhiteSpace(Host) || string.IsNullOrWhiteSpace(Database)) throw new InvalidDataException("Enter the server and database name supplied by your administrator.");
        return Provider switch
        {
            DatabaseProvider.MySql => new MySqlConnectionStringBuilder { Server = Host, Port = (uint)(Port == 0 ? 3306 : Port), Database = Database, UserID = Username, Password = password, SslMode = MySqlSslMode.Required, ConnectionTimeout = 15 }.ToString(),
            DatabaseProvider.PostgreSql => new NpgsqlConnectionStringBuilder { Host = Host, Port = Port == 0 ? 5432 : Port, Database = Database, Username = Username, Password = password, SslMode = SslMode.Require, Timeout = 15 }.ToString(),
            DatabaseProvider.SqlServer => new SqlConnectionStringBuilder { DataSource = Port == 0 ? Host : $"{Host},{Port}", InitialCatalog = Database, IntegratedSecurity = WindowsAuthentication, UserID = WindowsAuthentication ? "" : Username, Password = WindowsAuthentication ? "" : password, ConnectTimeout = 15, Encrypt = SqlConnectionEncryptOption.Mandatory }.ToString(),
            _ => throw new InvalidDataException("Choose a supported database type.")
        };
    }
}
