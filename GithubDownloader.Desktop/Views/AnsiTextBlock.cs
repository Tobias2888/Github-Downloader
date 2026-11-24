using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace GithubDownloader.Desktop.Views;

public sealed class AnsiTextBlock : TextBlock
{
    public static readonly StyledProperty<string?> AnsiTextProperty =
        AvaloniaProperty.Register<AnsiTextBlock, string?>(nameof(AnsiText), string.Empty);

    static AnsiTextBlock()
    {
        AnsiTextProperty.Changed.AddClassHandler<AnsiTextBlock>((control, _) => control.RenderAnsiText(control.AnsiText));
    }

    public string? AnsiText
    {
        get => GetValue(AnsiTextProperty);
        set => SetValue(AnsiTextProperty, value);
    }

    private void RenderAnsiText(string? value)
    {
        string text = value ?? string.Empty;
        EnsureInlines().Clear();
        Color? foreground = null;
        Color? background = null;
        bool bold = false;
        int textStart = 0;
        int position = 0;

        while (position < text.Length)
        {
            if (text[position] != '\u001b' || position + 1 >= text.Length)
            {
                position++;
                continue;
            }

            if (text[position + 1] == '[')
            {
                int end = FindControlSequenceEnd(text, position + 2);
                if (end < 0) { position++; continue; }
                AppendRun(text[textStart..position], foreground, background, bold);
                if (text[end] == 'm') ApplySgr(text.AsSpan(position + 2, end - position - 2), ref foreground, ref background, ref bold);
                position = end + 1;
                textStart = position;
                continue;
            }

            if (text[position + 1] == ']')
            {
                int end = FindOperatingSystemCommandEnd(text, position + 2);
                if (end >= 0)
                {
                    AppendRun(text[textStart..position], foreground, background, bold);
                    position = end;
                    textStart = position;
                    continue;
                }
            }

            position++;
        }

        AppendRun(text[textStart..], foreground, background, bold);
    }

    private void AppendRun(string text, Color? foreground, Color? background, bool bold)
    {
        if (text.Length == 0) return;
        Run run = new(text);
        if (foreground.HasValue) run.Foreground = new SolidColorBrush(foreground.Value);
        if (background.HasValue) run.Background = new SolidColorBrush(background.Value);
        if (bold) run.FontWeight = FontWeight.Bold;
        EnsureInlines().Add(run);
    }

    private InlineCollection EnsureInlines()
    {
        InlineCollection? inlines = Inlines;
        if (inlines is not null) return inlines;
        InlineCollection created = new();
        Inlines = created;
        return created;
    }

    private static int FindControlSequenceEnd(string value, int start)
    {
        for (int index = start; index < value.Length; index++)
            if (value[index] is >= '@' and <= '~') return index;
        return -1;
    }

    private static int FindOperatingSystemCommandEnd(string value, int start)
    {
        for (int index = start; index < value.Length; index++)
        {
            if (value[index] == '\a') return index + 1;
            if (value[index] == '\u001b' && index + 1 < value.Length && value[index + 1] == '\\') return index + 2;
        }
        return -1;
    }

    private static void ApplySgr(ReadOnlySpan<char> parameters, ref Color? foreground, ref Color? background, ref bool bold)
    {
        List<int> values = [];
        if (parameters.IsEmpty) values.Add(0);
        else
        {
            string[] components = parameters.ToString().Split(';', StringSplitOptions.None);
            foreach (string component in components)
                values.Add(int.TryParse(component, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0);
        }

        int index = 0;
        while (index < values.Count)
        {
            int code = values[index++];
            if (code == 0) { foreground = null; background = null; bold = false; }
            else if (code == 1) bold = true;
            else if (code == 22) bold = false;
            else if (code == 39) foreground = null;
            else if (code == 49) background = null;
            else if (code is >= 30 and <= 37) foreground = BasicColor(code - 30, false);
            else if (code is >= 90 and <= 97) foreground = BasicColor(code - 90, true);
            else if (code is >= 40 and <= 47) background = BasicColor(code - 40, false);
            else if (code is >= 100 and <= 107) background = BasicColor(code - 100, true);
            else if (code is 38 or 48)
            {
                bool isForeground = code == 38;
                if (index >= values.Count) break;
                int mode = values[index++];
                Color? color = null;
                if (mode == 2 && index + 2 < values.Count)
                {
                    color = Color.FromRgb(ToByte(values[index]), ToByte(values[index + 1]), ToByte(values[index + 2]));
                    index += 3;
                }
                else if (mode == 5 && index < values.Count)
                {
                    color = IndexedColor(values[index++]);
                }
                if (isForeground) foreground = color;
                else background = color;
            }
        }
    }

    private static Color BasicColor(int index, bool bright)
    {
        Color[] normal = [Color.FromRgb(0, 0, 0), Color.FromRgb(205, 49, 49), Color.FromRgb(13, 188, 121),
            Color.FromRgb(229, 229, 16), Color.FromRgb(36, 114, 200), Color.FromRgb(188, 63, 188),
            Color.FromRgb(17, 168, 205), Color.FromRgb(229, 229, 229)];
        Color[] intense = [Color.FromRgb(102, 102, 102), Color.FromRgb(241, 76, 76), Color.FromRgb(35, 209, 139),
            Color.FromRgb(245, 245, 67), Color.FromRgb(59, 142, 234), Color.FromRgb(214, 112, 214),
            Color.FromRgb(41, 184, 219), Color.FromRgb(255, 255, 255)];
        return bright ? intense[index] : normal[index];
    }

    private static Color IndexedColor(int index)
    {
        int bounded = Math.Clamp(index, 0, 255);
        if (bounded < 8) return BasicColor(bounded, false);
        if (bounded < 16) return BasicColor(bounded - 8, true);
        if (bounded < 232)
        {
            int cube = bounded - 16;
            byte[] levels = [0, 95, 135, 175, 215, 255];
            return Color.FromRgb(levels[cube / 36], levels[(cube / 6) % 6], levels[cube % 6]);
        }
        byte gray = (byte)(8 + (bounded - 232) * 10);
        return Color.FromRgb(gray, gray, gray);
    }

    private static byte ToByte(int value) => (byte)Math.Clamp(value, 0, 255);
}
