using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Anitime.Api.Tests;

/// <summary>
/// Counts SQL commands sent by EF Core inside a <see cref="Begin"/> scope. The scope flows into TestServer requests
/// because the fixture sets <c>PreserveExecutionContext</c>, so parallel tests never see each other's queries.
/// </summary>
public sealed class QueryCounter : DbCommandInterceptor
{
    private static readonly AsyncLocal<Scope?> Current = new();

    public static Scope Begin() => Current.Value = new Scope();

    public sealed class Scope : IDisposable
    {
        public ConcurrentQueue<string> Commands { get; } = new();
        public int Count => Commands.Count;
        public void Dispose() => Current.Value = null;
        public override string ToString() => string.Join("\n---\n", Commands);
    }

    private static void Record(DbCommand command) => Current.Value?.Commands.Enqueue(command.CommandText);

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        Record(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
    {
        Record(command);
        return ValueTask.FromResult(result);
    }
}
