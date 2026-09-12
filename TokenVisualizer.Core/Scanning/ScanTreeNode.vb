Imports System
Imports System.Collections.ObjectModel
Imports System.ComponentModel

Namespace Scanning

    ''' <summary>
    ''' A node in the scanned directory tree. Directory nodes hold child directories and files in a
    ''' single <see cref="Children"/> list (distinguished by <see cref="IsDirectory"/>). Directory
    ''' token/file counts aggregate their descendants; file nodes carry their own counts.
    ''' </summary>
    Public Class ScanTreeNode
        Implements INotifyPropertyChanged

        ''' <summary>The node's name (a file name or directory name).</summary>
        Public Property Name As String

        ''' <summary>The full path of the node on disk.</summary>
        Public Property FullPath As String

        ''' <summary>True for a directory node, False for a file node.</summary>
        Public Property IsDirectory As Boolean

        ''' <summary>
        ''' Child directories and files (directory nodes only; always empty for file nodes), kept in
        ''' <see cref="String.CompareOrdinal"/>-by-name order (directories and files mixed).
        ''' </summary>
        ''' <remarks>
        ''' An <see cref="ObservableCollection(Of T)"/> so a tree bound to a <c>TreeView</c> can be
        ''' updated incrementally: <c>Insert</c> and <c>Remove</c> only touch the affected item
        ''' containers, so expanded nodes and the selection survive, whereas <c>Clear</c> / <c>Move</c>
        ''' raise a Reset that recreates every container and therefore loses both. Incremental updates
        ''' must only ever insert or remove.
        ''' </remarks>
        Public ReadOnly Property Children As ObservableCollection(Of ScanTreeNode) = New ObservableCollection(Of ScanTreeNode)()

        Private _tokenCount As Long
        Private _fileCount As Long
        Private _fileSize As Long

        Public Sub New()
        End Sub

        Public Sub New(name As String, fullPath As String, isDirectory As Boolean)
            Me.Name = name
            Me.FullPath = fullPath
            Me.IsDirectory = isDirectory
        End Sub

        ''' <summary>Aggregated token count (the sum of all descendants for directory nodes).</summary>
        Public Property TokenCount As Long
            Get
                Return _tokenCount
            End Get
            Set(value As Long)
                If _tokenCount <> value Then
                    _tokenCount = value
                    NotifyPropertyChanged(NameOf(TokenCount))
                    NotifyPropertyChanged(NameOf(CountText))
                End If
            End Set
        End Property

        ''' <summary>Number of files (aggregated for directory nodes; 1 for file nodes).</summary>
        Public Property FileCount As Long
            Get
                Return _fileCount
            End Get
            Set(value As Long)
                If _fileCount <> value Then
                    _fileCount = value
                    NotifyPropertyChanged(NameOf(FileCount))
                End If
            End Set
        End Property

        ''' <summary>Total size in bytes (aggregated for directory nodes).</summary>
        Public Property FileSize As Long
            Get
                Return _fileSize
            End Get
            Set(value As Long)
                If _fileSize <> value Then
                    _fileSize = value
                    NotifyPropertyChanged(NameOf(FileSize))
                End If
            End Set
        End Property

        ''' <summary>Formatted token count for display (e.g. "1,234").</summary>
        Public ReadOnly Property CountText As String
            Get
                Return _tokenCount.ToString("N0")
            End Get
        End Property

        ''' <summary>
        ''' Adds a child node and folds its counts into this node. Used while the tree is being
        ''' assembled; <see cref="FolderScanner.BuildTree"/> additionally runs a post-order
        ''' aggregation pass (<see cref="ScanTreeEditor.Reaggregate"/>) so directory counts always
        ''' reflect their full subtree.
        ''' </summary>
        Public Sub AddChild(node As ScanTreeNode)
            If node Is Nothing Then Throw New ArgumentNullException(NameOf(node))
            Children.Add(node)
            TokenCount += node.TokenCount
            FileCount += node.FileCount
            FileSize += node.FileSize
        End Sub

#Region "INotifyPropertyChanged"

        Private _propertyChanged As PropertyChangedEventHandler

        Public Custom Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged
            AddHandler(value As PropertyChangedEventHandler)
                _propertyChanged = DirectCast([Delegate].Combine(_propertyChanged, value), PropertyChangedEventHandler)
            End AddHandler
            RemoveHandler(value As PropertyChangedEventHandler)
                _propertyChanged = DirectCast([Delegate].Remove(_propertyChanged, value), PropertyChangedEventHandler)
            End RemoveHandler
            RaiseEvent(sender As Object, e As PropertyChangedEventArgs)
                _propertyChanged?.Invoke(sender, e)
            End RaiseEvent
        End Event

        Private Sub NotifyPropertyChanged(propertyName As String)
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))
        End Sub

#End Region

    End Class
End Namespace
