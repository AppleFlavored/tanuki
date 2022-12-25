using System.Text;

namespace Tanuki.Html;

internal class HtmlTokenizer
{
    private readonly string _source;
    private readonly Queue<HtmlToken> _queuedTokens = new();
    private readonly StringBuilder _dataBuilder = new();
    private readonly List<char> _temporaryBuffer = new();
    private HtmlState _state = HtmlState.Data;
    private HtmlState _returnState = HtmlState.Data;
    private HtmlToken? _currentToken;
    private string _lastStartTagName;
    private int _position;

    private bool EndOfFile => _position >= _source.Length;

    internal HtmlTokenizer(string source)
    {
        _source = source;
    }

    internal HtmlToken? Next()
    {
        if (_queuedTokens.TryDequeue(out var token))
            return token;

        while (true)
        {
            if (EndOfFile) return null;
            if (!ProcessToken()) continue;
            return _queuedTokens.TryDequeue(out token) ? token : null;
        }
    }

    internal void SwitchState(HtmlState newState)
    {
        _state = newState;
    }

    private bool ProcessToken()
    {
        var ch = Consume();
        switch (_state)
        {
            case HtmlState.Data:
                switch (ch)
                {
                    case '&':
                        _returnState = HtmlState.Data;
                        _state = HtmlState.CharacterReference;
                        return false;
                    case '<':
                        _returnState = HtmlState.RcData;
                        _state = HtmlState.TagOpen;
                        return false;
                    case '\u0000':
                        // Parse error: unexpected null character
                        _queuedTokens.Enqueue(new HtmlToken.Character(ch));
                        return true;
                    default:
                        _queuedTokens.Enqueue(_position < _source.Length
                            ? new HtmlToken.Character(ch)
                            : new HtmlToken.EndOfFile());
                        return true;
                }
            case HtmlState.RcData:
                switch (ch)
                {
                    case '&':
                        _state = HtmlState.CharacterReference;
                        return false;
                    case '<':
                        _state = HtmlState.RcDataLessThanSign;
                        return false;
                    case '\u0000':
                        _queuedTokens.Enqueue(new HtmlToken.Character('\ufffd'));
                        return true;
                    default:
                        _queuedTokens.Enqueue(_position < _source.Length
                            ? new HtmlToken.Character(ch)
                            : new HtmlToken.EndOfFile());
                        return true;
                }
            case HtmlState.RawText:
                switch (ch)
                {
                    case '<':
                        _state = HtmlState.RawtextLessThanSign;
                        return false;
                    case '\u0000':
                        _queuedTokens.Enqueue(new HtmlToken.Character('\ufffd'));
                        return true;
                    default:
                        _queuedTokens.Enqueue(_position < _source.Length
                            ? new HtmlToken.Character(ch)
                            : new HtmlToken.EndOfFile());
                        return true;
                }
            case HtmlState.ScriptData:
                switch (ch)
                {
                    case '<':
                        _state = HtmlState.ScriptDataLessThanSign;
                        return false;
                    case '\u0000':
                        _queuedTokens.Enqueue(new HtmlToken.Character('\ufffd'));
                        return true;
                    default:
                        _queuedTokens.Enqueue(_position < _source.Length
                            ? new HtmlToken.Character(ch)
                            : new HtmlToken.EndOfFile());
                        return true;
                }
            case HtmlState.Plaintext:
                switch (ch)
                {
                    case '\u0000':
                        _queuedTokens.Enqueue(new HtmlToken.Character('\ufffd'));
                        return true;
                    default:
                        _queuedTokens.Enqueue(_position < _source.Length
                            ? new HtmlToken.Character(ch)
                            : new HtmlToken.EndOfFile());
                        return true;
                }
            case HtmlState.TagOpen:
                switch (ch)
                {
                    case '!':
                        _state = HtmlState.MarkupDeclarationOpen;
                        return false;
                    case '/':
                        _state = HtmlState.EndTagOpen;
                        return false;
                    case '?':
                        _currentToken = new HtmlToken.Comment();
                        Reconsume(HtmlState.BogusComment);
                        return false;
                    default:
                        if (char.IsAsciiLetter(ch))
                        {
                            _currentToken = new HtmlToken.StartTag();
                            Reconsume(HtmlState.TagName);
                            return false;
                        }

                        if (EndOfFile)
                        {
                            _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                        Reconsume(HtmlState.Data);
                        return true;
                }
            case HtmlState.EndTagOpen:
                if (char.IsAsciiLetter(ch))
                {
                    _currentToken = new HtmlToken.EndTag();
                    Reconsume(HtmlState.TagName);
                    return false;
                }

                if (ch == '>')
                {
                    _state = HtmlState.Data;
                    return false;
                }

                if (EndOfFile)
                {
                    _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                    _queuedTokens.Enqueue(new HtmlToken.Character('/'));
                    _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                    return true;
                }

                _currentToken = new HtmlToken.Comment();
                Reconsume(HtmlState.BogusComment);
                return false;
            case HtmlState.TagName:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        _state = HtmlState.BeforeAttributeName;
                        return false;
                    case '/':
                        _state = HtmlState.SelfClosingStartTag;
                        return false;
                    case '>':
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    case '\u0000':
                        _dataBuilder.Append('\ufffd');
                        return false;
                    default:
                        if (char.IsAsciiLetterUpper(ch))
                        {
                            _dataBuilder.Append(char.ToLower(ch));
                            return false;
                        }

                        if (EndOfFile)
                        {
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _dataBuilder.Append(ch);
                        return false;
                }
            case HtmlState.RcDataLessThanSign:
                if (ch == '/')
                {
                    _temporaryBuffer.Clear();
                    _state = HtmlState.RcDataEndTagOpen;
                    return false;
                }
                
                _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                Reconsume(HtmlState.RcData);
                return true;
            case HtmlState.RcDataEndTagOpen:
                if (char.IsAsciiLetter(ch))
                {
                    _currentToken = new HtmlToken.EndTag();
                    Reconsume(HtmlState.RcDataEndTagName);
                    return false;
                }
                
                _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                _queuedTokens.Enqueue(new HtmlToken.Character('/'));
                Reconsume(HtmlState.RcData);
                return true;
            case HtmlState.RcDataEndTagName:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        if (_lastStartTagName == _dataBuilder.ToString())
                        {
                            _state = HtmlState.BeforeAttributeName;
                            return false;
                        }
                        
                        goto rcDataEndTagName_anythingElse;
                    case '/':
                        if (_lastStartTagName == _dataBuilder.ToString())
                        {
                            _state = HtmlState.SelfClosingStartTag;
                            return false;
                        }
                        
                        goto rcDataEndTagName_anythingElse;
                    case '>':
                        if (_lastStartTagName == _dataBuilder.ToString())
                        {
                            _state = HtmlState.Data;
                            EmitTokenWithData();
                            return true;
                        }

                        goto rcDataEndTagName_anythingElse;
                    default:
                        if (char.IsAsciiLetter(ch))
                        {
                            _dataBuilder.Append(char.ToLower(ch));
                            _temporaryBuffer.Add(ch);
                            return false;
                        }

                        rcDataEndTagName_anythingElse:
                        _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                        _queuedTokens.Enqueue(new HtmlToken.Character('/'));
                        EmitCharactersInTemporaryBuffer();
                        Reconsume(HtmlState.RcData);
                        return true;
                }
            case HtmlState.RawtextLessThanSign:
                if (ch == '/')
                {
                    _temporaryBuffer.Clear();
                    _state = HtmlState.RawtextEndTagOpen;
                    return false;
                }

                _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                Reconsume(HtmlState.RawText);
                return true;
            case HtmlState.RawtextEndTagOpen:
                if (char.IsAsciiLetter(ch))
                {
                    _currentToken = new HtmlToken.EndTag();
                    Reconsume(HtmlState.RawtextEndTagName);
                    return false;
                }
                
                _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                _queuedTokens.Enqueue(new HtmlToken.Character('/'));
                Reconsume(HtmlState.RawText);
                return true;
            case HtmlState.RawtextEndTagName:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        if (_lastStartTagName == _dataBuilder.ToString())
                        {
                            _state = HtmlState.BeforeAttributeName;
                            return false;
                        }
                        
                        goto rawTextEndTagName_anythingElse;
                    case '/':
                        if (_lastStartTagName == _dataBuilder.ToString())
                        {
                            _state = HtmlState.SelfClosingStartTag;
                            return false;
                        }
                        
                        goto rawTextEndTagName_anythingElse;
                    case '>':
                        if (_lastStartTagName == _dataBuilder.ToString())
                        {
                            _state = HtmlState.Data;
                            EmitTokenWithData();
                            return true;
                        }

                        goto rawTextEndTagName_anythingElse;
                    default:
                        if (char.IsAsciiLetter(ch))
                        {
                            _dataBuilder.Append(char.ToLower(ch));
                            _temporaryBuffer.Add(ch);
                            return false;
                        }

                        rawTextEndTagName_anythingElse:
                        _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                        _queuedTokens.Enqueue(new HtmlToken.Character('/'));
                        EmitCharactersInTemporaryBuffer();
                        Reconsume(HtmlState.RawText);
                        return true;
                }
            case HtmlState.ScriptDataLessThanSign:
                switch (ch)
                {
                    case '/':
                        _temporaryBuffer.Clear();
                        _state = HtmlState.ScriptDataEndTagOpen;
                        return false;
                    case '!':
                        _state = HtmlState.ScriptDataEscapeStart;
                        _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                        _queuedTokens.Enqueue(new HtmlToken.Character('!'));
                        return true;
                    default:
                        _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                        Reconsume(HtmlState.ScriptData);
                        return true;
                }
            case HtmlState.ScriptDataEndTagOpen:
                if (char.IsAsciiLetter(ch))
                {
                    _currentToken = new HtmlToken.EndTag();
                    Reconsume(HtmlState.ScriptDataEndTagName);
                    return false;
                }
                
                _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                _queuedTokens.Enqueue(new HtmlToken.Character('/'));
                Reconsume(HtmlState.ScriptData);
                return true;
            case HtmlState.ScriptDataEndTagName:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        if (_lastStartTagName == _dataBuilder.ToString())
                        {
                            _state = HtmlState.BeforeAttributeName;
                            return false;
                        }
                        
                        goto scriptDataEndTagName_anythingElse;
                    case '/':
                        if (_lastStartTagName == _dataBuilder.ToString())
                        {
                            _state = HtmlState.SelfClosingStartTag;
                            return false;
                        }
                        
                        goto scriptDataEndTagName_anythingElse;
                    case '>':
                        if (_lastStartTagName == _dataBuilder.ToString())
                        {
                            _state = HtmlState.Data;
                            EmitTokenWithData();
                            return true;
                        }

                        goto scriptDataEndTagName_anythingElse;
                    default:
                        if (char.IsAsciiLetter(ch))
                        {
                            _dataBuilder.Append(char.ToLower(ch));
                            _temporaryBuffer.Add(ch);
                            return false;
                        }

                        scriptDataEndTagName_anythingElse:
                        _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                        _queuedTokens.Enqueue(new HtmlToken.Character('/'));
                        EmitCharactersInTemporaryBuffer();
                        Reconsume(HtmlState.ScriptData);
                        return true;
                }
            case HtmlState.ScriptDataEscapeStart:
                if (ch == '-')
                {
                    _state = HtmlState.ScriptDataEscapeStartDash;
                    _queuedTokens.Enqueue(new HtmlToken.Character('-'));
                    return true;
                }

