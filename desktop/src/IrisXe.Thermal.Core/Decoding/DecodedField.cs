namespace IrisXe.Thermal.Core.Decoding;

/// <summary>
/// Campo decodificado de um MSR, com a citacao documental que fundamenta a interpretacao.
/// Campos reservados/nao documentados sao listados como "Reservado" e NUNCA interpretados.
/// </summary>
public sealed record DecodedField
{
    /// <summary>Intervalo de bits, ex.: "[23:16]".</summary>
    public required string Bits { get; init; }

    /// <summary>Nome do campo conforme a documentacao Intel.</summary>
    public required string Name { get; init; }

    /// <summary>Mascara de 64 bits aplicada sobre o valor bruto.</summary>
    public required ulong Mask { get; init; }

    /// <summary>Deslocamento (bits) aplicado ao valor bruto.</summary>
    public required int Shift { get; init; }

    /// <summary>Accesso documentado: "RO", "R/W", "R/WC0" etc. Para leitura pura, tudo e efetivamente read-only.</summary>
    public required string Access { get; init; }

    /// <summary>Classificacao temporal: "Corrente", "Historico", "Configuracao", "Medicao" ou "Reservado".</summary>
    public required string Kind { get; init; }

    /// <summary>Significado do campo, em portugues, fiel a documentacao.</summary>
    public required string Meaning { get; init; }

    /// <summary>Citacao da fonte Intel (documento e secao/tabela).</summary>
    public required string Citation { get; init; }

    /// <summary>Valor bruto extraido com a mascara (ja deslocado).</summary>
    public required ulong RawValue { get; init; }

    /// <summary>Observacoes e limitacoes (ex.: conversao de Tau nao documentada).</summary>
    public string? Note { get; init; }

    public string RawHex => $"0x{RawValue:X}";
}
