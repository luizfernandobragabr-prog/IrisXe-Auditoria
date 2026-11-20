namespace IrisXe.Thermal.Core.Diagnostics;

using IrisXe.Thermal.Core.Decoding;

/// <summary>
/// Comparacao controlada antes/depois da inicializacao do ThrottleStop (requisito 14.1-B).
///
/// Limitacoes declaradas na propria interface:
///   - A comparacao NAO prova, por si so, que o ThrottleStop foi responsavel por qualquer
///     alteracao observada (outras tarefas/BIOS/SMM podem ter escrito nos registradores).
///   - A Fase 1 nao realiza leituras reais; esta comparacao funciona com valores reais
///     (Fase 2) ou com valores manuais de teste (sempre rotulados como SIMULADOS).
/// </summary>
public sealed record ThrottleStopBaseline
{
    public required string Label { get; init; } // "ANTES" ou "DEPOIS"

    public required DateTime TimestampUtc { get; init; }

    /// <summary>Valor bruto do 0x1A2 por processador logico.</summary>
    public required IReadOnlyDictionary<int, ulong> TemperatureTargetByLp { get; init; }

    /// <summary>Valor bruto do 0x19C por processador logico.</summary>
    public required IReadOnlyDictionary<int, ulong> ThermStatusByLp { get; init; }

    /// <summary>Valor bruto do 0x1B1 (pacote).</summary>
    public required ulong? PackageThermStatus { get; init; }

    public required bool Simulated { get; init; }
}

public sealed record ComparisonEntry
{
    public required int MsrAddress { get; init; }

    public required string MsrName { get; init; }

    public required int ProcessorNumber { get; init; }

    public required string BeforeHex { get; init; }

    public required string AfterHex { get; init; }

    public required bool Changed { get; init; }
}

public sealed record ComparisonReport
{
    public required DateTime BaselineAtUtc { get; init; }

    public required DateTime ComparedAtUtc { get; init; }

    public required IReadOnlyList<ComparisonEntry> Entries { get; init; }

    public required bool Simulated { get; init; }

    public const string Disclaimer =
        "A comparacao registra diferencas entre as duas leituras, mas NAO prova causalidade: o ThrottleStop não é o único "
        + "agente que pode escrever nesses registradores (BIOS/SMM também podem). Nenhuma configuração do ThrottleStop "
        + "foi alterada por este aplicativo. Este aplicativo nunca escreve nos registradores.";
}

/// <summary>Fabrica de baselines e relatorios de comparacao a partir de amostras do motor.</summary>
public static class ThrottleStopComparison
{
    /// <summary>Extrai uma linha de base rotulada de uma amostra.</summary>
    public static ThrottleStopBaseline FromSample(SampleSnapshot sample, string label)
    {
        var targetByLp = new Dictionary<int, ulong>();
        for (var i = 0; i < sample.TemperatureTargetReadings.Count; i++)
        {
            var reading = sample.TemperatureTargetReadings[i];
            var lp = i < sample.CoreReadings.Count ? sample.CoreReadings[i].ProcessorNumber : i;
            if (reading.Success)
            {
                targetByLp[lp] = reading.Value;
            }
        }

        var thermByLp = new Dictionary<int, ulong>();
        foreach (var reading in sample.CoreReadings)
        {
            if (reading.Result.Success)
            {
                thermByLp[reading.ProcessorNumber] = reading.Result.Value;
            }
        }

        return new ThrottleStopBaseline
        {
            Label = label,
            TimestampUtc = sample.TimestampUtc,
            TemperatureTargetByLp = targetByLp,
            ThermStatusByLp = thermByLp,
            PackageThermStatus = sample.PackageReadings.FirstOrDefault(r => r.Result.Success) is { } pkgReading
                ? pkgReading.Result.Value
                : null,
            Simulated = sample.Simulated,
        };
    }

    public static ComparisonReport Compare(ThrottleStopBaseline before, ThrottleStopBaseline after)
    {
        var entries = new List<ComparisonEntry>();

        foreach (var lp in before.TemperatureTargetByLp.Keys.Union(after.TemperatureTargetByLp.Keys).OrderBy(k => k))
        {
            var hasBefore = before.TemperatureTargetByLp.TryGetValue(lp, out var beforeValue);
            var hasAfter = after.TemperatureTargetByLp.TryGetValue(lp, out var afterValue);
            entries.Add(new ComparisonEntry
            {
                MsrAddress = MsrAddress.TemperatureTarget,
                MsrName = MsrAddress.Name(MsrAddress.TemperatureTarget),
                ProcessorNumber = lp,
                BeforeHex = hasBefore ? Bits.Hex16(beforeValue) : "N/D",
                AfterHex = hasAfter ? Bits.Hex16(afterValue) : "N/D",
                Changed = hasBefore && hasAfter && beforeValue != afterValue,
            });
        }

        foreach (var lp in before.ThermStatusByLp.Keys.Union(after.ThermStatusByLp.Keys).OrderBy(k => k))
        {
            var hasBefore = before.ThermStatusByLp.TryGetValue(lp, out var beforeValue);
            var hasAfter = after.ThermStatusByLp.TryGetValue(lp, out var afterValue);
            entries.Add(new ComparisonEntry
            {
                MsrAddress = MsrAddress.ThermStatus,
                MsrName = MsrAddress.Name(MsrAddress.ThermStatus),
                ProcessorNumber = lp,
                BeforeHex = hasBefore ? Bits.Hex16(beforeValue) : "N/D",
                AfterHex = hasAfter ? Bits.Hex16(afterValue) : "N/D",
                Changed = hasBefore && hasAfter && beforeValue != afterValue,
            });
        }

        if (before.PackageThermStatus is not null || after.PackageThermStatus is not null)
        {
            entries.Add(new ComparisonEntry
            {
                MsrAddress = MsrAddress.PackageThermStatus,
                MsrName = MsrAddress.Name(MsrAddress.PackageThermStatus),
                ProcessorNumber = -1, // escopo: pacote
                BeforeHex = before.PackageThermStatus is null ? "N/D" : Bits.Hex16(before.PackageThermStatus.Value),
                AfterHex = after.PackageThermStatus is null ? "N/D" : Bits.Hex16(after.PackageThermStatus.Value),
                Changed = before.PackageThermStatus is not null
                       && after.PackageThermStatus is not null
                       && before.PackageThermStatus != after.PackageThermStatus,
            });
        }

        return new ComparisonReport
        {
            BaselineAtUtc = before.TimestampUtc,
            ComparedAtUtc = after.TimestampUtc,
            Entries = entries,
            Simulated = before.Simulated || after.Simulated,
        };
    }
}