                Reconsume(HtmlState.ScriptData);
                return false;
            case HtmlState.ScriptDataEscapeStartDash:
                if (ch == '-')
                {
                    _state = HtmlState.ScriptDataEscapedDashDash;
                    _queuedTokens.Enqueue(new HtmlToken.Character('-'));
                    return true;
                }

                Reconsume(HtmlState.ScriptData);
                return false;
            case HtmlState.ScriptDataEscaped:
                switch (ch)
                {
                    case '-':
                        _state = HtmlState.ScriptDataEscapedDash;
                        _queuedTokens.Enqueue(new HtmlToken.Character('-'));
                        return true;
                    case '<':
                        _state = HtmlState.ScriptDataEscapedLessThanSign;
                        return false;
                    case '\u0000':
                        _queuedTokens.Enqueue(new HtmlToken.Character('\ufffd'));
                        return true;
                    default:
                        _queuedTokens.Enqueue(_position < _source.Length
                            ? new HtmlToken.Character(ch)
                            : new HtmlToken.EndOfFile());
                        return true;
                }
            case HtmlState.ScriptDataEscapedDash:
                switch (ch)
                {
                    case '-':
                        _state = HtmlState.ScriptDataEscapedDashDash;
                        _queuedTokens.Enqueue(new HtmlToken.Character('-'));
                        return true;
                    case '<':
                        _state = HtmlState.ScriptDataEscapedLessThanSign;
                        return false;
                    case '\u0000':
                        _state = HtmlState.ScriptDataEscaped;
                        _queuedTokens.Enqueue(new HtmlToken.Character('\ufffd'));
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _state = HtmlState.ScriptDataEscaped;
                        _queuedTokens.Enqueue(new HtmlToken.Character(ch));
                        return true;
                }
            case HtmlState.ScriptDataEscapedDashDash:
                switch (ch)
                {
                    case '-':
                        _queuedTokens.Enqueue(new HtmlToken.Character('-'));
                        return true;
                    case '<':
                        _state = HtmlState.ScriptDataEscapedLessThanSign;
                        return false;
                    case '>':
                        _state = HtmlState.ScriptData;
                        _queuedTokens.Enqueue(new HtmlToken.Character('>'));
                        return true;
                    case '\u0000':
                        _state = HtmlState.ScriptDataEscaped;
                        _queuedTokens.Enqueue(new HtmlToken.Character('\ufffd'));
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _state = HtmlState.ScriptDataEscaped;
                        _queuedTokens.Enqueue(new HtmlToken.Character(ch));
                        return true;
                }
            case HtmlState.ScriptDataEscapedLessThanSign:
                if (ch == '/')
                {
                    _temporaryBuffer.Clear();
                    _state = HtmlState.ScriptDataEscapedEndTagOpen;
                    return false;
                }

