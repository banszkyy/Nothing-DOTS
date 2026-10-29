using System;
using System.Collections.Generic;
using System.Text;

[Flags]
enum TerminalColor : byte
{
    Black = 0b_0000,
    Blue = 0b_0001,
    Green = 0b_0010,
    Cyan = 0b_0011,
    Red = 0b_0100,
    Magenta = 0b_0101,
    Yellow = 0b_0110,
    White = 0b_0111,
    BrightBlack = 0b_1000,
    BrightBlue = 0b_1001,
    BrightGreen = 0b_1010,
    BrightCyan = 0b_1011,
    BrightRed = 0b_1100,
    BrightMagenta = 0b_1101,
    BrightYellow = 0b_1110,
    BrightWhite = 0b_1111,
}

[Flags]
enum TerminalMode : byte
{
    None = 0,
    Bold = 1 << 0,
    Dim = 1 << 1,
    Italic = 1 << 2,
    Underline = 1 << 3,
    Blinking = 1 << 4,
    Inverse = 1 << 5,
    Hidden = 1 << 6,
    Strikethrough = 1 << 7,
}

readonly struct TerminalCharacter
{
    public readonly char Character;
    public readonly TerminalMode Mode;
    public readonly TerminalColor Color;
    public TerminalColor Foreground => (TerminalColor)((int)Color & 0b1111);
    public TerminalColor Background => (TerminalColor)((int)Color >> 4);

    public TerminalCharacter(char character, TerminalMode mode, TerminalColor color)
    {
        Character = character;
        Mode = mode;
        Color = color;
    }
}

sealed class TerminalLine
{
    static readonly string[] Hex =
    {
        "#000000", "#2472c8", "#0dbc79", "#11a8cd", "#cd3131", "#bc3fbc", "#e5e510", "#e5e5e5",
        "#666666", "#3b8eea", "#23d18b", "#29b8db", "#f14c4c", "#d670d6", "#f5f543", "#ffffff",
    };

    static readonly TerminalCharacter Blank = new(' ', TerminalMode.None, (TerminalColor)0b0111);

    readonly TerminalChunk _owner;
    TerminalCharacter[] _cells = new TerminalCharacter[96];
    int _count;
    string _markup = string.Empty;
    bool _stale;

    public TerminalLine(TerminalChunk owner)
    {
        _owner = owner;
    }

    void Touch()
    {
        _stale = true;
        _owner.Dirty = true;
    }

    void Push(TerminalCharacter c)
    {
        if (_count == _cells.Length) Array.Resize(ref _cells, _count * 2);
        _cells[_count++] = c;
    }

    public void Put(int i, TerminalCharacter c)
    {
        if (i < _count)
        {
            ref TerminalCharacter cur = ref _cells[i];
            if (cur.Character == c.Character && cur.Mode == c.Mode && cur.Color == c.Color) return;
            cur = c;
        }
        else
        {
            while (_count < i) Push(Blank);
            Push(c);
        }
        Touch();
    }

    public void RemoveAt(int i)
    {
        if ((uint)i >= (uint)_count) return;
        Array.Copy(_cells, i + 1, _cells, i, _count - i - 1);
        _count--;
        Touch();
    }

    public string GetMarkup(StringBuilder scratch)
    {
        if (_stale)
        {
            _markup = Build(scratch);
            _stale = false;
        }
        return _markup;
    }

    string Build(StringBuilder builder)
    {
        if (_count == 0) return string.Empty;
        builder.Clear();

        TerminalColor fg = TerminalColor.White, bg = TerminalColor.Black;
        TerminalMode mod = TerminalMode.None;

        for (int i = 0; i < _count; i++)
        {
            TerminalCharacter c = _cells[i];

            if (c.Mode != mod)
            {
                AppendModeDelta(builder, mod, c.Mode);
                mod = c.Mode;
            }

            TerminalColor cf = c.Foreground, cb = c.Background;
            if (cf != fg)
            {
                if (fg != TerminalColor.White) builder.Append("</color>");
                if (cf != TerminalColor.White) builder.Append("<color=").Append(Hex[(int)cf]).Append('>');
                fg = cf;
            }
            if (cb != bg)
            {
                if (bg != TerminalColor.Black) builder.Append("</mark>");
                if (cb != TerminalColor.Black) builder.Append("<mark=").Append(Hex[(int)cb]).Append("ff>");
                bg = cb;
            }

            if (c.Character == '<') builder.Append("<noparse><</noparse>");
            else builder.Append(c.Character);
        }

        AppendModeDelta(builder, mod, TerminalMode.None);
        if (fg != TerminalColor.White) builder.Append("</color>");
        if (bg != TerminalColor.Black) builder.Append("</mark>");
        return builder.ToString();
    }

