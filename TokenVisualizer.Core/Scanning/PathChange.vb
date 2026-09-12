Imports System

Namespace Scanning

    ''' <summary>
    ''' One reported change of a path, relative to the scanned root, as handed to
    ''' <see cref="ScanTreeEditor.ApplyChanges"/>. A rename is expressed by the caller as two
    ''' entries: a removal of the old path and a change of the new one.
    ''' </summary>
    ''' <remarks>
    ''' The constructor does not validate <see cref="RelativePath"/>: the editor ignores paths it
    ''' cannot use (empty, absolute or escaping the root), so a watcher callback never has to
    ''' second-guess the path it reports.
    ''' </remarks>
    Public NotInheritable Class PathChange

        Public Sub New(relativePath As String, isDirectory As Boolean, isRemoval As Boolean)
            Me.RelativePath = relativePath
            Me.IsDirectory = isDirectory
            Me.IsRemoval = isRemoval
        End Sub

        ''' <summary>The changed path, relative to the scanned root.</summary>
        Public ReadOnly Property RelativePath As String

        ''' <summary>True when the path is (or became) a directory; ignored for removals.</summary>
        Public ReadOnly Property IsDirectory As Boolean

        ''' <summary>True when the path disappeared.</summary>
        Public ReadOnly Property IsRemoval As Boolean

    End Class
End Namespace
