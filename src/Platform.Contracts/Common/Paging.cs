namespace Platform.Contracts.Common;

/// <summary>A normalised page request. Clamping is a decision, so it happens here (in the handler's hands), never in a repository (pitfall P-13).</summary>
public readonly record struct PageRequest
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    private PageRequest(int page, int pageSize)
    {
        Page = page;
        PageSize = pageSize;
    }

    public int Page { get; }

    public int PageSize { get; }

    public int Skip => (Page - 1) * PageSize;

    public static PageRequest Normalized(int? page, int? pageSize) =>
        new(Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}

/// <summary>The page and size a client asks for, before normalisation.</summary>
public sealed record PageQuery(int? Page, int? PageSize);

/// <summary>One page of a projection.</summary>
public sealed record Paged<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public static Paged<T> Empty(PageRequest page) => new([], page.Page, page.PageSize, 0);
}

/// <summary>The response of a create command: the id of the new resource.</summary>
public sealed record CreatedResource(Guid Id);
