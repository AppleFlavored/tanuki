namespace Tanuki.Html;

/// <summary>
/// The experimental HTML parser for the Tanuki browser engine. The goal of the experimental parser is to be as fast as
/// possible whilst being spec-compliant.
/// </summary>
public class HtmlParser
{
    private readonly SourceText _source;
    private HtmlTokenizer _tokenizer;

    public HtmlParser(string source)
    {
        _source = new SourceText(source.AsMemory());
        _tokenizer = new HtmlTokenizer(_source);
    }
}