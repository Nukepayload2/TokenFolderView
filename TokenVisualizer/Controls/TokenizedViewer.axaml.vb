Imports System.Collections
Imports System.Collections.Generic
Imports Avalonia
Imports Avalonia.Controls

Namespace Controls

    ''' <summary>
    ''' Reusable host for the virtualized token-colored view: a scrollable, virtualized list of
    ''' <see cref="TokenLine"/> rows plus a centered empty-state hint. The Explorer page and the
    ''' text-tokenize page share this container; only the token data pipeline
    ''' (<see cref="TokenSource"/>, <see cref="TokenLine"/>, <see cref="TokenizedTextView"/>)
    ''' feeds it. Simple property forwarding keeps the no-MVVM code-behind style used elsewhere.
    ''' </summary>
    Partial Public Class TokenizedViewer
        Inherits UserControl

        Public Sub New()
            InitializeComponent()
        End Sub

        ''' <summary>The token-colored lines to display (a <see cref="List(Of TokenLine)"/>).</summary>
        Public Property ItemsSource As IEnumerable
            Get
                Return TokenLines.ItemsSource
            End Get
            Set(value As IEnumerable)
                TokenLines.ItemsSource = value
            End Set
        End Property

        ''' <summary>Hint text shown when the view is empty.</summary>
        Public Property EmptyHint As String
            Get
                Return TblEmpty.Text
            End Get
            Set(value As String)
                TblEmpty.Text = value
            End Set
        End Property

        ''' <summary>Whether the centered empty-state hint is visible.</summary>
        Public Property IsEmptyVisible As Boolean
            Get
                Return TblEmpty.IsVisible
            End Get
            Set(value As Boolean)
                TblEmpty.IsVisible = value
            End Set
        End Property

        ''' <summary>Resets the scroll position to the top.</summary>
        Public Sub ResetScroll()
            TokenScroll.Offset = New Vector(0, 0)
        End Sub

        ''' <summary>
        ''' Shows a single plain-text line in place of token data (used to surface read/encode errors
        ''' without a separate non-virtualized surface). Hides the empty-state hint.
        ''' </summary>
        Public Sub ShowText(text As String)
            ItemsSource = New List(Of TokenLine)() From {TokenLine.FromPlainText(text)}
            IsEmptyVisible = False
        End Sub

    End Class

End Namespace
