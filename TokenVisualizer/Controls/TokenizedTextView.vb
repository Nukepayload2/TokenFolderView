Imports System
Imports System.Collections.Generic
Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Controls.Documents
Imports Avalonia.Media

Namespace Controls

    ''' <summary>
    ''' Builds the shared brushes and the per-line <see cref="InlineCollection"/>s used by the
    ''' virtualized token-colored view. The accent brush is resolved once and cached; accent runs get
    ''' the accent foreground plus a translucent gray background so odd tokens are easy to spot. The
    ''' heavy per-line data is precomputed on the background thread via <see cref="BuildLines"/>;
    ''' the brush resolution and per-line inlines run on the UI thread and only for lines that are
    ''' actually materialized by the virtualizer.
    ''' </summary>
    Public Module TokenizedTextView

        Private _accentBrush As IBrush
        Private ReadOnly _accentLock As New Object()

        Private _accentBgBrush As IBrush
        Private ReadOnly _accentBgLock As New Object()

        ''' <summary>
        ''' Resolves the accent brush used to highlight odd tokens. Tries, in order:
        ''' "SystemAccentColorBrush", "SystemControlHighlightAccentBrush", then "SystemAccentColor"
        ''' (a <see cref="Color"/>) wrapped in a <see cref="SolidColorBrush"/>. Falls back to a fixed
        ''' blue. The result is cached; theme changes are out of scope.
        ''' </summary>
        Public Function ResolveAccentBrush() As IBrush
            If _accentBrush IsNot Nothing Then Return _accentBrush
            SyncLock _accentLock
                If _accentBrush Is Nothing Then
                    _accentBrush = ResolveAccentBrushCore()
                End If
            End SyncLock
            Return _accentBrush
        End Function

        ''' <summary>
        ''' Returns the shared translucent gray brush (8% alpha) painted behind accent runs. Always the
        ''' same singleton instance so the renderer can cache it; never allocate one per run.
        ''' </summary>
        Public Function ResolveAccentBackgroundBrush() As IBrush
            If _accentBgBrush IsNot Nothing Then Return _accentBgBrush
            SyncLock _accentBgLock
                If _accentBgBrush Is Nothing Then
                    _accentBgBrush = New SolidColorBrush(Color.FromArgb(20, &H7F, &H7F, &H7F))
                End If
            End SyncLock
            Return _accentBgBrush
        End Function

        ''' <summary>
        ''' Builds the virtualized line objects for a decoded text and its token spans. Runs on the
        ''' background thread; only the cheap per-line records and one tiny <see cref="TokenLine"/> per
        ''' line are built here. The per-line run tuples and <see cref="InlineCollection"/>s are computed
        ''' lazily on the UI thread by <see cref="TokenLine.Inlines"/> when a virtualized container is
        ''' materialized.
        ''' </summary>
        Public Function BuildLines(text As String,
                                   spans As IReadOnlyList(Of (Integer, Integer, Integer))) As List(Of TokenLine)
            Dim source = TokenSource.Create(text, spans)
            Dim lines As New List(Of TokenLine)(source.Lines.Length)
            For i As Integer = 0 To source.Lines.Length - 1
                lines.Add(New TokenLine(source, i))
            Next
            Return lines
        End Function

        ''' <summary>
        ''' Computes the token-colored run tuples for a single line from the shared text, spans and its
        ''' line record. This is where the per-line <c>Substring</c> work happens; it runs on the UI
        ''' thread when the line's <see cref="TokenLine.Inlines"/> is first bound. A token that crosses
        ''' a line boundary is clamped to this line's range and uses the same global token index for its
        ''' accent; the newline itself is never rendered. Adjacent same-color runs are merged, matching
        ''' the previous non-virtualized <c>BuildRunsCore</c> semantics.
        ''' </summary>
        Public Function BuildLineRuns(text As String,
                                      spans As IReadOnlyList(Of (Integer, Integer, Integer)),
                                      record As LineRecord) As List(Of (Text As String, Accent As Boolean))
            Dim runs As New List(Of (Text As String, Accent As Boolean))()
            Dim pos As Integer = record.LineStart
            Dim idx As Integer = record.FirstTokenIdx
            For i As Integer = record.FirstSpanIdx To spans.Count - 1
                Dim span = spans(i)
                Dim s = span.Item2
                Dim e = span.Item3
                If s < pos Then s = pos
                If e < pos Then e = pos
                If e <= pos Then Continue For
                If s >= record.LineEnd Then Exit For

                If s > pos Then
                    AddRun(runs, text.Substring(pos, s - pos), False)
                    pos = s
                End If

                Dim segEnd = Math.Min(e, record.LineEnd)
                AddRun(runs, text.Substring(s, segEnd - s), (idx Mod 2 = 1))
                pos = segEnd
                idx += 1
                If e > record.LineEnd Then Exit For ' token crosses into the next line(s)
            Next

            ' Trailing text of this line after its last token (never includes the newline).
            If pos < record.LineEnd Then
                AddRun(runs, text.Substring(pos, record.LineEnd - pos), False)
            End If
            Return runs
        End Function

        ''' <summary>
        ''' Builds an <see cref="InlineCollection"/> for one line from its pre-computed run tuples.
        ''' Called on the UI thread only; every <see cref="Run"/> reuses the shared accent / background
        ''' brushes. Empty lines get a single space run so they still measure one line tall.
        ''' </summary>
        Public Function BuildInlines(runs As IReadOnlyList(Of (Text As String, Accent As Boolean)),
                                     accent As IBrush,
                                     accentBg As IBrush) As InlineCollection
            Dim inlines As New InlineCollection()
            If runs Is Nothing OrElse runs.Count = 0 Then
                inlines.Add(New Run With {.Text = " "})
                Return inlines
            End If
            For Each item In runs
                Dim r As New Run With {.Text = item.Text}
                If item.Accent Then
                    r.Foreground = accent
                    r.Background = accentBg
                End If
                inlines.Add(r)
            Next
            Return inlines
        End Function

        Private Sub AddRun(runs As List(Of (Text As String, Accent As Boolean)), text As String, accent As Boolean)
            If String.IsNullOrEmpty(text) Then Return
            If runs.Count > 0 AndAlso runs(runs.Count - 1).Accent = accent Then
                runs(runs.Count - 1) = (runs(runs.Count - 1).Text & text, accent)
            Else
                runs.Add((text, accent))
            End If
        End Sub

        Private Function ResolveAccentBrushCore() As IBrush
            Dim app As Application = Application.Current
            If app IsNot Nothing Then
                Dim resource As Object = Nothing

                If app.TryFindResource("SystemAccentColorBrush", resource) Then
                    Dim brush As IBrush = TryCast(resource, IBrush)
                    If brush IsNot Nothing Then Return brush
                End If

                If app.TryFindResource("SystemControlHighlightAccentBrush", resource) Then
                    Dim brush As IBrush = TryCast(resource, IBrush)
                    If brush IsNot Nothing Then Return brush
                End If

                If app.TryFindResource("SystemAccentColor", resource) AndAlso TypeOf resource Is Color Then
                    Return New SolidColorBrush(DirectCast(resource, Color))
                End If
            End If

            Return New SolidColorBrush(Color.FromArgb(255, 0, 120, 212))
        End Function

    End Module
End Namespace
