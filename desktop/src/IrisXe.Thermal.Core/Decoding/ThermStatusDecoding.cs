namespace IrisXe.Thermal.Core.Decoding;

/// <summary>
/// Decodificador do IA32_THERM_STATUS (0x19C) — escopo: POR PROCESSADOR LOGICO (SDM Vol. 4).
///
/// Fonte: Intel 64 and IA-32 SDM, Vol. 4, tabela IA32_THERM_STATUS (extraida da revisao corrente
/// do documento 335592-sdm-vol-4). Politica de classificacao temporal (requisito §6):
///   - Bits de status (RO) = estado CORRENTE no momento da leitura;
///   - Bits de log (R/WC0) = registro HISTORICO (permanecem setados ate que um SOFTWARE escreva 0;
///     a instrucao RDMSR nao os limpa — este aplicativo nunca escreve nos registradores).
/// Observacao: outras ferramentas (inclusive o ThrottleStop, se configurado para isso) podem
/// limpar bits de log escrevendo nos MSRs; esse efeito e externo a este aplicativo e, por isso,
/// todo evento e registrado em log de software no instante da amostragem.
///
/// O campo Digital Readout [22:16] fornece a temperatura do DTS em graus Celsius relativa a
/// temperatura de ativacao do TCC: T_core = TjMAX − DigitalReadout (valida quando Reading Valid = 1).
/// </summary>
public sealed record ThermStatusDecoding
{
    public required ulong Raw { get; init; }

    public required bool ThermalStatus { get; init; }          // bit 0 — corrente
    public required bool ThermalStatusLog { get; init; }       // bit 1 — historico
    public required bool ProchotEvent { get; init; }           // bit 2 — corrente (componente externo)
    public required bool ProchotLog { get; init; }             // bit 3 — historico
    public required bool CriticalTempStatus { get; init; }     // bit 4
    public required bool CriticalTempLog { get; init; }        // bit 5
    public required bool Threshold1Status { get; init; }       // bit 6
    public required bool Threshold1Log { get; init; }          // bit 7
    public required bool Threshold2Status { get; init; }       // bit 8
    public required bool Threshold2Log { get; init; }          // bit 9
    public required bool PowerLimitStatus { get; init; }       // bit 10
    public required bool PowerLimitLog { get; init; }          // bit 11
    public required bool CurrentLimitStatus { get; init; }     // bit 12
    public required bool CurrentLimitLog { get; init; }        // bit 13
    public required bool CrossDomainStatus { get; init; }      // bit 14
    public required bool CrossDomainLog { get; init; }         // bit 15
    public required int DigitalReadout { get; init; }          // bits [22:16]
    public required int ResolutionC { get; init; }             // bits [30:27]
    public required bool ReadingValid { get; init; }           // bit 31

    /// <summary>Temperatura do DTS em °C = TjMAX − DigitalReadout; nulo quando a leitura nao e valida.</summary>
    public required int? TemperatureC { get; init; }

    public required IReadOnlyList<DecodedField> Fields { get; init; }

    private const string Citation = "Intel SDM Vol. 4, tabela IA32_THERM_STATUS (MSR 0x19C); escopo: por processador logico";