                if (char.IsAsciiLetter(ch))
                {
                    _temporaryBuffer.Clear();
                    _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                    Reconsume(HtmlState.ScriptDataDoubleEscapeStart);
                    return true;
                }

                _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                Reconsume(HtmlState.ScriptDataEscaped);
                return true;
            case HtmlState.ScriptDataEscapedEndTagOpen:
                if (char.IsAsciiLetter(ch))
                {
                    _currentToken = new HtmlToken.EndTag();
                    Reconsume(HtmlState.ScriptDataEscapedEndTagName);
                    return false;
                }

                _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                _queuedTokens.Enqueue(new HtmlToken.Character('/'));
                Reconsume(HtmlState.ScriptDataEscaped);
                return true;
            case HtmlState.ScriptDataEscapedEndTagName:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        if (_lastStartTagName == _dataBuilder.ToString())
                        {
                            _state = HtmlState.BeforeAttributeName;
                            return false;
                        }

                        goto scriptDataEscapedEndTagName_anythingElse;
                    case '/':
                        if (_lastStartTagName == _dataBuilder.ToString())
                        {
                            _state = HtmlState.SelfClosingStartTag;
                            return false;
                        }

                        goto scriptDataEscapedEndTagName_anythingElse;
                    case '>':
                        if (_lastStartTagName == _dataBuilder.ToString())
                        {
                            _state = HtmlState.Data;
                            EmitTokenWithData();
                            return false;
                        }