    static void AppendModeDelta(StringBuilder builder, TerminalMode from, TerminalMode to)
    {
        TerminalMode d = from ^ to;
        if (d == 0) return;
        Toggle(builder, d, to, TerminalMode.Bold, "<b>", "</b>");
        Toggle(builder, d, to, TerminalMode.Italic, "<i>", "</i>");
        Toggle(builder, d, to, TerminalMode.Strikethrough, "<s>", "</s>");
        Toggle(builder, d, to, TerminalMode.Underline, "<u>", "</u>");
    }

    static void Toggle(StringBuilder builder, TerminalMode changed, TerminalMode now, TerminalMode flag, string open, string close)
    {
        if ((changed & flag) != 0) builder.Append((now & flag) != 0 ? open : close);
    }
}

sealed class TerminalChunk
{
    public readonly TerminalLine[] Lines = new TerminalLine[TerminalBuffer.LinesPerChunk];
    public int LineCount;
    public bool Dirty = true;
    string _markup = string.Empty;

    public TerminalLine AddLine()
    {
        Dirty = true;
        return Lines[LineCount++] = new TerminalLine(this);
    }

    public bool TryRebuild(StringBuilder chunkSb, StringBuilder lineSb, out string markup)
    {
        if (!Dirty)
        {
            markup = _markup;
            return false;
        }

        chunkSb.Clear();
        for (int i = 0; i < LineCount; i++)
        {
            if (i > 0) chunkSb.Append('\n');
            chunkSb.Append(Lines[i].GetMarkup(lineSb));
        }
        _markup = chunkSb.ToString();
        Dirty = false;
        markup = _markup;
        return true;
    }
}

sealed class TerminalBuffer
{
    public const int LinesPerChunk = 64;

    static readonly TerminalColor[] AnsiToColor =
    {
        TerminalColor.Black, TerminalColor.Red, TerminalColor.Green, TerminalColor.Yellow,
        TerminalColor.Blue, TerminalColor.Magenta, TerminalColor.Cyan, TerminalColor.White,
    };

    enum ParseState : byte { Ground, Escape, Csi }

    readonly int _maxChunks;
    readonly List<TerminalChunk> _chunks = new();
    readonly StringBuilder _chunkSb = new(8 * 1024);
    readonly StringBuilder _lineSb = new(512);
    readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    readonly int[] _params = new int[16];
    char[] _charBuf = new char[1024];

    ParseState _state;
    int _paramCount, _param;
    bool _hasParam;

    TerminalColor _fg, _bg;
    TerminalMode _mode;

    int _cursorX, _cursorY;
    int _firstChunk;
    TerminalLine? _line;

    public TerminalBuffer(int maxChunks = 16)
    {
        _maxChunks = Math.Max(2, maxChunks);
        Reset();
    }

    public int FirstChunkIndex => _firstChunk;
    public int ChunkCount => _chunks.Count;

    public bool TryTakeMarkup(int i, out string markup) => _chunks[i].TryRebuild(_chunkSb, _lineSb, out markup);

    public void Reset()
    {
        _decoder.Reset();
        _state = ParseState.Ground;
        ResetPen();
        ClearScreen();
    }

