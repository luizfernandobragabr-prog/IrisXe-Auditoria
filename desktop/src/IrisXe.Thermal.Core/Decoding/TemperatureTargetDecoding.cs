namespace IrisXe.Thermal.Core.Decoding;

/// <summary>
/// Decodificador do MSR_TEMPERATURE_TARGET (IA32_TEMPERATURE_TARGET, 0x1A2).
///
/// Fontes aplicaveis ao Tiger Lake (Core i7-1195G7):
///  - Intel 11th Generation Core Processor Families Datasheet, Vol. 1, documento 631121-012:
///      * §4.1.3.1  "Adaptive Thermal Monitor" — "TjMAX is factory calibrated and is not user
///        configurable. The default value is software visible in the TEMPERATURE_TARGET (0x1A2)
///        MSR, bits [23:16]."
///      * §4.1.3.1.1 "TCC Activation Offset" — offset em GRAUS CELSIUS escrito nos bits [29:24],
///        subtraido do valor dos bits [23:16]; e, no modo com janela temporal, o Tau e escrito
///        nos bits [6:0] do mesmo MSR, aplicado sobre uma media EWMA de temperatura.
///  - Intel 64 and IA-32 SDM, Vol. 4 (Model-Specific Registers), tabela do MSR_TEMPERATURE_TARGET:
///      * bits [23:16] Temperature Target; bits [27:24] TCC Activation Offset; bits [15:0] e
///        [63:28] reservados. O SDM generico NAO documenta o campo Tau.
///
/// Politica de decodificacao adotada (a mais especifica para Tiger Lake prevalece):
///  - TjMAX: bits [23:16] (fontes concordam).
///  - TCC Activation Offset: bits [29:24] (datasheet TGL, §4.1.3.1.1; corroborado pelo driver
///    termico Linux intel_tcc, que usa mascara 0x3F para TGL+). O valor segundo a mascara
///    generica do SDM ([27:24]) tambem e apresentado para conferencia; qualquer divergencia
///    entre as duas leituras e sinalizada explicitamente.
///  - Tau: bits [6:0] (datasheet TGL §4.1.3.1.1). A conversao do valor bruto para segundos
///    NAO e documentada para o TCC do Tiger Lake — o valor e exibido em bruto, com a
///    limitacao declarada. Nao confundir com o Tau do Turbo Boost (dominio de MSR distinto).
/// </summary>
public sealed record TemperatureTargetDecoding
{
    public required ulong Raw { get; init; }

    /// <summary>TjMAX em graus Celsius (bits [23:16]). NAO presumir 100 °C sem confirmar a leitura.</summary>
    public required int TjMaxC { get; init; }

    /// <summary>TCC Activation Offset segundo o datasheet Tiger Lake (bits [29:24]).</summary>
    public required int OffsetTgl { get; init; }

    /// <summary>TCC Activation Offset segundo o SDM Vol. 4 generico (bits [27:24]).</summary>
    public required int OffsetSdm { get; init; }

    /// <summary>Verdadeiro quando as duas mascaras produzem valores diferentes.</summary>
    public required bool OffsetDivergent { get; init; }

    /// <summary>Valor bruto do campo Tau (bits [6:0]). Sem conversao para segundos — limitacao documentada.</summary>
    public required int TauRaw { get; init; }

    /// <summary>Temperatura-alvo media = TjMAX − TCC Activation Offset (fonte: datasheet TGL §4.1.3.1.1).</summary>
    public required int? AverageTargetC { get; init; }

    public required IReadOnlyList<DecodedField> Fields { get; init; }

