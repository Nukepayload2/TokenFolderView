Namespace Internal

    ''' <summary>
    ''' Referential used to express offsets: relative to the original string or to the normalized
    ''' string. Mirrors the Rust <c>OffsetReferential</c>.
    ''' </summary>
    Public Enum OffsetReferential
        Original
        Normalized
    End Enum

    ''' <summary>
    ''' Type of offsets returned by <see cref="PreTokenizedString.GetSplits"/>. Mirrors the Rust
    ''' <c>OffsetType</c>.
    ''' </summary>
    Public Enum OffsetType
        [Byte]
        [Char]
        None
    End Enum

End Namespace
