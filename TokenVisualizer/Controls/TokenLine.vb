Imports System
Imports System.Collections.Generic
Imports Avalonia.Controls.Documents

Namespace Controls

    ''' <summary>
    ''' Immutable per-line record for the virtualized file reader: the line's visible content range
    ''' and the global token index / span index of its first intersecting token. Pure Int32 data,
    ''' built on the background thread with no string allocation. The newline characters themselves
    ''' are never part of a line's [LineStart, LineEnd) range.
    ''' </summary>
    Public Structure LineRecord
        ''' <summary>Start (inclusive) of the line's visible content.</summary>
        Public LineStart As Integer

        ''' <summary>End (exclusive) of the line's visible content — the newline position or the end of the text.</summary>
        Public LineEnd As Integer

        ''' <summary>
        ''' Global token index of the first token intersecting this line. Continuous across lines:
        ''' a token that crosses a line boundary counts once, so accent parity does not reset at
        ''' newlines. Tokens that fall entirely in a newline gap still consume an index.
        ''' </summary>
        Public FirstTokenIdx As Integer

        ''' <summary>Index into the spans list of the first span intersecting this line.</summary>
        Public FirstSpanIdx As Integer
    End Structure

    ''' <summary>
    ''' Shared, immutable backing data for one tokenized file: the decoded text, the per-token spans
    ''' and the per-line records. Built once on the background thread when a file is opened; every
    ''' <see cref="TokenLine"/> references this single instance, so opening a 10 MB file allocates
    ''' only the text, the spans, the line records and one tiny <see cref="TokenLine"/> per line.
    ''' </summary>
    Public NotInheritable Class TokenSource

        Private ReadOnly _text As String
        Private ReadOnly _spans As IReadOnlyList(Of (Integer, Integer, Integer))
        Private ReadOnly _lines As LineRecord()

        Private Sub New(text As String,
                        spans As IReadOnlyList(Of (Integer, Integer, Integer)),
                        lines As LineRecord())
            _text = text
            _spans = spans
            _lines = lines
        End Sub

        Public ReadOnly Property Text As String
            Get
                Return _text
            End Get
        End Property

        Public ReadOnly Property Spans As IReadOnlyList(Of (Integer, Integer, Integer))
            Get
                Return _spans
            End Get
        End Property

        Public ReadOnly Property Lines As LineRecord()
            Get
                Return _lines
            End Get
        End Property

        ''' <summary>
        ''' Splits <paramref name="text"/> into lines and maps each line to the token index and span
        ''' index of its first intersecting token, using one scan over the spans. No substring is
        ''' allocated here; the per-line run tuples are built lazily by <see cref="TokenLine"/>.
        ''' </summary>
        Public Shared Function Create(text As String,
                                      spans As IReadOnlyList(Of (Integer, Integer, Integer))) As TokenSource
            Dim records = BuildLineRecords(text, spans)
            Return New TokenSource(text, spans, records)
        End Function

        Private Shared Function BuildLineRecords(text As String,
                                                 spans As IReadOnlyList(Of (Integer, Integer, Integer))) As LineRecord()
            If String.IsNullOrEmpty(text) Then Return Array.Empty(Of LineRecord)()

            ' 1. Split into lines at \r\n, \n and \r.
            Dim lineStarts As New List(Of Integer)()
            Dim lineEnds As New List(Of Integer)()
            Dim newlineChars As Char() = {vbCr, vbLf}
            Dim start As Integer = 0
            While start < text.Length
                Dim nl = text.IndexOfAny(newlineChars, start)
                Dim lineEnd As Integer
                If nl < 0 Then
                    lineEnd = text.Length
                Else
                    lineEnd = nl
                End If
                lineStarts.Add(start)
                lineEnds.Add(lineEnd)
                If nl < 0 Then Exit While
                If text.Chars(nl) = vbCr AndAlso nl + 1 < text.Length AndAlso text.Chars(nl + 1) = vbLf Then
                    start = nl + 2
                Else
                    start = nl + 1
                End If
            End While
            ' A file that ends with a newline has a trailing empty line.
            If start = text.Length Then
                lineStarts.Add(start)
                lineEnds.Add(start)
            End If

            Dim lineCount = lineStarts.Count
            Dim records(lineCount - 1) As LineRecord
            For i As Integer = 0 To lineCount - 1
                records(i).LineStart = lineStarts(i)
                records(i).LineEnd = lineEnds(i)
            Next

            ' 2. One scan over the token spans assigns each line the global token index and the span
            '    index of its first intersecting token. A token that crosses a line boundary is
            '    recorded for every line it intersects with the same token index (it counts once);
            '    a token that lies entirely in a newline gap (e.g. a "\n" token) still consumes an
            '    index so accent parity stays continuous across lines.
            Dim pos As Integer = 0
            Dim idx As Integer = 0
            Dim li As Integer = 0

            For si As Integer = 0 To spans.Count - 1
                Dim span = spans(si)
                Dim s = span.Item2
                Dim e = span.Item3
                If s < pos Then s = pos
                If e < pos Then e = pos
                If e <= pos Then Continue For

                ' Lines that begin in the gap before this token.
                While li < lineCount AndAlso records(li).LineStart < s
                    records(li).FirstTokenIdx = idx
                    records(li).FirstSpanIdx = si
                    li += 1
                End While

                ' Lines that begin inside this token's span (the token crosses into them).
                While li < lineCount AndAlso records(li).LineStart < e
                    records(li).FirstTokenIdx = idx
                    records(li).FirstSpanIdx = si
                    li += 1
                End While

                pos = e
                idx += 1
            Next

            ' Lines after the last token: no token intersects them, all remaining text is normal.
            While li < lineCount
                records(li).FirstTokenIdx = idx
                records(li).FirstSpanIdx = spans.Count
                li += 1
            End While

            Return records
        End Function

    End Class

    ''' <summary>
    ''' One file line in the virtualized reader. Holds only a reference to the shared
    ''' <see cref="TokenSource"/> and its own line index, so construction allocates no strings and no
    ''' Avalonia objects. The <see cref="Inlines"/> collection is built lazily on the UI thread the
    ''' first time it is bound (i.e. when the virtualized container is realized) and then cached, so a
    ''' line object reused across container realizations pays nothing extra.
    ''' </summary>
    Public Class TokenLine

        Private ReadOnly _source As TokenSource
        Private ReadOnly _lineIndex As Integer
        Private _inlines As InlineCollection

        Friend Sub New(source As TokenSource, lineIndex As Integer)
            _source = source
            _lineIndex = lineIndex
        End Sub

        ''' <summary>
        ''' A single plain-text line (used to surface read errors in the virtualized view without a
        ''' separate non-virtualized surface).
        ''' </summary>
        Public Shared Function FromPlainText(text As String) As TokenLine
            Dim t = If(String.IsNullOrEmpty(text), " ", text)
            Dim source = TokenSource.Create(t, Array.Empty(Of (Integer, Integer, Integer))())
            Return New TokenLine(source, 0)
        End Function

        ''' <summary>
        ''' The line's token-colored inline content. Built once, on the UI thread, and cached.
        ''' </summary>
        Public ReadOnly Property Inlines As InlineCollection
            Get
                If _inlines Is Nothing Then
                    Dim record = _source.Lines(_lineIndex)
                    Dim runs = TokenizedTextView.BuildLineRuns(_source.Text, _source.Spans, record)
                    _inlines = TokenizedTextView.BuildInlines(runs,
                        TokenizedTextView.ResolveAccentBrush(),
                        TokenizedTextView.ResolveAccentBackgroundBrush())
                End If
                Return _inlines
            End Get
        End Property

    End Class

End Namespace
