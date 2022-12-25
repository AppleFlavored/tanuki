using Tanuki.Dom;

namespace Tanuki;

public static class NodeExtensions
{
    public static void Print(this Node node, int level = 0)
    {
        if (level > 0)
        {
            Console.Write(new string(' ', level * 4));
            Console.Write("└─── ");
        }
    
        Console.Write($"{node.Name} ");
        switch (node)
        {
            case Text text:
                Console.Write($"\"{text.Data}\"");
                break;
            case Comment comment:
                Console.Write($"<!--{comment.Data}-->");
                break;
            case DocumentType:
                Console.Write($"(doctype)");
                break;
            default:
                Console.Write(level == 0 ? "(root)" : "");
                break;
        }

        Console.WriteLine();
        if (node.Children is null) return;
        foreach (var child in node.Children)
        {
            Print(child, level + 1);
        }
    }
}