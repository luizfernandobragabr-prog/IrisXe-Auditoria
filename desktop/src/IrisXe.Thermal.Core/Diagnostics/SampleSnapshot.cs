namespace IrisXe.Thermal.Core.Diagnostics;

using IrisXe.Thermal.Core.Access;
using IrisXe.Thermal.Core.Decoding;

/// <summary>Leitura de um MSR em um processador logico, com decodificacao quando bem-sucedida.</summary>
public sealed record LogicalProcessorReading
{
    public required int ProcessorGroup { get; init; }

    public required int ProcessorNumber { get; init; }

    public required MsrReadResult Result { get; init; }

    public ThermStatusDecoding? Decoded { get; init; }

    /// <summary>Temperatura calculada (TjMAX − readout), quando valida. Nulo = nao disponivel.</summary>
    public int? TemperatureC => Decoded?.TemperatureC;
}

/// <summary>
/// Uma rodada completa de amostragem: 0x19C em cada processador logico; 0x1B1 e 0x1A2 em
/// todos os processadores logicos para conferencia de consistencia (divergencias entre LPs
/// NAO sao descartadas — viram notas na propria amostra, requisito 14.1-A).
/// </summary>
public sealed record SampleSnapshot
{
    public required DateTime TimestampUtc { get; init; }

    public required IReadOnlyList<LogicalProcessorReading> CoreReadings { get; init; }

    /// <summary>Leituras do MSR do pacote (0x1B1) em cada processador logico — conferencia.</summary>
    public required IReadOnlyList<LogicalProcessorReading> PackageReadings { get; init; }

    /// <summary>Leituras do MSR de temperatura-alvo (0x1A2) em cada processador logico — conferencia.</summary>
    public required IReadOnlyList<MsrReadResult> TemperatureTargetReadings { get; init; }

    /// <summary>Decodificacao do 0x1A2 (primeira leitura bem-sucedida).</summary>
    public TemperatureTargetDecoding? TemperatureTarget { get; init; }

    /// <summary>Decodificacao do 0x1B1 (primeira leitura bem-sucedida).</summary>
    public PackageThermStatusDecoding? Package { get; init; }

    /// <summary>Divergencias entre processadores logicos / falhas — nunca descartadas.</summary>
    public required IReadOnlyList<string> Notes { get; init; }

    /// <summary>Mecanismo que produziu a amostra (para rotulagem de modo simulado).</summary>
    public required string MechanismName { get; init; }

    public required bool Simulated { get; init; }
}
