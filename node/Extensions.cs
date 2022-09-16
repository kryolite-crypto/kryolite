using System.Linq.Expressions;
using LiteDB;

namespace Marccacoin;

public static class Extensions
{
    public static ILiteCollection<T> IncludeCollection<T, K>(this ILiteCollection<T> collection, Func<T, List<K>> keySelector)
    {
        return collection.Include(BsonExpression.Create($"$.{typeof(K).Name}s[*]"));
    }
}
