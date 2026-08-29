Namespace Internal

    ''' <summary>
    ''' Represents a byte range usable to index a NormalizedString, relative either to the
    ''' original string or the normalized string. Mirrors the Rust <c>Range&lt;T&gt;</c> type.
    ''' Offsets are UTF-8 byte offsets. An <see cref="End"/> value of -1 means "unbounded" (to
    ''' the end of the string), mirroring the Rust <c>Bound::Unbounded</c> case.
    ''' </summary>
    Public Structure OffsetRange
        ''' <summary>True if offsets refer to the original string, False for the normalized string.</summary>
        Public IsOriginal As Boolean
        ''' <summary>Start byte offset (inclusive).</summary>
        Public Start As Integer
        ''' <summary>End byte offset (exclusive), or -1 to mean "to the end".</summary>
        Public [End] As Integer

        Public Sub New(isOriginal As Boolean, start As Integer, [end] As Integer)
            Me.IsOriginal = isOriginal
            Me.Start = start
            Me.End = [end]
        End Sub

        ''' <summary>Converts this range to a concrete (start, end) byte range given a maximum length.</summary>
        Public Function IntoFullRange(maxLen As Integer) As (Integer, Integer)
            Dim s As Integer = If(Me.Start < 0, 0, Me.Start)
            Dim e As Integer = If(Me.End = -1, maxLen, Me.End)
            Return (s, e)
        End Function

        ''' <summary>A whole-string range in the original referential.</summary>
        Public Shared Function WholeOriginal() As OffsetRange
            Return New OffsetRange(True, 0, -1)
        End Function

        ''' <summary>A whole-string range in the normalized referential.</summary>
        Public Shared Function WholeNormalized() As OffsetRange
            Return New OffsetRange(False, 0, -1)
        End Function
    End Structure

End Namespace
