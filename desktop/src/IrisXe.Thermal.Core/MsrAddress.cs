namespace IrisXe.Thermal.Core;

/// <summary>
/// Enderecos dos tres unicos MSRs autorizados para leitura neste projeto.
/// Qualquer outro endereco e recusado pela <see cref="Security.ReadPolicy"/>,
/// inclusive nos modos de teste manual. (Requisito 8.1 e 15.4 do escopo.)
/// </summary>
public static class MsrAddress
{
    /// <summary>MSR_TEMPERATURE_TARGET (IA32_TEMPERATURE_TARGET).</summary>
    public const int TemperatureTarget = 0x1A2;

    /// <summary>IA32_THERM_STATUS — indicadores termicos por processador logico.</summary>
    public const int ThermStatus = 0x19C;

    /// <summary>MSR_PACKAGE_THERM_STATUS — indicadores termicos do pacote.</summary>
    public const int PackageThermStatus = 0x1B1;

    /// <summary>Lista fechada de enderecos autorizados.</summary>
    public static readonly IReadOnlyList<int> All = new[] { TemperatureTarget, ThermStatus, PackageThermStatus };

    public static bool IsAuthorized(int address) => All.Contains(address);

    public static string Name(int address) => address switch
    {
        TemperatureTarget => "MSR_TEMPERATURE_TARGET (IA32_TEMPERATURE_TARGET)",
        ThermStatus => "IA32_THERM_STATUS",
        PackageThermStatus => "MSR_PACKAGE_THERM_STATUS",
        _ => $"MSR 0x{address:X} (nao autorizado)",
    };

    /// <summary>Escopo documentado de cada registrador (SDM Vol. 4).</summary>
    public static string Scope(int address) => address switch
    {
        TemperatureTarget => "Por processador logico (SDM Vol. 4). Os valores sao calibrados em fabrica; "
                           + "divergencias entre processadores logicos NAO sao descartadas — sao reportadas.",
        ThermStatus => "Por processador logico (SDM Vol. 4). A leitura e realizada em cada processador logico.",
        PackageThermStatus => "Por pacote (SDM Vol. 4). A leitura e repetida em todos os processadores logicos "
                            + "apenas para conferencia de consistencia.",
        _ => "—",
    };

    public static string Hex(int address) => $"0x{address:X}";
}
