using System.Collections;
using System.Reflection;

namespace Platform.ArchitectureTests.Support;

/// <summary>Builds a populated instance of any event record from its widest constructor, so the round-trip gate needs no hand-written fixture per event.</summary>
internal static class EventSamples
{
    private static readonly DateTimeOffset Instant = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    public static object Sample(Type type)
    {
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public).OrderByDescending(c => c.GetParameters().Length).First();
        var arguments = constructor.GetParameters().Select(parameter => Value(parameter.ParameterType)).ToArray();
        return constructor.Invoke(arguments);
    }

    private static object? Value(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(Guid))
        {
            return Guid.CreateVersion7();
        }

        if (underlying == typeof(string))
        {
            return "sample";
        }

        if (underlying == typeof(DateTimeOffset))
        {
            return Instant;
        }

        if (underlying == typeof(bool))
        {
            return true;
        }

        if (underlying == typeof(int) || underlying == typeof(long) || underlying == typeof(decimal))
        {
            return Convert.ChangeType(7, underlying, System.Globalization.CultureInfo.InvariantCulture);
        }

        if (underlying.IsEnum)
        {
            return Enum.GetValues(underlying).GetValue(0);
        }

        if (underlying.IsGenericType && typeof(IEnumerable).IsAssignableFrom(underlying))
        {
            var item = underlying.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(item))!;
            if (item.IsClass && item != typeof(string) && item.GetConstructors().Length > 0)
            {
                list.Add(Sample(item));
            }

            return list;
        }

        if (underlying.IsClass && underlying.GetConstructors(BindingFlags.Instance | BindingFlags.Public).Length > 0)
        {
            return Sample(underlying);
        }

        return underlying.IsValueType ? Activator.CreateInstance(underlying) : null;
    }
}
