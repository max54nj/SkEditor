﻿using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using SkEditor.API;

namespace SkEditor.Utilities.Editor;

public class SelectionHandler
{
    public static void OnSelectionChanged(object? sender, EventArgs e)
    {
        TextEditor? textEditor = SkEditorAPI.Files.GetCurrentOpenedFile()?.Editor;
        if (textEditor == null) return;
        
        textEditor.TextArea.TextView.LineTransformers
            .Where(x => x is OccurenceBackgroundTransformer)
            .ToList()
            .ForEach(x => textEditor.TextArea.TextView.LineTransformers.Remove(x));

        int start = textEditor.SelectionStart;
        int length = textEditor.SelectionLength;
        string selectedText = textEditor.Document.GetText(start, length);

        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return;
        }

        IEnumerable<SimpleSegment> wordOccurrences =
            TextEditorUtilities.GetWordOccurrences(selectedText, textEditor.Document);
        IList<IVisualLineTransformer>? lineTransformers = textEditor.TextArea.TextView.LineTransformers;

        foreach (SimpleSegment segment in wordOccurrences.Where(s => s.Offset != start || s.Length != length))
        {
            int lineNumber = textEditor.Document.GetLineByOffset(segment.Offset).LineNumber;
            lineTransformers.Add(new OccurenceBackgroundTransformer
            {
                LineNumber = lineNumber,
                StartOffset = segment.Offset,
                EndOffset = segment.Offset + segment.Length
            });
        }
    }
}

public class OccurenceBackgroundTransformer : DocumentColorizingTransformer
{
    public int LineNumber { get; set; }

    public int StartOffset { get; set; }
    public int EndOffset { get; set; }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.LineNumber != LineNumber || StartOffset < 0 || EndOffset < 0)
        {
            return;
        }

        try
        {
            ChangeLinePart(StartOffset, EndOffset, ApplyChanges);
        }
        catch
        {
            // ignored
        }
    }

    private void ApplyChanges(VisualLineElement element)
    {
        Color color = new(100, 50, 211, 240);
        ImmutableSolidColorBrush brush = new(color);
        element.TextRunProperties.SetBackgroundBrush(brush);
    }
}