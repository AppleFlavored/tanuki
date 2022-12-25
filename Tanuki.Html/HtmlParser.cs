using Tanuki.Dom;

namespace Tanuki.Html;

public class HtmlParser
{
    private readonly ParsingFlags _flags;
    private readonly HtmlTokenizer _tokenizer;
    private readonly Document _document;
    private readonly Stack<Node> _openElements = new();
    private InsertionMode _insertionMode = InsertionMode.Initial;
    private InsertionMode _returnMode;
    private Element? _headElement;
    private Element? _formElement = null;

    private Node CurrentNode => _openElements.First();
    
    public HtmlParser(string source, ParsingFlags flags)
    {
        _flags = flags;
        _tokenizer = new HtmlTokenizer(source);
        _document = new Document();
    }

    public Document Parse()
    {
        while (true)
        {
            var token = _tokenizer.Next();
            if (token is null) break;

            reprocess:
            Console.WriteLine($"Current mode: {_insertionMode}");
            switch (_insertionMode)
            {
                case InsertionMode.Initial:
                {
                    if (token is HtmlToken.Character { IsWhitespace: true })
                        break;

                    if (token is HtmlToken.Comment comment)
                    {
                        _document.Children.Add(new Comment(comment.Data));
                        break;
                    }

                    if (token is HtmlToken.Doctype doctype)
                    {
                        _document.Children.Add(new DocumentType(doctype.Name, doctype.PublicIdentifier ?? string.Empty,
                            doctype.SystemIdentifier ?? string.Empty));
                        _insertionMode = InsertionMode.BeforeHtml;
                        break;
                    }

                    _insertionMode = InsertionMode.BeforeHtml;
                    break;
                }
                case InsertionMode.BeforeHtml:
                {
                    if (token is HtmlToken.Doctype or HtmlToken.Character { IsWhitespace: true })
                        break;

                    if (token is HtmlToken.Comment comment)
                    {
                        _document.Children.Add(new Comment(comment.Data));
                        break;
                    }

                    if (token is HtmlToken.StartTag { Name: "html" } tag)
                    {
                        InsertElement(tag, _document);
                        _insertionMode = InsertionMode.BeforeHead;
                        break;
                    }

                    if (token is HtmlToken.EndTag { Name: not "head" or "body" or "html" or "br" })
                        break;

                    InsertElement("html", _document);
                    _insertionMode = InsertionMode.BeforeHead;
                    break;
                }
                case InsertionMode.BeforeHead:
                {
                    if (token is HtmlToken.Character { IsWhitespace: true })
                        break;

                    if (token is HtmlToken.Comment comment)
                    {
                        CurrentNode.Children.Add(new Comment(comment.Data));
                        break;
                    }
                    
                    if (token is HtmlToken.Doctype)
                        break;

                    if (token is HtmlToken.StartTag { Name: "html" })
                        goto case InsertionMode.InBody;

                    if (token is HtmlToken.StartTag { Name: "head" } tag)
                    {
                        _headElement = InsertElement(tag);
                        _insertionMode = InsertionMode.InHead;
                        break;
                    }

                    if (token is HtmlToken.EndTag { Name: not "head" or "body" or "html" or "br" })
                        break;

                    _headElement = InsertElement("head");
                    _insertionMode = InsertionMode.InHead;
                    goto case InsertionMode.InHead;
                }
                case InsertionMode.InHead:
                {
                    if (token is HtmlToken.Character { IsWhitespace: true } ch)
                    {
                        InsertCharacter(ch);
                        break;
                    }

                    if (token is HtmlToken.Comment comment)
                    {
                        CurrentNode.Children.Add(new Comment(comment.Data));
                        break;
                    }

                    if (token is HtmlToken.Doctype)
                        break;

                    if (token is HtmlToken.StartTag startTag)
                    {
                        if (startTag.Name is "base" or "basefont" or "bgsound" or "link" or "meta")
                        {
                            InsertElement(startTag);
                            _openElements.Pop();
                            break;
                        }

                        if (startTag.Name is "title")
                        {
                            ParseElementWithRcData(startTag);
                            break;
                        }

                        if (startTag.Name is "noscript")
                        {
                            if (_flags.HasFlag(ParsingFlags.Scripting))
                            {
                                ParseElementWithRawText(startTag);
                            }
                            else
                            {
                                InsertElement(startTag);
                                _insertionMode = InsertionMode.InHeadNoScript;
                            }

                            break;
                        }

                        if (startTag.Name is "noframes" or "style")
                        {
                            ParseElementWithRawText(startTag);
                            break;
                        }

                        if (startTag.Name is "script")
                        {
                            // TODO: Handle "script" tag
                            break;
                        }

                        if (startTag.Name is "template")
                        {
                            // TODO: Handle "template" tag
                            break;
                        }
                        
                        if (startTag.Name is "head")
                            break;
                    }

                    if (token is HtmlToken.EndTag endTag)
                    {
                        if (endTag.Name is "head")
                        {
                            _openElements.Pop();
                            _insertionMode = InsertionMode.AfterHead;
                            break;
                        }

                        if (endTag.Name is not "body" or "html" or "br")
                            break;

                        if (endTag.Name is "template")
                        {
                            // TODO: Handle "template" tag
                            break;
                        }

                        break;
                    }
                    
                    _openElements.Pop();
                    _insertionMode = InsertionMode.AfterHead;
                    goto case InsertionMode.AfterHead;
                }
                // TODO: InsertionMode.InHeadNoScript
                case InsertionMode.AfterHead:
                {
                    if (token is HtmlToken.Character { IsWhitespace: true } ch)
                    {
                        InsertCharacter(ch);
                        break;
                    }

                    if (token is HtmlToken.Comment comment)
                    {
                        CurrentNode.Children.Add(new Comment(comment.Data));
                        break;
                    }

                    if (token is HtmlToken.Doctype)
                        break;

                    if (token is HtmlToken.StartTag startTag)
                    {
                        if (startTag.Name is "head")
                            break;

                        if (startTag.Name is "html")
                            goto case InsertionMode.InBody;

                        if (startTag.Name is "body")
                        {
                            InsertElement(startTag);
                            // TODO: Set the FramesetOk flag to "not ok"
                            _insertionMode = InsertionMode.InBody;
                            break;
                        }

                        if (startTag.Name is "frameset")
                        {
                            InsertElement(startTag);
                            _insertionMode = InsertionMode.InFrameset;
                            break;
                        }

                        if (startTag.Name is "base" or "basefont" or "bgsound" or "link" or "meta" or "noframes"
                            or "script" or "style" or "template" or "title")
                        {
                            // TODO: uh oh
                            break;
                        }
                    }

                    if (token is HtmlToken.EndTag { Name: "template" })
                        goto case InsertionMode.InHead;

                    if (token is HtmlToken.EndTag { Name: not "body" or "head" or "br" })
                        break;

                    InsertElement("body");
                    _insertionMode = InsertionMode.InBody;
                    goto case InsertionMode.InBody;
                }
                case InsertionMode.InBody:
                {
                    if (token is HtmlToken.Character { Data: '\u0000' })
                        break;

                    if (token is HtmlToken.Character { IsWhitespace: true } whitespace)
                    {
                        // TODO: Reconstruct the active formatting elements.
                        InsertCharacter(whitespace);
                        break;
                    }

                    if (token is HtmlToken.Character other)
                    {
                        // TODO: Reconstruct the active formatting elements.
                        InsertCharacter(other);
                        // TODO: Set FramesetOk flag to "not ok".
                        break;
                    }

                    if (token is HtmlToken.Comment comment)
                    {
                        CurrentNode.Children.Add(new Comment(comment.Data));
                        break;
                    }

                    if (token is HtmlToken.Doctype)
                        break;

                    break;
                }
                case InsertionMode.Text:
                {
                    if (token is HtmlToken.Character ch)
                    {
                        InsertCharacter(ch);
                        break;
                    }

                    if (token is HtmlToken.EndOfFile)
                    {
                        _openElements.Pop();
                        _insertionMode = _returnMode;
                        goto reprocess;
                    }

                    if (token is HtmlToken.EndTag)
                    {
                        _openElements.Pop();
                        _insertionMode = _returnMode;
                    }
                    
                    break;
                }
            }
        }
        
        return _document;
    }

