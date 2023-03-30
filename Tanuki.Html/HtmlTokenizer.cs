namespace Tanuki.Html;

internal class HtmlTokenizer
{
    private SourceText _source;
    
    internal HtmlTokenizer(SourceText text)
    {
        _source = text;
    }
}