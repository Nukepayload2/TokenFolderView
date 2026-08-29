Imports System.Collections.Generic

Namespace Scanning

    ''' <summary>
    ''' Options controlling how <see cref="FolderScanner"/> walks a directory tree: which folder
    ''' names are skipped at any depth, the maximum file size that is tokenized, and whether file
    ''' heads are sniffed for binary content.
    ''' </summary>
    Public NotInheritable Class ScanOptions

        ''' <summary>Folder names that are skipped at any depth (compared by name only, case-insensitive).</summary>
        Public Property FolderBlacklist As IReadOnlyList(Of String)

        ''' <summary>Files strictly larger than this many bytes are skipped without being tokenized.</summary>
        Public Property MaxFileSizeBytes As Long

        ''' <summary>When True, the first 4 KiB of each candidate file is checked for binary content.</summary>
        Public Property CheckBinary As Boolean

        Public Sub New()
            FolderBlacklist = New List(Of String) From {
                "bin", "obj", "node_modules", ".vs", ".git", "dist", "target"
            }
            MaxFileSizeBytes = 10 * 1024 * 1024
            CheckBinary = True
        End Sub

        Private Shared ReadOnly _default As New ScanOptions()

        ''' <summary>The shared default options instance.</summary>
        Public Shared ReadOnly Property [Default] As ScanOptions
            Get
                Return _default
            End Get
        End Property

    End Class
End Namespace
