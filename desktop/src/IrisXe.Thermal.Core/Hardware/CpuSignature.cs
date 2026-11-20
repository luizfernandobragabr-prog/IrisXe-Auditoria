using System.Globalization;
using System.Text.RegularExpressions;

namespace IrisXe.Thermal.Core.Hardware;

/// <summary>
/// Assinatura do processador equivalente a CPUID.1:EAX (família/modelo/stepping).
///
/// Estrategia (Fase 1, sem driver): a assinatura e obtida da cadeia "Identifier" do registro
/// Windows (HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0), que o proprio sistema
/// operacional deriva da instrucao CPUID durante o boot — ex.: "Intel64 Family 6 Model 140
/// Stepping 1". Isso equivale a consultar CPUID sem executar codigo nativo de usuario.
/// O nome comercial vem de "ProcessorNameString" na mesma chave.
///
/// Tiger Lake-U (i7-1195G7): Family 6, Model 0x8C (140 decimal).
/// Leituras em outros processadores sao RECUSADAS (requisito 9.1 — impedir leituras
/// incompativeis com o hardware detectado).
/// </summary>
public sealed record CpuSignature
{
    /// <summary>Model CPUID do Tiger Lake-U (i7-1195G7): 0x8C = 140.</summary>
    public const int TigerLakeUModel = 0x8C;

    public const int IntelFamily = 6;

    private static readonly Regex IdentifierRegex =
        new(@"Family\s+(?<family>\d+)\s+Model\s+(?<model>\d+)\s+Stepping\s+(?<stepping>\d+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public required string Identifier { get; init; }

    public required string BrandName { get; init; }

    public required int Family { get; init; }

    public required int Model { get; init; }

    public required int Stepping { get; init; }

    public bool IsIntelFamily6 =>
        Identifier.StartsWith("Intel64", StringComparison.OrdinalIgnoreCase) && Family == IntelFamily;

    public bool IsTigerLake1195G7 => IsIntelFamily6 && Model == TigerLakeUModel;

    public string Description => $"{BrandName} — {Identifier} (Família {Family}, Modelo 0x{Model:X}, Stepping {Stepping})";

    public string? UnsupportedReason
    {
        get
        {
            if (!Identifier.StartsWith("Intel64", StringComparison.OrdinalIgnoreCase))
            {
                return $"Processador não Intel64 ({Identifier}). As leituras foram recusadas: o hardware detectado não é o alvo do projeto.";
            }

            if (Family != IntelFamily)
            {
                return $"Família CPUID {Family} não suportada (esperado: 6). As leituras foram recusadas.";
            }

            if (Model != TigerLakeUModel)
            {
                return $"Modelo CPUID 0x{Model:X} não é o Tiger Lake-U 0x8C do i7-1195G7. As leituras foram recusadas: "
                     + "a decodificação foi validada apenas para este processador (requisito 9.1).";
            }

            return null;
        }
    }

    /// <summary>Analisador da cadeia "Identifier" do registro do Windows.</summary>
    /// <exception cref="FormatException">Quando a cadeia nao contem a assinatura familia/modelo/stepping.</exception>
    public static CpuSignature Parse(string identifier, string brandName)
    {
        var match = IdentifierRegex.Match(identifier);
        if (!match.Success)
        {
            throw new FormatException(
                $"Não foi possível interpretar a assinatura do processador a partir de '{identifier}'. "
                + "Formato esperado: 'Intel64 Family 6 Model 140 Stepping 1'.");
        }

        return new CpuSignature
        {
            Identifier = identifier.Trim(),
            BrandName = brandName.Trim(),
            Family = int.Parse(match.Groups["family"].Value, CultureInfo.InvariantCulture),
            Model = int.Parse(match.Groups["model"].Value, CultureInfo.InvariantCulture),
            Stepping = int.Parse(match.Groups["stepping"].Value, CultureInfo.InvariantCulture),
        };
    }
}
