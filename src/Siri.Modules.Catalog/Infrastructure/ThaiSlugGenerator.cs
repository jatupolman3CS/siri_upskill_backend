using System.Text;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>
/// Generates a URL-safe base slug from a course title that may contain Thai text (task P1-04: "slug
/// generator (ไทย→latin)"). Pure, stateless function — no DB access; <c>CreateCourseHandler</c> owns
/// turning the base slug this returns into a globally-unique one (checking <c>Courses.Slug</c> and
/// appending a numeric suffix on collision).
/// <para>
/// <b>Deliberately a hand-rolled character map, not a transliteration library</b> — checked the .NET
/// ecosystem first (backend.md: "ห้ามเดา version/API... ค้นหาหรือเช็คก่อน" extends to "don't reach for an
/// unvetted dependency without looking"). The only real options were
/// <c>ThaiRomanizationSharp</c> (bundles a native Torch/ML runtime, or requires a Python interop
/// fallback) and general transliteration libraries with no Thai rules built in — both far heavier than
/// "produce a readable, deterministic, URL-safe slug" needs, and the Python-interop path would put a
/// second language runtime inside a .NET-only backend. A simplified single-character map is the
/// pragmatic choice the same way many Thai CMS "slugify" implementations already work.
/// </para>
/// <para>
/// <b>Known, accepted limitations</b> (not full Royal Thai General System transliteration): leading
/// vowels (เ/แ/โ/ใ/ไ, written before the consonant they modify but pronounced after it) are emitted in
/// writing order, not phonetic order — so เก becomes "ek", not the phonetically-closer "ke". Thai script
/// has no spaces between words within a phrase, and this does no dictionary-based word segmentation, so
/// a long Thai run becomes one unbroken lowercase segment rather than several hyphenated words. Tone
/// marks and most diacritics are stripped rather than represented. None of this affects correctness of
/// what a slug actually needs: unique, ASCII, URL-safe, and roughly recognizable — not a phonetically
/// accurate transcription.
/// </para>
/// </summary>
public static class ThaiSlugGenerator
{
    private static readonly IReadOnlyDictionary<char, string> CharacterMap = BuildCharacterMap();

    /// <summary>
    /// Transliterates <paramref name="text"/> character-by-character and slugifies the result (lowercase,
    /// <c>[a-z0-9]</c> plus single hyphens as separators, no leading/trailing/doubled hyphens). ASCII
    /// letters/digits already in <paramref name="text"/> (e.g. course titles that mix Thai with English
    /// product/technology names) pass through unchanged. Returns <see cref="string.Empty"/> if nothing in
    /// <paramref name="text"/> maps to anything — the caller (<c>CreateCourseHandler</c>) is responsible
    /// for a fallback base slug in that case, the same "this function doesn't know about the rest of the
    /// system" split every other Infrastructure utility in this module keeps (mirrors
    /// <c>CategoryTreeAssembler</c> taking a flat list and knowing nothing about how it was queried).
    /// </summary>
    public static string GenerateBaseSlug(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var transliterated = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (CharacterMap.TryGetValue(ch, out var mapped))
            {
                transliterated.Append(mapped);
            }
            else
            {
                transliterated.Append(ch); // ASCII letters/digits/whitespace/punctuation pass through as-is
            }
        }

        return Slugify(transliterated.ToString());
    }

    private static string Slugify(string value)
    {
        var lowered = value.ToLowerInvariant();
        var builder = new StringBuilder(lowered.Length);
        var lastAppendedWasHyphen = false;

        foreach (var ch in lowered)
        {
            if (ch is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                builder.Append(ch);
                lastAppendedWasHyphen = false;
            }
            else if (builder.Length > 0 && !lastAppendedWasHyphen)
            {
                builder.Append('-');
                lastAppendedWasHyphen = true;
            }
        }

        return builder.ToString().TrimEnd('-');
    }

    private static IReadOnlyDictionary<char, string> BuildCharacterMap()
    {
        var map = new Dictionary<char, string>
        {
            // ---- Consonants (initial-position sound, simplified RTGS-ish) --------------------------
            ['ก'] = "k",
            ['ข'] = "kh",
            ['ฃ'] = "kh", // obsolete
            ['ค'] = "kh",
            ['ฅ'] = "kh", // obsolete
            ['ฆ'] = "kh",
            ['ง'] = "ng",
            ['จ'] = "ch",
            ['ฉ'] = "ch",
            ['ช'] = "ch",
            ['ซ'] = "s",
            ['ฌ'] = "ch",
            ['ญ'] = "y",
            ['ฎ'] = "d",
            ['ฏ'] = "t",
            ['ฐ'] = "th",
            ['ฑ'] = "th",
            ['ฒ'] = "th",
            ['ณ'] = "n",
            ['ด'] = "d",
            ['ต'] = "t",
            ['ถ'] = "th",
            ['ท'] = "th",
            ['ธ'] = "th",
            ['น'] = "n",
            ['บ'] = "b",
            ['ป'] = "p",
            ['ผ'] = "ph",
            ['ฝ'] = "f",
            ['พ'] = "ph",
            ['ฟ'] = "f",
            ['ภ'] = "ph",
            ['ม'] = "m",
            ['ย'] = "y",
            ['ร'] = "r",
            ['ล'] = "l",
            ['ว'] = "w",
            ['ศ'] = "s",
            ['ษ'] = "s",
            ['ส'] = "s",
            ['ห'] = "h",
            ['ฬ'] = "l",
            // 'อ' omitted deliberately: acts mostly as a silent vowel carrier (e.g. อา, เอา) — mapping it
            // to any consonant sound would be wrong far more often than it would be right.
            ['ฮ'] = "h",

            // ---- Vowels (leading vowels kept in writing order — see class doc comment) --------------
            ['เ'] = "e",
            ['แ'] = "ae",
            ['โ'] = "o",
            ['ใ'] = "ai",
            ['ไ'] = "ai",
            ['ะ'] = "a",
            ['ั'] = "a", // mai han-akat
            ['า'] = "a",
            ['ิ'] = "i",
            ['ี'] = "i",
            ['ึ'] = "ue",
            ['ื'] = "ue",
            ['ุ'] = "u",
            ['ู'] = "u",
            ['ำ'] = "am",
            ['ฤ'] = "rue",
            ['ฦ'] = "lue", // obsolete
            ['ๅ'] = "", // lakkhangyao — lengthens the preceding vowel, no separate sound of its own

            // ---- Tone marks / diacritics (no Latin sound of their own) -------------------------------
            ['่'] = "", // mai ek
            ['้'] = "", // mai tho
            ['๊'] = "", // mai tri
            ['๋'] = "", // mai chattawa
            ['์'] = "", // thanthakhat (silences the preceding consonant)
            ['็'] = "", // mai taikhu (short-vowel marker)
            ['ๆ'] = "", // mai yamok (repeat previous word)
            ['ฯ'] = "", // paiyannoi (abbreviation / etc.)
            ['฿'] = "", // baht sign

            // ---- Thai digits → Arabic digits ---------------------------------------------------------
            ['๐'] = "0",
            ['๑'] = "1",
            ['๒'] = "2",
            ['๓'] = "3",
            ['๔'] = "4",
            ['๕'] = "5",
            ['๖'] = "6",
            ['๗'] = "7",
            ['๘'] = "8",
            ['๙'] = "9",
        };

        return map;
    }
}
