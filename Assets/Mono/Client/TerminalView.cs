using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class TerminalView
{
    readonly ScrollView _scroll;
    readonly TerminalBuffer _buffer;
    readonly List<Label> _labels = new();
    int _viewFirst;

    public TerminalView(ScrollView scroll, int maxChunks = 16)
    {
        _scroll = scroll;
        _buffer = new TerminalBuffer(maxChunks);
    }

    public void Write(ReadOnlySpan<byte> utf8) => _buffer.Write(utf8);
    public void Reset() => _buffer.Reset();

    public void Flush()
    {
        int first = _buffer.FirstChunkIndex;
        while (_viewFirst < first && _labels.Count > 0)
        {
            _labels[0].RemoveFromHierarchy();
            _labels.RemoveAt(0);
            _viewFirst++;
        }
        _viewFirst = first;

        while (_labels.Count < _buffer.ChunkCount)
        {
            Label label = CreateLabel();
            _scroll.Add(label);
            _labels.Add(label);
        }

        for (int i = 0; i < _labels.Count; i++)
        {
            if (_buffer.TryTakeMarkup(i, out string markup))
                _labels[i].text = markup;
        }
    }

    static Label CreateLabel() => new()
    {
        enableRichText = true,
        emojiFallbackSupport = false,
        style =
        {
            whiteSpace = WhiteSpace.Pre,
            flexShrink = 0,
            marginTop = 0,
            marginBottom = 0,
            color = (Color)new Color32(0xe5, 0xe5, 0xe5, 0xff),
        },
    };
}