    public static TemperatureTargetDecoding Decode(ulong value)
    {
        var tjMax = (int)Bits.Extract(value, 16, 8);
        var offsetTgl = (int)Bits.Extract(value, 24, 6); // [29:24] — datasheet TGL
        var offsetSdm = (int)Bits.Extract(value, 24, 4); // [27:24] — SDM Vol. 4 generico
        var tauRaw = (int)Bits.Extract(value, 0, 7);     // [6:0] — datasheet TGL

        const string dsTgl = "Intel 11th Gen Core Datasheet Vol. 1, 631121-012, §4.1.3.1/§4.1.3.1.1";
        const string sdm4 = "Intel SDM Vol. 4, tabela MSR_TEMPERATURE_TARGET (0x1A2)";

        var fields = new List<DecodedField>
        {
            new()
            {
                Bits = Bits.Range(23, 16), Name = "Temperature Target (TjMAX)", Mask = 0xFF0000, Shift = 16,
                Access = "RO (calibrado em fabrica)", Kind = "Configuracao", RawValue = (ulong)tjMax,
                Meaning = "TjMAX — temperatura maxima de juncao em graus Celsius, calibrada em fabrica e nao configuravel pelo usuario. "
                        + "E a partir dela que o offset de ativacao do TCC e subtraido. NAO presumir 100 °C sem confirmar a leitura.",
                Citation = $"{dsTgl}; {sdm4}",
            },
            new()
            {
                Bits = Bits.Range(29, 24), Name = "TCC Activation Offset (fonte TGL)", Mask = 0x3F000000, Shift = 24,
                Access = "RO para leitura (escrita protegida por MSR_PLATFORM_INFO[30])", Kind = "Configuracao", RawValue = (ulong)offsetTgl,
                Meaning = "Offset, em graus Celsius, subtraido de TjMAX para determinar a temperatura-alvo media de ativacao do TCC. "
                        + "Temperatura-alvo media = TjMAX − offset.",
                Citation = dsTgl + " (offset em bits [29:24]); corroborado pela mascara 0x3F do driver intel_tcc para TGL+",
                Note = "O SDM Vol. 4 generico lista o campo como [27:24]; a fonte especifica do Tiger Lake prevalece. "
                     + "O valor pela mascara generica e exibido para conferencia e qualquer divergencia e sinalizada.",
            },
            new()
            {
                Bits = Bits.Range(27, 24), Name = "TCC Activation Offset (fonte SDM generica — conferencia)", Mask = 0x0F000000, Shift = 24,
                Access = "RO para leitura", Kind = "Configuracao", RawValue = (ulong)offsetSdm,
                Meaning = "Mesmo campo segundo a mascara generica do SDM Vol. 4 (bits [27:24]), apresentado apenas para conferencia "
                        + "contra a leitura de [29:24] do datasheet Tiger Lake.",
                Citation = sdm4,
            },
            new()
            {
                Bits = Bits.Range(6, 0), Name = "TCC Activation Temperature Time Window (Tau)", Mask = 0x7F, Shift = 0,
                Access = "RO para leitura", Kind = "Configuracao", RawValue = (ulong)tauRaw,
                Meaning = "Janela temporal opcional usada para gerenciar desempenho no valor do TCC Activation Offset via media "
                        + "exponencialmente ponderada (EWMA) de temperatura. O controle termico tambem atua no limite critico TjMAX; "
                        + "o offset NAO substitui o TjMAX.",
                Citation = dsTgl + " — \"an offset (degrees Celsius) is written to the TEMPERATURE_TARGET (0x1A2) MSR, bits [29:24], "
                         + "and the time window (Tau) is written to the TEMPERATURE_TARGET (0x1A2) MSR [6:0]\"",
                Note = "LIMITACAO: a conversao do valor bruto de Tau para segundos NAO e documentada para o mecanismo de TCC do Tiger Lake. "
                     + "O valor e exibido em bruto. Nao confundir com o Tau do Turbo Boost. O SDM Vol. 4 NAO documenta este campo "
                     + "(lista [15:0] como reservado) — a interpretacao aqui segue exclusivamente o datasheet Tiger Lake.",
            },
            new()
            {
                Bits = Bits.Range(15, 7), Name = "Reservado", Mask = 0xFE80, Shift = 7,
                Access = "—", Kind = "Reservado", RawValue = Bits.Extract(value, 7, 9),
                Meaning = "Bits reservados / nao documentados para Tiger Lake. NAO interpretados.",
                Citation = $"{sdm4} (lista [15:0] como reservado; o datasheet TGL define apenas [6:0] como Tau)",
            },
            new()
            {
                Bits = Bits.Range(63, 30), Name = "Reservado", Mask = 0xFFFFFFFF_C0000000, Shift = 30,
                Access = "—", Kind = "Reservado", RawValue = Bits.Extract(value, 30, 34),
                Meaning = "Bits reservados / nao documentados para Tiger Lake. NAO interpretados.",
                Citation = sdm4 + " (lista [63:28] como reservado; a extensao [29:24] segue o datasheet TGL)",
            },
        };

        return new TemperatureTargetDecoding
        {
            Raw = value,
            TjMaxC = tjMax,
            OffsetTgl = offsetTgl,
            OffsetSdm = offsetSdm,
            OffsetDivergent = offsetTgl != offsetSdm,
            TauRaw = tauRaw,
            AverageTargetC = tjMax - offsetTgl,
            Fields = fields,
        };
    }
}
