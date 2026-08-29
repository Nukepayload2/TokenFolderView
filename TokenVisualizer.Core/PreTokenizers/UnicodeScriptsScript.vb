Namespace PreTokenizers

    ''' <summary>
    ''' Unicode script categories. Ported from the Rust `unicode_scripts::scripts::Script`
    ''' (data generated from the Unicode Scripts.txt property).
    ''' </summary>
    Public Enum Script
        Any
        Adlam
        Ahom
        AnatolianHieroglyphs
        Arabic
        Armenian
        Avestan
        Balinese
        Bamum
        BassaVah
        Batak
        Bengali
        Bhaiksuki
        Bopomofo
        Brahmi
        Braille
        Buginese
        Buhid
        CanadianAboriginal
        Carian
        CaucasianAlbanian
        Chakma
        Cham
        Cherokee
        Common
        Coptic
        Cuneiform
        Cypriot
        Cyrillic
        Deseret
        Devanagari
        Duployan
        EgyptianHieroglyphs
        Elbasan
        Ethiopic
        Georgian
        Glagolitic
        Gothic
        Grantha
        Greek
        Gujarati
        Gurmukhi
        Han
        Hangul
        Hanunoo
        Hatran
        Hebrew
        Hiragana
        ImperialAramaic
        Inherited
        InscriptionalPahlavi
        InscriptionalParthian
        Javanese
        Kaithi
        Kannada
        Katakana
        KayahLi
        Kharoshthi
        Khmer
        Khojki
        Khudawadi
        Lao
        Latin
        Lepcha
        Limbu
        LinearA
        LinearB
        Lisu
        Lycian
        Lydian
        Mahajani
        Malayalam
        Mandaic
        Manichaean
        Marchen
        MeeteiMayek
        MendeKikakui
        MeroiticCursive
        MeroiticHieroglyphs
        Miao
        Modi
        Mongolian
        Mro
        Multani
        Myanmar
        Nabataean
        NewTaiLue
        Newa
        Nko
        Ogham
        OlChiki
        OldHungarian
        OldItalic
        OldNorthArabian
        OldPermic
        OldPersian
        OldSouthArabian
        OldTurkic
        Oriya
        Osage
        Osmanya
        PahawhHmong
        Palmyrene
        PauCinHau
        PhagsPa
        Phoenician
        PsalterPahlavi
        Rejang
        Runic
        Samaritan
        Saurashtra
        Sharada
        Shavian
        Siddham
        SignWriting
        Sinhala
        SoraSompeng
        Sundanese
        SylotiNagri
        Syriac
        Tagalog
        Tagbanwa
        TaiLe
        TaiTham
        TaiViet
        Takri
        Tamil
        Tangut
        Telugu
        Thaana
        Thai
        Tibetan
        Tifinagh
        Tirhuta
        Ugaritic
        Vai
        WarangCiti
        Yi
    End Enum

    ''' <summary>
    ''' Unicode script lookup. Ported mechanically from the Rust `get_script` match in
    ''' `pre_tokenizers/unicode_scripts/scripts.rs`.
    ''' </summary>
    Public Module UnicodeScripts

        ''' <summary>Returns the Unicode script of the given codepoint.</summary>
        Public Function GetScript(c As Integer) As Script
            Select Case c
                Case &H0 To &H1F, &H20, &H21 To &H23, &H24, &H25 To &H27, &H28
                    Return Script.Common
                Case &H29, &H2A, &H2B, &H2C, &H2D, &H2E To &H2F
                    Return Script.Common
                Case &H30 To &H39, &H3A To &H3B, &H3C To &H3E, &H3F To &H40, &H5B, &H5C
                    Return Script.Common
                Case &H5D, &H5E, &H5F, &H60, &H7B, &H7C
                    Return Script.Common
                Case &H7D, &H7E, &H7F To &H9F, &HA0, &HA1, &HA2 To &HA5
                    Return Script.Common
                Case &HA6, &HA7, &HA8, &HA9, &HAB, &HAC
                    Return Script.Common
                Case &HAD, &HAE, &HAF, &HB0, &HB1, &HB2 To &HB3
                    Return Script.Common
                Case &HB4, &HB5, &HB6 To &HB7, &HB8, &HB9, &HBB
                    Return Script.Common
                Case &HBC To &HBE, &HBF, &HD7, &HF7, &H2B9 To &H2C1, &H2C2 To &H2C5
                    Return Script.Common
                Case &H2C6 To &H2D1, &H2D2 To &H2DF, &H2E5 To &H2E9, &H2EC, &H2ED, &H2EE
                    Return Script.Common
                Case &H2EF To &H2FF, &H374, &H37E, &H385, &H387, &H589
                    Return Script.Common
                Case &H605, &H60C, &H61B, &H61C, &H61F, &H640
                    Return Script.Common
                Case &H6DD, &H8E2, &H964 To &H965, &HE3F, &HFD5 To &HFD8, &H10FB
                    Return Script.Common
                Case &H16EB To &H16ED, &H1735 To &H1736, &H1802 To &H1803, &H1805, &H1CD3, &H1CE1
                    Return Script.Common
                Case &H1CE9 To &H1CEC, &H1CEE To &H1CF1, &H1CF2 To &H1CF3, &H1CF5 To &H1CF6, &H2000 To &H200A, &H200B
                    Return Script.Common
                Case &H200E To &H200F, &H2010 To &H2015, &H2016 To &H2017, &H2018, &H2019, &H201A
                    Return Script.Common
                Case &H201B To &H201C, &H201D, &H201E, &H201F, &H2020 To &H2027, &H2028
                    Return Script.Common
                Case &H2029, &H202A To &H202E, &H202F, &H2030 To &H2038, &H2039, &H203A
                    Return Script.Common
                Case &H203B To &H203E, &H203F To &H2040, &H2041 To &H2043, &H2044, &H2045, &H2046
                    Return Script.Common
                Case &H2047 To &H2051, &H2052, &H2053, &H2054, &H2055 To &H205E, &H205F
                    Return Script.Common
                Case &H2060 To &H2064, &H2066 To &H206F, &H2070, &H2074 To &H2079, &H207A To &H207C, &H207D
                    Return Script.Common
                Case &H207E, &H2080 To &H2089, &H208A To &H208C, &H208D, &H208E, &H20A0 To &H20BE
                    Return Script.Common
                Case &H2100 To &H2101, &H2102, &H2103 To &H2106, &H2107, &H2108 To &H2109, &H210A To &H2113
                    Return Script.Common
                Case &H2114, &H2115, &H2116 To &H2117, &H2118, &H2119 To &H211D, &H211E To &H2123
                    Return Script.Common
                Case &H2124, &H2125, &H2127, &H2128, &H2129, &H212C To &H212D
                    Return Script.Common
                Case &H212E, &H212F To &H2131, &H2133 To &H2134, &H2135 To &H2138, &H2139, &H213A To &H213B
                    Return Script.Common
                Case &H213C To &H213F, &H2140 To &H2144, &H2145 To &H2149, &H214A, &H214B, &H214C To &H214D
                    Return Script.Common
                Case &H214F, &H2150 To &H215F, &H2189, &H218A To &H218B, &H2190 To &H2194, &H2195 To &H2199
                    Return Script.Common
                Case &H219A To &H219B, &H219C To &H219F, &H21A0, &H21A1 To &H21A2, &H21A3, &H21A4 To &H21A5
                    Return Script.Common
                Case &H21A6, &H21A7 To &H21AD, &H21AE, &H21AF To &H21CD, &H21CE To &H21CF, &H21D0 To &H21D1
                    Return Script.Common
                Case &H21D2, &H21D3, &H21D4, &H21D5 To &H21F3, &H21F4 To &H22FF, &H2300 To &H2307
                    Return Script.Common
                Case &H2308, &H2309, &H230A, &H230B, &H230C To &H231F, &H2320 To &H2321
                    Return Script.Common
                Case &H2322 To &H2328, &H2329, &H232A, &H232B To &H237B, &H237C, &H237D To &H239A
                    Return Script.Common
                Case &H239B To &H23B3, &H23B4 To &H23DB, &H23DC To &H23E1, &H23E2 To &H23FE, &H2400 To &H2426, &H2440 To &H244A
                    Return Script.Common
                Case &H2460 To &H249B, &H249C To &H24E9, &H24EA To &H24FF, &H2500 To &H25B6, &H25B7, &H25B8 To &H25C0
                    Return Script.Common
                Case &H25C1, &H25C2 To &H25F7, &H25F8 To &H25FF, &H2600 To &H266E, &H266F, &H2670 To &H2767
                    Return Script.Common
                Case &H2768, &H2769, &H276A, &H276B, &H276C, &H276D
                    Return Script.Common
                Case &H276E, &H276F, &H2770, &H2771, &H2772, &H2773
                    Return Script.Common
                Case &H2774, &H2775, &H2776 To &H2793, &H2794 To &H27BF, &H27C0 To &H27C4, &H27C5
                    Return Script.Common
                Case &H27C6, &H27C7 To &H27E5, &H27E6, &H27E7, &H27E8, &H27E9
                    Return Script.Common
                Case &H27EA, &H27EB, &H27EC, &H27ED, &H27EE, &H27EF
                    Return Script.Common
                Case &H27F0 To &H27FF, &H2900 To &H2982, &H2983, &H2984, &H2985, &H2986
                    Return Script.Common
                Case &H2987, &H2988, &H2989, &H298A, &H298B, &H298C
                    Return Script.Common
                Case &H298D, &H298E, &H298F, &H2990, &H2991, &H2992
                    Return Script.Common
                Case &H2993, &H2994, &H2995, &H2996, &H2997, &H2998
                    Return Script.Common
                Case &H2999 To &H29D7, &H29D8, &H29D9, &H29DA, &H29DB, &H29DC To &H29FB
                    Return Script.Common
                Case &H29FC, &H29FD, &H29FE To &H2AFF, &H2B00 To &H2B2F, &H2B30 To &H2B44, &H2B45 To &H2B46
                    Return Script.Common
                Case &H2B47 To &H2B4C, &H2B4D To &H2B73, &H2B76 To &H2B95, &H2B98 To &H2BB9, &H2BBD To &H2BC8, &H2BCA To &H2BD1
                    Return Script.Common
                Case &H2BEC To &H2BEF, &H2E00 To &H2E01, &H2E02, &H2E03, &H2E04, &H2E05
                    Return Script.Common
                Case &H2E06 To &H2E08, &H2E09, &H2E0A, &H2E0B, &H2E0C, &H2E0D
                    Return Script.Common
                Case &H2E0E To &H2E16, &H2E17, &H2E18 To &H2E19, &H2E1A, &H2E1B, &H2E1C
                    Return Script.Common
                Case &H2E1D, &H2E1E To &H2E1F, &H2E20, &H2E21, &H2E22, &H2E23
                    Return Script.Common
                Case &H2E24, &H2E25, &H2E26, &H2E27, &H2E28, &H2E29
                    Return Script.Common
                Case &H2E2A To &H2E2E, &H2E2F, &H2E30 To &H2E39, &H2E3A To &H2E3B, &H2E3C To &H2E3F, &H2E40
                    Return Script.Common
                Case &H2E41, &H2E42, &H2E43 To &H2E44, &H2FF0 To &H2FFB, &H3000, &H3001 To &H3003
                    Return Script.Common
                Case &H3004, &H3006, &H3008, &H3009, &H300A, &H300B
                    Return Script.Common
                Case &H300C, &H300D, &H300E, &H300F, &H3010, &H3011
                    Return Script.Common
                Case &H3012 To &H3013, &H3014, &H3015, &H3016, &H3017, &H3018
                    Return Script.Common
                Case &H3019, &H301A, &H301B, &H301C, &H301D, &H301E To &H301F
                    Return Script.Common
                Case &H3020, &H3030, &H3031 To &H3035, &H3036 To &H3037, &H303C, &H303D
                    Return Script.Common
                Case &H303E To &H303F, &H309B To &H309C, &H30A0, &H30FB, &H30FC, &H3190 To &H3191
                    Return Script.Common
                Case &H3192 To &H3195, &H3196 To &H319F, &H31C0 To &H31E3, &H3220 To &H3229, &H322A To &H3247, &H3248 To &H324F
                    Return Script.Common
                Case &H3250, &H3251 To &H325F, &H327F, &H3280 To &H3289, &H328A To &H32B0, &H32B1 To &H32BF
                    Return Script.Common
                Case &H32C0 To &H32CF, &H3358 To &H33FF, &H4DC0 To &H4DFF, &HA700 To &HA716, &HA717 To &HA71F, &HA720 To &HA721
                    Return Script.Common
                Case &HA788, &HA789 To &HA78A, &HA830 To &HA835, &HA836 To &HA837, &HA838, &HA839
                    Return Script.Common
                Case &HA92E, &HA9CF, &HAB5B, &HFD3E, &HFD3F, &HFE10 To &HFE16
                    Return Script.Common
                Case &HFE17, &HFE18, &HFE19, &HFE30, &HFE31 To &HFE32, &HFE33 To &HFE34
                    Return Script.Common
                Case &HFE35, &HFE36, &HFE37, &HFE38, &HFE39, &HFE3A
                    Return Script.Common
                Case &HFE3B, &HFE3C, &HFE3D, &HFE3E, &HFE3F, &HFE40
                    Return Script.Common
                Case &HFE41, &HFE42, &HFE43, &HFE44, &HFE45 To &HFE46, &HFE47
                    Return Script.Common
                Case &HFE48, &HFE49 To &HFE4C, &HFE4D To &HFE4F, &HFE50 To &HFE52, &HFE54 To &HFE57, &HFE58
                    Return Script.Common
                Case &HFE59, &HFE5A, &HFE5B, &HFE5C, &HFE5D, &HFE5E
                    Return Script.Common
                Case &HFE5F To &HFE61, &HFE62, &HFE63, &HFE64 To &HFE66, &HFE68, &HFE69
                    Return Script.Common
                Case &HFE6A To &HFE6B, &HFEFF, &HFF01 To &HFF03, &HFF04, &HFF05 To &HFF07, &HFF08
                    Return Script.Common
                Case &HFF09, &HFF0A, &HFF0B, &HFF0C, &HFF0D, &HFF0E To &HFF0F
                    Return Script.Common
                Case &HFF10 To &HFF19, &HFF1A To &HFF1B, &HFF1C To &HFF1E, &HFF1F To &HFF20, &HFF3B, &HFF3C
                    Return Script.Common
                Case &HFF3D, &HFF3E, &HFF3F, &HFF40, &HFF5B, &HFF5C
                    Return Script.Common
                Case &HFF5D, &HFF5E, &HFF5F, &HFF60, &HFF61, &HFF62
                    Return Script.Common
                Case &HFF63, &HFF64 To &HFF65, &HFF70, &HFF9E To &HFF9F, &HFFE0 To &HFFE1, &HFFE2
                    Return Script.Common
                Case &HFFE3, &HFFE4, &HFFE5 To &HFFE6, &HFFE8, &HFFE9 To &HFFEC, &HFFED To &HFFEE
                    Return Script.Common
                Case &HFFF9 To &HFFFB, &HFFFC To &HFFFD, &H10100 To &H10102, &H10107 To &H10133, &H10137 To &H1013F, &H10190 To &H1019B
                    Return Script.Common
                Case &H101D0 To &H101FC, &H102E1 To &H102FB, &H1BCA0 To &H1BCA3, &H1D000 To &H1D0F5, &H1D100 To &H1D126, &H1D129 To &H1D164
                    Return Script.Common
                Case &H1D165 To &H1D166, &H1D16A To &H1D16C, &H1D16D To &H1D172, &H1D173 To &H1D17A, &H1D183 To &H1D184, &H1D18C To &H1D1A9
                    Return Script.Common
                Case &H1D1AE To &H1D1E8, &H1D300 To &H1D356, &H1D360 To &H1D371, &H1D400 To &H1D454, &H1D456 To &H1D49C, &H1D49E To &H1D49F
                    Return Script.Common
                Case &H1D4A2, &H1D4A5 To &H1D4A6, &H1D4A9 To &H1D4AC, &H1D4AE To &H1D4B9, &H1D4BB, &H1D4BD To &H1D4C3
                    Return Script.Common
                Case &H1D4C5 To &H1D505, &H1D507 To &H1D50A, &H1D50D To &H1D514, &H1D516 To &H1D51C, &H1D51E To &H1D539, &H1D53B To &H1D53E
                    Return Script.Common
                Case &H1D540 To &H1D544, &H1D546, &H1D54A To &H1D550, &H1D552 To &H1D6A5, &H1D6A8 To &H1D6C0, &H1D6C1
                    Return Script.Common
                Case &H1D6C2 To &H1D6DA, &H1D6DB, &H1D6DC To &H1D6FA, &H1D6FB, &H1D6FC To &H1D714, &H1D715
                    Return Script.Common
                Case &H1D716 To &H1D734, &H1D735, &H1D736 To &H1D74E, &H1D74F, &H1D750 To &H1D76E, &H1D76F
                    Return Script.Common
                Case &H1D770 To &H1D788, &H1D789, &H1D78A To &H1D7A8, &H1D7A9, &H1D7AA To &H1D7C2, &H1D7C3
                    Return Script.Common
                Case &H1D7C4 To &H1D7CB, &H1D7CE To &H1D7FF, &H1F000 To &H1F02B, &H1F030 To &H1F093, &H1F0A0 To &H1F0AE, &H1F0B1 To &H1F0BF
                    Return Script.Common
                Case &H1F0C1 To &H1F0CF, &H1F0D1 To &H1F0F5, &H1F100 To &H1F10C, &H1F110 To &H1F12E, &H1F130 To &H1F16B, &H1F170 To &H1F1AC
                    Return Script.Common
                Case &H1F1E6 To &H1F1FF, &H1F201 To &H1F202, &H1F210 To &H1F23B, &H1F240 To &H1F248, &H1F250 To &H1F251, &H1F300 To &H1F3FA
                    Return Script.Common
                Case &H1F3FB To &H1F3FF, &H1F400 To &H1F6D2, &H1F6E0 To &H1F6EC, &H1F6F0 To &H1F6F6, &H1F700 To &H1F773, &H1F780 To &H1F7D4
                    Return Script.Common
                Case &H1F800 To &H1F80B, &H1F810 To &H1F847, &H1F850 To &H1F859, &H1F860 To &H1F887, &H1F890 To &H1F8AD, &H1F910 To &H1F91E
                    Return Script.Common
                Case &H1F920 To &H1F927, &H1F930, &H1F933 To &H1F93E, &H1F940 To &H1F94B, &H1F950 To &H1F95E, &H1F980 To &H1F991
                    Return Script.Common
                Case &H1F9C0, &HE0001, &HE0020 To &HE007F
                    Return Script.Common
                Case &H41 To &H5A, &H61 To &H7A, &HAA, &HBA, &HC0 To &HD6, &HD8 To &HF6
                    Return Script.Latin
                Case &HF8 To &H1BA, &H1BB, &H1BC To &H1BF, &H1C0 To &H1C3, &H1C4 To &H293, &H294
                    Return Script.Latin
                Case &H295 To &H2AF, &H2B0 To &H2B8, &H2E0 To &H2E4, &H1D00 To &H1D25, &H1D2C To &H1D5C, &H1D62 To &H1D65
                    Return Script.Latin
                Case &H1D6B To &H1D77, &H1D79 To &H1D9A, &H1D9B To &H1DBE, &H1E00 To &H1EFF, &H2071, &H207F
                    Return Script.Latin
                Case &H2090 To &H209C, &H212A To &H212B, &H2132, &H214E, &H2160 To &H2182, &H2183 To &H2184
                    Return Script.Latin
                Case &H2185 To &H2188, &H2C60 To &H2C7B, &H2C7C To &H2C7D, &H2C7E To &H2C7F, &HA722 To &HA76F, &HA770
                    Return Script.Latin
                Case &HA771 To &HA787, &HA78B To &HA78E, &HA78F, &HA790 To &HA7AE, &HA7B0 To &HA7B7, &HA7F7
                    Return Script.Latin
                Case &HA7F8 To &HA7F9, &HA7FA, &HA7FB To &HA7FF, &HAB30 To &HAB5A, &HAB5C To &HAB5F, &HAB60 To &HAB64
                    Return Script.Latin
                Case &HFB00 To &HFB06, &HFF21 To &HFF3A, &HFF41 To &HFF5A
                    Return Script.Latin
                Case &H370 To &H373, &H375, &H376 To &H377, &H37A, &H37B To &H37D, &H37F
                    Return Script.Greek
                Case &H384, &H386, &H388 To &H38A, &H38C, &H38E To &H3A1, &H3A3 To &H3E1
                    Return Script.Greek
                Case &H3F0 To &H3F5, &H3F6, &H3F7 To &H3FF, &H1D26 To &H1D2A, &H1D5D To &H1D61, &H1D66 To &H1D6A
                    Return Script.Greek
                Case &H1DBF, &H1F00 To &H1F15, &H1F18 To &H1F1D, &H1F20 To &H1F45, &H1F48 To &H1F4D, &H1F50 To &H1F57
                    Return Script.Greek
                Case &H1F59, &H1F5B, &H1F5D, &H1F5F To &H1F7D, &H1F80 To &H1FB4, &H1FB6 To &H1FBC
                    Return Script.Greek
                Case &H1FBD, &H1FBE, &H1FBF To &H1FC1, &H1FC2 To &H1FC4, &H1FC6 To &H1FCC, &H1FCD To &H1FCF
                    Return Script.Greek
                Case &H1FD0 To &H1FD3, &H1FD6 To &H1FDB, &H1FDD To &H1FDF, &H1FE0 To &H1FEC, &H1FED To &H1FEF, &H1FF2 To &H1FF4
                    Return Script.Greek
                Case &H1FF6 To &H1FFC, &H1FFD To &H1FFE, &H2126, &HAB65, &H10140 To &H10174, &H10175 To &H10178
                    Return Script.Greek
                Case &H10179 To &H10189, &H1018A To &H1018B, &H1018C To &H1018E, &H101A0, &H1D200 To &H1D241, &H1D242 To &H1D244
                    Return Script.Greek
                Case &H1D245
                    Return Script.Greek
                Case &H400 To &H481, &H482, &H483 To &H484, &H487, &H488 To &H489, &H48A To &H52F
                    Return Script.Cyrillic
                Case &H1C80 To &H1C88, &H1D2B, &H1D78, &H2DE0 To &H2DFF, &HA640 To &HA66D, &HA66E
                    Return Script.Cyrillic
                Case &HA66F, &HA670 To &HA672, &HA673, &HA674 To &HA67D, &HA67E, &HA67F
                    Return Script.Cyrillic
                Case &HA680 To &HA69B, &HA69C To &HA69D, &HA69E To &HA69F, &HFE2E To &HFE2F
                    Return Script.Cyrillic
                Case &H531 To &H556, &H559, &H55A To &H55F, &H561 To &H587, &H58A, &H58D To &H58E
                    Return Script.Armenian
                Case &H58F, &HFB13 To &HFB17
                    Return Script.Armenian
                Case &H591 To &H5BD, &H5BE, &H5BF, &H5C0, &H5C1 To &H5C2, &H5C3
                    Return Script.Hebrew
                Case &H5C4 To &H5C5, &H5C6, &H5C7, &H5D0 To &H5EA, &H5F0 To &H5F2, &H5F3 To &H5F4
                    Return Script.Hebrew
                Case &HFB1D, &HFB1E, &HFB1F To &HFB28, &HFB29, &HFB2A To &HFB36, &HFB38 To &HFB3C
                    Return Script.Hebrew
                Case &HFB3E, &HFB40 To &HFB41, &HFB43 To &HFB44, &HFB46 To &HFB4F
                    Return Script.Hebrew
                Case &H600 To &H604, &H606 To &H608, &H609 To &H60A, &H60B, &H60D, &H60E To &H60F
                    Return Script.Arabic
                Case &H610 To &H61A, &H61E, &H620 To &H63F, &H641 To &H64A, &H656 To &H65F, &H660 To &H669
                    Return Script.Arabic
                Case &H66A To &H66D, &H66E To &H66F, &H671 To &H6D3, &H6D4, &H6D5, &H6D6 To &H6DC
                    Return Script.Arabic
                Case &H6DE, &H6DF To &H6E4, &H6E5 To &H6E6, &H6E7 To &H6E8, &H6E9, &H6EA To &H6ED
                    Return Script.Arabic
                Case &H6EE To &H6EF, &H6F0 To &H6F9, &H6FA To &H6FC, &H6FD To &H6FE, &H6FF, &H750 To &H77F
                    Return Script.Arabic
                Case &H8A0 To &H8B4, &H8B6 To &H8BD, &H8D4 To &H8E1, &H8E3 To &H8FF, &HFB50 To &HFBB1, &HFBB2 To &HFBC1
                    Return Script.Arabic
                Case &HFBD3 To &HFD3D, &HFD50 To &HFD8F, &HFD92 To &HFDC7, &HFDF0 To &HFDFB, &HFDFC, &HFDFD
                    Return Script.Arabic
                Case &HFE70 To &HFE74, &HFE76 To &HFEFC, &H10E60 To &H10E7E, &H1EE00 To &H1EE03, &H1EE05 To &H1EE1F, &H1EE21 To &H1EE22
                    Return Script.Arabic
                Case &H1EE24, &H1EE27, &H1EE29 To &H1EE32, &H1EE34 To &H1EE37, &H1EE39, &H1EE3B
                    Return Script.Arabic
                Case &H1EE42, &H1EE47, &H1EE49, &H1EE4B, &H1EE4D To &H1EE4F, &H1EE51 To &H1EE52
                    Return Script.Arabic
                Case &H1EE54, &H1EE57, &H1EE59, &H1EE5B, &H1EE5D, &H1EE5F
                    Return Script.Arabic
                Case &H1EE61 To &H1EE62, &H1EE64, &H1EE67 To &H1EE6A, &H1EE6C To &H1EE72, &H1EE74 To &H1EE77, &H1EE79 To &H1EE7C
                    Return Script.Arabic
                Case &H1EE7E, &H1EE80 To &H1EE89, &H1EE8B To &H1EE9B, &H1EEA1 To &H1EEA3, &H1EEA5 To &H1EEA9, &H1EEAB To &H1EEBB
                    Return Script.Arabic
                Case &H1EEF0 To &H1EEF1
                    Return Script.Arabic
                Case &H700 To &H70D, &H70F, &H710, &H711, &H712 To &H72F, &H730 To &H74A
                    Return Script.Syriac
                Case &H74D To &H74F
                    Return Script.Syriac
                Case &H780 To &H7A5, &H7A6 To &H7B0, &H7B1
                    Return Script.Thaana
                Case &H900 To &H902, &H903, &H904 To &H939, &H93A, &H93B, &H93C
                    Return Script.Devanagari
                Case &H93D, &H93E To &H940, &H941 To &H948, &H949 To &H94C, &H94D, &H94E To &H94F
                    Return Script.Devanagari
                Case &H950, &H953 To &H957, &H958 To &H961, &H962 To &H963, &H966 To &H96F, &H970
                    Return Script.Devanagari
                Case &H971, &H972 To &H97F, &HA8E0 To &HA8F1, &HA8F2 To &HA8F7, &HA8F8 To &HA8FA, &HA8FB
                    Return Script.Devanagari
                Case &HA8FC, &HA8FD
                    Return Script.Devanagari
                Case &H980, &H981, &H982 To &H983, &H985 To &H98C, &H98F To &H990, &H993 To &H9A8
                    Return Script.Bengali
                Case &H9AA To &H9B0, &H9B2, &H9B6 To &H9B9, &H9BC, &H9BD, &H9BE To &H9C0
                    Return Script.Bengali
                Case &H9C1 To &H9C4, &H9C7 To &H9C8, &H9CB To &H9CC, &H9CD, &H9CE, &H9D7
                    Return Script.Bengali
                Case &H9DC To &H9DD, &H9DF To &H9E1, &H9E2 To &H9E3, &H9E6 To &H9EF, &H9F0 To &H9F1, &H9F2 To &H9F3
                    Return Script.Bengali
                Case &H9F4 To &H9F9, &H9FA, &H9FB
                    Return Script.Bengali
                Case &HA01 To &HA02, &HA03, &HA05 To &HA0A, &HA0F To &HA10, &HA13 To &HA28, &HA2A To &HA30
                    Return Script.Gurmukhi
                Case &HA32 To &HA33, &HA35 To &HA36, &HA38 To &HA39, &HA3C, &HA3E To &HA40, &HA41 To &HA42
                    Return Script.Gurmukhi
                Case &HA47 To &HA48, &HA4B To &HA4D, &HA51, &HA59 To &HA5C, &HA5E, &HA66 To &HA6F
                    Return Script.Gurmukhi
                Case &HA70 To &HA71, &HA72 To &HA74, &HA75
                    Return Script.Gurmukhi
                Case &HA81 To &HA82, &HA83, &HA85 To &HA8D, &HA8F To &HA91, &HA93 To &HAA8, &HAAA To &HAB0
                    Return Script.Gujarati
                Case &HAB2 To &HAB3, &HAB5 To &HAB9, &HABC, &HABD, &HABE To &HAC0, &HAC1 To &HAC5
                    Return Script.Gujarati
                Case &HAC7 To &HAC8, &HAC9, &HACB To &HACC, &HACD, &HAD0, &HAE0 To &HAE1
                    Return Script.Gujarati
                Case &HAE2 To &HAE3, &HAE6 To &HAEF, &HAF0, &HAF1, &HAF9
                    Return Script.Gujarati
                Case &HB01, &HB02 To &HB03, &HB05 To &HB0C, &HB0F To &HB10, &HB13 To &HB28, &HB2A To &HB30
                    Return Script.Oriya
                Case &HB32 To &HB33, &HB35 To &HB39, &HB3C, &HB3D, &HB3E, &HB3F
                    Return Script.Oriya
                Case &HB40, &HB41 To &HB44, &HB47 To &HB48, &HB4B To &HB4C, &HB4D, &HB56
                    Return Script.Oriya
                Case &HB57, &HB5C To &HB5D, &HB5F To &HB61, &HB62 To &HB63, &HB66 To &HB6F, &HB70
                    Return Script.Oriya
                Case &HB71, &HB72 To &HB77
                    Return Script.Oriya
                Case &HB82, &HB83, &HB85 To &HB8A, &HB8E To &HB90, &HB92 To &HB95, &HB99 To &HB9A
                    Return Script.Tamil
                Case &HB9C, &HB9E To &HB9F, &HBA3 To &HBA4, &HBA8 To &HBAA, &HBAE To &HBB9, &HBBE To &HBBF
                    Return Script.Tamil
                Case &HBC0, &HBC1 To &HBC2, &HBC6 To &HBC8, &HBCA To &HBCC, &HBCD, &HBD0
                    Return Script.Tamil
                Case &HBD7, &HBE6 To &HBEF, &HBF0 To &HBF2, &HBF3 To &HBF8, &HBF9, &HBFA
                    Return Script.Tamil
                Case &HC00, &HC01 To &HC03, &HC05 To &HC0C, &HC0E To &HC10, &HC12 To &HC28, &HC2A To &HC39
                    Return Script.Telugu
                Case &HC3D, &HC3E To &HC40, &HC41 To &HC44, &HC46 To &HC48, &HC4A To &HC4D, &HC55 To &HC56
                    Return Script.Telugu
                Case &HC58 To &HC5A, &HC60 To &HC61, &HC62 To &HC63, &HC66 To &HC6F, &HC78 To &HC7E, &HC7F
                    Return Script.Telugu
                Case &HC80, &HC81, &HC82 To &HC83, &HC85 To &HC8C, &HC8E To &HC90, &HC92 To &HCA8
                    Return Script.Kannada
                Case &HCAA To &HCB3, &HCB5 To &HCB9, &HCBC, &HCBD, &HCBE, &HCBF
                    Return Script.Kannada
                Case &HCC0 To &HCC4, &HCC6, &HCC7 To &HCC8, &HCCA To &HCCB, &HCCC To &HCCD, &HCD5 To &HCD6
                    Return Script.Kannada
                Case &HCDE, &HCE0 To &HCE1, &HCE2 To &HCE3, &HCE6 To &HCEF, &HCF1 To &HCF2
                    Return Script.Kannada
                Case &HD01, &HD02 To &HD03, &HD05 To &HD0C, &HD0E To &HD10, &HD12 To &HD3A, &HD3D
                    Return Script.Malayalam
                Case &HD3E To &HD40, &HD41 To &HD44, &HD46 To &HD48, &HD4A To &HD4C, &HD4D, &HD4E
                    Return Script.Malayalam
                Case &HD4F, &HD54 To &HD56, &HD57, &HD58 To &HD5E, &HD5F To &HD61, &HD62 To &HD63
                    Return Script.Malayalam
                Case &HD66 To &HD6F, &HD70 To &HD78, &HD79, &HD7A To &HD7F
                    Return Script.Malayalam
                Case &HD82 To &HD83, &HD85 To &HD96, &HD9A To &HDB1, &HDB3 To &HDBB, &HDBD, &HDC0 To &HDC6
                    Return Script.Sinhala
                Case &HDCA, &HDCF To &HDD1, &HDD2 To &HDD4, &HDD6, &HDD8 To &HDDF, &HDE6 To &HDEF
                    Return Script.Sinhala
                Case &HDF2 To &HDF3, &HDF4, &H111E1 To &H111F4
                    Return Script.Sinhala
                Case &HE01 To &HE30, &HE31, &HE32 To &HE33, &HE34 To &HE3A, &HE40 To &HE45, &HE46
                    Return Script.Thai
                Case &HE47 To &HE4E, &HE4F, &HE50 To &HE59, &HE5A To &HE5B
                    Return Script.Thai
                Case &HE81 To &HE82, &HE84, &HE87 To &HE88, &HE8A, &HE8D, &HE94 To &HE97
                    Return Script.Lao
                Case &HE99 To &HE9F, &HEA1 To &HEA3, &HEA5, &HEA7, &HEAA To &HEAB, &HEAD To &HEB0
                    Return Script.Lao
                Case &HEB1, &HEB2 To &HEB3, &HEB4 To &HEB9, &HEBB To &HEBC, &HEBD, &HEC0 To &HEC4
                    Return Script.Lao
                Case &HEC6, &HEC8 To &HECD, &HED0 To &HED9, &HEDC To &HEDF
                    Return Script.Lao
                Case &HF00, &HF01 To &HF03, &HF04 To &HF12, &HF13, &HF14, &HF15 To &HF17
                    Return Script.Tibetan
                Case &HF18 To &HF19, &HF1A To &HF1F, &HF20 To &HF29, &HF2A To &HF33, &HF34, &HF35
                    Return Script.Tibetan
                Case &HF36, &HF37, &HF38, &HF39, &HF3A, &HF3B
                    Return Script.Tibetan
                Case &HF3C, &HF3D, &HF3E To &HF3F, &HF40 To &HF47, &HF49 To &HF6C, &HF71 To &HF7E
                    Return Script.Tibetan
                Case &HF7F, &HF80 To &HF84, &HF85, &HF86 To &HF87, &HF88 To &HF8C, &HF8D To &HF97
                    Return Script.Tibetan
                Case &HF99 To &HFBC, &HFBE To &HFC5, &HFC6, &HFC7 To &HFCC, &HFCE To &HFCF, &HFD0 To &HFD4
                    Return Script.Tibetan
                Case &HFD9 To &HFDA
                    Return Script.Tibetan
                Case &H1000 To &H102A, &H102B To &H102C, &H102D To &H1030, &H1031, &H1032 To &H1037, &H1038
                    Return Script.Myanmar
                Case &H1039 To &H103A, &H103B To &H103C, &H103D To &H103E, &H103F, &H1040 To &H1049, &H104A To &H104F
                    Return Script.Myanmar
                Case &H1050 To &H1055, &H1056 To &H1057, &H1058 To &H1059, &H105A To &H105D, &H105E To &H1060, &H1061
                    Return Script.Myanmar
                Case &H1062 To &H1064, &H1065 To &H1066, &H1067 To &H106D, &H106E To &H1070, &H1071 To &H1074, &H1075 To &H1081
                    Return Script.Myanmar
                Case &H1082, &H1083 To &H1084, &H1085 To &H1086, &H1087 To &H108C, &H108D, &H108E
                    Return Script.Myanmar
                Case &H108F, &H1090 To &H1099, &H109A To &H109C, &H109D, &H109E To &H109F, &HA9E0 To &HA9E4
                    Return Script.Myanmar
                Case &HA9E5, &HA9E6, &HA9E7 To &HA9EF, &HA9F0 To &HA9F9, &HA9FA To &HA9FE, &HAA60 To &HAA6F
                    Return Script.Myanmar
                Case &HAA70, &HAA71 To &HAA76, &HAA77 To &HAA79, &HAA7A, &HAA7B, &HAA7C
                    Return Script.Myanmar
                Case &HAA7D, &HAA7E To &HAA7F
                    Return Script.Myanmar
                Case &H10A0 To &H10C5, &H10C7, &H10CD, &H10D0 To &H10FA, &H10FC, &H10FD To &H10FF
                    Return Script.Georgian
                Case &H2D00 To &H2D25, &H2D27, &H2D2D
                    Return Script.Georgian
                Case &H1100 To &H11FF, &H302E To &H302F, &H3131 To &H318E, &H3200 To &H321E, &H3260 To &H327E, &HA960 To &HA97C
                    Return Script.Hangul
                Case &HAC00 To &HD7A3, &HD7B0 To &HD7C6, &HD7CB To &HD7FB, &HFFA0 To &HFFBE, &HFFC2 To &HFFC7, &HFFCA To &HFFCF
                    Return Script.Hangul
                Case &HFFD2 To &HFFD7, &HFFDA To &HFFDC
                    Return Script.Hangul
                Case &H1200 To &H1248, &H124A To &H124D, &H1250 To &H1256, &H1258, &H125A To &H125D, &H1260 To &H1288
                    Return Script.Ethiopic
                Case &H128A To &H128D, &H1290 To &H12B0, &H12B2 To &H12B5, &H12B8 To &H12BE, &H12C0, &H12C2 To &H12C5
                    Return Script.Ethiopic
                Case &H12C8 To &H12D6, &H12D8 To &H1310, &H1312 To &H1315, &H1318 To &H135A, &H135D To &H135F, &H1360 To &H1368
                    Return Script.Ethiopic
                Case &H1369 To &H137C, &H1380 To &H138F, &H1390 To &H1399, &H2D80 To &H2D96, &H2DA0 To &H2DA6, &H2DA8 To &H2DAE
                    Return Script.Ethiopic
                Case &H2DB0 To &H2DB6, &H2DB8 To &H2DBE, &H2DC0 To &H2DC6, &H2DC8 To &H2DCE, &H2DD0 To &H2DD6, &H2DD8 To &H2DDE
                    Return Script.Ethiopic
                Case &HAB01 To &HAB06, &HAB09 To &HAB0E, &HAB11 To &HAB16, &HAB20 To &HAB26, &HAB28 To &HAB2E
                    Return Script.Ethiopic
                Case &H13A0 To &H13F5, &H13F8 To &H13FD, &HAB70 To &HABBF
                    Return Script.Cherokee
                Case &H1400, &H1401 To &H166C, &H166D To &H166E, &H166F To &H167F, &H18B0 To &H18F5
                    Return Script.CanadianAboriginal
                Case &H1680, &H1681 To &H169A, &H169B, &H169C
                    Return Script.Ogham
                Case &H16A0 To &H16EA, &H16EE To &H16F0, &H16F1 To &H16F8
                    Return Script.Runic
                Case &H1780 To &H17B3, &H17B4 To &H17B5, &H17B6, &H17B7 To &H17BD, &H17BE To &H17C5, &H17C6
                    Return Script.Khmer
                Case &H17C7 To &H17C8, &H17C9 To &H17D3, &H17D4 To &H17D6, &H17D7, &H17D8 To &H17DA, &H17DB
                    Return Script.Khmer
                Case &H17DC, &H17DD, &H17E0 To &H17E9, &H17F0 To &H17F9, &H19E0 To &H19FF
                    Return Script.Khmer
                Case &H1800 To &H1801, &H1804, &H1806, &H1807 To &H180A, &H180B To &H180D, &H180E
                    Return Script.Mongolian
                Case &H1810 To &H1819, &H1820 To &H1842, &H1843, &H1844 To &H1877, &H1880 To &H1884, &H1885 To &H1886
                    Return Script.Mongolian
                Case &H1887 To &H18A8, &H18A9, &H18AA, &H11660 To &H1166C
                    Return Script.Mongolian
                Case &H3041 To &H3096, &H309D To &H309E, &H309F, &H1B001, &H1F200
                    Return Script.Hiragana
                Case &H30A1 To &H30FA, &H30FD To &H30FE, &H30FF, &H31F0 To &H31FF, &H32D0 To &H32FE, &H3300 To &H3357
                    Return Script.Katakana
                Case &HFF66 To &HFF6F, &HFF71 To &HFF9D, &H1B000
                    Return Script.Katakana
                Case &H2EA To &H2EB, &H3105 To &H312D, &H31A0 To &H31BA
                    Return Script.Bopomofo
                Case &H2E80 To &H2E99, &H2E9B To &H2EF3, &H2F00 To &H2FD5, &H3005, &H3007, &H3021 To &H3029
                    Return Script.Han
                Case &H3038 To &H303A, &H303B, &H3400 To &H4DB5, &H4E00 To &H9FD5, &HF900 To &HFA6D, &HFA70 To &HFAD9
                    Return Script.Han
                Case &H20000 To &H2A6D6, &H2A700 To &H2B734, &H2B740 To &H2B81D, &H2B820 To &H2CEA1, &H2F800 To &H2FA1D
                    Return Script.Han
                Case &HA000 To &HA014, &HA015, &HA016 To &HA48C, &HA490 To &HA4C6
                    Return Script.Yi
                Case &H10300 To &H1031F, &H10320 To &H10323
                    Return Script.OldItalic
                Case &H10330 To &H10340, &H10341, &H10342 To &H10349, &H1034A
                    Return Script.Gothic
                Case &H10400 To &H1044F
                    Return Script.Deseret
                Case &H300 To &H36F, &H485 To &H486, &H64B To &H655, &H670, &H951 To &H952, &H1AB0 To &H1ABD
                    Return Script.Inherited
                Case &H1ABE, &H1CD0 To &H1CD2, &H1CD4 To &H1CE0, &H1CE2 To &H1CE8, &H1CED, &H1CF4
                    Return Script.Inherited
                Case &H1CF8 To &H1CF9, &H1DC0 To &H1DF5, &H1DFB To &H1DFF, &H200C To &H200D, &H20D0 To &H20DC, &H20DD To &H20E0
                    Return Script.Inherited
                Case &H20E1, &H20E2 To &H20E4, &H20E5 To &H20F0, &H302A To &H302D, &H3099 To &H309A, &HFE00 To &HFE0F
                    Return Script.Inherited
                Case &HFE20 To &HFE2D, &H101FD, &H102E0, &H1D167 To &H1D169, &H1D17B To &H1D182, &H1D185 To &H1D18B
                    Return Script.Inherited
                Case &H1D1AA To &H1D1AD, &HE0100 To &HE01EF
                    Return Script.Inherited
                Case &H1700 To &H170C, &H170E To &H1711, &H1712 To &H1714
                    Return Script.Tagalog
                Case &H1720 To &H1731, &H1732 To &H1734
                    Return Script.Hanunoo
                Case &H1740 To &H1751, &H1752 To &H1753
                    Return Script.Buhid
                Case &H1760 To &H176C, &H176E To &H1770, &H1772 To &H1773
                    Return Script.Tagbanwa
                Case &H1900 To &H191E, &H1920 To &H1922, &H1923 To &H1926, &H1927 To &H1928, &H1929 To &H192B, &H1930 To &H1931
                    Return Script.Limbu
                Case &H1932, &H1933 To &H1938, &H1939 To &H193B, &H1940, &H1944 To &H1945, &H1946 To &H194F
                    Return Script.Limbu
                Case &H1950 To &H196D, &H1970 To &H1974
                    Return Script.TaiLe
                Case &H10000 To &H1000B, &H1000D To &H10026, &H10028 To &H1003A, &H1003C To &H1003D, &H1003F To &H1004D, &H10050 To &H1005D
                    Return Script.LinearB
                Case &H10080 To &H100FA
                    Return Script.LinearB
                Case &H10380 To &H1039D, &H1039F
                    Return Script.Ugaritic
                Case &H10450 To &H1047F
                    Return Script.Shavian
                Case &H10480 To &H1049D, &H104A0 To &H104A9
                    Return Script.Osmanya
                Case &H10800 To &H10805, &H10808, &H1080A To &H10835, &H10837 To &H10838, &H1083C, &H1083F
                    Return Script.Cypriot
                Case &H2800 To &H28FF
                    Return Script.Braille
                Case &H1A00 To &H1A16, &H1A17 To &H1A18, &H1A19 To &H1A1A, &H1A1B, &H1A1E To &H1A1F
                    Return Script.Buginese
                Case &H3E2 To &H3EF, &H2C80 To &H2CE4, &H2CE5 To &H2CEA, &H2CEB To &H2CEE, &H2CEF To &H2CF1, &H2CF2 To &H2CF3
                    Return Script.Coptic
                Case &H2CF9 To &H2CFC, &H2CFD, &H2CFE To &H2CFF
                    Return Script.Coptic
                Case &H1980 To &H19AB, &H19B0 To &H19C9, &H19D0 To &H19D9, &H19DA, &H19DE To &H19DF
                    Return Script.NewTaiLue
                Case &H2C00 To &H2C2E, &H2C30 To &H2C5E, &H1E000 To &H1E006, &H1E008 To &H1E018, &H1E01B To &H1E021, &H1E023 To &H1E024
                    Return Script.Glagolitic
                Case &H1E026 To &H1E02A
                    Return Script.Glagolitic
                Case &H2D30 To &H2D67, &H2D6F, &H2D70, &H2D7F
                    Return Script.Tifinagh
                Case &HA800 To &HA801, &HA802, &HA803 To &HA805, &HA806, &HA807 To &HA80A, &HA80B
                    Return Script.SylotiNagri
                Case &HA80C To &HA822, &HA823 To &HA824, &HA825 To &HA826, &HA827, &HA828 To &HA82B
                    Return Script.SylotiNagri
                Case &H103A0 To &H103C3, &H103C8 To &H103CF, &H103D0, &H103D1 To &H103D5
                    Return Script.OldPersian
                Case &H10A00, &H10A01 To &H10A03, &H10A05 To &H10A06, &H10A0C To &H10A0F, &H10A10 To &H10A13, &H10A15 To &H10A17
                    Return Script.Kharoshthi
                Case &H10A19 To &H10A33, &H10A38 To &H10A3A, &H10A3F, &H10A40 To &H10A47, &H10A50 To &H10A58
                    Return Script.Kharoshthi
                Case &H1B00 To &H1B03, &H1B04, &H1B05 To &H1B33, &H1B34, &H1B35, &H1B36 To &H1B3A
                    Return Script.Balinese
                Case &H1B3B, &H1B3C, &H1B3D To &H1B41, &H1B42, &H1B43 To &H1B44, &H1B45 To &H1B4B
                    Return Script.Balinese
                Case &H1B50 To &H1B59, &H1B5A To &H1B60, &H1B61 To &H1B6A, &H1B6B To &H1B73, &H1B74 To &H1B7C
                    Return Script.Balinese
                Case &H12000 To &H12399, &H12400 To &H1246E, &H12470 To &H12474, &H12480 To &H12543
                    Return Script.Cuneiform
                Case &H10900 To &H10915, &H10916 To &H1091B, &H1091F
                    Return Script.Phoenician
                Case &HA840 To &HA873, &HA874 To &HA877
                    Return Script.PhagsPa
                Case &H7C0 To &H7C9, &H7CA To &H7EA, &H7EB To &H7F3, &H7F4 To &H7F5, &H7F6, &H7F7 To &H7F9
                    Return Script.Nko
                Case &H7FA
                    Return Script.Nko
                Case &H1B80 To &H1B81, &H1B82, &H1B83 To &H1BA0, &H1BA1, &H1BA2 To &H1BA5, &H1BA6 To &H1BA7
                    Return Script.Sundanese
                Case &H1BA8 To &H1BA9, &H1BAA, &H1BAB To &H1BAD, &H1BAE To &H1BAF, &H1BB0 To &H1BB9, &H1BBA To &H1BBF
                    Return Script.Sundanese
                Case &H1CC0 To &H1CC7
                    Return Script.Sundanese
                Case &H1C00 To &H1C23, &H1C24 To &H1C2B, &H1C2C To &H1C33, &H1C34 To &H1C35, &H1C36 To &H1C37, &H1C3B To &H1C3F
                    Return Script.Lepcha
                Case &H1C40 To &H1C49, &H1C4D To &H1C4F
                    Return Script.Lepcha
                Case &H1C50 To &H1C59, &H1C5A To &H1C77, &H1C78 To &H1C7D, &H1C7E To &H1C7F
                    Return Script.OlChiki
                Case &HA500 To &HA60B, &HA60C, &HA60D To &HA60F, &HA610 To &HA61F, &HA620 To &HA629, &HA62A To &HA62B
                    Return Script.Vai
                Case &HA880 To &HA881, &HA882 To &HA8B3, &HA8B4 To &HA8C3, &HA8C4 To &HA8C5, &HA8CE To &HA8CF, &HA8D0 To &HA8D9
                    Return Script.Saurashtra
                Case &HA900 To &HA909, &HA90A To &HA925, &HA926 To &HA92D, &HA92F
                    Return Script.KayahLi
                Case &HA930 To &HA946, &HA947 To &HA951, &HA952 To &HA953, &HA95F
                    Return Script.Rejang
                Case &H10280 To &H1029C
                    Return Script.Lycian
                Case &H102A0 To &H102D0
                    Return Script.Carian
                Case &H10920 To &H10939, &H1093F
                    Return Script.Lydian
                Case &HAA00 To &HAA28, &HAA29 To &HAA2E, &HAA2F To &HAA30, &HAA31 To &HAA32, &HAA33 To &HAA34, &HAA35 To &HAA36
                    Return Script.Cham
                Case &HAA40 To &HAA42, &HAA43, &HAA44 To &HAA4B, &HAA4C, &HAA4D, &HAA50 To &HAA59
                    Return Script.Cham
                Case &HAA5C To &HAA5F
                    Return Script.Cham
                Case &H1A20 To &H1A54, &H1A55, &H1A56, &H1A57, &H1A58 To &H1A5E, &H1A60
                    Return Script.TaiTham
                Case &H1A61, &H1A62, &H1A63 To &H1A64, &H1A65 To &H1A6C, &H1A6D To &H1A72, &H1A73 To &H1A7C
                    Return Script.TaiTham
                Case &H1A7F, &H1A80 To &H1A89, &H1A90 To &H1A99, &H1AA0 To &H1AA6, &H1AA7, &H1AA8 To &H1AAD
                    Return Script.TaiTham
                Case &HAA80 To &HAAAF, &HAAB0, &HAAB1, &HAAB2 To &HAAB4, &HAAB5 To &HAAB6, &HAAB7 To &HAAB8
                    Return Script.TaiViet
                Case &HAAB9 To &HAABD, &HAABE To &HAABF, &HAAC0, &HAAC1, &HAAC2, &HAADB To &HAADC
                    Return Script.TaiViet
                Case &HAADD, &HAADE To &HAADF
                    Return Script.TaiViet
                Case &H10B00 To &H10B35, &H10B39 To &H10B3F
                    Return Script.Avestan
                Case &H13000 To &H1342E
                    Return Script.EgyptianHieroglyphs
                Case &H800 To &H815, &H816 To &H819, &H81A, &H81B To &H823, &H824, &H825 To &H827
                    Return Script.Samaritan
                Case &H828, &H829 To &H82D, &H830 To &H83E
                    Return Script.Samaritan
                Case &HA4D0 To &HA4F7, &HA4F8 To &HA4FD, &HA4FE To &HA4FF
                    Return Script.Lisu
                Case &HA6A0 To &HA6E5, &HA6E6 To &HA6EF, &HA6F0 To &HA6F1, &HA6F2 To &HA6F7, &H16800 To &H16A38
                    Return Script.Bamum
                Case &HA980 To &HA982, &HA983, &HA984 To &HA9B2, &HA9B3, &HA9B4 To &HA9B5, &HA9B6 To &HA9B9
                    Return Script.Javanese
                Case &HA9BA To &HA9BB, &HA9BC, &HA9BD To &HA9C0, &HA9C1 To &HA9CD, &HA9D0 To &HA9D9, &HA9DE To &HA9DF
                    Return Script.Javanese
                Case &HAAE0 To &HAAEA, &HAAEB, &HAAEC To &HAAED, &HAAEE To &HAAEF, &HAAF0 To &HAAF1, &HAAF2
                    Return Script.MeeteiMayek
                Case &HAAF3 To &HAAF4, &HAAF5, &HAAF6, &HABC0 To &HABE2, &HABE3 To &HABE4, &HABE5
                    Return Script.MeeteiMayek
                Case &HABE6 To &HABE7, &HABE8, &HABE9 To &HABEA, &HABEB, &HABEC, &HABED
                    Return Script.MeeteiMayek
                Case &HABF0 To &HABF9
                    Return Script.MeeteiMayek
                Case &H10840 To &H10855, &H10857, &H10858 To &H1085F
                    Return Script.ImperialAramaic
                Case &H10A60 To &H10A7C, &H10A7D To &H10A7E, &H10A7F
                    Return Script.OldSouthArabian
                Case &H10B40 To &H10B55, &H10B58 To &H10B5F
                    Return Script.InscriptionalParthian
                Case &H10B60 To &H10B72, &H10B78 To &H10B7F
                    Return Script.InscriptionalPahlavi
                Case &H10C00 To &H10C48
                    Return Script.OldTurkic
                Case &H11080 To &H11081, &H11082, &H11083 To &H110AF, &H110B0 To &H110B2, &H110B3 To &H110B6, &H110B7 To &H110B8
                    Return Script.Kaithi
                Case &H110B9 To &H110BA, &H110BB To &H110BC, &H110BD, &H110BE To &H110C1
                    Return Script.Kaithi
                Case &H1BC0 To &H1BE5, &H1BE6, &H1BE7, &H1BE8 To &H1BE9, &H1BEA To &H1BEC, &H1BED
                    Return Script.Batak
                Case &H1BEE, &H1BEF To &H1BF1, &H1BF2 To &H1BF3, &H1BFC To &H1BFF
                    Return Script.Batak
                Case &H11000, &H11001, &H11002, &H11003 To &H11037, &H11038 To &H11046, &H11047 To &H1104D
                    Return Script.Brahmi
                Case &H11052 To &H11065, &H11066 To &H1106F, &H1107F
                    Return Script.Brahmi
                Case &H840 To &H858, &H859 To &H85B, &H85E
                    Return Script.Mandaic
                Case &H11100 To &H11102, &H11103 To &H11126, &H11127 To &H1112B, &H1112C, &H1112D To &H11134, &H11136 To &H1113F
                    Return Script.Chakma
                Case &H11140 To &H11143
                    Return Script.Chakma
                Case &H109A0 To &H109B7, &H109BC To &H109BD, &H109BE To &H109BF, &H109C0 To &H109CF, &H109D2 To &H109FF
                    Return Script.MeroiticCursive
                Case &H10980 To &H1099F
                    Return Script.MeroiticHieroglyphs
                Case &H16F00 To &H16F44, &H16F50, &H16F51 To &H16F7E, &H16F8F To &H16F92, &H16F93 To &H16F9F
                    Return Script.Miao
                Case &H11180 To &H11181, &H11182, &H11183 To &H111B2, &H111B3 To &H111B5, &H111B6 To &H111BE, &H111BF To &H111C0
                    Return Script.Sharada
                Case &H111C1 To &H111C4, &H111C5 To &H111C9, &H111CA To &H111CC, &H111CD, &H111D0 To &H111D9, &H111DA
                    Return Script.Sharada
                Case &H111DB, &H111DC, &H111DD To &H111DF
                    Return Script.Sharada
                Case &H110D0 To &H110E8, &H110F0 To &H110F9
                    Return Script.SoraSompeng
                Case &H11680 To &H116AA, &H116AB, &H116AC, &H116AD, &H116AE To &H116AF, &H116B0 To &H116B5
                    Return Script.Takri
                Case &H116B6, &H116B7, &H116C0 To &H116C9
                    Return Script.Takri
                Case &H10530 To &H10563, &H1056F
                    Return Script.CaucasianAlbanian
                Case &H16AD0 To &H16AED, &H16AF0 To &H16AF4, &H16AF5
                    Return Script.BassaVah
                Case &H1BC00 To &H1BC6A, &H1BC70 To &H1BC7C, &H1BC80 To &H1BC88, &H1BC90 To &H1BC99, &H1BC9C, &H1BC9D To &H1BC9E
                    Return Script.Duployan
                Case &H1BC9F
                    Return Script.Duployan
                Case &H10500 To &H10527
                    Return Script.Elbasan
                Case &H11300 To &H11301, &H11302 To &H11303, &H11305 To &H1130C, &H1130F To &H11310, &H11313 To &H11328, &H1132A To &H11330
                    Return Script.Grantha
                Case &H11332 To &H11333, &H11335 To &H11339, &H1133C, &H1133D, &H1133E To &H1133F, &H11340
                    Return Script.Grantha
                Case &H11341 To &H11344, &H11347 To &H11348, &H1134B To &H1134D, &H11350, &H11357, &H1135D To &H11361
                    Return Script.Grantha
                Case &H11362 To &H11363, &H11366 To &H1136C, &H11370 To &H11374
                    Return Script.Grantha
                Case &H16B00 To &H16B2F, &H16B30 To &H16B36, &H16B37 To &H16B3B, &H16B3C To &H16B3F, &H16B40 To &H16B43, &H16B44
                    Return Script.PahawhHmong
                Case &H16B45, &H16B50 To &H16B59, &H16B5B To &H16B61, &H16B63 To &H16B77, &H16B7D To &H16B8F
                    Return Script.PahawhHmong
                Case &H11200 To &H11211, &H11213 To &H1122B, &H1122C To &H1122E, &H1122F To &H11231, &H11232 To &H11233, &H11234
                    Return Script.Khojki
                Case &H11235, &H11236 To &H11237, &H11238 To &H1123D, &H1123E
                    Return Script.Khojki
                Case &H10600 To &H10736, &H10740 To &H10755, &H10760 To &H10767
                    Return Script.LinearA
                Case &H11150 To &H11172, &H11173, &H11174 To &H11175, &H11176
                    Return Script.Mahajani
                Case &H10AC0 To &H10AC7, &H10AC8, &H10AC9 To &H10AE4, &H10AE5 To &H10AE6, &H10AEB To &H10AEF, &H10AF0 To &H10AF6
                    Return Script.Manichaean
                Case &H1E800 To &H1E8C4, &H1E8C7 To &H1E8CF, &H1E8D0 To &H1E8D6
                    Return Script.MendeKikakui
                Case &H11600 To &H1162F, &H11630 To &H11632, &H11633 To &H1163A, &H1163B To &H1163C, &H1163D, &H1163E
                    Return Script.Modi
                Case &H1163F To &H11640, &H11641 To &H11643, &H11644, &H11650 To &H11659
                    Return Script.Modi
                Case &H16A40 To &H16A5E, &H16A60 To &H16A69, &H16A6E To &H16A6F
                    Return Script.Mro
                Case &H10A80 To &H10A9C, &H10A9D To &H10A9F
                    Return Script.OldNorthArabian
                Case &H10880 To &H1089E, &H108A7 To &H108AF
                    Return Script.Nabataean
                Case &H10860 To &H10876, &H10877 To &H10878, &H10879 To &H1087F
                    Return Script.Palmyrene
                Case &H11AC0 To &H11AF8
                    Return Script.PauCinHau
                Case &H10350 To &H10375, &H10376 To &H1037A
                    Return Script.OldPermic
                Case &H10B80 To &H10B91, &H10B99 To &H10B9C, &H10BA9 To &H10BAF
                    Return Script.PsalterPahlavi
                Case &H11580 To &H115AE, &H115AF To &H115B1, &H115B2 To &H115B5, &H115B8 To &H115BB, &H115BC To &H115BD, &H115BE
                    Return Script.Siddham
                Case &H115BF To &H115C0, &H115C1 To &H115D7, &H115D8 To &H115DB, &H115DC To &H115DD
                    Return Script.Siddham
                Case &H112B0 To &H112DE, &H112DF, &H112E0 To &H112E2, &H112E3 To &H112EA, &H112F0 To &H112F9
                    Return Script.Khudawadi
                Case &H11480 To &H114AF, &H114B0 To &H114B2, &H114B3 To &H114B8, &H114B9, &H114BA, &H114BB To &H114BE
                    Return Script.Tirhuta
                Case &H114BF To &H114C0, &H114C1, &H114C2 To &H114C3, &H114C4 To &H114C5, &H114C6, &H114C7
                    Return Script.Tirhuta
                Case &H114D0 To &H114D9
                    Return Script.Tirhuta
                Case &H118A0 To &H118DF, &H118E0 To &H118E9, &H118EA To &H118F2, &H118FF
                    Return Script.WarangCiti
                Case &H11700 To &H11719, &H1171D To &H1171F, &H11720 To &H11721, &H11722 To &H11725, &H11726, &H11727 To &H1172B
                    Return Script.Ahom
                Case &H11730 To &H11739, &H1173A To &H1173B, &H1173C To &H1173E, &H1173F
                    Return Script.Ahom
                Case &H14400 To &H14646
                    Return Script.AnatolianHieroglyphs
                Case &H108E0 To &H108F2, &H108F4 To &H108F5, &H108FB To &H108FF
                    Return Script.Hatran
                Case &H11280 To &H11286, &H11288, &H1128A To &H1128D, &H1128F To &H1129D, &H1129F To &H112A8, &H112A9
                    Return Script.Multani
                Case &H10C80 To &H10CB2, &H10CC0 To &H10CF2, &H10CFA To &H10CFF
                    Return Script.OldHungarian
                Case &H1D800 To &H1D9FF, &H1DA00 To &H1DA36, &H1DA37 To &H1DA3A, &H1DA3B To &H1DA6C, &H1DA6D To &H1DA74, &H1DA75
                    Return Script.SignWriting
                Case &H1DA76 To &H1DA83, &H1DA84, &H1DA85 To &H1DA86, &H1DA87 To &H1DA8B, &H1DA9B To &H1DA9F, &H1DAA1 To &H1DAAF
                    Return Script.SignWriting
                Case &H1E900 To &H1E943, &H1E944 To &H1E94A, &H1E950 To &H1E959, &H1E95E To &H1E95F
                    Return Script.Adlam
                Case &H11C00 To &H11C08, &H11C0A To &H11C2E, &H11C2F, &H11C30 To &H11C36, &H11C38 To &H11C3D, &H11C3E
                    Return Script.Bhaiksuki
                Case &H11C3F, &H11C40, &H11C41 To &H11C45, &H11C50 To &H11C59, &H11C5A To &H11C6C
                    Return Script.Bhaiksuki
                Case &H11C70 To &H11C71, &H11C72 To &H11C8F, &H11C92 To &H11CA7, &H11CA9, &H11CAA To &H11CB0, &H11CB1
                    Return Script.Marchen
                Case &H11CB2 To &H11CB3, &H11CB4, &H11CB5 To &H11CB6
                    Return Script.Marchen
                Case &H11400 To &H11434, &H11435 To &H11437, &H11438 To &H1143F, &H11440 To &H11441, &H11442 To &H11444, &H11445
                    Return Script.Newa
                Case &H11446, &H11447 To &H1144A, &H1144B To &H1144F, &H11450 To &H11459, &H1145B, &H1145D
                    Return Script.Newa
                Case &H104B0 To &H104D3, &H104D8 To &H104FB
                    Return Script.Osage
                Case &H16FE0, &H17000 To &H187EC, &H18800 To &H18AF2
                    Return Script.Tangut
                Case Else
                    Return Script.Any
            End Select
        End Function

        ''' <summary>
        ''' The sentencepiece-style fixed script mapping used by the UnicodeScripts
        ''' pre-tokenizer: U+30FC maps to Han, Hiragana/Katakana map to Han, space maps
        ''' to Any.
        ''' </summary>
        Public Function FixedScript(c As Integer) As Script
            If c = &H30FC Then Return Script.Han
            If c = &H20 Then Return Script.Any
            Dim raw As Script = GetScript(c)
            If raw = Script.Hiragana OrElse raw = Script.Katakana Then Return Script.Han
            Return raw
        End Function

    End Module

End Namespace