    private Element InsertElement(string name, Node? parent = null)
    {
        var element = new Element(name);
        // TODO: Check if possible to insert element.
        (parent ?? CurrentNode).Children.Add(element);
        _openElements.Push(element);
        return element;
    }
    
    private Element InsertElement(in HtmlToken.StartTag tag, Node? parent = null)
    {
        var element = new Element(tag.Name) { Attributes = tag.Attributes };
        // TODO: Check if possible to insert element.
        (parent ?? CurrentNode).Children.Add(element);
        _openElements.Push(element);
        return element;
    }

    private void InsertCharacter(in HtmlToken.Character ch)
    {
        if (CurrentNode is Document) return;
        var textNode = CurrentNode.Children.FindLast(node => node is Text) as Text;
        if (textNode is not null)
        {
            textNode.Data += ch.Data;
            return;
        }
        CurrentNode.Children.Add(new Text(ch.Data.ToString()));
    }

    private void ParseElementWithRawText(in HtmlToken.StartTag tag)
    {
        InsertElement(tag);
        _tokenizer.SwitchState(HtmlState.RawText);
        _returnMode = _insertionMode;
        _insertionMode = InsertionMode.Text;
    }
    
    private void ParseElementWithRcData(in HtmlToken.StartTag tag)
    {
        InsertElement(tag);
        _tokenizer.SwitchState(HtmlState.RcData);
        _returnMode = _insertionMode;
        _insertionMode = InsertionMode.Text;
    }

    private enum InsertionMode
    {
        Initial,
        BeforeHtml,
        BeforeHead,
        InHead,
        InHeadNoScript,
        AfterHead,
        InBody,
        Text,
        InTable,
        InTableText,
        InCaption,
        InColumnGroup,
        InTableBody,
        InRow,
        InCell,
        InSelect,
        InSelectInTable,
        InTemplate,
        AfterBody,
        InFrameset,
        AfterFrameset,
        AfterAfterBody,
        AfterAfterFrameset
    }
}