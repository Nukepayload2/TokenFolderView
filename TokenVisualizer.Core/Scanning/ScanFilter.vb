Imports System
Imports System.Collections.Generic

Namespace Scanning

    ''' <summary>
    ''' Pure, I/O-free decision helpers used while enumerating a directory tree. Every helper is
    ''' unit-testable without touching the file system.
    ''' </summary>
    Public NotInheritable Class ScanFilter

        ''' <summary>
        ''' True when <paramref name="folderName"/> matches any blacklist entry (case-insensitive).
        ''' The comparison is name-at-any-depth: only the folder's name is compared, never its path.
        ''' </summary>
        Public Shared Function ShouldSkipFolder(folderName As String, blacklist As IReadOnlyList(Of String)) As Boolean
            If String.IsNullOrEmpty(folderName) OrElse blacklist Is Nothing Then Return False
            For Each entry As String In blacklist
                If String.Equals(folderName, entry, StringComparison.OrdinalIgnoreCase) Then
                    Return True
                End If
            Next
            Return False
        End Function

        ''' <summary>True when the file length exceeds the maximum allowed size (boundary is inclusive).</summary>
        Public Shared Function ShouldSkipFileSize(length As Long, maxBytes As Long) As Boolean
            Return length > maxBytes
        End Function

        ''' <summary>
        ''' Composes the folder-blacklist and size checks for a relative path: the file is skipped
        ''' when any path segment that is a folder part is blacklisted, or when its size exceeds the
        ''' limit. The relative path is split on both <c>/</c> and <c>\</c>.
        ''' </summary>
        Public Shared Function ShouldSkipFile(relativePath As String, length As Long, options As ScanOptions) As Boolean
            If options Is Nothing Then Throw New ArgumentNullException(NameOf(options))
            If ShouldSkipFileSize(length, options.MaxFileSizeBytes) Then Return True
            If String.IsNullOrEmpty(relativePath) Then Return False

            Dim normalized As String = relativePath.Replace("\"c, "/"c)
            Dim segments As String() = normalized.Split("/"c)
            ' Every segment except the last is a folder part; the last is the file name.
            For i As Integer = 0 To segments.Length - 2
                If ShouldSkipFolder(segments(i), options.FolderBlacklist) Then Return True
            Next
            Return False
        End Function

    End Class
End Namespace
