// Usage: dotnet tests/SqlTool/bin/.../SqlTool.dll <database file> "<SQL>"
// Prints each result row as tab-separated values (or "rows affected: N" for updates).
// Refuses to touch a file named altobank.db so it can never change the real database.
using Microsoft.Data.Sqlite;

if (args.Length < 2) { Console.Error.WriteLine("usage: SqlTool <db file> \"<sql>\""); return 2; }
var file = args[0];
if (Path.GetFileName(file).Equals("altobank.db", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Refusing to run against the real altobank.db. Use the scratch test database.");
    return 3;
}
if (!File.Exists(file)) { Console.Error.WriteLine($"No such database: {file}"); return 4; }

using var conn = new SqliteConnection($"Data Source={file};Mode=ReadWrite;Default Timeout=30");
conn.Open();
using (var pragma = conn.CreateCommand()) { pragma.CommandText = "PRAGMA busy_timeout = 10000;"; pragma.ExecuteNonQuery(); }
using var cmd = conn.CreateCommand();
cmd.CommandText = args[1];
using var reader = cmd.ExecuteReader();
var any = false;
do
{
    while (reader.Read())
    {
        any = true;
        Console.WriteLine(string.Join('\t', Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "" : reader.GetValue(i).ToString())));
    }
} while (reader.NextResult());
if (!any && reader.RecordsAffected >= 0) Console.WriteLine($"rows affected: {reader.RecordsAffected}");
return 0;
