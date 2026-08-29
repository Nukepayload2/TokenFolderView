Imports Avalonia
Imports Avalonia.Controls
Imports Avalonia.Controls.Documents
Imports Avalonia.Media
Imports Avalonia.Threading

Namespace Controls

    ''' <summary>
    ''' Helper that paints a pre-computed list of runs onto a <see cref="TextBlock"/>.
    ''' The heavy work (reading the file, tokenizing, building the run list) happens off the UI
    ''' thread in the Explorer page; this module only appends <see cref="Run"/> objects on the UI
    ''' thread in bounded batches so the window stays responsive for very large files.
    ''' Token runs alternate foreground: even tokens inherit the normal foreground, odd tokens use
    ''' the accent brush (resolved once and cached).
    ''' </summary>
    Public Module TokenizedTextView

        Private Const BatchSize As Integer = 4000

        Private _accentBrush As IBrush
        Private ReadOnly _accentLock As New Object()

        ''' <summary>
        ''' Resolves the accent brush used to highlight odd tokens. Tries, in order:
        ''' "SystemAccentColorBrush", "SystemControlHighlightAccentBrush", then "SystemAccentColor"
        ''' (a <see cref="Color"/>) wrapped in a <see cref="SolidColorBrush"/>. Falls back to a fixed
        ''' blue. The result is cached; theme changes are out of scope for P12.
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
        ''' Replaces <paramref name="textBlock"/>'s content with the given runs, appending in batches
        ''' of <see cref="BatchSize"/> on the UI dispatcher so the frame stays responsive.
        ''' Stale appends (from a previously superseded load) are dropped by comparing
        ''' <c>textBlock.Tag</c> to the run list.
        ''' </summary>
        Public Sub Populate(textBlock As TextBlock, runs As IReadOnlyList(Of (Text As String, Accent As Boolean)))
            Dim accent As IBrush = ResolveAccentBrush()
            textBlock.Inlines.Clear()
            textBlock.Tag = runs
            If runs Is Nothing OrElse runs.Count = 0 Then
                textBlock.Text = ""
                Return
            End If
            AppendBatch(textBlock, runs, accent, 0)
        End Sub

        Private Sub AppendBatch(textBlock As TextBlock,
                                runs As IReadOnlyList(Of (Text As String, Accent As Boolean)),
                                accent As IBrush,
                                start As Integer)
            ' A newer Populate (different run list) may have replaced this one.
            If textBlock.Tag IsNot runs Then Return

            Dim endExclusive As Integer = Math.Min(start + BatchSize, runs.Count)
            For i As Integer = start To endExclusive - 1
                Dim item As (Text As String, Accent As Boolean) = runs(i)
                Dim r As New Run With {.Text = item.Text}
                If item.Accent Then r.Foreground = accent
                textBlock.Inlines.Add(r)
            Next

            If endExclusive < runs.Count Then
                Dispatcher.UIThread.Post(Sub() AppendBatch(textBlock, runs, accent, endExclusive))
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
