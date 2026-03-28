using System;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace HostsManager.Views;

public class HostsSyntaxColorizer : DocumentColorizingTransformer
{
    private static readonly IBrush CommentBrush = new SolidColorBrush(Color.Parse("#7F8C98"));
    private static readonly IBrush IpBrush = new SolidColorBrush(Color.Parse("#6CB6FF"));
    private static readonly IBrush HostBrush = new SolidColorBrush(Color.Parse("#A5D6A7"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#FFCC80"));
    private static readonly IBrush InvalidBrush = new SolidColorBrush(Color.Parse("#EF9A9A"));

    public static bool HasIssues(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        foreach (var raw in lines)
        {
            if (HasLineIssue(raw))
            {
                return true;
            }
        }

        return false;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var commentIndex = text.IndexOf('#');
        if (commentIndex == 0)
        {
            Apply(line, 0, text.Length, CommentBrush);
            return;
        }

        var bodyLength = commentIndex >= 0 ? commentIndex : text.Length;
        var body = text[..bodyLength];

        var first = FindNextToken(body, 0);
        if (first.length == 0)
        {
            if (commentIndex >= 0)
            {
                Apply(line, commentIndex, text.Length - commentIndex, CommentBrush);
            }

            return;
        }

        var firstToken = body.Substring(first.start, first.length);
        var hasValidIp = System.Net.IPAddress.TryParse(firstToken, out _);
        Apply(line, first.start, first.length, hasValidIp ? IpBrush : InvalidBrush);

        if (!hasValidIp)
        {
            var highlightLength = Math.Max(0, bodyLength - first.start);
            if (highlightLength > first.length)
            {
                Apply(line, first.start + first.length, highlightLength - first.length, InvalidBrush);
            }

            if (commentIndex >= 0)
            {
                Apply(line, commentIndex, text.Length - commentIndex, CommentBrush);
            }

            return;
        }

        var cursor = first.start + first.length;
        var hostCount = 0;
        while (true)
        {
            var token = FindNextToken(body, cursor);
            if (token.length == 0)
            {
                break;
            }

            var hostToken = body.Substring(token.start, token.length);
            Apply(line, token.start, token.length, IsValidHostname(hostToken) ? HostBrush : WarningBrush);
            hostCount++;
            cursor = token.start + token.length;
        }

        if (hostCount == 0)
        {
            Apply(line, first.start, first.length, WarningBrush);
        }

        if (commentIndex >= 0)
        {
            Apply(line, commentIndex, text.Length - commentIndex, CommentBrush);
        }
    }

    private static (int start, int length) FindNextToken(string text, int startAt)
    {
        var index = startAt;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        if (index >= text.Length)
        {
            return (0, 0);
        }

        var end = index;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        return (index, end - index);
    }

    private static bool IsValidHostname(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 253)
        {
            return false;
        }

        var token = value.EndsWith('.') ? value[..^1] : value;
        if (token.Length == 0)
        {
            return false;
        }

        var labels = token.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length == 0)
        {
            return false;
        }

        foreach (var label in labels)
        {
            if (label.Length == 0 || label.Length > 63)
            {
                return false;
            }

            if (!char.IsLetterOrDigit(label[0]) || !char.IsLetterOrDigit(label[^1]))
            {
                return false;
            }

            for (var i = 1; i < label.Length - 1; i++)
            {
                var c = label[i];
                if (!(char.IsLetterOrDigit(c) || c == '-'))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasLineIssue(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            return false;
        }

        var commentIndex = line.IndexOf('#');
        var body = commentIndex >= 0 ? line[..commentIndex] : line;
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        var tokens = body
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (tokens.Length == 0)
        {
            return false;
        }

        if (!System.Net.IPAddress.TryParse(tokens[0], out _))
        {
            return true;
        }

        if (tokens.Length < 2)
        {
            return true;
        }

        for (var i = 1; i < tokens.Length; i++)
        {
            if (!IsValidHostname(tokens[i]))
            {
                return true;
            }
        }

        return false;
    }

    private void Apply(DocumentLine line, int start, int length, IBrush brush)
    {
        if (length <= 0)
        {
            return;
        }

        var startOffset = line.Offset + start;
        var endOffset = startOffset + length;
        ChangeLinePart(startOffset, endOffset, element =>
        {
            element.TextRunProperties.SetForegroundBrush(brush);
        });
    }
}
