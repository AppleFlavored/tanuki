using System.Diagnostics;
using Tanuki.Dom;

namespace Tanuki.Html;

public class HtmlParser
{
    private readonly ParsingFlags _flags;
    private readonly HtmlTokenizer _tokenizer;
    private readonly Document _document;
    private readonly Stack<Element> _openElements = new();
    private InsertionMode _insertionMode = InsertionMode.Initial;
    private InsertionMode _returnMode;
    private FramesetOk _framesetOk = FramesetOk.Ok;
    private Element? _headElement;
    private Element? _formElement;

    private static readonly List<string> SpecialElements = new()
    {
        "address", "applet", "area", "article", "aside", "base", "basefont", "bgsound", "blockquote", "body", "br",
        "button", "caption", "center", "col", "colgroup", "dd", "details", "dir", "div", "dl", "dt", "embed",
        "fieldset", "figcaption", "figure", "footer", "form", "frame", "frameset", "h1", "h2", "h3", "h4", "h5", "h6",
        "head", "header", "hgroup", "hr", "html", "iframe", "img", "input", "keygen", "li", "link", "listing", "main",
        "marquee", "menu", "meta", "nav", "noembed", "noframes", "noscript", "object", "ol", "p", "param", "plaintext",
        "pre", "script", "section", "select", "source", "style", "summary", "table", "tbody", "td", "template",
        "textarea", "tfoot", "th", "thead", "title", "tr", "track", "ul", "wbr", "xmp"
    };

    private static readonly List<string> FormattingElements = new()
    {
        "a", "b", "big", "code", "em", "font", "i", "nobr", "s", "small", "strike", "strong", "tt", "u"
    };

    private static readonly List<string> BaseScopeList = new() { "applet", "caption", "html", "table", "td", "th", "marquee", "object", "template" };
    private static readonly List<string> EndTagList = new() { "dd", "dt", "li", "optgroup", "option", "p", "rb", "rp", "rt", "rtc" };

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
            Console.WriteLine(_insertionMode);
            var result = _insertionMode switch
            {
                InsertionMode.Initial => HandleInitial(token),
                InsertionMode.BeforeHtml => HandleBeforeHtml(token),
                InsertionMode.BeforeHead => HandleBeforeHead(token),
                InsertionMode.InHead => HandleInHead(token),
                InsertionMode.InHeadNoScript => HandleInHeadNoScript(token),
                InsertionMode.AfterHead => HandleAfterHead(token),
                InsertionMode.InBody => HandleInBody(token),
                InsertionMode.Text => HandleText(token),
                InsertionMode.InTable => HandleInTable(token),
                InsertionMode.InTableText => HandleInTableText(token),
                InsertionMode.InCaption => HandleInCaption(token),
                InsertionMode.InColumnGroup => HandleInColumnGroup(token),
                InsertionMode.InTableBody => HandleInTableBody(token),
                InsertionMode.InRow => HandleInRow(token),
                InsertionMode.InCell => HandleInCell(token),
                InsertionMode.InSelect => HandleInSelect(token),
                InsertionMode.InSelectInTable => HandleInSelectInTable(token),
                InsertionMode.InTemplate => HandleInTemplate(token),
                InsertionMode.AfterBody => HandleAfterBody(token),
                InsertionMode.InFrameset => HandleInFrameset(token),
                InsertionMode.AfterFrameset => HandleAfterFrameset(token),
                InsertionMode.AfterAfterBody => HandleAfterAfterBody(token),
                InsertionMode.AfterAfterFrameset => HandleAfterAfterFrameset(token),
                _ => throw new Exception("If this exception was thrown, something went really wrong.")
            };

