Imports System
Imports System.Threading

Namespace Scanning

    ''' <summary>
    ''' Live counters for a folder scan. All fields are read/written through
    ''' <see cref="Interlocked"/>, so the UI can poll them from another thread while
    ''' <see cref="FolderScanner"/> runs the parallel tokenization pass.
    ''' </summary>
    Public NotInheritable Class ScanProgress

        Private _filesScanned As Long
        Private _filesSkipped As Long
        Private _totalTokens As Long
        Private _filesWithErrors As Long

        ''' <summary>The token source the UI uses to request cancellation of a running scan.</summary>
        Public ReadOnly Property Cancellation As CancellationTokenSource = New CancellationTokenSource()

        Public Sub IncrementFilesScanned()
            Interlocked.Increment(_filesScanned)
        End Sub

        Public Sub IncrementFilesSkipped()
            Interlocked.Increment(_filesSkipped)
        End Sub

        Public Sub IncrementFilesWithErrors()
            Interlocked.Increment(_filesWithErrors)
        End Sub

        Public Sub AddTotalTokens(count As Long)
            Interlocked.Add(_totalTokens, count)
        End Sub

        Public Function ReadFilesScanned() As Long
            Return Interlocked.Read(_filesScanned)
        End Function

        Public Function ReadFilesSkipped() As Long
            Return Interlocked.Read(_filesSkipped)
        End Function

        Public Function ReadTotalTokens() As Long
            Return Interlocked.Read(_totalTokens)
        End Function

        Public Function ReadFilesWithErrors() As Long
            Return Interlocked.Read(_filesWithErrors)
        End Function

    End Class
End Namespace
