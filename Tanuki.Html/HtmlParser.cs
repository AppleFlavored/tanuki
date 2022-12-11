namespace Tanuki.Html;

public class HtmlParser
{
    private HtmlTokenizer _tokenizer;

    public HtmlParser(string source)
    {
        _tokenizer = new HtmlTokenizer(source);
    }

    public HtmlDocument Parse()
    {
        return new HtmlDocument();
    }
}