    public void Write(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty) return;
        int max = Encoding.UTF8.GetMaxCharCount(utf8.Length);
        if (_charBuf.Length < max) _charBuf = new char[Math.Max(max, _charBuf.Length * 2)];
        int n = _decoder.GetChars(utf8, _charBuf, flush: false);
        Write(new ReadOnlySpan<char>(_charBuf, 0, n));
    }

    public void Write(ReadOnlySpan<char> text)
    {
        foreach (char ch in text)
        {
            switch (_state)
            {
                case ParseState.Ground:
                    Ground(ch);
                    break;
                case ParseState.Escape:
                    if (ch == '[')
                    {
                        _state = ParseState.Csi;
                        _paramCount = 0;
                        _param = 0;
                        _hasParam = false;
                    }
                    else _state = ParseState.Ground;
                    break;
                case ParseState.Csi:
                default:
                    Csi(ch);
                    break;
            }
        }
    }

    void Ground(char ch)
    {
        switch (ch)
        {
            case '\u001b': _state = ParseState.Escape; break;
            case '\n': LineFeed(); break;
            case '\r': _cursorX = 0; break;
            case '\b':
                if (_cursorX > 0) _line.RemoveAt(--_cursorX);
                break;
            default:
                if (ch is < ' ' and not '\t') break;
                _line.Put(_cursorX++, new TerminalCharacter(ch, _mode, (TerminalColor)(((byte)_bg << 4) | (byte)_fg)));
                break;
        }
    }

    void Csi(char ch)
    {
        if (ch is >= '0' and <= '9')
        {
            if (_param < 100_000) _param = _param * 10 + (ch - '0');
            _hasParam = true;
        }
        else if (ch == ';')
        {
            PushParam();
        }
        else if (ch is >= '@' and <= '~')
        {
            if (_hasParam || _paramCount > 0) PushParam();
            _state = ParseState.Ground;
            Dispatch(ch);
        }
    }

    void PushParam()
    {
        if (_paramCount < _params.Length) _params[_paramCount++] = _hasParam ? _param : 0;
        _param = 0;
        _hasParam = false;
    }

    void Dispatch(char final)
    {
        switch (final)
        {
            case 'H':
                if (_paramCount == 0)
                {
                    _cursorX = 0;
                    _cursorY = 0;
                    _line = GetLine(0);
                }
                break;
            case 'J':
                if (_paramCount == 0 || _params[0] is 0 or 2 or 3) ClearScreen();
                break;
            case 'm':
                ApplySgr();
                break;
            default:
                break;
        }
    }

    void ApplySgr()
    {
        if (_paramCount == 0)
        {
            ResetPen();
            return;
        }

        for (int i = 0; i < _paramCount; i++)
        {
            int p = _params[i];
            switch (p)
            {
                case 0: ResetPen(); break;
                case >= 1 and <= 9: _mode |= ModeBit(p); break;
                case 22: _mode &= ~(TerminalMode.Bold | TerminalMode.Dim); break;
                case >= 23 and <= 29: _mode &= ~ModeBit(p - 20); break;

                case >= 30 and <= 37: _fg = AnsiToColor[p - 30]; break;
                case 39: _fg = TerminalColor.White; break;
                case >= 40 and <= 47: _bg = AnsiToColor[p - 40]; break;
                case 49: _bg = TerminalColor.Black; break;
                case >= 90 and <= 97: _fg = Bright(AnsiToColor[p - 90]); break;
                case >= 100 and <= 107: _bg = Bright(AnsiToColor[p - 100]); break;

                case 38 or 48:
                    if (i + 1 < _paramCount)
                        i += _params[i + 1] == 5 ? 2 : _params[i + 1] == 2 ? 4 : 0;
                    break;

                default:
                    break;
            }
        }
    }

    static TerminalMode ModeBit(int p) => p switch
    {
        1 => TerminalMode.Bold,
        2 => TerminalMode.Dim,
        3 => TerminalMode.Italic,
        4 => TerminalMode.Underline,
        5 => TerminalMode.Blinking,
        7 => TerminalMode.Inverse,
        8 => TerminalMode.Hidden,
        9 => TerminalMode.Strikethrough,
        _ => TerminalMode.None,
    };

    static TerminalColor Bright(TerminalColor c) => (TerminalColor)((byte)c | 0b1000);

    void ResetPen()
    {
        _fg = TerminalColor.White;
        _bg = TerminalColor.Black;
        _mode = TerminalMode.None;
    }

    void ClearScreen()
    {
        _firstChunk += _chunks.Count;
        _chunks.Clear();
        _cursorX = 0;
        _cursorY = 0;
        EnsureLine(0);
        _line = GetLine(0);
    }

    TerminalLine GetLine(int y) => _chunks[y / LinesPerChunk].Lines[y % LinesPerChunk];

    void EnsureLine(int y)
    {
        int ci = y / LinesPerChunk;
        while (_chunks.Count <= ci) _chunks.Add(new TerminalChunk());
        TerminalChunk chunk = _chunks[ci];
        while (chunk.LineCount <= y % LinesPerChunk) chunk.AddLine();
    }

    void LineFeed()
    {
        _cursorX = 0;
        EnsureLine(++_cursorY);

        if (_chunks.Count > _maxChunks && _cursorY >= LinesPerChunk)
        {
            _chunks.RemoveAt(0);
            _firstChunk++;
            _cursorY -= LinesPerChunk;
        }
        _line = GetLine(_cursorY);
    }
}
