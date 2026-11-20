namespace IrisXe.Thermal.Core.Decoding;

/// <summary>
/// Decodificador do MSR_PACKAGE_THERM_STATUS (0x1B1) — escopo: POR PACOTE (SDM Vol. 4).
///
/// Fonte: Intel 64 and IA-32 SDM, Vol. 4, tabela do registro. Suporte verificavel via
/// CPUID.06H:EAX (bit 6 — "Package thermal status via MSR 0x1B1").
/// Classificacao temporal identica a IA32_THERM_STATUS: bits de status (RO) = corrente;
/// bits de log (R/WC0) = historico; RDMSR nao os limpa e este aplicativo nunca escreve.
/// Bits acima de [22:16] nao constam na tabela extraida do SDM Vol. 4 e sao tratados como
/// NAO DOCUMENTADOS (exibidos apenas em bruto). O datasheet TGL 631121-012 §4.1.3.2.1 menciona
/// um bit "Out of Specification" neste MSR e em IA32_THERM_STATUS, porem sem posicao definida
/// na tabela extraida — portanto NAO decodificado aqui.
/// </summary>
public sealed record PackageThermStatusDecoding
{
    public required ulong Raw { get; init; }

    public required bool ThermalStatus { get; init; }      // bit 0 — corrente
    public required bool ThermalStatusLog { get; init; }   // bit 1 — historico
    public required bool ProchotEvent { get; init; }       // bit 2 — corrente (componente externo)
    public required bool ProchotLog { get; init; }         // bit 3 — historico
    public required bool CriticalTempStatus { get; init; } // bit 4
    public required bool CriticalTempLog { get; init; }    // bit 5
    public required bool Threshold1Status { get; init; }   // bit 6
    public required bool Threshold1Log { get; init; }      // bit 7
    public required bool Threshold2Status { get; init; }   // bit 8
    public required bool Threshold2Log { get; init; }      // bit 9
    public required bool PowerLimitStatus { get; init; }   // bit 10
    public required bool PowerLimitLog { get; init; }      // bit 11
    public required int DigitalReadout { get; init; }      // bits [22:16]

    /// <summary>Temperatura do pacote em °C = TjMAX − DigitalReadout (quando aplicavel).</summary>
    public required int? TemperatureC { get; init; }

    public required IReadOnlyList<DecodedField> Fields { get; init; }

    private const string Citation = "Intel SDM Vol. 4, tabela MSR_PACKAGE_THERM_STATUS (MSR 0x1B1); escopo: por pacote";

    public static PackageThermStatusDecoding Decode(ulong value, int? tjMaxC)
    {
        bool B(int bit) => Bits.Extract(value, bit, 1) == 1UL;
        var readout = (int)Bits.Extract(value, 16, 7);

        DecodedField Flag(int bit, string name, string meaning, string kind) => new()
        {
            Bits = Bits.Range(bit, bit), Name = name, Mask = 1UL << bit, Shift = bit,
            Access = kind == "Historico" ? "R/WC0 (log — limpo apenas por escrita de 0; RDMSR nao limpa)" : "RO",
            Kind = kind, RawValue = B(bit) ? 1UL : 0UL, Meaning = meaning, Citation = Citation,
        };

        var fields = new List<DecodedField>
        {
            Flag(0, "Package Thermal Status", "Indica que a saida de alta temperatura do sensor termico digital DO PACOTE esta ativa "
                 + "neste momento (temperatura do pacote atingiu a temperatura de ativacao do TCC).", "Corrente"),
            Flag(1, "Package Thermal Status Log", "Registro historico do sensor de alta temperatura do pacote. Permanece setado ate "
                 + "limpeza por software; RDMSR nao o limpa.", "Historico"),
            Flag(2, "Package PROCHOT# Event", "Indica que o sinal PROCHOT# do pacote esta sendo asserido por OUTRO componente da "
                 + "plataforma neste momento (por exemplo, VR termicamente sobrecarregado, fonte ou outro dispositivo).", "Corrente"),
            Flag(3, "Package PROCHOT# Log", "Registro historico de assercao de PROCHOT# por componente externo no pacote.", "Historico"),
            Flag(4, "Package Critical Temperature Status", "Status corrente do detector de temperatura critica do pacote.", "Corrente"),
            Flag(5, "Package Critical Temperature Status Log", "Registro historico de temperatura critica do pacote.", "Historico"),
            Flag(6, "Package Thermal Threshold #1 Status", "Status corrente do limiar termico #1 do pacote.", "Corrente"),
            Flag(7, "Package Thermal Threshold #1 Log", "Registro historico do limiar termico #1 do pacote.", "Historico"),
            Flag(8, "Package Thermal Threshold #2 Status", "Status corrente do limiar termico #2 do pacote.", "Corrente"),
            Flag(9, "Package Thermal Threshold #2 Log", "Registro historico do limiar termico #2 do pacote.", "Historico"),
            Flag(10, "Package Power Limitation Status", "Status corrente de limitacao por potencia no pacote.", "Corrente"),
            Flag(11, "Package Power Limitation Log", "Registro historico de limitacao por potencia no pacote.", "Historico"),
            new()
            {
                Bits = Bits.Range(15, 12), Name = "Reservado", Mask = 0xF000, Shift = 12,
                Access = "—", Kind = "Reservado", RawValue = Bits.Extract(value, 12, 4),
                Meaning = "Bits reservados. NAO interpretados.", Citation = Citation,
            },
            new()
            {
                Bits = Bits.Range(22, 16), Name = "Package Digital Readout", Mask = 0x7F0000, Shift = 16,
                Access = "RO", Kind = "Medicao", RawValue = (ulong)readout,
                Meaning = "Leitura digital do sensor do pacote em graus Celsius relativa a temperatura de ativacao do TCC: "
                        + "T_package = TjMAX − Package Digital Readout.",
                Citation = Citation + "; datasheet TGL 631121-012 §4.1.3.2",
            },
            new()
            {
                Bits = Bits.Range(63, 23), Name = "Nao documentado (tratado como reservado)", Mask = 0xFFFFFFFF_FF800000, Shift = 23,
                Access = "—", Kind = "Reservado", RawValue = Bits.Extract(value, 23, 41),
                Meaning = "Bits sem definicao na tabela extraida do SDM Vol. 4 para este MSR. NAO interpretados. O datasheet TGL "
                        + "§4.1.3.2.1 referencia um bit 'Out of Specification' sem posicao definida nesta tabela — nao decodificado.",
                Citation = Citation + "; datasheet TGL 631121-012 §4.1.3.2.1 (mencao a 'Out of Specification')",
            },
        };

        return new PackageThermStatusDecoding
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
            DigitalReadout = readout,
            TemperatureC = tjMaxC is not null ? tjMaxC - readout : null,
            Fields = fields,
        };
    }
}
