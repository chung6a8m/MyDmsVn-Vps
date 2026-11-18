using System;
namespace MyDmsVn.Server.DbMigrator;
public sealed class DatabaseMigrationPreflightException : Exception
{
    public DatabaseMigrationPreflightException(string code, string safeMessage) : base(safeMessage)
    { Code = code; }
    public string Code { get; }
}