            switch (result)
            {
                case HandleResult.Continue:
                    continue;
                case HandleResult.Reprocess:
                    goto reprocess;
            }
        }
        
        return _document;
    }

    private HandleResult HandleInitial(HtmlToken token)
    {
        if (token is HtmlToken.Character { IsWhitespace: true })
            return HandleResult.Continue;

        if (token is HtmlToken.Comment comment)
        {
            InsertComment(comment, _document);
            return HandleResult.Continue;
        }

        if (token is HtmlToken.Doctype doctype)
        {
            _document.Children.Add(new DocumentType(doctype.Name, doctype.PublicIdentifier ?? string.Empty,
                doctype.SystemIdentifier ?? string.Empty));
            _insertionMode = InsertionMode.BeforeHtml;
            return HandleResult.Continue;
        }
        
        _insertionMode = InsertionMode.BeforeHtml;
        return HandleResult.Continue;
    }

    private HandleResult HandleBeforeHtml(HtmlToken token)
    {
        if (token is HtmlToken.Doctype)
            return HandleResult.Continue;

        if (token is HtmlToken.Comment comment)
        {
            InsertComment(comment, _document);
            return HandleResult.Continue;
        }

        if (token is HtmlToken.Character)
            return HandleResult.Continue;

        if (token is HtmlToken.StartTag { Name: "html" } startTag)
        {
            InsertElement(startTag, _document);
            _insertionMode = InsertionMode.BeforeHead;
            return HandleResult.Continue;
        }
        
        if (token is HtmlToken.EndTag { Name: not ("head" or "body" or "html" or "br") })
            return HandleResult.Continue;

        InsertElement("html", _document);
        _insertionMode = InsertionMode.BeforeHead;
        return HandleResult.Reprocess;
    }
    
    private HandleResult HandleBeforeHead(HtmlToken token)
    {
        if (token is HtmlToken.Character { IsWhitespace: true })
            return HandleResult.Continue;

        if (token is HtmlToken.Comment comment)
        {
            InsertComment(comment);
            return HandleResult.Continue;
        }

        if (token is HtmlToken.Doctype)
            return HandleResult.Continue;

        if (token is HtmlToken.StartTag startTag)
        {
            _headElement = InsertElement(startTag);
            _insertionMode = InsertionMode.InHead;
            return HandleResult.Continue;
        }

        if (token is HtmlToken.EndTag { Name: not ("head" or "body" or "html" or "br") })
            return HandleResult.Continue;

        _headElement = InsertElement("head");
        _insertionMode = InsertionMode.InHead;
        return HandleResult.Reprocess;
    }
    
    private HandleResult HandleInHead(HtmlToken token)
    {
        if (token is HtmlToken.Character { IsWhitespace: true } character)
        {
            InsertCharacter(character);
            return HandleResult.Continue;
        }

        if (token is HtmlToken.Comment comment)
        {
            InsertComment(comment);
            return HandleResult.Continue;
        }

        if (token is HtmlToken.Doctype)
            return HandleResult.Continue;

        if (token is HtmlToken.StartTag startTag)
        {
            if (startTag.Name is "html")
                return HandleInBody(token);

            if (startTag.Name is "base" or "basefont" or "bgsound" or "link")
            {
                InsertElement(startTag);
                _openElements.Pop();
                return HandleResult.Continue;
            }

            if (startTag.Name is "meta")
            {
                InsertElement(startTag);
                _openElements.Pop();
                return HandleResult.Continue;
            }

            if (startTag.Name is "title")
            {
                ParseElementWithRcData(startTag);
                return HandleResult.Continue;
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

                return HandleResult.Continue;
            }

            if (startTag.Name is "noframes" or "style")
            {
                ParseElementWithRawText(startTag);
                return HandleResult.Continue;
            }

            if (startTag.Name is "script")
            {
                // TODO: Handle start tag whose tag name is "script"
                return HandleResult.Continue;
            }

            if (startTag.Name is "template")
            {
                // TODO: Handle start tag whose tag name is "template"
                return HandleResult.Continue;
            }

            if (startTag.Name is "head")
                return HandleResult.Continue;
        }

        if (token is HtmlToken.EndTag endTag)
        {
            if (endTag.Name is "head")
            {
                _openElements.Pop();
                _insertionMode = InsertionMode.AfterHead;
                return HandleResult.Continue;
            }

            if (endTag.Name is "template")
            {
                if (!IsElementOpen("template"))
                    return HandleResult.Continue;
                GenerateImpliedEndTagsThoroughly();
                PopElementsUntil("template");
                // TODO: Finish rest of step.
                return HandleResult.Continue;
            }
            
            if (endTag.Name is not ("body" or "html" or "br"))
                return HandleResult.Continue;
        }

        _openElements.Pop();
        _insertionMode = InsertionMode.AfterHead;
        return HandleResult.Reprocess;
    }
    
    private HandleResult HandleInHeadNoScript(HtmlToken token)
    {
        if (token is HtmlToken.Doctype)
            return HandleResult.Continue;

        if (token is HtmlToken.Character { IsWhitespace: true } or HtmlToken.Comment)
            return HandleInHead(token);
        
        if (token is HtmlToken.StartTag startTag)
        {
            if (startTag.Name is "html")
                return HandleInBody(token);

            if (startTag.Name is "basefont" or "bgsound" or "link" or "meta" or "noframes" or "style")
                return HandleInHead(token);

            if (startTag.Name is "head" or "noscript")
                return HandleResult.Continue;
        }

        if (token is HtmlToken.EndTag endTag)
        {
            if (endTag.Name is "noscript")
            {
                _openElements.Pop();
                _insertionMode = InsertionMode.InHead;
                return HandleResult.Continue;
            }
            
            if (endTag.Name is not "br")
                return HandleResult.Continue;
        }

        _openElements.Pop();
        _insertionMode = InsertionMode.InHead;
        return HandleResult.Reprocess;
    }
    
    private HandleResult HandleAfterHead(HtmlToken token)
    {
        if (token is HtmlToken.Character character)
        {
            InsertCharacter(character);
            return HandleResult.Continue;
        }

        if (token is HtmlToken.Comment comment)
        {
            InsertComment(comment);
            return HandleResult.Continue;
        }

        if (token is HtmlToken.Doctype)
            return HandleResult.Continue;

        if (token is HtmlToken.StartTag startTag)
        {
            if (startTag.Name is "html")
                return HandleInBody(token);

            if (startTag.Name is "body")
            {
                InsertElement(startTag);
                _framesetOk = FramesetOk.NotOk;
                _insertionMode = InsertionMode.InBody;
                return HandleResult.Continue;
            }

            if (startTag.Name is "frameset")
            {
                InsertElement(startTag);
                _insertionMode = InsertionMode.InFrameset;
                return HandleResult.Continue;
            }

            if (startTag.Name is "base" or "basefont" or "bgsound" or "link" or "meta" or "noframes" or "script"
                or "style" or "template" or "title")
            {
                _openElements.Push(_headElement!);
                var result = HandleInHead(token);
                // TODO: Remove node pointed to by _headElement on stack of open elements.
                return result;
            }

            if (startTag.Name is "head")
                return HandleResult.Continue;
        }

        if (token is HtmlToken.EndTag endTag)
        {
            if (endTag.Name is "template")
                return HandleInHead(token);

            if (endTag.Name is not ("body" or "html" or "br"))
                return HandleResult.Continue;
        }

        InsertElement("body");
        _insertionMode = InsertionMode.InBody;
        return HandleResult.Reprocess;
    }

    private HandleResult HandleInBody(HtmlToken token)
    {
        if (token is HtmlToken.Character { Data: '\u0000' })
            return HandleResult.Continue;

        if (token is HtmlToken.Character { IsWhitespace: true } whitespace)
        {
            // TODO: Reconstruct the active formatting elements.
            InsertCharacter(whitespace);
            return HandleResult.Continue;
        }

        if (token is HtmlToken.Character character)
        {
            // TODO: Reconstruct the active formatting elements.
            InsertCharacter(character);
            _framesetOk = FramesetOk.NotOk;
            return HandleResult.Continue;
        }

        if (token is HtmlToken.Comment comment)
        {
            InsertComment(comment);
            return HandleResult.Continue;
        }

        if (token is HtmlToken.Doctype)
            return HandleResult.Continue;

        if (token is HtmlToken.StartTag startTag)
        {
            if (startTag.Name is "html")
            {
                if (IsElementOpen("template"))
                    return HandleResult.Continue;

                var topElement = _openElements.Last();
                foreach (var attribute in startTag.Attributes)
                {
                    if (!topElement.Attributes.Contains(attribute)) 
                        topElement.Attributes.Add(attribute);
                }

                return HandleResult.Continue;
            }

            if (startTag.Name is "base" or "basefont" or "bgsound" or "link" or "meta" or "noframes" or "script"
                or "style" or "template" or "title")
                return HandleInHead(token);

            if (startTag.Name is "body")
            {
                var element = _openElements.ElementAt(1);
                if (element.Name != "body" || _openElements.Count == 1 || IsElementOpen("template"))
                    return HandleResult.Continue;

                _framesetOk = FramesetOk.NotOk;
                foreach (var attribute in startTag.Attributes)
                {
                    if (!element.Attributes.Contains(attribute))
                        element.Attributes.Add(attribute);
                }

                return HandleResult.Continue;
            }

            if (startTag.Name is "frameset")
            {
                // TODO: Handle start tag whose name is "frameset"
            }

            if (startTag.Name is "address" or "article" or "aside" or "blockquote" or "center" or "details" or "dialog"
                or "dir" or "div" or "dl" or "fieldset" or "figcaption" or "figure" or "footer" or "header" or "hgroup"
                or "main" or "menu" or "nav" or "ol" or "p" or "section" or "summary" or "ul")
            {
                if (IsElementInButtonScope("p")) ClosePElement();
                InsertElement(startTag);
                return HandleResult.Continue;
            }

            if (startTag.Name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
            {
                if (IsElementInButtonScope("p")) ClosePElement();
                if (CurrentNode.Name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
                    _openElements.Pop();
                InsertElement(startTag);
                return HandleResult.Continue;
            }

            if (startTag.Name is "pre" or "listing")
            {
                if (IsElementInButtonScope("p")) ClosePElement();
                InsertElement(startTag);
                // TODO: Check next token for new line.
                _framesetOk = FramesetOk.NotOk;
                return HandleResult.Continue;
            }

            if (startTag.Name is "form")
            {
                if (_formElement is not null && IsElementOpen("template"))
                    return HandleResult.Continue;
                
                if (IsElementInButtonScope("p")) ClosePElement();
                var formElement = InsertElement(startTag);

                if (!IsElementOpen("template")) _formElement = formElement;
                return HandleResult.Continue;
            }

            if (startTag.Name is "li")
            {
                // TODO: Handle start tag whose tag name is "li"
            }
            
            if (startTag.Name is "dd" or "dt")
            {
                // TODO: Handle start tag whose tag name is "dd" or "dt"
            }

            if (startTag.Name is "plaintext")
            {
                if (IsElementInButtonScope("p")) ClosePElement();
                InsertElement(startTag);
                _tokenizer.SwitchState(HtmlState.Plaintext);
                return HandleResult.Continue;
            }

            if (startTag.Name is "button")
            {
                if (IsElementInScope("button"))
                {
                    GenerateImpliedEndTags();
                    PopElementsUntil("button");
                }
                // TODO: Reconstruct the active formatting elements.
                InsertElement(startTag);
                _framesetOk = FramesetOk.NotOk;
                return HandleResult.Continue;
            }

            if (startTag.Name is "a")
            {
                // TODO: Handle start tag whose tag name is "a"
            }

            if (startTag.Name is "b" or "big" or "code" or "em" or "font" or "i" or "s" or "small" or "strike"
                or "string" or "tt" or "u")
            {
                // TODO: Reconstruct the active formatting elements.
                var element = InsertElement(startTag);
                // TODO: Push element onto list of active formatting elements.
                return HandleResult.Continue;
            }

            if (startTag.Name is "nobr")
            {
                // TODO: Handle start tag whose tag name is "nobr"
            }

            if (startTag.Name is "applet" or "marquee" or "object")
            {
                // TODO: Reconstruct the active formatting elements.
                InsertElement(startTag);
                // TODO: Insert marker at end of list of active formatting elements.
                _framesetOk = FramesetOk.NotOk;
                return HandleResult.Continue;
            }

            if (startTag.Name is "table")
            {
                // TODO: Check if quirks mode is not set.
                if (IsElementInButtonScope("p")) ClosePElement();
                InsertElement(startTag);
                _framesetOk = FramesetOk.NotOk;
                _insertionMode = InsertionMode.InTable;
                return HandleResult.Continue;
            }

            if (startTag.Name is "area" or "br" or "embed" or "img" or "keygen" or "wbr")
            {
                // TODO: Reconstruct active formatting elements.
                InsertElement(startTag);
                _openElements.Pop();
                _framesetOk = FramesetOk.NotOk;
                return HandleResult.Continue;
            }

            if (startTag.Name is "input")
            {
                // TODO: Reconstruct active formatting elements.
                InsertElement(startTag);
                _openElements.Pop();
                // TODO: Set frameset-ok flag based on conditions.
                return HandleResult.Continue;
            }

            if (startTag.Name is "param" or "source" or "track")
            {
                InsertElement(startTag);
                _openElements.Pop();
                return HandleResult.Continue;
            }

            if (startTag.Name is "hr")
            {
                if (IsElementInButtonScope("p")) ClosePElement();
                InsertElement(startTag);
                _openElements.Pop();
                _framesetOk = FramesetOk.NotOk;
                return HandleResult.Continue;
            }

            if (startTag.Name is "image")
            {
                startTag.Name = "img";
                return HandleResult.Reprocess;
            }

            if (startTag.Name is "textarea")
            {
                InsertElement(startTag);
                // TODO: Check if next token is new line and ignore.
                _tokenizer.SwitchState(HtmlState.RcData);
                _returnMode = _insertionMode;
                _framesetOk = FramesetOk.NotOk;
                _insertionMode = InsertionMode.Text;
                return HandleResult.Continue;
            }

            if (startTag.Name is "xmp")
            {
                if (IsElementInButtonScope("p")) ClosePElement();
                // TODO: Reconstruct the active formatting elements.
                _framesetOk = FramesetOk.NotOk;
                ParseElementWithRawText(startTag);
                return HandleResult.Continue;
            }

            if (startTag.Name is "iframe")
            {
                _framesetOk = FramesetOk.NotOk;
                ParseElementWithRawText(startTag);
                return HandleResult.Continue;
            }

            if (startTag.Name is "noembed" || startTag.Name is "noscript" && _flags.HasFlag(ParsingFlags.Scripting))
            {
                ParseElementWithRawText(startTag);
                return HandleResult.Continue;
            }

            if (startTag.Name is "select")
            {
                // TODO: Reconstruct the active formatting elements.
                InsertElement(startTag);
                _framesetOk = FramesetOk.NotOk;
                _insertionMode = _insertionMode is InsertionMode.InTable or InsertionMode.InCaption or InsertionMode.InTableBody
                    or InsertionMode.InRow or InsertionMode.InCell
                    ? InsertionMode.InSelectInTable
                    : InsertionMode.InSelect;
                return HandleResult.Continue;
            }

            if (startTag.Name is "optgroup" or "option")
            {
                if (CurrentNode.Name is "option")
                    _openElements.Pop();
                // TODO: Reconstruct the active formatting elements.
                InsertElement(startTag);
                return HandleResult.Continue;
            }

            if (startTag.Name is "rb" or "rtc")
            {
                if (IsElementInScope("ruby"))
                    GenerateImpliedEndTags();
                InsertElement(startTag);
                return HandleResult.Continue;
            }

            if (startTag.Name is "rp" or "rt")
            {
                if (IsElementInScope("ruby"))
                    GenerateImpliedEndTags("rtc");
                InsertElement(startTag);
                return HandleResult.Continue;
            }

            if (startTag.Name is "math")
            {
                // TODO: Handle start tag whose tag name is "math"
            }

            if (startTag.Name is "svg")
            {
                // TODO: Handle start tag whose tag name is "svg"
            }

            if (startTag.Name is "caption" or "col" or "colgroup" or "frame" or "head" or "tbody" or "td" or "tfoot"
                or "th" or "thead" or "tr")
                return HandleResult.Continue;
            
            // TODO: Reconstruct the active formatting elements.
            InsertElement(startTag);
            return HandleResult.Continue;
        }

        if (token is HtmlToken.EndTag endTag)
        {
            if (endTag.Name is "template")
                return HandleInHead(token);

            if (endTag.Name is "body")
            {
                if (!IsElementInScope("body"))
                    return HandleResult.Continue;

                _insertionMode = InsertionMode.AfterBody;
                return HandleResult.Continue;
            }

            if (endTag.Name is "html")
            {
                if (!IsElementInScope("body"))
                    return HandleResult.Continue;

                _insertionMode = InsertionMode.AfterBody;
                return HandleResult.Reprocess;
            }

            if (endTag.Name is "address" or "article" or "aside" or "blockquote" or "button" or "center" or "details"
                or "dialog" or "dir" or "div" or "dl" or "fieldset" or "figcaption" or "figure" or "footer" or "header"
                or "hgroup" or "listing" or "main" or "menu" or "nav" or "ol" or "pre" or "section" or "summary"
                or "ul")
            {
                if (!IsElementInScope(endTag.Name))
                    return HandleResult.Continue;

                GenerateImpliedEndTags();
                PopElementsUntil(endTag.Name);
                return HandleResult.Continue;
            }

            if (endTag.Name is "form")
            {
                if (!IsElementOpen("template"))
                {
                    var node = _formElement;
                    _formElement = null;
                    if (node == null || !IsElementInScope(node.Name))
                        return HandleResult.Continue;
                    GenerateImpliedEndTags();
                    // TODO: Remove node from stack of open elements.
                }
                else
                {
                    if (!IsElementInScope("form"))
                        return HandleResult.Continue;
                    GenerateImpliedEndTags();
                    PopElementsUntil("form");
                }
                
                return HandleResult.Continue;
            }

            if (endTag.Name is "p")
            {
                if (!IsElementInButtonScope("p"))
                    InsertElement("p");
                ClosePElement();
                return HandleResult.Continue;
            }

            if (endTag.Name is "li")
            {
                if (!IsElementInListItemScope("li"))
                    return HandleResult.Continue;
                
                GenerateImpliedEndTags("li");
                PopElementsUntil("li");
                return HandleResult.Continue;
            }

            if (endTag.Name is "dd" or "dt")
            {
                if (!IsElementInScope(endTag.Name))
                    return HandleResult.Continue;
                
                GenerateImpliedEndTags(endTag.Name);
                PopElementsUntil(endTag.Name);
                return HandleResult.Continue;
            }

            if (endTag.Name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
            {
                if (!IsElementInScope("h1") && !IsElementInScope("h2") && !IsElementInScope("h3") &&
                    !IsElementInScope("h4") && !IsElementInScope("h5") && !IsElementInScope("h6"))
                    return HandleResult.Continue;
                
                GenerateImpliedEndTags();
                for (var i = 0; i < _openElements.Count; i++)
                {
                    var element = _openElements.Pop();
                    if (element.Name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6") break;
                }
                return HandleResult.Continue;
            }

            if (endTag.Name is "b" or "big" or "code" or "em" or "font" or "i" or "s" or "small" or "strike"
                or "string" or "tt" or "u")
            {
                // TODO: Run the adoption agency algorithm.
            }

            if (endTag.Name is "applet" or "marquee" or "object")
            {
                if (!IsElementInScope(endTag.Name))
                    return HandleResult.Continue;

                GenerateImpliedEndTags();
                PopElementsUntil(endTag.Name);
                // TODO: Clear list of active formatting elements up to last marker.
                return HandleResult.Continue;
            }

            if (endTag.Name is "br")
            {
                // TODO: Reconstruct active formatting elements.
                InsertElement("p");
                _openElements.Pop();
                _framesetOk = FramesetOk.NotOk;
                return HandleResult.Continue;
            }

            for (var i = 0; i < _openElements.Count; i++)
            {
                if (CurrentNode.Name == endTag.Name)
                {
                    GenerateImpliedEndTags();
                    _openElements.Pop();
                    break;
                }

                if (SpecialElements.Contains(CurrentNode.Name))
                    break;
                
                _openElements.Pop();
            }

            return HandleResult.Continue;
        }

        if (token is HtmlToken.EndOfFile)
        {
            // TODO: Handle end of file token
            return HandleResult.Continue;
        }

        return HandleResult.Continue;
    }

    private HandleResult HandleText(HtmlToken token)
    {
        if (token is HtmlToken.Character character)
        {
            InsertCharacter(character);
            return HandleResult.Continue;
        }

        if (token is HtmlToken.EndOfFile)
        {
            // TODO: Fully handle end of file token
            _openElements.Pop();
            _insertionMode = _returnMode;
            return HandleResult.Reprocess;
        }

        if (token is HtmlToken.EndTag endTag)
        {
            if (endTag.Name is "script")
            {
                // TODO: Handle end tag whose tag name is "script"
            }

            _openElements.Pop();
            _insertionMode = _returnMode;
        }
        
        return HandleResult.Continue;
    }

    private HandleResult HandleInTable(HtmlToken token)
    {
        throw new NotImplementedException();
    }

    private HandleResult HandleInTableText(HtmlToken token)
    {
        throw new NotImplementedException();
    }

    private HandleResult HandleInCaption(HtmlToken token)
    {
        throw new NotImplementedException();
    }

    private HandleResult HandleInColumnGroup(HtmlToken token)
    {
        throw new NotImplementedException();
    }

    private HandleResult HandleInTableBody(HtmlToken token)
    {
        throw new NotImplementedException();
    }

    private HandleResult HandleInRow(HtmlToken token)
    {
        throw new NotImplementedException();
    }

    private HandleResult HandleInCell(HtmlToken token)
    {
        throw new NotImplementedException();
    }

    private HandleResult HandleInSelect(HtmlToken token)
    {
        throw new NotImplementedException();
    }

    private HandleResult HandleInSelectInTable(HtmlToken token)
    {
        throw new NotImplementedException();
    }

    private HandleResult HandleInTemplate(HtmlToken token)
    {
        throw new NotImplementedException();
    }

    private HandleResult HandleAfterBody(HtmlToken token)
    {
        if (token is HtmlToken.Character { IsWhitespace: true })
            return HandleInBody(token);

        if (token is HtmlToken.Comment comment)
        {
            InsertComment(comment, _openElements.Last());
            return HandleResult.Continue;
        }

        if (token is HtmlToken.Doctype)
            return HandleResult.Continue;

        if (token is HtmlToken.StartTag { Name: "html" })
            return HandleInBody(token);

        if (token is HtmlToken.EndTag { Name: "html" })
        {
            _insertionMode = InsertionMode.AfterAfterBody;
            return HandleResult.Continue;
        }

        if (token is HtmlToken.EndOfFile)
        {
            StopParsing();
            return HandleResult.Continue;
        }

        _insertionMode = InsertionMode.InBody;
        return HandleResult.Reprocess;
    }

    private HandleResult HandleInFrameset(HtmlToken token)
    {
        throw new NotImplementedException();
    }

    private HandleResult HandleAfterFrameset(HtmlToken token)
    {
        throw new NotImplementedException();
    }

    private HandleResult HandleAfterAfterBody(HtmlToken token)
    {
        if (token is HtmlToken.Comment comment)
        {
            InsertComment(comment, _document);
            return HandleResult.Continue;
        }

        if (token is HtmlToken.Doctype or HtmlToken.Character { IsWhitespace: true }
            or HtmlToken.StartTag { Name: "html" })
            return HandleInBody(token);

        if (token is HtmlToken.EndOfFile)
        {
            StopParsing();
            return HandleResult.Continue;
        }

        _insertionMode = InsertionMode.InBody;
        return HandleResult.Reprocess;
    }

    private HandleResult HandleAfterAfterFrameset(HtmlToken token)
    {
        throw new NotImplementedException();
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
        (parent ?? CurrentNode).Children.Add(element);
        _openElements.Push(element);
        return element;
    }

    private void InsertComment(in HtmlToken.Comment comment, Node? parent = default)
    {
        (parent ?? CurrentNode).Children.Add(new Comment(comment.Data));
    }

    private void InsertCharacter(in HtmlToken.Character ch)
    {
        if (CurrentNode is Document) return;
        if (CurrentNode.Children.LastOrDefault() is Text text)
            text.Data += ch.Data;
        else
            CurrentNode.Children.Add(new Text(ch.Data.ToString()));
    }

    private void PopElementsUntil(string targetElement)
    {
        for (var i = 0; i < _openElements.Count; i++)
        {
            var elem = _openElements.Pop();
            if (elem.Name == targetElement) break;
        }
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

    private bool IsElementOpen(string elementName)
    {
        foreach (var element in _openElements)
        {
            if (element.Name == elementName)
                return true;
        }
        return false;
    }

    private bool IsElementInScope(string targetNode)
    {
        foreach (var node in _openElements)
        {
            if (node.Name == targetNode) return true;
            if (BaseScopeList.Contains(CurrentNode.Name)) break;
        }
        return false;
    }

    private bool IsElementInListItemScope(string targetNode)
    {
        var list = new List<string>(BaseScopeList) { "ol", "ul" };
        foreach (var node in _openElements)
        {
            if (node.Name == targetNode) return true;
            if (list.Contains(CurrentNode.Name)) break;
        }
        return false;
    }
    
    private bool IsElementInButtonScope(string targetNode)
    {
        var list = new List<string>(BaseScopeList) { "button" };
        foreach (var node in _openElements)
        {
            if (node.Name == targetNode) return true;
            if (list.Contains(CurrentNode.Name)) break;
        }
        return false;
    }
    
    private bool IsElementInTableScope(string targetNode)
    {
        foreach (var node in _openElements)
        {
            if (node.Name == targetNode) return true;
            if (CurrentNode.Name is "html" or "table" or "template") break;
        }
        return false;
    }
    
    private bool IsElementInSelectScope(string targetNode)
    {
        foreach (var node in _openElements)
        {
            if (node.Name == targetNode) return true;
            if (CurrentNode.Name is "optgroup" or "option") break;
        }
        return false;
    }

    private void ClosePElement()
    {
        GenerateImpliedEndTags("p");
        PopElementsUntil("p");
    }

    private void GenerateImpliedEndTags(string? exclude = default)
    {
        while (CurrentNode.Name != exclude && EndTagList.Contains(CurrentNode.Name))
        {
            _openElements.Pop();
        }
    }

    private void GenerateImpliedEndTagsThoroughly()
    {
        var list = new List<string>(EndTagList) { "caption", "colgroup", "tbody", "td", "tfoot", "th", "thead", "tr" };
        while (list.Contains(CurrentNode.Name))
        {
            _openElements.Pop();
        }
    }

    private void StopParsing()
    {
        _openElements.Clear();
    }

    private enum HandleResult
    {
        Continue,
        Reprocess
    }

    private enum FramesetOk
    {
        Ok,
        NotOk
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