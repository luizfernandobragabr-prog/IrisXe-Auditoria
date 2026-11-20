namespace IrisXe.Thermal.Core.Diagnostics;

/// <summary>
/// Registro de auditoria de UMA leitura (requisito 14.1-D): processador logico consultado,
/// endereco do MSR, valor bruto, data e horario, resultado e erro ocorrido, quando aplicavel.
/// </summary>
public sealed record ReadingRecord
{
    public required DateTime TimestampUtc { get; init; }

    public required int ProcessorGroup { get; init; }

    public required int ProcessorNumber { get; init; }

    public required int MsrAddress { get; init; }

    public required string MsrName { get; init; }

    public required string Mechanism { get; init; }

    /// <summary>"Sucesso", "Falha" ou "Nao disponivel".</summary>
    public required string Status { get; init; }

    /// <summary>Valor bruto em hexadecimal, quando obtido.</summary>
    public string? RawHex { get; init; }

    public string? Error { get; init; }

    public int? Win32Error { get; init; }

    /// <summary>Verdadeiro quando o valor e de teste manual (SIMULADO) — sempre rotulado.</summary>
    public required bool Simulated { get; init; }
}
