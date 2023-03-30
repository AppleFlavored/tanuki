namespace Tanuki.DeprecatedHtml;

[Flags]
public enum ParsingFlags : byte
{
    None = 0,
    Scripting = 1 << 0
}