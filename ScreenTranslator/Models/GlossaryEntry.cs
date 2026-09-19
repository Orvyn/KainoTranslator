using System;
using System.Collections.Generic;

namespace ScreenTranslator.Models;

public sealed class GlossaryProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<GlossaryEntry> Entries { get; set; } = new();
}

/// <summary>
/// A single glossary rule: how to handle one recurring name/term.
/// - TargetTerm empty: the term is protected from translation (kept exactly as written).
/// - TargetTerm set: the term is always replaced with this exact translation instead of
///   whatever the translator API would have produced for it.
/// </summary>
public sealed class GlossaryEntry
{
    public string SourceTerm { get; set; } = "";
    public string TargetTerm { get; set; } = "";
}
