namespace IrisXe.Thermal.Core.Diagnostics;

using IrisXe.Thermal.Core;

/// <summary>
/// Transicao de um indicador termico detectada entre duas amostragens.
///
/// REGRA DE HONESTIDADE TEMPORAL (requisito §6): o horario registrado e o da AMOSTRAGEM em
/// que a mudanca foi observada. O inicio real do evento ocorreu em algum momento entre a
/// amostra anterior e a atual e NAO pode ser determinado com precisao dentro do intervalo de
/// amostragem — a nota do evento deixa isso explicito.
/// </summary>
public sealed record TransitionEvent
{
    public required DateTime DetectedAtUtc { get; init; }

    /// <summary>Endereco do MSR (0x19C ou 0x1B1).</summary>
    public required int MsrAddress { get; init; }

    /// <summary>"Nucleo (LP n)" ou "Pacote".</summary>
    public required string Scope { get; init; }

    public required string Indicator { get; init; }

    /// <summary>"Corrente" ou "Historico".</summary>
    public required string Kind { get; init; }

    public required string From { get; init; }

    public required string To { get; init; }

    public required double IntervalSeconds { get; init; }

    public string HonestTimestampNote =>
        $"Detectado na amostragem de {DetectedAtUtc:HH:mm:ss} UTC; o início real ocorreu em algum momento nos "
        + $"{IntervalSeconds:0.#} s anteriores (entre amostras) e não pode ser determinado com precisão dentro do intervalo de amostragem.";

    public static string State(bool active) => active ? "ativo" : "inativo";

    public static string LogState(bool set) => set ? "registrado" : "não registrado";
}
