namespace Tanuki.Html;

internal class SourceText
{
    private readonly ReadOnlyMemory<char> _chars;
    
    internal SourceText(ReadOnlyMemory<char> chars)
    {
        _chars = chars;
    }

    /// <summary>
    /// Performs a case-insensitive string comparison at the <see cref="startIndex"/>.
    /// </summary>
    public bool CompareAscii(int startIndex, ReadOnlySpan<char> other)
    {
        var span = _chars.Slice(startIndex, other.Length).Span;
        return span.Equals(other, StringComparison.OrdinalIgnoreCase);
    }
}