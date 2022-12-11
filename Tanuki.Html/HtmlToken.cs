using System.Text;

namespace Tanuki.Html;

public abstract class HtmlToken
{
    internal void SetData(StringBuilder builder)
    {
        var data = builder.ToString();
        switch (this)
        {
            case Comment comment:
                comment.Data = data;
                break;
            case StartTag tag:
                tag.Name = data;
                break;
            case EndTag tag:
                tag.Name = data;
                break;
            case Doctype doctype:
                doctype.Name = data;
                break;
            default:
                throw new Exception("Data cannot be set for unsupported token type.");
        }
    }
    
    internal void AppendData(char c)
    {
        switch (this)
        {
            case Comment comment:
                comment.Data += c;
                break;
            case StartTag tag:
                tag.Name += c;
                break;
            case EndTag tag:
                tag.Name += c;
                break;
            default:
                throw new Exception("Data cannot be set for unsupported token type.");
        }
    }

    public string GetData() => this switch
    {
        Comment comment => comment.Data,
        StartTag tag => tag.Name,
        EndTag tag => tag.Name,
        Doctype doctype => doctype.Name,
        Character ch => ch.Data.ToString(),
        _ => ""
    };

    internal void SetSelfClosing(bool flag)
    {
        switch (this)
        {
            case StartTag tag:
                tag.SelfClosing = flag;
                break;
            case EndTag tag:
                tag.SelfClosing = flag;
                break;
            default:
                throw new Exception("Self-closing flag cannot be set for non-tag token.");
        }
    }
    
    internal void SetForceQuirks(bool flag)
    {
        if (this is not Doctype token)
            throw new Exception("Force quirks flag cannot be set for non-doctype token.");
        token.ForceQuirks = flag;
    }

    public void AppendPublicIdentifier(char c)
    {
        if (this is not Doctype token)
            throw new Exception("Public identifier cannot be set for non-doctype token.");

        token.PublicIdentifier ??= "";
        token.PublicIdentifier += c;
    }
    
    public void AppendSystemIdentifier(char c)
    {
        if (this is not Doctype token)
            throw new Exception("System identifier cannot be set for non-doctype token.");

        token.PublicIdentifier ??= "";
        token.PublicIdentifier += c;
    }

    public void CreateAttribute(string initialName = "", string initialValue = "")
    {
        switch (this)
        {
            case StartTag tag:
                tag.Attributes.Add(new Attribute(initialName, initialValue));
                break;
            case EndTag tag:
                tag.Attributes.Add(new Attribute(initialName, initialValue));
                break;
            default:
                throw new Exception("Cannot create attribute for non-tag token.");
        }
    }

    public void AppendAttributeName(char c)
    {
        Attribute attr;
        switch (this)
        {
            case StartTag tag:
                attr = tag.Attributes[^1];
                attr.Name += c;
                break;
            case EndTag tag:
                attr = tag.Attributes[^1];
                attr.Name += c;
                break;
            default:
                throw new Exception("Attribute cannot be set for non-tag token.");
        }
    }
    
    public void AppendAttributeValue(char c)
    {
        Attribute attr;
        switch (this)
        {
            case StartTag tag:
                attr = tag.Attributes[^1];
                attr.Value += c;
                break;
            case EndTag tag:
                attr = tag.Attributes[^1];
                attr.Value += c;
                break;
            default:
                throw new Exception("Attribute cannot be set for non-tag token.");
        }
    }

    public sealed class Doctype : HtmlToken
    {
        public string Name { get; set; }
        public string? PublicIdentifier { get; set; }
        public string? SystemIdentifier { get; set; }
        public bool ForceQuirks { get; set; }
    }

    public sealed class StartTag : HtmlToken
    {
        public string Name { get; set; }
        public bool SelfClosing { get; set; }
        public List<Attribute> Attributes { get; } = new();
    }

    public sealed class EndTag : HtmlToken
    {
        public string Name { get; set; }
        public bool SelfClosing { get; set; }
        public List<Attribute> Attributes { get; } = new();
    }

    public sealed class Comment : HtmlToken
    {
        public string Data { get; set; }
    }

    public sealed class Character : HtmlToken
    {
        public char Data { get; init; }

        public Character(char data)
        {
            Data = data;
        }
    }

    public sealed class EndOfFile : HtmlToken
    {
    }
}