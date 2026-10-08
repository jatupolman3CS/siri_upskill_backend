using System.Linq.Expressions;

namespace Siri.UnitTests.Learning;

/// <summary>
/// Lets a plain in-memory sequence be used where production code runs an EF query ending in <c>ToListAsync</c>: EF's async operators require the source to be an
/// <see cref="IAsyncEnumerable{T}"/>, which <c>List.AsQueryable()</c> is not. Every operator applied on top (<c>Where</c>, <c>Select</c>, ...) is evaluated by LINQ to Objects, but the
/// result is again an async-capable query, so the whole chain keeps working. Only for unit tests of code that needs "a queryable", never a substitute for a real database.
/// </summary>
internal static class AsyncQueryable
{
    public static IQueryable<T> From<T>(IEnumerable<T> source) => new AsyncEnumerableQuery<T>(source);

    private sealed class AsyncEnumerableQuery<T> : EnumerableQuery<T>, IAsyncEnumerable<T>, IQueryable<T>
    {
        public AsyncEnumerableQuery(IEnumerable<T> enumerable)
            : base(enumerable)
        {
        }

        public AsyncEnumerableQuery(Expression expression)
            : base(expression)
        {
        }

        IQueryProvider IQueryable.Provider => AsyncQueryProvider.Instance;

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) =>
            new AsyncEnumerator<T>(((IEnumerable<T>)this).GetEnumerator());
    }

    private sealed class AsyncQueryProvider : IQueryProvider
    {
        public static readonly AsyncQueryProvider Instance = new();

        // The expression already carries its own data source, so any LINQ-to-Objects provider can execute it.
        private static readonly IQueryProvider Inner = Array.Empty<int>().AsQueryable().Provider;

        public IQueryable CreateQuery(Expression expression) => new AsyncEnumerableQuery<object>(expression);

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) => new AsyncEnumerableQuery<TElement>(expression);

        public object? Execute(Expression expression) => Inner.Execute(expression);

        public TResult Execute<TResult>(Expression expression) => Inner.Execute<TResult>(expression);
    }

    private sealed class AsyncEnumerator<T>(IEnumerator<T> inner) : IAsyncEnumerator<T>
    {
        public T Current => inner.Current;

        public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(inner.MoveNext());

        public ValueTask DisposeAsync()
        {
            inner.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