                        goto scriptDataEscapedEndTagName_anythingElse;
                    default:
                        if (char.IsAsciiLetter(ch))
                        {
                            _dataBuilder.Append(char.ToLower(ch));
                            _temporaryBuffer.Add(ch);
                            return false;
                        }
                        
                        scriptDataEscapedEndTagName_anythingElse:
                        _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                        _queuedTokens.Enqueue(new HtmlToken.Character('/'));
                        EmitCharactersInTemporaryBuffer();
                        return true;
                }
            case HtmlState.ScriptDataDoubleEscapeStart:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                    case '/':
                    case '>':
                        if (CompareTemporaryBuffer("script"))
                            _state = HtmlState.ScriptDataDoubleEscaped;
                        else
                            _state = HtmlState.ScriptDataEscaped;
                        
                        _queuedTokens.Enqueue(new HtmlToken.Character(ch));
                        return true;
                    default:
                        if (char.IsAsciiLetter(ch))
                        {
                            _temporaryBuffer.Add(char.ToLower(ch));
                            _queuedTokens.Enqueue(new HtmlToken.Character(ch));
                            return true;
                        }

                        Reconsume(HtmlState.ScriptDataEscaped);
                        return false;
                }
            case HtmlState.ScriptDataDoubleEscaped:
                switch (ch)
                {
                    case '-':
                        _state = HtmlState.ScriptDataDoubleEscapedDash;
                        _queuedTokens.Enqueue(new HtmlToken.Character('-'));
                        return true;
                    case '<':
                        _state = HtmlState.ScriptDataDoubleEscapedLessThanSign;
                        _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                        return true;
                    case '\u0000':
                        _queuedTokens.Enqueue(new HtmlToken.Character('\ufffd'));
                        return true;
                    default:
                        _queuedTokens.Enqueue(_position < _source.Length
                            ? new HtmlToken.Character(ch)
                            : new HtmlToken.EndOfFile());
                        return true;
                }
            case HtmlState.ScriptDataDoubleEscapedDash:
                switch (ch)
                {
                    case '-':
                        _state = HtmlState.ScriptDataDoubleEscapedDashDash;
                        _queuedTokens.Enqueue(new HtmlToken.Character('-'));
                        return true;
                    case '<':
                        _state = HtmlState.ScriptDataDoubleEscapedLessThanSign;
                        _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                        return true;
                    case '\u0000':
                        _state = HtmlState.ScriptDataDoubleEscaped;
                        _queuedTokens.Enqueue(new HtmlToken.Character('\ufffd'));
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _state = HtmlState.ScriptDataDoubleEscaped;
                        _queuedTokens.Enqueue(new HtmlToken.Character(ch));
                        return true;
                }
            case HtmlState.ScriptDataDoubleEscapedDashDash:
                switch (ch)
                {
                    case '-':
                        _queuedTokens.Enqueue(new HtmlToken.Character('-'));
                        return true;
                    case '<':
                        _state = HtmlState.ScriptDataDoubleEscapedLessThanSign;
                        _queuedTokens.Enqueue(new HtmlToken.Character('<'));
                        return true;
                    case '>':
                        _state = HtmlState.ScriptData;
                        _queuedTokens.Enqueue(new HtmlToken.Character('>'));
                        return true;
                    case '\u0000':
                        _state = HtmlState.ScriptDataDoubleEscaped;
                        _queuedTokens.Enqueue(new HtmlToken.Character('\ufffd'));
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _state = HtmlState.ScriptDataDoubleEscaped;
                        _queuedTokens.Enqueue(new HtmlToken.Character(ch));
                        return true;
                }
            case HtmlState.ScriptDataDoubleEscapedLessThanSign:
                if (ch == '/')
                {
                    _temporaryBuffer.Clear();
                    _state = HtmlState.ScriptDataDoubleEscapeEnd;
                    _queuedTokens.Enqueue(new HtmlToken.Character('/'));
                    return true;
                }

                Reconsume(HtmlState.ScriptDataDoubleEscaped);
                return false;
            case HtmlState.ScriptDataDoubleEscapeEnd:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                    case '/':
                    case '>':
                        _state = CompareTemporaryBuffer("script")
                            ? HtmlState.ScriptDataEscaped
                            : HtmlState.ScriptDataDoubleEscaped;
                        _queuedTokens.Enqueue(new HtmlToken.Character(ch));
                        return true;
                    default:
                        if (char.IsAsciiLetter(ch))
                        {
                            _temporaryBuffer.Add(char.ToLower(ch));
                            _queuedTokens.Enqueue(new HtmlToken.Character(ch));
                            return true;
                        }

                        Reconsume(HtmlState.ScriptDataDoubleEscaped);
                        return false;
                }
            case HtmlState.BeforeAttributeName:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        return false;
                    case '/':
                    case '>':
                        Reconsume(HtmlState.AfterAttributeName);
                        return false;
                    case '=':
                        _currentToken!.CreateAttribute(ch.ToString());
                        _state = HtmlState.AttributeName;
                        return false;
                    default:
                        if (EndOfFile)
                        {
                            Reconsume(HtmlState.AfterAttributeName);
                            return false;
                        }

                        _currentToken!.CreateAttribute();
                        Reconsume(HtmlState.AttributeName);
                        return false;
                }
            case HtmlState.AttributeName:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                    case '/':
                    case '>':
                        Reconsume(HtmlState.AfterAttributeName);
                        return false;
                    case '=':
                        _state = HtmlState.BeforeAttributeValue;
                        return false;
                    case '\u0000':
                        _currentToken!.AppendAttributeName('\ufffd');
                        return false;
                    default:
                        if (EndOfFile)
                        {
                            Reconsume(HtmlState.AfterAttributeName);
                            return false;
                        }

                        _currentToken!.AppendAttributeName(char.ToLower(ch));
                        return false;
                }
            case HtmlState.AfterAttributeName:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        return false;
                    case '/':
                        _state = HtmlState.SelfClosingStartTag;
                        return false;
                    case '=':
                        _state = HtmlState.BeforeAttributeValue;
                        return false;
                    case '>':
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _currentToken!.CreateAttribute();
                        Reconsume(HtmlState.AttributeName);
                        return false;
                }
            case HtmlState.BeforeAttributeValue:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        return false;
                    case '\"':
                        _state = HtmlState.AttributeValueDoubleQuoted;
                        return false;
                    case '\'':
                        _state = HtmlState.AttributeValueSingleQuoted;
                        return false;
                    case '>':
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    default:
                        Reconsume(HtmlState.AttributeValueUnquoted);
                        return false;
                }
            case HtmlState.AttributeValueDoubleQuoted:
                switch (ch)
                {
                    case '\"':
                        _state = HtmlState.AfterAttributeValueQuoted;
                        return false;
                    case '&':
                        _returnState = HtmlState.AttributeValueDoubleQuoted;
                        _state = HtmlState.CharacterReference;
                        return false;
                    case '\u0000':
                        _currentToken!.AppendAttributeValue('\ufffd');
                        return false;
                    default:
                        if (EndOfFile)
                        {
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _currentToken!.AppendAttributeValue(ch);
                        return false;
                }
            case HtmlState.AttributeValueSingleQuoted:
                switch (ch)
                {
                    case '\'':
                        _state = HtmlState.AfterAttributeValueQuoted;
                        return false;
                    case '&':
                        _returnState = HtmlState.AttributeValueSingleQuoted;
                        _state = HtmlState.CharacterReference;
                        return false;
                    case '\u0000':
                        _currentToken!.AppendAttributeValue('\ufffd');
                        return false;
                    default:
                        if (EndOfFile)
                        {
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _currentToken!.AppendAttributeValue(ch);
                        return false;
                }
            case HtmlState.AttributeValueUnquoted:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        _state = HtmlState.BeforeAttributeName;
                        return false;
                    case '&':
                        _returnState = HtmlState.AttributeValueUnquoted;
                        _state = HtmlState.CharacterReference;
                        return false;
                    case '>':
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    case '\u0000':
                        _currentToken!.AppendAttributeValue('\ufffd');
                        return false;
                    default:
                        if (EndOfFile)
                        {
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _currentToken!.AppendAttributeValue(ch);
                        return false;
                }
            case HtmlState.AfterAttributeValueQuoted:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        _state = HtmlState.BeforeAttributeName;
                        return false;
                    case '\"':
                        _state = HtmlState.SelfClosingStartTag;
                        return false;
                    case '>':
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        Reconsume(HtmlState.BeforeAttributeName);
                        return false;
                }
            case HtmlState.SelfClosingStartTag:
                if (ch == '>')
                {
                    _currentToken!.SetSelfClosing(true);
                    _state = HtmlState.Data;
                    EmitTokenWithData();
                    return true;
                }

                if (EndOfFile)
                {
                    _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                    return true;
                }

                Reconsume(HtmlState.BeforeAttributeName);
                return false;
            case HtmlState.BogusComment:
                switch (ch)
                {
                    case '>':
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    case '\u0000':
                        _dataBuilder.Append('\ufffd');
                        return false;
                    default:
                        if (EndOfFile)
                        {
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _dataBuilder.Append(ch);
                        return false;
                }
            case HtmlState.MarkupDeclarationOpen:
                if (Peek("--", -1))
                {
                    _currentToken = new HtmlToken.Comment();
                    _state = HtmlState.CommentStart;
                    return false;
                }

                if (PeekAscii("DOCTYPE", -1))
                {
                    _state = HtmlState.Doctype;
                    return false;
                }

                if (Peek("[CDATA[", -1))
                {
                    // TODO: Get adjusted current node from parser.
                    _currentToken = new HtmlToken.Comment();
                    _dataBuilder.Append("[CDATA[");
                    _state = HtmlState.BogusComment;
                    return false;
                }

                _currentToken = new HtmlToken.Comment();
                Reconsume(HtmlState.BogusComment);
                return false;
            case HtmlState.CommentStart:
                switch (ch)
                {
                    case '-':
                        _state = HtmlState.CommentStart;
                        return false;
                    case '>':
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    default:
                        Reconsume(HtmlState.Comment);
                        return false;
                }
            case HtmlState.CommentStartDash:
                switch (ch)
                {
                    case '-':
                        _state = HtmlState.CommentEnd;
                        return false;
                    case '>':
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _dataBuilder.Append('-');
                        Reconsume(HtmlState.Comment);
                        return false;
                }
            case HtmlState.Comment:
                switch (ch)
                {
                    case '<':
                        _dataBuilder.Append(ch);
                        _state = HtmlState.CommentLessThanSign;
                        return false;
                    case '-':
                        _state = HtmlState.CommentEndDash;
                        return false;
                    case '\u0000':
                        _dataBuilder.Append('\ufffd');
                        return false;
                    default:
                        if (EndOfFile)
                        {
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _dataBuilder.Append(ch);
                        return false;
                }
            case HtmlState.CommentLessThanSign:
                switch (ch)
                {
                    case '!':
                        _dataBuilder.Append(ch);
                        _state = HtmlState.CommentLessThanSignBang;
                        return false;
                    case '<':
                        _dataBuilder.Append(ch);
                        return false;
                    default:
                        Reconsume(HtmlState.Comment);
                        return false;
                }
            case HtmlState.CommentLessThanSignBang:
                if (ch == '-')
                {
                    _state = HtmlState.CommentLessThanSignBangDash;
                    return false;
                }

                Reconsume(HtmlState.CommentEndDash);
                return false;
            case HtmlState.CommentLessThanSignBangDash:
                if (ch == '-')
                {
                    _state = HtmlState.CommentLessThanSignBangDashDash;
                    return false;
                }

                Reconsume(HtmlState.CommentEndDash);
                return false;
            case HtmlState.CommentLessThanSignBangDashDash:
                if (ch == '>' || EndOfFile)
                {
                    Reconsume(HtmlState.CommentEnd);
                    return false;
                }

                Reconsume(HtmlState.CommentEnd);
                return false;
            case HtmlState.CommentEndDash:
                if (ch == '-')
                {
                    _state = HtmlState.CommentEnd;
                    return false;
                }

                if (EndOfFile)
                {
                    EmitTokenWithData();
                    _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                    return true;
                }

                _dataBuilder.Append('-');
                Reconsume(HtmlState.Comment);
                return false;
            case HtmlState.CommentEnd:
                switch (ch)
                {
                    case '>':
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    case '!':
                        _state = HtmlState.CommentEndBang;
                        return false;
                    case '-':
                        _dataBuilder.Append('-');
                        return false;
                    default:
                        if (EndOfFile)
                        {
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _dataBuilder.Append("--");
                        Reconsume(HtmlState.Comment);
                        return false;
                }
            case HtmlState.CommentEndBang:
                switch (ch)
                {
                    case '-':
                        _dataBuilder.Append("--!");
                        _state = HtmlState.CommentEnd;
                        return false;
                    case '>':
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _dataBuilder.Append("--!");
                        Reconsume(HtmlState.Comment);
                        return false;
                }
            case HtmlState.Doctype:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        _state = HtmlState.BeforeDoctypeName;
                        return false;
                    case '>':
                        Reconsume(HtmlState.BeforeDoctypeName);
                        return false;
                    default:
                        if (EndOfFile)
                        {
                            _currentToken = new HtmlToken.Doctype { ForceQuirks = true };
                            _queuedTokens.Enqueue(_currentToken);
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        Reconsume(HtmlState.BeforeDoctypeName);
                        return false;
                }
            case HtmlState.BeforeDoctypeName:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        return false;
                    case '\u0000':
                        _currentToken = new HtmlToken.Doctype { Name = "\ufffd" };
                        _state = HtmlState.DoctypeName;
                        return false;
                    case '>':
                        _currentToken = new HtmlToken.Doctype { ForceQuirks = true };
                        _state = HtmlState.Data;
                        _queuedTokens.Enqueue(_currentToken);
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _currentToken = new HtmlToken.Doctype { ForceQuirks = true };
                            _queuedTokens.Enqueue(_currentToken);
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _currentToken = new HtmlToken.Doctype();
                        _dataBuilder.Append(char.ToLower(ch));
                        _state = HtmlState.DoctypeName;
                        return false;
                }
            case HtmlState.DoctypeName:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        _state = HtmlState.AfterDoctypeName;
                        return false;
                    case '\u0000':
                        _dataBuilder.Append('\ufffd');
                        return false;
                    case '>':
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _currentToken!.SetForceQuirks(true);
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _dataBuilder.Append(char.ToLower(ch));
                        return false;
                }
            case HtmlState.AfterDoctypeName:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        return false;
                    case '>':
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _currentToken!.SetForceQuirks(true);
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        if (PeekAscii("PUBLIC", -1))
                        {
                            _state = HtmlState.AfterDoctypePublicKeyword;
                            return false;
                        }

                        if (PeekAscii("SYSTEM", -1))
                        {
                            _state = HtmlState.AfterDoctypeSystemKeyword;
                            return false;
                        }

                        _currentToken!.SetForceQuirks(true);
                        Reconsume(HtmlState.BogusDoctype);
                        return false;
                }
            case HtmlState.AfterDoctypePublicKeyword:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        _state = HtmlState.BeforeDoctypePublicIdentifier;
                        return false;
                    case '\"':
                        _state = HtmlState.DoctypePublicIdentifierDoubleQuoted;
                        return false;
                    case '\'':
                        _state = HtmlState.DoctypePublicIdentifierSingleQuoted;
                        return false;
                    case '>':
                        _currentToken!.SetForceQuirks(true);
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _currentToken!.SetForceQuirks(true);
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _currentToken!.SetForceQuirks(true);
                        Reconsume(HtmlState.BogusDoctype);
                        return false;
                }
            case HtmlState.BeforeDoctypePublicIdentifier:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        return false;
                    case '\"':
                        _state = HtmlState.DoctypePublicIdentifierDoubleQuoted;
                        return false;
                    case '\'':
                        _state = HtmlState.DoctypePublicIdentifierSingleQuoted;
                        return false;
                    case '>':
                        _currentToken!.SetForceQuirks(true);
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _currentToken!.SetForceQuirks(true);
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _currentToken!.SetForceQuirks(true);
                        Reconsume(HtmlState.BogusDoctype);
                        return false;
                }
            case HtmlState.DoctypePublicIdentifierDoubleQuoted:
                switch (ch)
                {
                    case '\"':
                        _state = HtmlState.AfterDoctypePublicIdentifier;
                        return false;
                    case '\u0000':
                        _currentToken!.AppendPublicIdentifier('\ufffd');
                        return false;
                    case '>':
                        _currentToken!.SetForceQuirks(true);
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _currentToken!.SetForceQuirks(true);
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _currentToken!.AppendPublicIdentifier(ch);
                        return false;
                }
            case HtmlState.DoctypePublicIdentifierSingleQuoted:
                switch (ch)
                {
                    case '\'':
                        _state = HtmlState.AfterDoctypePublicIdentifier;
                        return false;
                    case '\u0000':
                        _currentToken!.AppendPublicIdentifier('\ufffd');
                        return false;
                    case '>':
                        _currentToken!.SetForceQuirks(true);
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _currentToken!.SetForceQuirks(true);
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _currentToken!.AppendPublicIdentifier(ch);
                        return false;
                }
            case HtmlState.AfterDoctypePublicIdentifier:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        _state = HtmlState.BetweenDoctypePublicAndSystemIdentifiers;
                        return false;
                    case '>':
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    case '\"':
                        _state = HtmlState.DoctypeSystemIdentifierDoubleQuoted;
                        return false;
                    case '\'':
                        _state = HtmlState.DoctypeSystemIdentifierSingleQuoted;
                        return false;
                    default:
                        if (EndOfFile)
                        {
                            _currentToken!.SetForceQuirks(true);
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return false;
                        }

                        _currentToken!.SetForceQuirks(true);
                        Reconsume(HtmlState.BogusDoctype);
                        return false;
                }
            case HtmlState.BetweenDoctypePublicAndSystemIdentifiers:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        return false;
                    case '>':
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    case '\"':
                        _state = HtmlState.DoctypeSystemIdentifierDoubleQuoted;
                        return false;
                    case '\'':
                        _state = HtmlState.DoctypeSystemIdentifierSingleQuoted;
                        return false;
                    default:
                        if (EndOfFile)
                        {
                            _currentToken!.SetForceQuirks(true);
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _currentToken!.SetForceQuirks(true);
                        Reconsume(HtmlState.BogusDoctype);
                        return false;
                }
            case HtmlState.AfterDoctypeSystemKeyword:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        _state = HtmlState.BeforeDoctypeSystemIdentifier;
                        return false;
                    case '\"':
                        _state = HtmlState.DoctypeSystemIdentifierDoubleQuoted;
                        return false;
                    case '\'':
                        _state = HtmlState.DoctypeSystemIdentifierSingleQuoted;
                        return false;
                    case '>':
                        _currentToken!.SetForceQuirks(true);
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _currentToken!.SetForceQuirks(true);
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _currentToken!.SetForceQuirks(true);
                        Reconsume(HtmlState.BogusDoctype);
                        return false;
                }
            case HtmlState.BeforeDoctypeSystemIdentifier:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        return false;
                    case '\"':
                        _state = HtmlState.DoctypeSystemIdentifierDoubleQuoted;
                        return false;
                    case '\'':
                        _state = HtmlState.DoctypeSystemIdentifierSingleQuoted;
                        return false;
                    case '>':
                        _currentToken!.SetForceQuirks(true);
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _currentToken!.SetForceQuirks(true);
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _currentToken!.SetForceQuirks(true);
                        Reconsume(HtmlState.BogusDoctype);
                        return false;
                }
            case HtmlState.DoctypeSystemIdentifierDoubleQuoted:
                switch (ch)
                {
                    case '\"':
                        _state = HtmlState.AfterDoctypeSystemIdentifier;
                        return false;
                    case '\u0000':
                        _currentToken!.AppendPublicIdentifier('\ufffd');
                        return false;
                    case '>':
                        _currentToken!.SetForceQuirks(true);
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _currentToken!.SetForceQuirks(true);
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _currentToken!.AppendSystemIdentifier(ch);
                        return false;
                }
            case HtmlState.DoctypeSystemIdentifierSingleQuoted:
                switch (ch)
                {
                    case '\'':
                        _state = HtmlState.AfterDoctypeSystemIdentifier;
                        return false;
                    case '\u0000':
                        _currentToken!.AppendPublicIdentifier('\ufffd');
                        return false;
                    case '>':
                        _currentToken!.SetForceQuirks(true);
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _currentToken!.SetForceQuirks(true);
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        _currentToken!.AppendSystemIdentifier(ch);
                        return false;
                }
            case HtmlState.AfterDoctypeSystemIdentifier:
                switch (ch)
                {
                    case '\t':
                    case '\u000a': // Line Feed (LF)
                    case '\u000c': // Form Feed (FF)
                    case ' ':
                        return false;
                    case '>':
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    default:
                        if (EndOfFile)
                        {
                            _currentToken!.SetForceQuirks(true);
                            EmitTokenWithData();
                            _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                            return true;
                        }

                        Reconsume(HtmlState.BogusDoctype);
                        return false;
                }
            case HtmlState.BogusDoctype:
                switch (ch)
                {
                    case '>':
                        _state = HtmlState.Data;
                        EmitTokenWithData();
                        return true;
                    case '\u0000':
                        return false;
                    default:
                        if (!EndOfFile) return false;

                        EmitTokenWithData();
                        _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                        return true;
                }
            case HtmlState.CDataSection:
                if (ch == ']')
                {
                    _state = HtmlState.CDataSectionBracket;
                    return false;
                }

                if (EndOfFile)
                {
                    _queuedTokens.Enqueue(new HtmlToken.EndOfFile());
                    return true;
                }
                
                _queuedTokens.Enqueue(new HtmlToken.Character(ch));
                return true;
            case HtmlState.CDataSectionBracket:
                if (ch == ']')
                {
                    _state = HtmlState.CDataSectionEnd;
                    return false;
                }

                _queuedTokens.Enqueue(new HtmlToken.Character(']'));
                Reconsume(HtmlState.CDataSection);
                return true;
            case HtmlState.CDataSectionEnd:
                if (ch == ']')
                {
                    _queuedTokens.Enqueue(new HtmlToken.Character(']'));
                    return true;
                }

                if (ch == '>')
                {
                    _state = HtmlState.Data;
                    return false;
                }
                
                _queuedTokens.Enqueue(new HtmlToken.Character(']'));
                _queuedTokens.Enqueue(new HtmlToken.Character(']'));
                Reconsume(HtmlState.CDataSection);
                return true;
            case HtmlState.CharacterReference:
                _temporaryBuffer.Clear();

                if (char.IsAsciiLetterOrDigit(ch))
                {
                    Reconsume(HtmlState.NamedCharacterReference);
                    return false;
                }

                if (ch == '#')
                {
                    _temporaryBuffer.Add(ch);
                    _state = HtmlState.NumericCharacterReference;
                    return false;
                }

                FlushCharactersAsCharacterReference();
                Reconsume(_returnState);
                return false;
            // TODO: HtmlState.NamedCharacterReference
            // TODO: HtmlState.AmbiguousAmpersand
            // TODO: HtmlState.NumericCharacterReference
            // TODO: HtmlState.HexadecimalCharacterReferenceStart
            // TODO: HtmlState.DecimalCharacterReferenceStart
            // TODO: HtmlState.HexadecimalCharacterReference
            // TODO: HtmlState.DecimalCharacterReference
            // TODO: HtmlState.NumericCharacterReferenceEnd
            default:
                throw new Exception($"Tokenizer reached unhandled state: {_state}");
        }
    }

    private char Consume()
    {
        var next = _source[_position];
        _position++;
        return next;
    }

    private void EmitCharactersInTemporaryBuffer()
    {
        foreach (var c in _temporaryBuffer)
            _queuedTokens.Enqueue(new HtmlToken.Character(c));
    }

    private void FlushCharactersAsCharacterReference()
    {
        foreach (var c in _temporaryBuffer)
        {
            if (_returnState
                is HtmlState.AttributeValueDoubleQuoted
                or HtmlState.AttributeValueSingleQuoted
                or HtmlState.AttributeValueUnquoted)
            {
                _currentToken!.AppendAttributeValue(c);
            }
            else
            {
                _queuedTokens.Enqueue(new HtmlToken.Character(c));
            }
        }
    }

    private bool PeekAscii(string str, int offset = 0)
    {
        str = str.ToLower();
        for (var i = 0; i < str.Length; i++)
        {
            if (str[i] != char.ToLower(_source[_position + offset + i]))
                return false;
        }
        _position += str.Length + offset;
        return true;
    }

    private bool Peek(string str, int offset = 0)
    {
        for (var i = 0; i < str.Length; i++)
        {
            if (_source[_position + offset + i] != str[i])
                return false;
        }
        _position += str.Length + offset;
        return true;
    }

    private void Reconsume(HtmlState newState)
    {
        _position--;
        _state = newState;
    }

    private void EmitTokenWithData()
    {
        // For finding the appropriate end tag.
        if (_currentToken is HtmlToken.StartTag)
            _lastStartTagName = _dataBuilder.ToString();
        
        _currentToken!.SetData(_dataBuilder);
        _queuedTokens.Enqueue(_currentToken);
        _dataBuilder.Clear();
    }

    private bool CompareTemporaryBuffer(string str)
    {
        if (_temporaryBuffer.Count != str.Length)
            return false;
        for (var i = 0; i < str.Length; i++)
        {
            if (_temporaryBuffer[i] != str[i])
                return false;
        }
        return true;
    }
}