    public static ThermStatusDecoding Decode(ulong value, int? tjMaxC)
    {
        bool B(int bit) => Bits.Extract(value, bit, 1) == 1UL;
        var readout = (int)Bits.Extract(value, 16, 7);
        var valid = B(31);
        var resolution = (int)Bits.Extract(value, 27, 4);

        DecodedField Flag(int bit, string name, string meaning, string kind, string? note = null) => new()
        {
            Bits = Bits.Range(bit, bit), Name = name, Mask = 1UL << bit, Shift = bit,
            Access = kind == "Historico" ? "R/WC0 (log — limpo apenas por escrita de 0; RDMSR nao limpa)" : "RO",
            Kind = kind, RawValue = B(bit) ? 1UL : 0UL, Meaning = meaning, Citation = Citation, Note = note,
        };

        var fields = new List<DecodedField>
        {
            Flag(0, "Thermal Status", "Indica que a saida de alta temperatura do sensor termico digital (DTS) do processador logico "
                 + "esta ativa no momento — ou seja, a temperatura atingiu a temperatura de ativacao do TCC (throttling termico corrente).", "Corrente"),
            Flag(1, "Thermal Status Log", "Registro historico: o sensor de alta temperatura foi ativado em algum momento desde o "
                 + "ultimo reset ou desde a ultima limpeza por software. Permanece setado; RDMSR nao o limpa.", "Historico"),
            Flag(2, "PROCHOT# ou FORCEPR# Event", "Indica que o sinal PROCHOT#/FORCEPR# ESTA SENDO asserido por OUTRO componente "
                 + "da plataforma neste momento — ou seja, um sinal externo ao nucleo. Nao confundir com o bit 0 (evento termico interno).", "Corrente"),
            Flag(3, "PROCHOT# ou FORCEPR# Log", "Registro historico: o sinal PROCHOT#/FORCEPR# foi asserido por componente externo "
                 + "em algum momento desde o ultimo reset/limpeza. Permanece setado; RDMSR nao o limpa.", "Historico"),
            Flag(4, "Critical Temperature Status", "Status corrente do detector de temperatura critica do processador logico.", "Corrente"),
            Flag(5, "Critical Temperature Status Log", "Registro historico de temperatura critica no processador logico.", "Historico"),
            Flag(6, "Thermal Threshold #1 Status", "Status corrente do limiar termico #1.", "Corrente"),
            Flag(7, "Thermal Threshold #1 Log", "Registro historico do limiar termico #1.", "Historico"),
            Flag(8, "Thermal Threshold #2 Status", "Status corrente do limiar termico #2.", "Corrente"),
            Flag(9, "Thermal Threshold #2 Log", "Registro historico do limiar termico #2.", "Historico"),
            Flag(10, "Power Limitation Status", "Status corrente de limitacao por potencia (power limiting).", "Corrente"),
            Flag(11, "Power Limitation Log", "Registro historico de limitacao por potencia.", "Historico"),
            Flag(12, "Current Limit Status", "Status corrente de limitacao por corrente (electrical current limiting).", "Corrente"),
            Flag(13, "Current Limit Log", "Registro historico de limitacao por corrente.", "Historico"),
            Flag(14, "Cross Domain Limit Status", "Status corrente de limitacao entre dominios.", "Corrente"),
            Flag(15, "Cross Domain Limit Log", "Registro historico de limitacao entre dominios.", "Historico"),
            new()
            {
                Bits = Bits.Range(22, 16), Name = "Digital Readout", Mask = 0x7F0000, Shift = 16,
                Access = "RO", Kind = "Medicao", RawValue = (ulong)readout,
                Meaning = "Leitura digital do DTS em graus Celsius RELATIVA a temperatura de ativacao do TCC. "
                        + "Temperatura do nucleo = TjMAX − Digital Readout (valida quando Reading Valid = 1).",
                Citation = Citation + "; datasheet TGL 631121-012 §4.1.3.2 (DTS)",
            },
            new()
            {
                Bits = Bits.Range(26, 23), Name = "Reservado", Mask = 0x7800000, Shift = 23,
                Access = "—", Kind = "Reservado", RawValue = Bits.Extract(value, 23, 4),
                Meaning = "Bits reservados. NAO interpretados (revisoes antigas do SDM definiam o bit 23 como 'Resolution of Digital "
                        + "Readout'; a tabela corrente lista [26:23] reservado — o valor bruto e sempre exibido).",
                Citation = Citation,
            },
            new()
            {
                Bits = Bits.Range(30, 27), Name = "Resolution in Degrees Celsius", Mask = 0x78000000, Shift = 27,
                Access = "RO", Kind = "Medicao", RawValue = (ulong)resolution,
                Meaning = "Resolucao da leitura digital do DTS em graus Celsius.", Citation = Citation,
            },
            new()
            {
                Bits = Bits.Range(31, 31), Name = "Reading Valid", Mask = 0x80000000, Shift = 31,
                Access = "RO", Kind = "Medicao", RawValue = valid ? 1UL : 0UL,
                Meaning = "1 = o campo Digital Readout [22:16] contem uma leitura valida. Se 0, a temperatura NAO e exibida "
                        + "(aparece 'Nao disponivel — leitura invalida'), nunca um valor presumido.",
                Citation = Citation,
            },
            new()
            {
                Bits = Bits.Range(63, 32), Name = "Reservado", Mask = 0xFFFFFFFF00000000, Shift = 32,
                Access = "—", Kind = "Reservado", RawValue = Bits.Extract(value, 32, 32),
                Meaning = "Bits reservados. NAO interpretados.", Citation = Citation,
            },
        };

        return new ThermStatusDecoding
        {
            Raw = value,
            ThermalStatus = B(0),
            ThermalStatusLog = B(1),
            ProchotEvent = B(2),
            ProchotLog = B(3),
            CriticalTempStatus = B(4),
            CriticalTempLog = B(5),
            Threshold1Status = B(6),
            Threshold1Log = B(7),
            Threshold2Status = B(8),
            Threshold2Log = B(9),
            PowerLimitStatus = B(10),
            PowerLimitLog = B(11),
            CurrentLimitStatus = B(12),
            CurrentLimitLog = B(13),
            CrossDomainStatus = B(14),
            CrossDomainLog = B(15),
            DigitalReadout = readout,
            ResolutionC = resolution,
            ReadingValid = valid,
            TemperatureC = valid && tjMaxC is not null ? tjMaxC - readout : null,
            Fields = fields,
        };
    }
}
